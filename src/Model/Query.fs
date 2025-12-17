// =============================================================================
// QUERY MODEL (Read Model DTOs)
// =============================================================================
// These types represent the READ side of CQRS - simple DTOs optimized for
// querying and display. They map directly to SQLite table rows.
//
// Key differences from Command model:
//   - No validation logic (data is already validated when written)
//   - No behavior (pure data containers)
//   - CLIMutable attribute enables Dapper to hydrate objects
//   - Uses primitive types (string, int64) for easy serialization
// =============================================================================

module Query

// Document as stored in the read model (current state)
[<CLIMutable>]
type Document = {
    Id: string
    Title: string
    Body: string
    Version: int64
    CreatedAt: string
    UpdatedAt: string
}

// Historical version of a document (for time-travel/audit)
[<CLIMutable>]
type DocumentVersion = {
    Id: string
    Version: int64
    Title: string
    Body: string
    CreatedAt: string
}
