import os
import logging
from ds_translator import api as api
from ds_translator.rich_progress import run_task
from ds_translator.rich_progress import shared_console as console
from ds_translator.icons import icon

# package logger (configured by init_logging in main)
logger = logging.getLogger("ds_translator")


def parse_srt(content):
    """更鲁棒地解析 SRT 字幕内容"""
    lines = content.strip().split('\n')
    subtitles = []
    i = 0
    while i < len(lines):
        line = lines[i].strip()
        # 跳过空行
        if not line:
            i += 1
            continue

        # 序号行（必须是数字）
        if not line.isdigit():
            i += 1
            continue

        index = line
        i += 1

        # 时间码行（包含 -->）
        if i >= len(lines) or '-->' not in lines[i]:
            i += 1
            continue

        timecode = lines[i].strip()
        i += 1

        # 提取字幕文本（直到下一个空行或 EOF）
        text_lines = []
        while i < len(lines) and lines[i].strip() != '':
            text_lines.append(lines[i].strip())
            i += 1

        text = ' '.join(text_lines).strip()
        subtitles.append((index, timecode, text))

        # 跳过空行
        while i < len(lines) and lines[i].strip() == '':
            i += 1

        # Parsing count is returned to caller; caller will decide how to display
        # this informational message (via progress callback or subtle console).
    return subtitles


def _read_text_with_fallback(path, progress_callback=None):
    """尝试一组常见编码读取文件，返回 (text, encoding_used)。

    This avoids crashes when files are encoded in Shift_JIS/CP932 or contain a BOM.
    """
    encodings = [
        "utf-8",
        "utf-8-sig",
        "cp932",
        "shift_jis",
        "euc_jp",
        "iso2022_jp",
        "latin-1",
    ]
    with open(path, "rb") as f:
        raw = f.read()

    for enc in encodings:
        try:
            text = raw.decode(enc)
            msg = f"{icon('info')} 读取文件 {os.path.basename(path)}，检测到编码: {enc}"
            if progress_callback:
                try:
                    progress_callback('info', msg)
                except Exception:
                    pass
            else:
                console.print(msg, style="italic dim")
            return text, enc
        except Exception:
            continue

    # 最后保底：使用 latin-1 且替换错误字节，避免抛出异常
    text = raw.decode("latin-1", errors="replace")
    msg = f"{icon('warn')} 无法检测到正确编码，已使用 'latin-1' (errors=replace) 读取 {os.path.basename(path)}"
    if progress_callback:
        try:
            progress_callback('info', msg)
        except Exception:
            pass
    else:
        console.print(msg, style="italic dim")
    return text, "latin-1"


def rebuild_srt(translated_subs):
    """重组双语字幕"""
    lines = []
    for idx, timecode, original, trans in translated_subs:
        lines.append(idx)
        lines.append(timecode)
        lines.append(original)
        lines.append(trans)
        lines.append("")  # 空行
    return "\n".join(lines)


def translate_srt_file(input_path, output_path, lexicon=None, progress_callback=None):
    """处理单个 SRT 文件"""
    # Use a robust reader that tries several encodings to avoid utf-8 decode errors
    content, used_encoding = _read_text_with_fallback(input_path, progress_callback=progress_callback)

    subtitles = parse_srt(content)
    if not subtitles:
        msg = f"警告: {input_path} 未解析到字幕内容"
        if progress_callback:
            try:
                progress_callback('info', msg)
            except Exception:
                pass
        else:
            console.print(msg, style="italic dim")
        return

    # Notify caller that we parsed the subtitles. If caller provided a
    # progress_callback it can display this as subtle per-file info; otherwise
    # print it via the shared console.
    if progress_callback:
        try:
            # Initialize the right-hand info field to show numeric progress
            # e.g. "0/1000" instead of a parsed-count message.
            progress_callback('info', f"0/{len(subtitles)}")
        except Exception:
            pass
    else:
        console.print(f"{icon('search')} 成功解析 {len(subtitles)} 条字幕", style="italic dim")

    # If caller provided a progress callback, tell it how many items we'll process
    if progress_callback:
        try:
            progress_callback('set_total', len(subtitles))
        except Exception:
            pass

    translated_subs = []
    new_translations = 0

    # window_size: how many neighboring lines to include before/after (default 1)
    window_size = 1

    # If caller provided a progress_callback (e.g. main.py shows its own Progress UI),
    # avoid creating an internal rich progress to prevent duplicate/multiple bars.
    if progress_callback:
        for i, sub in enumerate(subtitles):
            idx, timecode, text = sub
            if not text.strip():
                translated = ""
            else:
                # Build a small context: previous line(s), mark current as [NOW], next line(s)
                before = "\n".join([s[2] for s in subtitles[max(0, i - window_size):i]])
                after = "\n".join([s[2] for s in subtitles[i+1:i+1+window_size]])
                ctx_parts = []
                if before:
                    ctx_parts.append("[BEFORE] " + before)
                ctx_parts.append("[NOW] " + text)
                if after:
                    ctx_parts.append("[AFTER] " + after)
                context = "\n".join(ctx_parts)

                translated = api.translate_text(text, lexicon=lexicon, context=context)
                if translated and translated != "[翻译失败]":
                    new_translations += 1

            translated_subs.append((idx, timecode, text, translated))

            # Notify caller-provided callback for backward compatibility
            try:
                progress_callback('advance', 1)
                # Also update the compact numeric info displayed to the
                # right of the progress bar (e.g. "1/1000") so users see
                # per-item counts inline.
                progress_callback('info', f"{i+1}/{len(subtitles)}")
            except Exception:
                pass
    else:
        # No external progress provided: show internal single-line rich progress
        with run_task("Translating", len(subtitles)) as p:
            for i, sub in enumerate(subtitles):
                idx, timecode, text = sub
                if not text.strip():
                    translated = ""
                else:
                    # Build a small context: previous line(s), mark current as [NOW], next line(s)
                    before = "\n".join([s[2] for s in subtitles[max(0, i - window_size):i]])
                    after = "\n".join([s[2] for s in subtitles[i+1:i+1+window_size]])
                    ctx_parts = []
                    if before:
                        ctx_parts.append("[BEFORE] " + before)
                    ctx_parts.append("[NOW] " + text)
                    if after:
                        ctx_parts.append("[AFTER] " + after)
                    context = "\n".join(ctx_parts)

                    translated = api.translate_text(text, lexicon=lexicon, context=context)
                    if translated and translated != "[翻译失败]":
                        new_translations += 1

                translated_subs.append((idx, timecode, text, translated))

                # Update rich progress (single-line) with a short info string
                try:
                    p.update(advance=1, info=f"{i+1}/{len(subtitles)}")
                except Exception:
                    pass

    # 保存双语字幕
    output_content = rebuild_srt(translated_subs)
    with open(output_path, "w", encoding="utf-8") as f:
        f.write(output_content)

    msg = f"{icon('success')} 已保存双语字幕: {output_path} (源文件编码: {locals().get('used_encoding', 'unknown')})"
    if progress_callback:
        try:
            # send as a final message so it is printed after the per-file
            # progress task is removed and remains visible to the user
            progress_callback('final', msg)
        except Exception:
            console.print(msg, style="italic dim")
    else:
        console.print(msg, style="italic dim")

    if new_translations > 0:
        msg2 = f"{icon('new')} 新增 {new_translations} 条翻译，已存入数据库"
        if progress_callback:
            try:
                progress_callback('final', msg2)
            except Exception:
                console.print(msg2, style="italic dim")
        else:
            console.print(msg2, style="italic dim")
