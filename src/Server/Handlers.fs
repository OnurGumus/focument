module Handlers

open System
open Microsoft.AspNetCore.Http
open FCQRS
open FCQRS.Query
open FCQRS.Model.Data
open Command
open Model.Command
open FsToolkit.ErrorHandling

let getDocuments (connectionString: string) () =
    Projection.getDocuments connectionString |> Seq.toArray

let getDocumentHistory (connectionString: string) (ctx: HttpContext) =
    let id = ctx.Request.RouteValues["id"].ToString()
    Projection.getDocumentHistory connectionString id |> Seq.toArray

let createOrUpdateDocument
    (connectionString: string)
    (cid: unit -> CID)
    (subs: ISubscribe<IMessageWithCID>)
    (commandHandler: CommandHandler.ICommandHandlers)
    (ctx: HttpContext) = task {
    let! result = taskResult {
        let! form = ctx.Request.ReadFormAsync()
        let title = form["Title"].ToString()
        let content = form["Content"].ToString()
        let existingId = form["Id"].ToString()
        let docId =
            if String.IsNullOrEmpty(existingId) then Guid.NewGuid()
            else Guid.Parse(existingId)
        let! aggregateId: AggregateId = docId.ToString() |> ValueLens.CreateAsResult
        let docId: DocumentId = docId |> ValueLens.Create
        let! title: Title = title |> ValueLens.CreateAsResult
        let! content: Content = content |> ValueLens.CreateAsResult

        let document: Document = {
            Id = docId
            Title = title
            Content = content
        }

        let correlationId = cid()
        let cidValue = correlationId |> ValueLens.Value |> ValueLens.Value

        use awaiter = subs.Subscribe((fun (e: IMessageWithCID) -> e.CID |> ValueLens.Value |> ValueLens.Value = cidValue), 1)

        let! res =
            commandHandler.DocumentHandler
                (fun _ -> true)
                correlationId
                aggregateId
                (Document.CreateOrUpdate document)

        do! awaiter.Task

        printfn "Handler result: %A" res
        return "Document received!"
    }
    return match result with
           | Ok msg -> msg
           | Error err -> $"Error: %A{err}"
}

let restoreVersion
    (connectionString: string)
    (cid: unit -> CID)
    (subs: ISubscribe<IMessageWithCID>)
    (commandHandler: CommandHandler.ICommandHandlers)
    (ctx: HttpContext) = task {
    let! form = ctx.Request.ReadFormAsync()
    let docId = form["Id"].ToString()
    let version = form["Version"].ToString() |> int64

    let history = Projection.getDocumentHistory connectionString docId
    let versionData = history |> Seq.tryFind (fun v -> v.Version = version)

    match versionData with
    | None -> return "Error: Version not found"
    | Some v ->
        let aggregateIdResult: Result<AggregateId, _> = docId |> ValueLens.CreateAsResult
        let titleResult: Result<Title, _> = v.Title |> ValueLens.CreateAsResult
        let contentResult: Result<Content, _> = v.Body |> ValueLens.CreateAsResult

        match aggregateIdResult, titleResult, contentResult with
        | Ok aggregateId, Ok title, Ok content ->
            let docIdParsed: DocumentId = Guid.Parse(docId) |> ValueLens.Create

            let document: Document = {
                Id = docIdParsed
                Title = title
                Content = content
            }

            let correlationId = cid()
            let cidValue = correlationId |> ValueLens.Value |> ValueLens.Value

            use awaiter = subs.Subscribe((fun (e: IMessageWithCID) -> e.CID |> ValueLens.Value |> ValueLens.Value = cidValue), 1)

            let! _ =
                commandHandler.DocumentHandler
                    (fun _ -> true)
                    correlationId
                    aggregateId
                    (Document.CreateOrUpdate document)

            do! awaiter.Task

            return "Version restored!"
        | _ -> return "Error: Invalid data"
}
