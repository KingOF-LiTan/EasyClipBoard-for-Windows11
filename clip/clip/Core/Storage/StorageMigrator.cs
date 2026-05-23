using System;
using System.Threading.Tasks;

namespace clip.Core.Storage;

/// <summary>
/// Schema creation, migration (ALTER TABLE), and index management.
/// </summary>
internal sealed class StorageMigrator
{
    private readonly StorageConnection _conn;

    public StorageMigrator(StorageConnection conn)
    {
        _conn = conn;
    }

    public async Task InitializeDatabaseAsync()
    {
        // Create base table
        await _conn.ExecAsync(@"
            CREATE TABLE IF NOT EXISTS items (
                id              INTEGER PRIMARY KEY AUTOINCREMENT,
                type            INTEGER NOT NULL,
                captured_at     TEXT    NOT NULL,
                is_favorite     INTEGER NOT NULL DEFAULT 0,
                tag             INTEGER NOT NULL DEFAULT 0,
                is_sensitive    INTEGER NOT NULL DEFAULT 0,
                sensitive_type  INTEGER NOT NULL DEFAULT 0,
                text_content    TEXT,
                username        TEXT,
                image_blob      TEXT,
                image_hash      TEXT,
                image_w         INTEGER,
                image_h         INTEGER,
                file_paths      TEXT,
                ocr_text        TEXT
            );");

        // Ensure legacy tables have the required columns
        try { await _conn.ExecAsync("ALTER TABLE items ADD COLUMN tag INTEGER NOT NULL DEFAULT 0;"); } catch { }
        try { await _conn.ExecAsync("ALTER TABLE items ADD COLUMN is_sensitive INTEGER NOT NULL DEFAULT 0;"); } catch { }
        try { await _conn.ExecAsync("ALTER TABLE items ADD COLUMN sensitive_type INTEGER NOT NULL DEFAULT 0;"); } catch { }
        try { await _conn.ExecAsync("ALTER TABLE items ADD COLUMN username TEXT;"); } catch { }
        try { await _conn.ExecAsync("ALTER TABLE items ADD COLUMN alias TEXT;"); } catch { }
        try { await _conn.ExecAsync("ALTER TABLE items ADD COLUMN remark TEXT;"); } catch { }
        try { await _conn.ExecAsync("ALTER TABLE items ADD COLUMN ocr_text TEXT;"); } catch { }

        // Create indexes
        await _conn.ExecAsync("CREATE INDEX IF NOT EXISTS idx_items_captured ON items(captured_at);");
        await _conn.ExecAsync("CREATE INDEX IF NOT EXISTS idx_items_favorite ON items(is_favorite);");
        await _conn.ExecAsync("CREATE INDEX IF NOT EXISTS idx_items_hash ON items(image_hash);");
        await _conn.ExecAsync("CREATE INDEX IF NOT EXISTS idx_items_tag ON items(tag);");
        await _conn.ExecAsync("CREATE INDEX IF NOT EXISTS idx_items_sensitive ON items(is_sensitive);");
    }
}
