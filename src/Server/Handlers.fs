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
open Microsoft.AspNetCore.Http
open FCQRS
open FCQRS.Model.Data
open Command
open Model.Command
open FsToolkit.ErrorHandling

type ISubscribe<'T> = Query.ISubscribe<'T>

// -----------------------------------------------------------------------------
// QUERY HANDLERS (Read Side)
// -----------------------------------------------------------------------------
// These simply query the read model - fast and simple
// -----------------------------------------------------------------------------

let getDocuments (connectionString: string) () =
    ServerQuery.getDocuments connectionString |> Seq.toArray

let getDocumentHistory (connectionString: string) (ctx: HttpContext) =
    let id = ctx.Request.RouteValues["id"].ToString()
    ServerQuery.getDocumentHistory connectionString id |> Seq.toArray

// -----------------------------------------------------------------------------
// COMMAND HANDLERS (Write Side)
// -----------------------------------------------------------------------------
// These send commands to actors and await confirmation via events
// -----------------------------------------------------------------------------

// Creates a new document or updates an existing one
let createOrUpdateDocument
    (cid: unit -> CID)                              // Correlation ID factory
    (subs: ISubscribe<IMessageWithCID>)             // Event subscription service
    (commandHandler: CommandHandler.ICommandHandlers)
    (ctx: HttpContext)
    =
    task {
        let! result =
            taskResult {
                // Parse form data from HTTP request
                let! form = ctx.Request.ReadFormAsync()
                let title = form["Title"].ToString()
                let content = form["Content"].ToString()
                let existingId = form["Id"].ToString()

                // Generate new ID or use existing
                let docId =
                    if String.IsNullOrEmpty existingId then
                        Guid.NewGuid()
                    else
                        Guid.Parse existingId

                // Create validated domain objects (may fail if invalid)
                let! aggregateId = docId.ToString() |> ValueLens.CreateAsResult
                let! document = Document.Create(docId, title, content)

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

                // Wait for the event to be projected (confirms read model is updated)
                do! awaiter.Task

                return "Document received!"
            }

        return
            match result with
            | Ok msg -> msg
            | Error err -> $"Error: %A{err}"
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
        let! result =
            taskResult {
                let! form = ctx.Request.ReadFormAsync()
                let docId = form["Id"].ToString()
                let version = form["Version"].ToString() |> int64

                // Look up the historical version from the read model
                let history = ServerQuery.getDocumentHistory connectionString docId
                let! versionData =
                    history
                    |> Seq.tryFind (fun v -> v.Version = version)
                    |> Result.requireSome [ ModelError.OtherError ( "Version not found"  |> ValueLens.TryCreate |>  Result.value)  ]

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

                return "Version restored!"
            }

        return
            match result with
            | Ok msg -> msg
            | Error err -> $"Error: %A{err}"
    }
