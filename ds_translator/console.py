from __future__ import annotations

from typing import Any
from ds_translator.rich_progress import shared_console


def subtle(message: Any, **print_kwargs) -> None:
    """Print a subtle informational message to the shared console.

    By default this prints with style "italic dim". Additional keyword
    arguments are forwarded to Rich's Console.print.
    """
    style = print_kwargs.pop("style", "italic dim")
    try:
        shared_console.print(message, style=style, **print_kwargs)
    except Exception:
        # If printing via rich fails for any reason, fall back to built-in
        # print to avoid crashing the application.
        built_in = __import__("builtins").print
        built_in(str(message))
