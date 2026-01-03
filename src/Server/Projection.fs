// =============================================================================
// PROJECTION (Read Model Builder)
// =============================================================================
// This module implements the QUERY side of CQRS by projecting events into
// a read-optimized SQLite database.
//
// How it works:
//   1. Events are read from the event store (Akka.Persistence journal)
//   2. Each event is processed by handleEventWrapper
//   3. The read model (SQLite) is updated to reflect the event
//   4. Offset tracking ensures exactly-once processing
//
// This creates "eventual consistency" - the read model may lag slightly
// behind the write model, but will eventually catch up.
// =============================================================================

module Projection

open System
open Microsoft.Extensions.Logging
open Microsoft.Data.Sqlite
open Dapper
open FCQRS.Common
open FCQRS.Model.Data
open Model.Command
open Model.Command.Document

// -----------------------------------------------------------------------------
// SCHEMA INITIALIZATION
// -----------------------------------------------------------------------------
// Creates the read model tables if they don't exist.
// This is idempotent - safe to call on every startup.
// -----------------------------------------------------------------------------
let ensureTables (connString: string) =
    use conn = new SqliteConnection(connString)
    conn.Open()

    // SQLite performance optimizations for concurrent read/write
    conn.Execute("PRAGMA journal_mode=WAL") |> ignore
    conn.Execute("PRAGMA synchronous=NORMAL") |> ignore
    conn.Execute("PRAGMA busy_timeout=5000") |> ignore
    conn.Execute("PRAGMA cache_size=10000") |> ignore

    conn.Execute(
        """
        create table if not exists Documents (
            Id text primary key,
            Title text not null,
            Body text not null,
            Version integer not null,
            CreatedAt text not null,
            UpdatedAt text not null,
            ApprovalStatus text not null default 'Pending'
        )
    """
    )
    |> ignore

    // Migration: Add ApprovalStatus column if it doesn't exist
    try
        conn.Execute("ALTER TABLE Documents ADD COLUMN ApprovalStatus TEXT NOT NULL DEFAULT 'Pending'") |> ignore
    with :? SqliteException -> () // Column already exists

    conn.Execute(
        """
        create table if not exists Offsets (
            OffsetName text primary key,
            OffsetCount integer not null
        )
    """
    )
    |> ignore

    conn.Execute(
        """
        insert or ignore into Offsets (OffsetName, OffsetCount) values ('DocumentProjection', 0)
    """
    )
    |> ignore

    conn.Execute(
        """
        create table if not exists DocumentVersions (
            Id text not null,
            Version integer not null,
            Title text not null,
            Body text not null,
            CreatedAt text not null,
            primary key (Id, Version)
        )
    """
    )
    |> ignore

// -----------------------------------------------------------------------------
// EVENT HANDLER (Projection Logic)
// -----------------------------------------------------------------------------
// This function is called for each event from the event store.
// It transforms domain events into read model updates.
//
// Parameters:
//   - offsetValue: Sequential position in the event stream (for resumption)
//   - event: The domain event to process
//
// The offset is stored atomically with the projection update to ensure
// exactly-once semantics even if the process crashes mid-projection.
// -----------------------------------------------------------------------------
let handleEventWrapper (loggerFactory: ILoggerFactory) (connString: string) (offsetValue: int64) (event: obj) =
    let log = loggerFactory.CreateLogger "Projection"
    log.LogInformation("Event: {Event} Offset: {Offset}", event.ToString(), offsetValue)

    try
        use conn = new SqliteConnection(connString)
        conn.Open()
        use transaction = conn.BeginTransaction()

        let dataEvent =
            match event with
            | :? Event<Event> as docEvent ->
                let eventTime = docEvent.CreationDate.ToString("o")

                match docEvent.EventDetails with
                | CreatedOrUpdated doc ->
                    let docId = doc.Id.ToString()
                    let title = doc.Title.ToString()
                    let content = doc.Content.ToString()

                    // Get next document version (only counts CreatedOrUpdated events)
                    let maxVersion =
                        conn.QueryFirstOrDefault<Nullable<int64>>(
                            "select max(Version) from DocumentVersions where Id = @Id",
                            {| Id = docId |},
                            transaction
                        )
                    let docVersion = if maxVersion.HasValue then maxVersion.Value + 1L else 1L

                    let existing =
                        conn.QueryFirstOrDefault<string>(
                            "select Id from Documents where Id = @Id",
                            {| Id = docId |},
                            transaction
                        )

                    if isNull existing then
                        conn.Execute(
                            """insert into Documents (Id, Title, Body, Version, CreatedAt, UpdatedAt, ApprovalStatus)
                               values (@Id, @Title, @Body, @Version, @CreatedAt, @UpdatedAt, 'Pending')""",
                            {|
                                Id = docId
                                Title = title
                                Body = content
                                Version = docVersion
                                CreatedAt = eventTime
                                UpdatedAt = eventTime
                            |},
                            transaction
                        )
                        |> ignore
                    else
                        conn.Execute(
                            """update Documents
                               set Title = @Title, Body = @Body, Version = @Version, UpdatedAt = @UpdatedAt, ApprovalStatus = 'Pending'
                               where Id = @Id""",
                            {|
                                Id = docId
                                Title = title
                                Body = content
                                Version = docVersion
                                UpdatedAt = eventTime
                            |},
                            transaction
                        )
                        |> ignore

                    // Store version history
                    conn.Execute(
                        """insert or ignore into DocumentVersions (Id, Version, Title, Body, CreatedAt)
                           values (@Id, @Version, @Title, @Body, @CreatedAt)""",
                        {|
                            Id = docId
                            Version = docVersion
                            Title = title
                            Body = content
                            CreatedAt = eventTime
                        |},
                        transaction
                    )
                    |> ignore

                    [ docEvent :> IMessageWithCID ]

                // Saga events - update ApprovalStatus
                | ApprovalCodeSet _ -> [ docEvent :> IMessageWithCID ]
                | Approved docId ->
                    conn.Execute(
                        "update Documents set ApprovalStatus = 'Approved', UpdatedAt = @UpdatedAt where Id = @Id",
                        {| Id = (ValueLens.Value docId).ToString(); UpdatedAt = eventTime |},
                        transaction
                    ) |> ignore
                    [ docEvent :> IMessageWithCID ]
                | Rejected docId ->
                    conn.Execute(
                        "update Documents set ApprovalStatus = 'Rejected', UpdatedAt = @UpdatedAt where Id = @Id",
                        {| Id = (ValueLens.Value docId).ToString(); UpdatedAt = eventTime |},
                        transaction
                    ) |> ignore
                    [ docEvent :> IMessageWithCID ]

                | Error _ -> []

            | _ -> []

        conn.Execute(
            "update Offsets set OffsetCount = @Offset where OffsetName = @Name",
            {|
                Offset = offsetValue
                Name = "DocumentProjection"
            |},
            transaction
        )
        |> ignore

        transaction.Commit()
        dataEvent

    with ex ->
        log.LogError(
            ex,
            "Projection failed for event at offset {Offset}: {EventType}",
            offsetValue,
            event.GetType().Name
        )

        reraise ()
