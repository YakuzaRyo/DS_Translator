import csv
import os

LEXICON_PATH = "./data/lexicon/lexicon.csv"


def ensure_lexicon_exists():
    """If no lexicon file exists, create a small example one."""
    if not os.path.exists(LEXICON_PATH):
        with open(LEXICON_PATH, "w", encoding="utf-8", newline="") as f:
            writer = csv.writer(f)
            writer.writerow(["original", "translation"])
            # Add a few example mappings (users can edit this file)
            writer.writerow(["こんにちは", "你好"])
            writer.writerow(["ありがとう", "谢谢"])


def load_lexicon():
    """Load lexicon CSV into a dict. CSV columns: original,translation"""
    mapping = {}
    if not os.path.exists(LEXICON_PATH):
        return mapping

    with open(LEXICON_PATH, "r", encoding="utf-8") as f:
        reader = csv.DictReader(f)
        for row in reader:
            orig = row.get("original")
            trans = row.get("translation")
            if orig and trans:
                mapping[orig.strip()] = trans.strip()
    return mapping


def get_lexicon_translation(text, lexicon=None):
    """Return translation from lexicon dict if exists, otherwise None."""
    if lexicon is None:
        lexicon = load_lexicon()
    return lexicon.get(text.strip())
