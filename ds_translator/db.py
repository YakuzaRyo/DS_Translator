import sqlite3
import os
from datetime import datetime

# Use a stable absolute path for the cache DB
CACHE_DB = os.path.abspath(os.path.join(os.getcwd(), "data", "cache_db", "translation_cache.db"))


def _connect_db():
    """Return an sqlite3.Connection configured to decode TEXT as UTF-8.

    We set conn.text_factory so that bytes stored in the database are decoded
    using UTF-8 with 'replace' on errors. This makes reads robust and avoids
    raising decode errors if the DB contains invalid sequences.
    """
    conn = sqlite3.connect(CACHE_DB)
    # Ensure TEXT columns are decoded as UTF-8 (replace invalid bytes)
    conn.text_factory = lambda b: b.decode('utf-8', 'replace') if isinstance(b, (bytes, bytearray)) else str(b)
    return conn


def init_db():
    """初始化 SQLite 数据库"""
    conn = _connect_db()
    cursor = conn.cursor()
    cursor.execute('''
        CREATE TABLE IF NOT EXISTS translation_cache (
            original TEXT PRIMARY KEY ON CONFLICT REPLACE,
            translation TEXT NOT NULL,
            hit_count INTEGER DEFAULT 1,
            created_at TEXT,
            updated_at TEXT
        )
    ''')
    cursor.execute('CREATE INDEX IF NOT EXISTS idx_original ON translation_cache(original)')
    conn.commit()
    conn.close()


def get_translation_from_db(text):
    conn = _connect_db()
    cursor = conn.cursor()
    cursor.execute(
        'SELECT translation, hit_count FROM translation_cache WHERE original = ?',
        (text,)
    )
    row = cursor.fetchone()
    conn.close()
    if row:
        update_hit_count(text)
        return row[0]
    return None


def update_hit_count(text):
    conn = _connect_db()
    cursor = conn.cursor()
    cursor.execute('''
        UPDATE translation_cache 
        SET hit_count = hit_count + 1, updated_at = ?
        WHERE original = ?
    ''', (datetime.now().isoformat(), text))
    conn.commit()
    conn.close()


def save_translation_to_db(original, translation):
    now = datetime.now().isoformat()
    conn = _connect_db()
    cursor = conn.cursor()
    cursor.execute('''
        INSERT OR REPLACE INTO translation_cache 
        (original, translation, hit_count, created_at, updated_at)
        VALUES (?, ?, COALESCE((SELECT hit_count FROM translation_cache WHERE original = ?), 1), ?, ?)
    ''', (original, translation, original, now, now))
    conn.commit()
    conn.close()


def show_stats():
    conn = _connect_db()
    cursor = conn.cursor()
    cursor.execute('SELECT COUNT(*), SUM(hit_count) FROM translation_cache')
    total, hits = cursor.fetchone()
    conn.close()
    try:
        # Use the shared console for subtle informational output
        from ds_translator.rich_progress import shared_console as console
        console.print(f"[DB] 翻译缓存统计：共 {total} 条翻译，总命中 {hits or 0} 次", style="italic dim")
    except Exception:
        # Fallback to plain print if shared console not available
        print(f"[DB] 翻译缓存统计：共 {total} 条翻译，总命中 {hits or 0} 次")


# ----------------------
# Retry queue support
# ----------------------
def _ensure_retry_table(cursor):
    cursor.execute('''
        CREATE TABLE IF NOT EXISTS retry_queue (
            original TEXT PRIMARY KEY,
            attempts INTEGER DEFAULT 0,
            next_try_at INTEGER,
            last_error TEXT,
            added_at TEXT
        )
    ''')


def enqueue_retry(original, error_text=None):
    """Add text to the persistent retry queue (or update existing entry).

    If the item already exists we do not reset attempts; caller should use
    increment_retry to record extra attempts.
    """
    now_ts = int(datetime.now().timestamp())
    now_iso = datetime.now().isoformat()
    conn = _connect_db()
    cursor = conn.cursor()
    _ensure_retry_table(cursor)
    # insert or ignore; if exists, update last_error
    cursor.execute('''
        INSERT OR REPLACE INTO retry_queue (original, attempts, next_try_at, last_error, added_at)
        VALUES (?, COALESCE((SELECT attempts FROM retry_queue WHERE original = ?), 0), ?, ?, COALESCE((SELECT added_at FROM retry_queue WHERE original = ?), ?))
    ''', (original, original, now_ts, error_text or '', original, now_iso))
    conn.commit()
    conn.close()


def get_due_retries(limit=10):
    """Return a list of dicts for retry items whose next_try_at <= now.

    Each dict contains: original, attempts, next_try_at, last_error, added_at
    """
    now_ts = int(datetime.now().timestamp())
    conn = _connect_db()
    cursor = conn.cursor()
    _ensure_retry_table(cursor)
    cursor.execute('''
        SELECT original, attempts, next_try_at, last_error, added_at
        FROM retry_queue
        WHERE next_try_at IS NULL OR next_try_at <= ?
        ORDER BY next_try_at ASC
        LIMIT ?
    ''', (now_ts, limit))
    rows = cursor.fetchall()
    conn.close()
    out = []
    for r in rows:
        out.append({
            'original': r[0],
            'attempts': r[1] or 0,
            'next_try_at': r[2],
            'last_error': r[3],
            'added_at': r[4]
        })
    return out


def increment_retry(original, error_text=None, backoff_seconds=None):
    """Increase attempts count and set next_try_at using exponential backoff.

    If backoff_seconds is provided, use it; otherwise compute 2 ** attempts (capped).
    """
    conn = _connect_db()
    cursor = conn.cursor()
    _ensure_retry_table(cursor)
    cursor.execute('SELECT attempts FROM retry_queue WHERE original = ?', (original,))
    row = cursor.fetchone()
    attempts = (row[0] if row else 0) + 1
    # compute backoff (cap to 1 hour)
    if backoff_seconds is None:
        backoff = min(2 ** attempts, 3600)
    else:
        backoff = backoff_seconds
    next_try = int(datetime.now().timestamp()) + backoff
    cursor.execute('''
        INSERT OR REPLACE INTO retry_queue (original, attempts, next_try_at, last_error, added_at)
        VALUES (?, ?, ?, ?, COALESCE((SELECT added_at FROM retry_queue WHERE original = ?), ?))
    ''', (original, attempts, next_try, error_text or '', original, datetime.now().isoformat()))
    conn.commit()
    conn.close()


def remove_retry(original):
    conn = _connect_db()
    cursor = conn.cursor()
    _ensure_retry_table(cursor)
    cursor.execute('DELETE FROM retry_queue WHERE original = ?', (original,))
    conn.commit()
    conn.close()
