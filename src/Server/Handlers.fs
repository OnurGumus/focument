// =============================================================================
// HTTP HANDLERS (API Layer)
// =============================================================================
// This module contains the HTTP handlers that bridge the web API to the
// CQRS/ES domain. Handlers either:
//   - Query the read model (simple database queries)
//   - Send commands to actors and await events (write operations)
//
// Pattern for write operations:
//   1. Parse and validate HTTP request
//   2. Create domain objects using smart constructors
//   3. Generate correlation ID for tracking
//   4. Subscribe to events with that correlation ID
//   5. Send command to actor
//   6. Await the resulting event (confirms persistence)
//   7. Return response to client
// =============================================================================

module Handlers

open System
open System.Diagnostics
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Logging
open FCQRS
open FCQRS.Model.Data
open Command
open Model.Command
open FsToolkit.ErrorHandling

type ISubscribe<'T> = Query.ISubscribe<'T>

// Logger for handlers
let mutable private logger: ILogger option = None

let setLogger (loggerFactory: ILoggerFactory) =
    logger <- Some (loggerFactory.CreateLogger("Handlers"))

let private log level message =
    logger |> Option.iter (fun l -> l.Log(level, message))

// -----------------------------------------------------------------------------
// QUERY HANDLERS (Read Side)
// -----------------------------------------------------------------------------
// These simply query the read model - fast and simple
// -----------------------------------------------------------------------------

let getDocuments (connectionString: string) () =
    let docs = ServerQuery.getDocuments connectionString
    let cutoff = DateTime.UtcNow.AddMinutes(-10.0)

    // Filter to documents updated in last 10 minutes, plus always show Hello/World example
    docs
    |> Seq.filter (fun d ->
        match DateTime.TryParse(d.UpdatedAt) with
        | true, updated -> updated > cutoff
        | false, _ -> false
        || (d.Title = "Hello" && d.Body = "World"))
    |> Seq.toArray

let getDocumentHistory (connectionString: string) (ctx: HttpContext) =
    let id = ctx.Request.RouteValues["id"].ToString()
    ServerQuery.getDocumentHistory connectionString id |> Seq.toArray

// -----------------------------------------------------------------------------
// COMMAND HANDLERS (Write Side)
// -----------------------------------------------------------------------------
// These send commands to actors and await confirmation via events
// -----------------------------------------------------------------------------

[<Literal>]
let private MaxLength = 2000

