open System
open System.IO
open System.Threading.RateLimiting
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.HttpOverrides
open Microsoft.AspNetCore.RateLimiting
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open FCQRS
open FCQRS.Model.Data
open Command

let logf = LoggerFactory.Create(fun x -> x.AddConsole() |> ignore)

// Initialize handler logger
Handlers.setLogger logf

// Configurable database path
let dbPath = Environment.GetEnvironmentVariable("FOCUMENT_DB_PATH") |> Option.ofObj |> Option.defaultValue "focument.db"
let connectionString = $"Data Source={dbPath};"

let connection = {
    Actor.DBType = Actor.Sqlite
    Actor.ConnectionString = connectionString |> ValueLens.TryCreate |> Result.value
}

let cid () : CID =
    Guid.CreateVersion7().ToString() |> ValueLens.CreateAsResult |> Result.value

// Initialize projection tables
Projection.ensureTables connectionString

let builder = WebApplication.CreateBuilder()

// Configure forwarded headers (for running behind reverse proxy)
builder.Services.Configure<ForwardedHeadersOptions>(fun (options: ForwardedHeadersOptions) ->
    options.ForwardedHeaders <- ForwardedHeaders.XForwardedFor ||| ForwardedHeaders.XForwardedProto
    options.KnownIPNetworks.Clear()
    options.KnownProxies.Clear()
) |> ignore

// Configure antiforgery
builder.Services.AddAntiforgery() |> ignore

// Configure form options (limit field sizes)
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(fun (options: Microsoft.AspNetCore.Http.Features.FormOptions) ->
    options.ValueLengthLimit <- 2000
    options.MultipartBodyLengthLimit <- 8192L
) |> ignore

// Configure rate limiting
builder.Services.AddRateLimiter(fun options ->
    options.AddPolicy("WritePolicy", fun (context: HttpContext) ->
        RateLimitPartition.GetSlidingWindowLimiter(
            context.Connection.RemoteIpAddress |> Option.ofObj |> Option.map string |> Option.defaultValue "unknown",
            fun _ -> SlidingWindowRateLimiterOptions(
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1.0),
                SegmentsPerWindow = 6
            )
        )
    ) |> ignore
) |> ignore

let actorApi =
    Actor.api builder.Configuration logf (Some connection) ("FocumentCluster" |> ValueLens.TryCreate |> Result.value)

// Initialize document factory first (saga needs it)
let documentFactory = Document.Shard.Factory actorApi

// Initialize the saga
let sagaFac = DocumentSaga.init actorApi documentFactory
let sagaFactory = DocumentSaga.factory actorApi documentFactory

// Initialize saga starter - triggers saga when document is created
actorApi.InitializeSagaStarter(fun evt ->
    match evt with
    | :? FCQRS.Common.Event<Model.Command.Document.Event> as e ->
        match e.EventDetails with
        | Model.Command.Document.CreatedOrUpdated _ ->
            printfn ">>> Saga starter: CreatedOrUpdated event detected"
            [ (sagaFactory, id |> Some |> FCQRS.Common.PrefixConversion, evt) ]
        | _ -> []
    | _ -> []
)

// Initialize projection subscription
let lastOffset = ServerQuery.getLastOffset connectionString
let subs = Query.init actorApi (int lastOffset) (Projection.handleEventWrapper logf connectionString)

let commandHandler = CommandHandler.api actorApi

let app = builder.Build()

// Path base configuration for subpath deployment
let pathBase = Environment.GetEnvironmentVariable("ASPNETCORE_PATHBASE")
if not (String.IsNullOrEmpty pathBase) then
    app.UsePathBase(pathBase) |> ignore

app.UseForwardedHeaders() |> ignore

// Security headers middleware
app.Use(fun (context: HttpContext) (next: RequestDelegate) ->
    task {
        context.Response.Headers.["X-Content-Type-Options"] <- "nosniff"
        context.Response.Headers.["X-Frame-Options"] <- "DENY"
        context.Response.Headers.["X-XSS-Protection"] <- "1; mode=block"
        context.Response.Headers.["Referrer-Policy"] <- "strict-origin-when-cross-origin"
        return! next.Invoke(context)
    } :> System.Threading.Tasks.Task
) |> ignore

// HTTPS redirection and HSTS in production
if not (app.Environment.IsDevelopment()) then
    app.UseHttpsRedirection() |> ignore
    app.UseHsts() |> ignore

app.UseRouting() |> ignore
app.UseRateLimiter() |> ignore
app.UseAntiforgery() |> ignore

// Dynamic index.html with base path injection
let indexHtmlPath = Path.Combine(app.Environment.WebRootPath, "index.html")
let indexHtmlTemplate =
    if File.Exists(indexHtmlPath) then File.ReadAllText(indexHtmlPath)
    else ""

app.MapGet("/", Func<HttpContext, IResult>(fun ctx ->
    let basePath = (ctx.Request.PathBase.Value |> Option.ofObj |> Option.defaultValue "") + "/"
    let html = indexHtmlTemplate.Replace("{{BASE}}", basePath)
    Results.Content(html, "text/html")
)) |> ignore

app.UseDefaultFiles() |> ignore
app.UseStaticFiles() |> ignore

// API endpoints
app.MapGet("/api/test", Func<string>(fun () -> "Hello from test!")) |> ignore
app.MapGet("/api/antiforgery-token", Func<HttpContext, IResult>(fun ctx ->
    let antiforgery = ctx.RequestServices.GetRequiredService<Microsoft.AspNetCore.Antiforgery.IAntiforgery>()
    let tokens = antiforgery.GetAndStoreTokens(ctx)
    Results.Ok({| token = tokens.RequestToken |})
)) |> ignore

app.MapGet("/api/documents", Func<_>(Handlers.getDocuments connectionString)) |> ignore
app.MapGet("/api/document/{id}/history", Func<_, _>(Handlers.getDocumentHistory connectionString)) |> ignore

app.MapPost("/api/document", Func<_, _>(Handlers.createOrUpdateDocument cid subs commandHandler))
    .RequireRateLimiting("WritePolicy") |> ignore
app.MapPost("/api/document/restore", Func<_, _>(Handlers.restoreVersion connectionString cid subs commandHandler))
    .RequireRateLimiting("WritePolicy") |> ignore

app.Run()
