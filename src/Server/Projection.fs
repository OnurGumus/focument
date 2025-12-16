module Projection

open System
open Microsoft.Extensions.Logging
open Microsoft.Data.Sqlite
open Dapper
open FCQRS.Common
open FCQRS.Model.Data
open Model.Command
open Model.Command.Document

let ensureTables (connString: string) =
    use conn = new SqliteConnection(connString)
    conn.Open()

    conn.Execute("""
        CREATE TABLE IF NOT EXISTS Documents (
            Id TEXT PRIMARY KEY,
            Title TEXT NOT NULL,
            Body TEXT NOT NULL,
            Version INTEGER NOT NULL,
            CreatedAt TEXT NOT NULL,
            UpdatedAt TEXT NOT NULL
        )
    """) |> ignore

    conn.Execute("""
        CREATE TABLE IF NOT EXISTS Offsets (
            OffsetName TEXT PRIMARY KEY,
            OffsetCount INTEGER NOT NULL
        )
    """) |> ignore

    conn.Execute("""
        INSERT OR IGNORE INTO Offsets (OffsetName, OffsetCount) VALUES ('DocumentProjection', 0)
    """) |> ignore

    conn.Execute("""
        CREATE TABLE IF NOT EXISTS DocumentVersions (
            Id TEXT NOT NULL,
            Version INTEGER NOT NULL,
            Title TEXT NOT NULL,
            Body TEXT NOT NULL,
            CreatedAt TEXT NOT NULL,
            PRIMARY KEY (Id, Version)
        )
    """) |> ignore

let getLastOffset (connString: string) : int64 =
    use conn = new SqliteConnection(connString)
    conn.Open()
    let result = conn.QueryFirstOrDefault<int64>(
        "SELECT OffsetCount FROM Offsets WHERE OffsetName = @Name",
        {| Name = "DocumentProjection" |})
    result

let getDocuments (connString: string) : Query.Document list =
    use conn = new SqliteConnection(connString)
    conn.Open()
    conn.Query<Query.Document>("SELECT Id, Title, Body, Version, CreatedAt, UpdatedAt FROM Documents ORDER BY UpdatedAt DESC")
    |> Seq.toList

let getDocumentHistory (connString: string) (docId: string) : Query.DocumentVersion list =
    use conn = new SqliteConnection(connString)
    conn.Open()
    conn.Query<Query.DocumentVersion>(
        "SELECT Id, Version, Title, Body, CreatedAt FROM DocumentVersions WHERE Id = @Id ORDER BY Version DESC",
        {| Id = docId |})
    |> Seq.toList

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
                let version = docEvent.Version |> ValueLens.Value
                let eventTime = docEvent.CreationDate.ToString("o")

                match docEvent.EventDetails with
                | CreatedOrUpdated doc ->
                    let docId = doc.Id.ToString()
                    let title = doc.Title.ToString()
                    let content = doc.Content.ToString()

                    let existing = conn.QueryFirstOrDefault<string>(
                        "SELECT Id FROM Documents WHERE Id = @Id",
                        {| Id = docId |},
                        transaction)

                    if isNull existing then
                        conn.Execute(
                            """INSERT INTO Documents (Id, Title, Body, Version, CreatedAt, UpdatedAt)
                               VALUES (@Id, @Title, @Body, @Version, @CreatedAt, @UpdatedAt)""",
                            {| Id = docId
                               Title = title
                               Body = content
                               Version = version
                               CreatedAt = eventTime
                               UpdatedAt = eventTime |},
                            transaction) |> ignore
                    else
                        conn.Execute(
                            """UPDATE Documents
                               SET Title = @Title, Body = @Body, Version = @Version, UpdatedAt = @UpdatedAt
                               WHERE Id = @Id""",
                            {| Id = docId
                               Title = title
                               Body = content
                               Version = version
                               UpdatedAt = eventTime |},
                            transaction) |> ignore

                    // Store version history
                    conn.Execute(
                        """INSERT OR IGNORE INTO DocumentVersions (Id, Version, Title, Body, CreatedAt)
                           VALUES (@Id, @Version, @Title, @Body, @CreatedAt)""",
                        {| Id = docId
                           Version = version
                           Title = title
                           Body = content
                           CreatedAt = eventTime |},
                        transaction) |> ignore

                    [ docEvent :> IMessageWithCID ]

                | Error _ -> []

            | _ -> []

        conn.Execute(
            "UPDATE Offsets SET OffsetCount = @Offset WHERE OffsetName = @Name",
            {| Offset = offsetValue; Name = "DocumentProjection" |},
            transaction) |> ignore

        transaction.Commit()
        dataEvent

    with ex ->
        log.LogError(ex, "Projection failed for event at offset {Offset}: {EventType}", offsetValue, event.GetType().Name)
        reraise()
