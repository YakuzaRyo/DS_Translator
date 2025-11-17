import os
import time
import threading
import concurrent.futures
import requests
import logging
from ds_translator import db as db
from ds_translator import lexicon as lex
from ds_translator.logging_config import init_logging

# initialize package logger
logger = init_logging()
# dedicated retry logger (writes to file via logging_config)
retry_logger = logging.getLogger("ds_translator.retry")

# Load configuration from env (expect load_dotenv to be called by caller/main)
API_KEY = os.getenv("deepseek_api_key")
API_BASE = os.getenv("deepseek_api_url", "https://api.deepseek.com/")
MODEL = os.getenv("deepseek_model", "deepseek-chat")

HEADERS = {
    "Authorization": f"Bearer {API_KEY}" if API_KEY else "",
    "Content-Type": "application/json"
}

# Retry worker configuration (env-driven)
# seconds to wait between worker requests (throttle)
RETRY_REQUEST_INTERVAL_SECONDS = float(os.getenv("RETRY_REQUEST_INTERVAL_SECONDS", "1.0"))
# maximum concurrent worker threads (reserved for future/concurrency expansion)
RETRY_MAX_CONCURRENCY = int(os.getenv("RETRY_MAX_CONCURRENCY", "1"))
# if RETRY_MAX_ATTEMPTS > 0, items will be dropped after that many attempts; 0 means infinite attempts
try:
    RETRY_MAX_ATTEMPTS = int(os.getenv("RETRY_MAX_ATTEMPTS", "0"))
except Exception:
    RETRY_MAX_ATTEMPTS = 0

# internal worker handle
_retry_worker_thread = None
_retry_worker_lock = threading.Lock()
# Executor and futures tracking for concurrent retries
_retry_executor = None
_retry_futures = set()


def _mask_auth_header(headers: dict) -> dict:
    """Return a copy of headers where the Authorization token is partially masked for safe logging."""
    h = headers.copy()
    auth = h.get("Authorization")
    if auth:
        # auth looks like: 'Bearer sk-...'
        try:
            prefix, token = auth.split(" ", 1)
            if len(token) > 8:
                token = token[:8] + "..."
            h["Authorization"] = f"{prefix} {token}"
        except Exception:
            h["Authorization"] = "<masked>"
    return h


