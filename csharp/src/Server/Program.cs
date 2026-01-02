using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Model;
using Server;
using static FCQRS.Common;
using static FCQRS.CSharp;
using CID = FCQRS.Model.Data.CID;
using IMessageWithCID = FCQRS.Model.Data.IMessageWithCID;

var logf = LoggerFactory.Create(x => x.AddConsole());

const string connectionString = "Data Source=focument_csharp.db;";

CID GetCid() => Helpers.NewCID();

// Initialize projection tables
Projection.EnsureTables(connectionString);

var builder = WebApplication.CreateBuilder(args);

var actorApi = ActorApi.Create(
    builder.Configuration,
    logf,
    connectionString,
    "FocumentCluster");

// Initialize Document aggregate first (so saga can access the factory)
var documentFactory = DocumentShard.Factory(actorApi);

// Initialize the approval saga
var sagaFac = DocumentApprovalSaga.Init(actorApi);
var sagaFactory = DocumentApprovalSaga.Factory(actorApi);

// Initialize saga starter - triggers saga when document is created
IActorExtensions.InitSagaStarter(actorApi, evt =>
{
    // When a document is created, start the approval saga
    if (evt is Event<DocumentEvent> { EventDetails: DocumentEvent.CreatedOrUpdated })
    {
        return new List<SagaDefinition>
        {
            new SagaDefinition
            {
                Factory = sagaFactory,
                PrefixConversion = PrefixConversions.Identity,
                StartingEvent = evt
            }
        };
    }
    return new List<SagaDefinition>();
});

// Initialize projection subscription
var lastOffset = ServerQuery.GetLastOffset(connectionString);

var subs = QueryApi.InitWithList(
    actorApi,
    (int)lastOffset,
    (offset, evt) => Projection.HandleEventWrapper(logf, connectionString, offset, evt));

var commandHandler = CommandHandlerFactory.Create(actorApi);

var app = builder.Build();

app.UseRouting();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/documents", () => Handlers.GetDocuments(connectionString));

app.MapGet("/api/test", () => "Hello from test!");

app.MapGet("/api/document/{id}/history", (HttpContext ctx) =>
    Handlers.GetDocumentHistory(connectionString, ctx));

app.MapPost("/api/document", async (HttpContext ctx) =>
    Microsoft.AspNetCore.Http.Results.Text(await Handlers.CreateOrUpdateDocument(GetCid, subs, commandHandler, ctx)));

app.MapPost("/api/document/restore", async (HttpContext ctx) =>
    Microsoft.AspNetCore.Http.Results.Text(await Handlers.RestoreVersion(connectionString, GetCid, subs, commandHandler, ctx)));

app.Run();
