import os
import sys
from utils.registry import register_commond
from pathlib import Path
from ds_translator.rich_progress import shared_console as console
from ds_translator.icons import icon





@register_commond("plugin", "roasted")
def plugin_roasted(source:str,destination:str)->list|None:
    """Find .srt files in the input directory that have not yet been 'roasted' (processed)."""
    input_dir = Path(source)
    output_dir = Path(destination)
    raw_files = [f for f in os.listdir(input_dir) if f.lower().endswith(".srt")]
    if not raw_files:
        console.print(f"{icon('error')} 错误: '{source}' 文件夹中没有 .srt 文件。", style="italic dim")
        sys.exit(0)
    roasted_files = [f.replace("-roasted", "") for f in os.listdir(output_dir) if f.lower().endswith(".srt")]
    pending_files = [f for f in raw_files if f not in roasted_files]

    return pending_files