def translate_text(text, retry=40, lexicon=None, max_chars=None, context=None):
    """Translate text using lexicon -> DB cache -> external API.

    Returns the translated string. On failure returns "[翻译失败]" and caches it.
    """
    text = text.strip()
    if not text:
        return ""

    # 0. Lexicon (user editable) exact match
    if lexicon is None:
        lexicon = lex.load_lexicon()
    lex_trans = lex.get_lexicon_translation(text, lexicon)
    if lex_trans is not None:
        return lex_trans

    # 1. DB cache
    cached = db.get_translation_from_db(text)
    if cached is not None:
        return cached

    # 2. External API
    # If a lexicon dict is provided, include a truncated formatted mapping in the system prompt so the model
    # preferentially uses those translations. Keep the lexicon chunk size limited to avoid overly long prompts.
    def _format_lexicon_for_prompt(lexicon_dict, max_chars=1500):
        if not lexicon_dict:
            return ""
        # create lines like: original -> translation
        lines = []
        for k, v in lexicon_dict.items():
            lines.append(f"{k} -> {v}")
        s = "\n".join(lines)
        if len(s) <= max_chars:
            return s
        # otherwise truncate by entries until under limit
        out = []
        total = 0
        for k, v in lexicon_dict.items():
            line = f"{k} -> {v}\n"
            if total + len(line) > max_chars:
                break
            out.append(line.strip())
            total += len(line)
        return "\n".join(out)

    # Determine the maximum chars to send from env or passed-in parameter
    try:
        env_max = int(os.getenv("LEXICON_MAX_CHARS", "1500"))
    except Exception:
        env_max = 1500
    if max_chars is None:
        max_chars = env_max

    base_system = """你是一位资深字幕翻译员，正在为一档日本女声优（2~3人的）综艺节目制作中文字幕。请将以下日语对话翻译成**生动、口语化、符合中文观众习惯**的字幕，要求：
            - 保留说话人的性格特征（如元气、傲娇、毒舌等）
            - 语气词要转化为中文等效表达（如「ね」→“嘛”、“对吧”；「わ」→“哦”、“啦”）
            - 可适当使用网络流行语或综艺常用语（如“绝了”“上头”“破防”），但不要过度
            - 使用中文全角标点，感叹号/问号可重复（！！？？）表达情绪
            - 不要解释，只输出译文
            - 不要添加额外说明"""  
    lex_prompt = _format_lexicon_for_prompt(lexicon, max_chars=max_chars)
    if lex_prompt:
        system_content = base_system + "\n\n优先使用下列词典映射（若存在完全匹配，请直接使用对应翻译）：\n" + lex_prompt
    else:
        system_content = base_system

    # If a context string is provided (neighboring subtitle lines), include it as an extra user message
    # that clearly marks which line should be translated. This is optional and backward-compatible.
    def _format_context_for_prompt(ctx, max_chars=1500):
        if not ctx:
            return ""
        s = ctx if isinstance(ctx, str) else str(ctx)
        if len(s) <= max_chars:
            return s
        return s[-max_chars:]

    messages = [{"role": "system", "content": system_content}]
    if context:
        ctx = _format_context_for_prompt(context, max_chars=max_chars)
        # instruct model to only translate the line marked as [NOW]
        messages.append({"role": "user", "content": "上下文（仅供参考）：\n" + ctx + "\n\n请只翻译标记为 [NOW] 的那一行，且仅输出译文。"})

    messages.append({"role": "user", "content": text})

    payload = {
        "model": MODEL,
        "messages": messages,
        "temperature": 0.1,
        "max_tokens": 200
    }
    last_error = None
    for attempt in range(retry):
        try:
            # log the outgoing request headers (mask token for safety)
            logger.debug("API request headers: %s", _mask_auth_header(HEADERS))
            response = requests.post(f"{API_BASE}/chat/completions", headers=HEADERS, json=payload, timeout=30)
            if response.status_code == 200:
                result = response.json()
                translated = result["choices"][0]["message"]["content"].strip()
                db.save_translation_to_db(text, translated)
                return translated
            elif response.status_code == 429:
                wait = min(2 ** attempt, 3600)
                last_error = f"429 Too Many Requests"
                logger.warning("请求过于频繁，等待 %s 秒后重试...", wait)
                time.sleep(wait)
            else:
                last_error = f"API 错误 [{response.status_code}]: {response.text}"
                # Log to console logger and also write to the persistent retry log file
                logger.error(last_error)
                try:
                    retry_logger.error(last_error)
                except Exception:
                    # keep original logging behavior even if retry logger fails
                    logger.exception("无法将 API 错误写入重试日志: %s", last_error)
                # non-retriable HTTP error -> break and enqueue for later retry
                break
        except Exception as e:
            last_error = str(e)
            logger.exception("连接异常: %s", e)
            # also persist connection exceptions to retry log
            try:
                retry_logger.exception("连接异常: %s", e)
            except Exception:
                logger.exception("无法将连接异常写入重试日志: %s", e)
            # short sleep before retrying
            time.sleep(2)

    # if we reach here, the immediate attempts failed. Instead of saving a permanent
    # "[翻译失败]" marker into the cache, enqueue for persistent background retries.
    try:
        db.enqueue_retry(text, error_text=last_error)
        retry_logger.info("已将文本加入重试队列（持久化）：%s", text)
    except Exception as e:
        # write enqueue failures to retry log file as well
        retry_logger.exception("加入重试队列失败: %s", e)

    return "[翻译失败]"


