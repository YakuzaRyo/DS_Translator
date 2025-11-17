import os
import sys
import pkgutil
import importlib
from dotenv import load_dotenv
from pathlib import Path
from utils.registry import get_registry
# Load environment variables
load_dotenv()
from ds_translator.logging_config import init_logging

from rich.progress import (
    Progress,
    SpinnerColumn,
    BarColumn,
    TextColumn,
    TimeElapsedColumn,
    TimeRemainingColumn,
)
# PercentageColumn may not be available in all Rich versions; use TextColumn for percentage
import time

# initialize logging for CLI runs
logger = init_logging()

from ds_translator.console import subtle
# Use the shared console defined in ds_translator.rich_progress so all
# modules that use the shared_console render to the same terminal instance.
from ds_translator.rich_progress import shared_console as console
from ds_translator.icons import icon


# Note: we no longer override builtins.print or patch tqdm globally here.
# Keep output handling explicit (use `console.print` where formatted output
# is needed) to avoid surprising global side-effects.

def init_data_paths():
    """Compute and create data directories.

    Returns (input_dir, output_dir) as strings. Uses the repository
    directory as base for ./data/subtitle and ./data/roast by default.
    Can be overridden with environment variables INPUT_DIR and OUTPUT_DIR.
    """
    base = Path(__file__).parent.resolve()
    data_dir = base / "data"

    # Allow environment override
    env_input = os.getenv("INPUT_DIR")
    env_output = os.getenv("OUTPUT_DIR")

    input_dir = Path(env_input) if env_input else (data_dir / "subtitle")
    output_dir = Path(env_output) if env_output else (data_dir / "roast")

    # Make sure base data directory exists and create common subfolders used
    try:
        data_dir.mkdir(parents=True, exist_ok=True)
    except Exception:
        # best-effort: don't crash here, caller may handle missing dirs
        subtle(f"Warning: failed to create base data directory: {data_dir}")

    # Common data subdirectories the app expects
    common_dirs = [
        data_dir / "logs",
        data_dir / "subtitle",
        data_dir / "roast",
        data_dir / "cache_db",
        data_dir / "lexicon",
        data_dir / "proofread",
    ]

    for d in common_dirs:
        try:
            d.mkdir(parents=True, exist_ok=True)
        except Exception:
            # don't raise; emit a subtle informational message so first-run won't crash unexpectedly
            subtle(f"Warning: failed to create directory: {d}")

    # Ensure input/output dirs (may be env-overridden and outside data_dir)
    try:
        input_dir.mkdir(parents=True, exist_ok=True)
    except Exception:
        subtle(f"Warning: failed to create input directory: {input_dir}")
    try:
        output_dir.mkdir(parents=True, exist_ok=True)
    except Exception:
        subtle(f"Warning: failed to create output directory: {output_dir}")

    return str(input_dir), str(output_dir)

def load_plugins():
    """Load plugins from the plugins directory."""
    import plugins
    
    for _, module_name, _ in pkgutil.iter_modules(plugins.__path__, plugins.__name__ + "."):
        subtle(f"[SYS]{module_name} loaded")
        importlib.import_module(module_name)

from ds_translator.db import init_db, show_stats
from ds_translator.lexicon import ensure_lexicon_exists, load_lexicon
import importlib.util

# Prefer the pure-Python `ds_translator/srt.py` implementation when present.
# The package also contains a compiled extension `srt.cp313-win_amd64.pyd` which
# may be selected by the importer and can produce its own tqdm output. To ensure
# we use the editable Python source (so our progress handling changes apply),
# load the source file explicitly and bind `translate_srt_file` from it.
try:
    src_path = os.path.join(os.path.dirname(__file__), "ds_translator", "srt.py")
    if os.path.exists(src_path):
        spec = importlib.util.spec_from_file_location("ds_translator.srt_py", src_path)
        _srt_module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(_srt_module)  # type: ignore
        translate_srt_file = _srt_module.translate_srt_file
    else:
        # Fallback to regular import if source not present
        from ds_translator.srt import translate_srt_file  # type: ignore
