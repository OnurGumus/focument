open System
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
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
let lastOffset = Projection.getLastOffset connectionString
let subs = Query.init<IMessageWithCID, _, _> actorApi (int lastOffset) (Projection.handleEventWrapper logf connectionString)

let commandHandler = CommandHandler.api actorApi

let app = builder.Build()

app.UseRouting() |> ignore

// Prevent caching for API routes
app.Use(Func<HttpContext, Func<Threading.Tasks.Task>, _>(fun ctx next ->
    if ctx.Request.Path.StartsWithSegments "/api" then
        ctx.Response.Headers["Cache-Control"] <- "no-cache, no-store, must-revalidate"
        ctx.Response.Headers["Pragma"] <- "no-cache"
        ctx.Response.Headers["Expires"] <- "0"
    next.Invoke()
)) |> ignore

app.UseDefaultFiles() |> ignore
app.UseStaticFiles() |> ignore

app.MapGet("/api/documents", Func<_>(Handlers.getDocuments connectionString)) |> ignore
app.MapGet("/api/document/{id}/history", Func<HttpContext, _>(Handlers.getDocumentHistory connectionString)) |> ignore
app.MapPost("/api/document", Func<HttpContext, _>(Handlers.createOrUpdateDocument connectionString cid subs commandHandler)) |> ignore
app.MapPost("/api/document/restore", Func<HttpContext, _>(Handlers.restoreVersion connectionString cid subs commandHandler)) |> ignore

app.Run()
