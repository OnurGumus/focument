open System
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Logging
open FCQRS
open FCQRS.Model.Data
open Command
open Model.Command
open FsToolkit.ErrorHandling

let logf = LoggerFactory.Create(fun x -> x.AddConsole() |> ignore)

[<Literal>]
let connectionString = @"Data Source=focument.db;"

let connection = {
    Actor.DBType = Actor.Sqlite
    Actor.ConnectionString = connectionString |> ValueLens.TryCreate |> Result.value
}
let cid () : CID =
    Guid.CreateVersion7().ToString() |> ValueLens.CreateAsResult |> Result.value


let builder = WebApplication.CreateBuilder()

let actorApi =
    Actor.api  builder.Configuration logf (Some connection) ("FocumentCluster" |> ValueLens.TryCreate |> Result.value)


let commandHandler = CommandHandler.api actorApi

let app = builder.Build()

app.UseDefaultFiles() |> ignore
app.UseStaticFiles() |> ignore

app.MapGet("/document", Func<string>(fun () -> "Use POST to submit a document")) |> ignore

app.MapPost(
    "/document",
    Func<HttpContext, _>(fun ctx -> task {
        let! result = taskResult {
            let! form = ctx.Request.ReadFormAsync()
            let title = form["Title"].ToString()
            let content = form["Content"].ToString()
            let! aggregateId: AggregateId = title |> ValueLens.CreateAsResult
            let docId: DocumentId = Guid.NewGuid() |> ValueLens.Create
            let! title: Title = title |> ValueLens.CreateAsResult
            let! content: Content = content |> ValueLens.CreateAsResult

            let document: Document = {
                Id = docId
                Title = title
                Content = content
            }
            let! res =
                commandHandler.DocumentHandler
                    (fun _ -> true)
                    (cid())
                    aggregateId
                    (Document.CreateOrUpdate document)
            printfn "Handler result: %A" res
            return "Document received!"
        }
        return match result with
               | Ok msg -> msg
               | Error err -> $"Error: %A{err}"
    })
)
|> ignore

app.Run()
