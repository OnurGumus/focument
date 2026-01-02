// =============================================================================
// QUERY MODULE (Read Side of CQRS)
// =============================================================================
// This module provides read-only queries against the projected read model.
// =============================================================================

using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;
using Dapper;

namespace Server;

public static class ServerQuery
{
    // Gets the last processed event offset (used for projection resumption)
    public static long GetLastOffset(string connString)
    {
        using var conn = new SqliteConnection(connString);
        conn.Open();

        return conn.QueryFirstOrDefault<long>(
            "SELECT OffsetCount FROM Offsets WHERE OffsetName = @Name",
            new { Name = "DocumentProjection" });
    }

    // Returns all documents, ordered by most recently updated
    public static List<Query.Document> GetDocuments(string connString)
    {
        using var conn = new SqliteConnection(connString);
        conn.Open();

        return conn.Query<Query.Document>(
            "SELECT Id, Title, Body, Version, CreatedAt, UpdatedAt FROM Documents ORDER BY UpdatedAt DESC")
            .ToList();
    }

    // Returns version history for a specific document (enables time-travel queries)
    public static List<Query.DocumentVersion> GetDocumentHistory(string connString, string docId)
    {
        using var conn = new SqliteConnection(connString);
        conn.Open();

        return conn.Query<Query.DocumentVersion>(
            "SELECT Id, Version, Title, Body, CreatedAt FROM DocumentVersions WHERE Id = @Id ORDER BY Version DESC",
            new { Id = docId })
            .ToList();
    }
}
