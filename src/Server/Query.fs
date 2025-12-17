// =============================================================================
// QUERY MODULE (Read Side of CQRS)
// =============================================================================
// This module provides read-only queries against the projected read model.
// These queries are optimized for reading - they hit the SQLite database
// that was built by the Projection module, NOT the event store.
//
// Benefits of this separation:
//   1. Queries can be optimized independently of writes
//   2. No contention between reads and writes
//   3. Can use different data models for reading vs writing
//   4. Easy to rebuild read models by replaying events
// =============================================================================

module ServerQuery

open Microsoft.Data.Sqlite
open Dapper

// Gets the last processed event offset (used for projection resumption)
let getLastOffset (connString: string) : int64 =
    use conn = new SqliteConnection(connString)
    conn.Open()

    conn.QueryFirstOrDefault<int64>(
        "SELECT OffsetCount FROM Offsets WHERE OffsetName = @Name",
        {| Name = "DocumentProjection" |}
    )

// Returns all documents, ordered by most recently updated
let getDocuments (connString: string) : Query.Document list =
    use conn = new SqliteConnection(connString)
    conn.Open()

    conn.Query<Query.Document>(
        "SELECT Id, Title, Body, Version, CreatedAt, UpdatedAt FROM Documents ORDER BY UpdatedAt DESC"
    )
    |> Seq.toList

// Returns version history for a specific document (enables time-travel queries)
let getDocumentHistory (connString: string) (docId: string) : Query.DocumentVersion list =
    use conn = new SqliteConnection(connString)
    conn.Open()

    conn.Query<Query.DocumentVersion>(
        "SELECT Id, Version, Title, Body, CreatedAt FROM DocumentVersions WHERE Id = @Id ORDER BY Version DESC",
        {| Id = docId |}
    )
    |> Seq.toList
