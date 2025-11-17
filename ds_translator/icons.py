from __future__ import annotations

"""Terminal icon helper.

Provides a small mapping of semantic icon names to either emoji or
ASCII-friendly replacements. Emoji usage can be enabled by setting
the environment variable ``DS_USE_EMOJI=1`` (or any of: true, yes).

On Windows, emoji are disabled by default unless the env var is set,
because many Windows terminals render emoji poorly.
"""
import os
import sys

_env_val = os.getenv("DS_USE_EMOJI")
if _env_val is not None:
    _USE_EMOJI = str(_env_val).lower() in ("1", "true", "yes")
else:
    # Default: enable on non-Windows, disable on Windows
    _USE_EMOJI = sys.platform != "win32"


_ICONS_EMOJI = {
    "info": "ℹ️",
    "success": "✅",
    "error": "❌",
    "warn": "⚠️",
    "search": "🔍",
    "new": "🆕",
    "folder": "📁",
    "done": "✔️",
    "party": "🎉",
}

_ICONS_ASCII = {
    "info": "[i]",
    "success": "[OK]",
    "error": "[X]",
    "warn": "[!]",
    "search": "[?]",
    "new": "[NEW]",
    "folder": "[DIR]",
    "done": "[OK]",
    "party": "[DONE]",
}


def icon(name: str) -> str:
    """Return a short icon string for the given semantic name.

    If emoji are enabled, an emoji glyph is returned; otherwise an
    ASCII-friendly replacement is used.
    """
    if _USE_EMOJI:
        return _ICONS_EMOJI.get(name, _ICONS_ASCII.get(name, ""))
    return _ICONS_ASCII.get(name, _ICONS_EMOJI.get(name, ""))
