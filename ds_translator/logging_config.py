import logging
import sys
import os
from logging import StreamHandler, FileHandler
from logging import Logger


def init_logging(level=logging.INFO):
    """Initialize a simple global logger for the ds_translator package."""
    logger = logging.getLogger("ds_translator")
    if logger.handlers:
        # already configured
        return logger

    logger.setLevel(level)
    fmt = logging.Formatter("%(asctime)s %(levelname)s [%(name)s] %(message)s", "%Y-%m-%d %H:%M:%S")

    # Optionally add a console handler. Prefer RichHandler (colored/pretty)
    # to resemble uv/uvicorn console output, but fall back to a plain
    # StreamHandler when Rich is not available or DS_CONSOLE_LOG is disabled.
    console_enabled = os.getenv("DS_CONSOLE_LOG", "1").lower() not in ("0", "false", "no")
    if console_enabled:
        try:
            # Rich provides nicer, uv-like colored output. Configure it to show
            # timestamps and level badges similar to uv/uvicorn while keeping
            # tracebacks pretty.
            from rich.logging import RichHandler
            from rich.console import Console

            rich_console = Console()
            # For subtle informational output we prefer not to include the
            # time/level badges for every INFO message; keep tracebacks rich
            # but render plain INFO messages without extra badges so they
            # visually match `subtle()` output.
            rh = RichHandler(
                console=rich_console,
                rich_tracebacks=True,
                show_time=False,
                show_level=False,
                show_path=False,
                markup=True,
            )
            rh.setLevel(level)
            # RichHandler generally handles formatting internally; avoid double
            # formatting by setting a minimal formatter for compatibility.
            rh.setFormatter(logging.Formatter("%(message)s"))
            logger.addHandler(rh)
        except Exception:
            # fallback to a plain console handler with padded level names (uv-style)
            sh = StreamHandler(stream=sys.stdout)
            sh.setLevel(level)
            sh.setFormatter(fmt)
            logger.addHandler(sh)

    # Also prepare a dedicated retry logger that writes to a file. This keeps
    # retry-related noise out of the console while making a persistent record.
    try:
        logs_dir = os.path.join(os.path.dirname(__file__), '..', 'data', 'logs')
        logs_dir = os.path.abspath(logs_dir)
        os.makedirs(logs_dir, exist_ok=True)
        retry_log_path = os.getenv('DS_RETRY_LOG', os.path.join(logs_dir, 'retry.log'))
        retry_logger = logging.getLogger('ds_translator.retry')
        if not retry_logger.handlers:
            fh = FileHandler(retry_log_path, encoding='utf-8')
            fh.setLevel(logging.DEBUG)
            fh.setFormatter(fmt)
            retry_logger.addHandler(fh)
            retry_logger.propagate = False
    except Exception:
        # Never let logging setup crash the application
        logger.exception('无法初始化重试日志文件处理器')

    # avoid propagation to root logger handlers twice
    logger.propagate = False
    return logger
