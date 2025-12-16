open System
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.Logging
open FCQRS
open FCQRS.Model.Data
open Command

let logf = LoggerFactory.Create(fun x -> x.AddConsole() |> ignore)

[<Literal>]
let connectionString = @"Data Source=focument.db;"

let connection = {
    Actor.DBType = Actor.Sqlite
    Actor.ConnectionString = connectionString |> ValueLens.TryCreate |> Result.value
}

let cid () : CID =
    Guid.CreateVersion7().ToString() |> ValueLens.CreateAsResult |> Result.value

// Initialize projection tables
Projection.ensureTables connectionString

let builder = WebApplication.CreateBuilder()

let actorApi =
    Actor.api builder.Configuration logf (Some connection) ("FocumentCluster" |> ValueLens.TryCreate |> Result.value)

// Initialize projection subscription
let lastOffset = ServerQuery.getLastOffset connectionString
let subs = Query.init actorApi (int lastOffset) (Projection.handleEventWrapper logf connectionString)

let commandHandler = CommandHandler.api actorApi

let app = builder.Build()

app.UseRouting() |> ignore

app.UseDefaultFiles() |> ignore
app.UseStaticFiles() |> ignore

app.MapGet("/api/documents", Func<_>(Handlers.getDocuments connectionString)) |> ignore
app.MapGet("/api/document/{id}/history", Func<_, _>(Handlers.getDocumentHistory connectionString)) |> ignore
app.MapPost("/api/document", Func<_, _>(Handlers.createOrUpdateDocument  cid subs commandHandler)) |> ignore
app.MapPost("/api/document/restore", Func<_, _>(Handlers.restoreVersion connectionString cid subs commandHandler)) |> ignore

app.Run()
