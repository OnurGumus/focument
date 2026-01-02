// =============================================================================
// QUERY MODEL (Read Model DTOs)
// =============================================================================
// These types represent the READ side of CQRS - simple DTOs optimized for
// querying and display. They map directly to SQLite table rows.
//
// Key differences from Command model:
//   - No validation logic (data is already validated when written)
//   - No behavior (pure data containers)
//   - Uses primitive types (string, int64) for easy serialization
// =============================================================================

namespace Query;

/// <summary>
/// Document as stored in the read model (current state)
/// </summary>
public class Document
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public long Version { get; set; }
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
}

/// <summary>
/// Historical version of a document (for time-travel/audit)
/// </summary>
public class DocumentVersion
{
    public string Id { get; set; } = "";
    public long Version { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string CreatedAt { get; set; } = "";
}
