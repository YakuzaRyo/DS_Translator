from __future__ import annotations

from contextlib import contextmanager
from typing import Optional
import sys
import os

from rich.console import Console
from rich.progress import (
    BarColumn,
    SpinnerColumn,
    TextColumn,
    TimeElapsedColumn,
    Progress,
)

# Shared Console instance for subtle/info printing across the package. Use
# this instead of creating independent Console() instances so output from
# different modules uses the same rendering context and won't disturb live
# Progress rendering.
shared_console = Console()


class SingleLineProgress:
    """A thin wrapper around rich.Progress that keeps output to a single concise line.

    Usage:
        with SingleLineProgress() as p:
            p.start("Loading", total)
            for i in range(total):
                # do work
                p.update(advance=1, info=f"item {i+1}")

    The progress line shows: spinner, description, progress bar, percentage, current info, elapsed time.
    """

    def __init__(self, console: Optional[Console] = None, refresh_per_second: int = 10) -> None:
        # Prefer the shared console to ensure consistent rendering and avoid
        # interleaving between different Console instances.
        self.console = console or shared_console
        # Debug information to help diagnose why live rendering (spinner/progress)
        # may not appear in some runtimes (for example when stdout isn't a TTY
        # or when the environment captures output).
        try:
            is_tty = sys.stdout.isatty()
        except Exception:
            is_tty = False
        # Print debug info to stderr so it doesn't interfere with normal stdout
        # processing of the application. Render it subtly (dim italic) so it
        # doesn't steal focus from the main progress UI.
        try:
            from rich.console import Console as _Console

            _Console(file=sys.stderr).print(
                f"[rich-debug] stdout.isatty={is_tty}, Console.is_terminal={self.console.is_terminal}, "
                f"Console.color_system={self.console.color_system}, TERM={os.environ.get('TERM')}",
                style="italic dim",
            )
        except Exception:
            # If even this small debug print fails, ignore it quietly.
            pass
        # Compose a compact single-line set of columns
        self.progress = Progress(
            # Use a green 'dots' spinner to show the classic green dot spinning
            # effect in terminals that support colors.
            SpinnerColumn(spinner_name="dots", style="green"),
            TextColumn("[bold blue]{task.description}"),
            BarColumn(bar_width=None),
            # PercentageColumn may not exist in all rich versions; use a TextColumn
            # to display percentage which is compatible across versions.
            TextColumn("{task.percentage:>3.0f}%"),
            TextColumn("{task.fields[info]}", justify="right"),
            TimeElapsedColumn(),
            console=self.console,
            transient=False,
            refresh_per_second=refresh_per_second,
        )
        self.task_id: Optional[int] = None

    def __enter__(self) -> "SingleLineProgress":
        self.progress.__enter__()
        return self

    def __exit__(self, exc_type, exc, tb) -> None:
        # ensure progress exits cleanly
        self.progress.__exit__(exc_type, exc, tb)

    def start(self, description: str, total: int) -> int:
        """Start a single task. Returns the internal task id."""
        if self.task_id is not None:
            raise RuntimeError("A task is already started on this progress instance")
        self.task_id = self.progress.add_task(description, total=total, info="")
        return self.task_id

    def update(self, *, advance: int = 0, info: Optional[str] = None, completed: Optional[int] = None) -> None:
        """Update the running task.

        - advance: amount to advance the counter
        - info: short text to display at the right of the line
        - completed: set an absolute completed count
        """
        if self.task_id is None:
            raise RuntimeError("No task started")
        if info is not None:
            self.progress.update(self.task_id, info=info)
        if completed is not None:
            self.progress.update(self.task_id, completed=completed)
        if advance:
            self.progress.advance(self.task_id, advance)


@contextmanager
def run_task(description: str, total: int):
    """Convenience context manager: start a progress instance and yield it.

    Example:
        with run_task('Processing', 10) as p:
            for i in range(10):
                # work
                p.update(advance=1, info=f"{i+1}/10")
    """
    with SingleLineProgress() as p:
        p.start(description, total)
        yield p
