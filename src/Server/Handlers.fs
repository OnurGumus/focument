module Handlers

open System
open Microsoft.AspNetCore.Http
open FCQRS
open FCQRS.Model.Data
open Command
open Model.Command
open FsToolkit.ErrorHandling

type ISubscribe<'T> = Query.ISubscribe<'T>

let getDocuments (connectionString: string) () =
    ServerQuery.getDocuments connectionString |> Seq.toArray

let getDocumentHistory (connectionString: string) (ctx: HttpContext) =
    let id = ctx.Request.RouteValues["id"].ToString()
    ServerQuery.getDocumentHistory connectionString id |> Seq.toArray

let createOrUpdateDocument
    (cid: unit -> CID)
    (subs: ISubscribe<IMessageWithCID>)
    (commandHandler: CommandHandler.ICommandHandlers)
    (ctx: HttpContext)
    =
    task {
        let! result =
            taskResult {
                let! form = ctx.Request.ReadFormAsync()
                let title = form["Title"].ToString()
                let content = form["Content"].ToString()
                let existingId = form["Id"].ToString()

                let docId =
                    if String.IsNullOrEmpty existingId then
                        Guid.NewGuid()
                    else
                        Guid.Parse existingId

                let! aggregateId = docId.ToString() |> ValueLens.CreateAsResult
        

                let! document = Document.Create(docId, title, content)

                let correlationId = cid ()

                use awaiter = subs.Subscribe((fun e -> e.CID = correlationId), 1)

                let! _ =
                    commandHandler.DocumentHandler
                        (fun _ -> true)
                        correlationId
                        aggregateId
                        (Document.CreateOrUpdate document)

                do! awaiter.Task

                return "Document received!"
            }

        return
            match result with
            | Ok msg -> msg
            | Error err -> $"Error: %A{err}"
    }

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

                let history = ServerQuery.getDocumentHistory connectionString docId
                let! versionData =
                    history
                    |> Seq.tryFind (fun v -> v.Version = version)
                    |> Result.requireSome [ ModelError.OtherError ( "Version not found"  |> ValueLens.TryCreate |>  Result.value)  ]

                let guid = Guid.Parse(docId)
                let! aggregateId = docId |> ValueLens.CreateAsResult
                let! document = Document.Create(guid, versionData.Title, versionData.Body)

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