def _attempt_translate_once(text, lexicon=None, max_chars=None, context=None):
    """Attempt a single immediate API translation (no local DB checks).

    Returns (success: bool, translated_or_error: str)
    """
    # Prepare a payload similar to translate_text but minimal: reuse lexicon prompt
    base_system = "你是一位资深动漫字幕翻译员，正在为一档日本女声优综艺节目制作中文字幕，请将日语准确翻译为简体中文。只返回翻译结果，不要添加额外说明。"
    lex_prompt = ""
    if lexicon:
        # reuse small formatter from translate_text
        try:
            env_max = int(os.getenv("LEXICON_MAX_CHARS", "1500"))
        except Exception:
            env_max = 1500
        max_chars = max_chars or env_max
        # build small mapping
        lines = [f"{k} -> {v}" for k, v in lexicon.items()]
        s = "\n".join(lines)
        lex_prompt = s[:max_chars]
    if lex_prompt:
        system_content = base_system + "\n\n优先使用下列词典映射（若存在完全匹配，请直接使用对应翻译）：\n" + lex_prompt
    else:
        system_content = base_system

    messages = [{"role": "system", "content": system_content}]
    if context:
        try:
            env_max = int(os.getenv("LEXICON_MAX_CHARS", "1500"))
        except Exception:
            env_max = 1500
        ctx = context if isinstance(context, str) else str(context)
        if len(ctx) > (max_chars or env_max):
            ctx = ctx[-(max_chars or env_max):]
        messages.append({"role": "user", "content": "上下文（仅供参考）：\n" + ctx + "\n\n请只翻译标记为 [NOW] 的那一行，且仅输出译文。"})

    messages.append({"role": "user", "content": text})

    payload = {
        "model": MODEL,
        "messages": messages,
        "temperature": 0.1,
        "max_tokens": 200
    }

    try:
        logger.debug("Retry worker calling API headers: %s", _mask_auth_header(HEADERS))
        response = requests.post(f"{API_BASE}/chat/completions", headers=HEADERS, json=payload, timeout=30)
        if response.status_code == 200:
            result = response.json()
            translated = result["choices"][0]["message"]["content"].strip()
            return True, translated
        else:
            return False, f"HTTP {response.status_code}: {response.text}"
    except Exception as e:
        return False, str(e)


def _retry_worker_loop():
    """Background loop that processes due retry items from DB.

    It respects RETRY_REQUEST_INTERVAL_SECONDS between attempts and uses exponential
    backoff by updating the retry row via db.increment_retry().
    """
    retry_logger.info("重试工作线程已启动 (间隔 %.2fs, max_attempts=%s, concurrency=%s)", RETRY_REQUEST_INTERVAL_SECONDS, RETRY_MAX_ATTEMPTS or "∞", RETRY_MAX_CONCURRENCY)

    def _process_item(item):
        try:
            original = item["original"] if isinstance(item, dict) else item[0]
            attempts = item.get("attempts", 0) if isinstance(item, dict) else 0

            if RETRY_MAX_ATTEMPTS > 0 and attempts >= RETRY_MAX_ATTEMPTS:
                retry_logger.warning("重试次数已达上限，放弃: %s", original)
                db.save_translation_to_db(original, "[翻译失败]")
                db.remove_retry(original)
                return

            success, result = _attempt_translate_once(original, lexicon=None)
            if success:
                retry_logger.info("重试成功，保存翻译：%s", original)
                db.save_translation_to_db(original, result)
                db.remove_retry(original)
            else:
                retry_logger.info("重试失败（将安排下一次尝试）：%s -> %s", original, result)
                db.increment_retry(original, error_text=result)

        except Exception as e:
            retry_logger.exception("处理重试项时出错: %s", e)

    global _retry_executor, _retry_futures
    # Create executor if not present
    if _retry_executor is None:
        # ThreadPoolExecutor threads are non-daemon by default; that's acceptable for long-running apps.
        _retry_executor = concurrent.futures.ThreadPoolExecutor(max_workers=max(1, RETRY_MAX_CONCURRENCY), thread_name_prefix="ds_retry")
        _retry_futures = set()

    while True:
        try:
            items = db.get_due_retries(limit=50)
            if not items:
                time.sleep(RETRY_REQUEST_INTERVAL_SECONDS)
                continue

            # prune completed futures
            _retry_futures = {f for f in _retry_futures if not f.done()}

            for it in items:
                # if we've saturated concurrency, wait a bit and re-check
                if len([f for f in _retry_futures if not f.done()]) >= max(1, RETRY_MAX_CONCURRENCY):
                    break

                fut = _retry_executor.submit(_process_item, it)
                _retry_futures.add(fut)
                # throttle submission rate to avoid bursts
                time.sleep(RETRY_REQUEST_INTERVAL_SECONDS)

            # small sleep before next fetch cycle
            time.sleep(RETRY_REQUEST_INTERVAL_SECONDS)

        except Exception as e:
            logger.exception("重试工作线程异常: %s", e)
            time.sleep(RETRY_REQUEST_INTERVAL_SECONDS)


def start_retry_worker():
    """Start the background retry worker thread (idempotent).

    Call this once from application startup (e.g. in main).
    """
    global _retry_worker_thread
    with _retry_worker_lock:
        if _retry_worker_thread is None or not _retry_worker_thread.is_alive():
            _retry_worker_thread = threading.Thread(target=_retry_worker_loop, daemon=True, name="ds_translator_retry")
            _retry_worker_thread.start()
