// =============================================================================
// PROJECTION (Read Model Builder)
// =============================================================================
// This module implements the QUERY side of CQRS by projecting events into
// a read-optimized SQLite database.
// =============================================================================

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Data.Sqlite;
using Dapper;
using Model;
using IMessageWithCID = FCQRS.Model.Data.IMessageWithCID;

namespace Server;

public static class Projection
{
    // -----------------------------------------------------------------------------
    // SCHEMA INITIALIZATION
    // -----------------------------------------------------------------------------
    public static void EnsureTables(string connString)
    {
        using var conn = new SqliteConnection(connString);
        conn.Open();

        conn.Execute("""
            CREATE TABLE IF NOT EXISTS Documents (
                Id TEXT PRIMARY KEY,
                Title TEXT NOT NULL,
                Body TEXT NOT NULL,
                Version INTEGER NOT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            )
            """);

        conn.Execute("""
            CREATE TABLE IF NOT EXISTS Offsets (
                OffsetName TEXT PRIMARY KEY,
                OffsetCount INTEGER NOT NULL
            )
            """);

        conn.Execute("""
            INSERT OR IGNORE INTO Offsets (OffsetName, OffsetCount) VALUES ('DocumentProjection', 0)
            """);

        conn.Execute("""
            CREATE TABLE IF NOT EXISTS DocumentVersions (
                Id TEXT NOT NULL,
                Version INTEGER NOT NULL,
                Title TEXT NOT NULL,
                Body TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                PRIMARY KEY (Id, Version)
            )
            """);
    }

    // -----------------------------------------------------------------------------
    // EVENT HANDLER (Projection Logic)
    // -----------------------------------------------------------------------------
    public static IList<IMessageWithCID> HandleEventWrapper(
        ILoggerFactory loggerFactory,
        string connString,
        long offsetValue,
        object eventObj)
    {
        var log = loggerFactory.CreateLogger("Projection");
        log.LogInformation("Event: {Event} Offset: {Offset}", eventObj, offsetValue);

        try
        {
            using var conn = new SqliteConnection(connString);
            conn.Open();
            using var transaction = conn.BeginTransaction();

            var dataEvents = new List<IMessageWithCID>();

            // Try to extract document event from various wrappers
            FCQRS.Common.Event<DocumentEvent>? docEvent = null;

            if (eventObj is FCQRS.Common.Event<DocumentEvent> directEvent)
            {
                docEvent = directEvent;
            }
            // Only process aggregate events - saga events are for recovery only

            if (docEvent != null)
            {
                // Get the version value from the F# Version type
                var version = FCQRS.Model.Data.Version.Value_.Item1.Invoke(docEvent.Version);
                var eventTime = docEvent.CreationDate.ToString("o");

                if (docEvent.EventDetails is DocumentEvent.CreatedOrUpdated created)
                {
                    var doc = created.Document;
                    var docId = doc.Id.ToString();
                    var title = doc.Title.ToString();
                    var content = doc.Content.ToString();

                    var existing = conn.QueryFirstOrDefault<string>(
                        "SELECT Id FROM Documents WHERE Id = @Id",
                        new { Id = docId },
                        transaction);

                    if (existing is null)
                    {
                        conn.Execute(
                            """
                            INSERT INTO Documents (Id, Title, Body, Version, CreatedAt, UpdatedAt)
                            VALUES (@Id, @Title, @Body, @Version, @CreatedAt, @UpdatedAt)
                            """,
                            new
                            {
                                Id = docId,
                                Title = title,
                                Body = content,
                                Version = version,
                                CreatedAt = eventTime,
                                UpdatedAt = eventTime
                            },
                            transaction);
                    }
                    else
                    {
                        conn.Execute(
                            """
                            UPDATE Documents
                            SET Title = @Title, Body = @Body, Version = @Version, UpdatedAt = @UpdatedAt
                            WHERE Id = @Id
                            """,
                            new
                            {
                                Id = docId,
                                Title = title,
                                Body = content,
                                Version = version,
                                UpdatedAt = eventTime
                            },
                            transaction);
                    }

                    // Store version history
                    conn.Execute(
                        """
                        INSERT OR IGNORE INTO DocumentVersions (Id, Version, Title, Body, CreatedAt)
                        VALUES (@Id, @Version, @Title, @Body, @CreatedAt)
                        """,
                        new
                        {
                            Id = docId,
                            Version = version,
                            Title = title,
                            Body = content,
                            CreatedAt = eventTime
                        },
                        transaction);

                    dataEvents.Add(docEvent);
                }
            }

            conn.Execute(
                "UPDATE Offsets SET OffsetCount = @Offset WHERE OffsetName = @Name",
                new { Offset = offsetValue, Name = "DocumentProjection" },
                transaction);

            transaction.Commit();
            return dataEvents;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Projection failed for event at offset {Offset}: {EventType}",
                offsetValue, eventObj.GetType().Name);
            throw;
        }
    }
}