except Exception:
    # If anything goes wrong, fall back to default import to avoid crashing
    from ds_translator.srt import translate_srt_file  # type: ignore
from ds_translator import api as api_module


def main():
    load_plugins()
    registry = get_registry()
    # Validate environment and input directory
    api_key = os.getenv("DEEPSEEK_API_KEY")
    if not api_key:
        logger.error(f"{icon('error')} 错误: 未找到 DEEPSEEK_API_KEY，请检查 .env 文件")
        return
    verify_type = os.getenv("VERIFY_TYPE", "roasted")
    # Initialize data paths (creates directories if missing)
    input_dir, output_dir = init_data_paths()
    subtle(f"{icon('folder')} 使用输入目录: {input_dir}")
    subtle(f"{icon('folder')} 使用输出目录: {output_dir}")

    if not os.path.exists(input_dir):
        logger.error(f"{icon('error')} 错误: 找不到 '{input_dir}' 文件夹，请确保字幕文件存放在此目录。")
        return

    for group, name, func in registry:
        if group == "plugin" and name == verify_type:
            found_function = func
            break
    srt_files = found_function(input_dir, output_dir)
    if srt_files is not None and len(srt_files) == 0:
        subtle(f"{icon('done')} 提示: '{input_dir}' 文件夹中没有找到需要执行的 .srt 文件。")
        return

    # 初始化数据库与词库（如果不存在则创建示例）
    init_db()
    ensure_lexicon_exists()
    lexicon = load_lexicon()

    # 启动后台重试工作线程（会处理持久化的重试队列）
    try:
        api_module.start_retry_worker()
    except Exception as e:
        logger.warning(f"{icon('warn')} 无法启动重试工作线程: {e}")

    # 显示当前缓存状态
    show_stats()

    subtle(f"{icon('search')} 发现 {len(srt_files)} 个字幕文件，开始翻译...")

    try:
        # Use Rich Progress to show translation progress. Use a single-line
        # progress: description + bar + percentage + elapsed. This renders as:
        # "Translating <file> ━━━ 100% 0:00:00" on one line.
        with Progress(
            # Add a spinner column (green 'dots') so the UI shows the familiar
            # spinning green-dot indicator like the demo spinner.
            SpinnerColumn(spinner_name="dots", style="green"),
            TextColumn("{task.description}"),
            BarColumn(bar_width=None),
            # Right-aligned info field (used for small per-file messages like
            # "parsed N subtitles") so they appear on the progress line.
            TextColumn("{task.fields[info]}", justify="right"),
            TextColumn("[progress.percentage]{task.percentage:>3.0f}%"),
            TimeElapsedColumn(),
            console=console,
        ) as progress:
            # For each file we'll create a short-lived per-file task that tracks
            # subtitle-level progress. We remove the task when the file is done
            # so only one summarized line is visible at a time.
            for filename in srt_files:
                input_path = os.path.join(input_dir, filename)
                # 在输出文件名中加入 roasted 信息，例如 "movie.srt" -> "movie-roasted.srt"
                name, ext = os.path.splitext(filename)
                roasted_filename = f"{name}-roasted{ext}"
                output_path = os.path.join(output_dir, roasted_filename)

                file_task = progress.add_task(f"Translating {filename}", total=0, info="")

                # Create a callback closure the srt translator can call. It will
                # set the total when known and advance the completed count; we
                # also compute and show lines/sec in the task description.
                def make_progress_callback(task_id, fname):
                    start_time = None
                    completed = 0
                    total = None
                    # Throttle description updates to avoid excessive terminal writes
                    last_desc_time = 0.0
                    desc_update_interval = 0.5  # seconds
                    # Collector for messages that should be printed after the
                    # per-file task is removed (so they don't get lost when the
                    # task is removed immediately after translation finishes).
                    final_msgs = []

                    def _cb(op, value=None):
                        nonlocal start_time, completed, total
                        if op == 'set_total':
                            total = int(value) if value is not None else None
                            start_time = time.perf_counter()
                            try:
                                # Set the total and initial description once.
                                progress.update(task_id, total=total, description=f"Translating {fname}")
                            except Exception:
                                pass
                        elif op == 'advance':
                            inc = int(value) if value is not None else 1
                            completed += inc
                            try:
                                # Only update numeric completed count; visual bar/percent
                                # and elapsed time are rendered by Rich on the single line.
                                progress.update(task_id, completed=completed)
                            except Exception:
                                pass
                        elif op == 'info':
                            # small informational text to display on the right of the progress line
                            try:
                                progress.update(task_id, info=value)
                            except Exception:
                                pass
                        elif op == 'final':
                            # store final messages (e.g., saved path) to be printed
                            # after the task is removed to avoid interfering with
                            # the single-line progress UI.
                            try:
                                final_msgs.append(value)
                            except Exception:
                                pass

                    # attach the final_msgs list to the callback function so
                    # callers can inspect it after translation finishes.
                    _cb._final_msgs = final_msgs
                    return _cb

                # Attach the final_msgs list to the callback so the outer scope
                # can access messages accumulated during translation.
                # We'll set it after creation below.

                progress_cb = make_progress_callback(file_task, filename)
                # expose final_msgs list from the callback for later printing
                final_msgs = getattr(progress_cb, '_final_msgs', None)
                if final_msgs is None:
                    # if not set yet, try to attach by retrieving closure attribute
                    try:
                        # _cb created inside make_progress_callback; it will have
                        # a final_msgs attribute set below; ensure it's present.
                        progress_cb._final_msgs = []
                        final_msgs = progress_cb._final_msgs
                    except Exception:
                        final_msgs = []

                try:
                    try:
                        # Prefer calling with progress callback when supported
                        translate_srt_file(input_path, output_path, lexicon=lexicon, progress_callback=progress_cb)
                    except TypeError as te:
                        # Some installs may use a compiled extension that doesn't
                        # accept the extra kwarg; fall back to calling without it.
                        msg = str(te)
                        if 'unexpected keyword argument' in msg or 'got an unexpected keyword argument' in msg:
                            translate_srt_file(input_path, output_path, lexicon=lexicon)
                        else:
                            raise
                    # Ensure task reaches full completion if set
                    try:
                        t = progress.tasks[file_task]
                        if t.total and t.completed < t.total:
                            progress.update(file_task, completed=t.total)
                    except Exception:
                        pass
                except Exception as e:
                    # Use style argument instead of markup to avoid issues when
                    # the icon text contains square brackets in ASCII mode.
                    console.print(f"{icon('error')} 处理文件 {filename} 时出错: {e}", style="red")
                finally:
                    # Remove the per-file task so subsequent files replace the
                    # single progress line instead of accumulating.
                    try:
                        # If final messages were collected, show the last one
                        # on the progress line briefly so the spinner/final
                        # info are visible to the user before we remove the
                        # task. Then remove the task and print the final
                        # messages so they persist in the log.
                        msgs = getattr(progress_cb, '_final_msgs', [])
                        if msgs:
                            last = msgs[-1]
                            try:
                                progress.update(file_task, info=last)
                                # small pause to allow the spinner/info to be seen
                                time.sleep(0.7)
                            except Exception:
                                pass
                        progress.remove_task(file_task)
                    except Exception:
                        pass
                    # Print any final messages collected during translation.
                    # We print them after removing the task so they don't
                    # disrupt the single-line progress UI. Use subtle style.
                    try:
                        msgs = getattr(progress_cb, '_final_msgs', [])
                        for m in msgs:
                            # Print without soft wrapping so long paths remain on one line
                            console.print(m, style="italic dim", soft_wrap=False)
                    except Exception:
                        pass
    finally:
        # 最终统计
        show_stats()

    console.print(f"{icon('party')} 所有字幕翻译完成！")


if __name__ == "__main__":
    main()