// Creates a new document or updates an existing one
let createOrUpdateDocument
    (cid: unit -> CID)                              // Correlation ID factory
    (subs: ISubscribe<IMessageWithCID>)             // Event subscription service
    (commandHandler: CommandHandler.ICommandHandlers)
    (ctx: HttpContext)
    =
    task {
        let sw = Stopwatch.StartNew()
        let mutable parseTime = 0L
        let mutable validateTime = 0L
        let mutable commandTime = 0L
        let mutable projectionTime = 0L

        let! result =
            taskResult {
                // Parse form data from HTTP request
                log LogLevel.Debug "Starting document create/update"
                let! form = ctx.Request.ReadFormAsync()
                let title = form["Title"].ToString()
                let content = form["Content"].ToString()
                let existingId = form["Id"].ToString()
                parseTime <- sw.ElapsedMilliseconds
                log LogLevel.Debug $"Parse complete: {parseTime}ms"

                // Input validation
                do! if title.Length > MaxLength then
                        Error [ ModelError.OtherError ($"Title exceeds maximum length of {MaxLength} characters" |> ValueLens.TryCreate |> Result.value) ]
                    else Ok ()
                do! if content.Length > MaxLength then
                        Error [ ModelError.OtherError ($"Content exceeds maximum length of {MaxLength} characters" |> ValueLens.TryCreate |> Result.value) ]
                    else Ok ()

                // Generate new ID or use existing
                let docId =
                    if String.IsNullOrEmpty existingId then
                        Guid.NewGuid()
                    else
                        Guid.Parse existingId

                // Create validated domain objects (may fail if invalid)
                let! aggregateId = docId.ToString() |> ValueLens.CreateAsResult
                let! document = Document.Create(docId, title, content)
                validateTime <- sw.ElapsedMilliseconds - parseTime
                log LogLevel.Debug $"Validate complete: {validateTime}ms"

                // Generate correlation ID to track this operation
                let correlationId = cid ()

                // Subscribe to events with this correlation ID BEFORE sending command
                // This ensures we don't miss the event due to race conditions
                use awaiter = subs.Subscribe((fun e -> e.CID = correlationId), 1)

                // Send command to the actor (this triggers event persistence)
                let! _ =
                    commandHandler.DocumentHandler
                        (fun _ -> true)     // Filter function (accept all)
                        correlationId       // For tracking/correlation
                        aggregateId         // Routes to correct actor instance
                        (Document.CreateOrUpdate document)
                commandTime <- sw.ElapsedMilliseconds - parseTime - validateTime
                log LogLevel.Debug $"Command complete: {commandTime}ms"

                // Wait for the event to be projected (confirms read model is updated)
                do! awaiter.Task
                projectionTime <- sw.ElapsedMilliseconds - parseTime - validateTime - commandTime
                log LogLevel.Debug $"Projection complete: {projectionTime}ms"

                return "Document received!"
            }

        let totalTime = sw.ElapsedMilliseconds
        log LogLevel.Debug $"Total time: {totalTime}ms"

        // Add Server-Timing header for performance analysis
        ctx.Response.Headers.["Server-Timing"] <-
            $"parse;dur={parseTime}, validate;dur={validateTime}, command;dur={commandTime}, projection;dur={projectionTime}, total;dur={totalTime}"

        return
            match result with
            | Ok msg -> msg
            | Error err ->
                log LogLevel.Warning $"Validation error: {err}"
                $"Error: %A{err}"
    }

// Restores a document to a previous version (time-travel feature)
let restoreVersion
    (connectionString: string)
    (cid: unit -> CID)
    (subs: ISubscribe<IMessageWithCID>)
    (commandHandler: CommandHandler.ICommandHandlers)
    (ctx: HttpContext)
    =
    task {
        let sw = Stopwatch.StartNew()
        log LogLevel.Debug "Starting version restore"

        let! result =
            taskResult {
                let! form = ctx.Request.ReadFormAsync()
                let docId = form["Id"].ToString()
                let versionStr = form["Version"].ToString()

                // Validate inputs
                do! if String.IsNullOrEmpty docId then
                        Error [ ModelError.OtherError ("Document ID is required" |> ValueLens.TryCreate |> Result.value) ]
                    else Ok ()

                let version =
                    match Int64.TryParse versionStr with
                    | true, v -> v
                    | false, _ -> 0L

                do! if version <= 0L then
                        Error [ ModelError.OtherError ("Invalid version number" |> ValueLens.TryCreate |> Result.value) ]
                    else Ok ()

                // Look up the historical version from the read model
                let history = ServerQuery.getDocumentHistory connectionString docId
                let! versionData =
                    history
                    |> Seq.tryFind (fun v -> v.Version = version)
                    |> Result.requireSome [ ModelError.OtherError ("Version not found" |> ValueLens.TryCreate |> Result.value) ]

                // Recreate the document from historical data
                let guid = Guid.Parse(docId)
                let! aggregateId = docId |> ValueLens.CreateAsResult
                let! document = Document.Create(guid, versionData.Title, versionData.Body)

                // Send as a normal update (creates new version with old content)
                let correlationId = cid ()
                use awaiter = subs.Subscribe((fun e -> e.CID = correlationId), 1)

                let! _ =
                    commandHandler.DocumentHandler
                        (fun _ -> true)
                        correlationId
                        aggregateId
                        (Document.CreateOrUpdate document)

                do! awaiter.Task
                log LogLevel.Debug $"Version restore complete: {sw.ElapsedMilliseconds}ms"

                return "Version restored!"
            }

        return
            match result with
            | Ok msg -> msg
            | Error err ->
                log LogLevel.Warning $"Restore error: {err}"
                $"Error: %A{err}"
    }
