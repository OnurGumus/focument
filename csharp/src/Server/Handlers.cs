// =============================================================================
// HTTP HANDLERS (API Layer)
// =============================================================================
// This module contains the HTTP handlers that bridge the web API to the
// CQRS/ES domain.
// =============================================================================

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Model;
using static FCQRS.CSharp;
using CID = FCQRS.Model.Data.CID;
using IMessageWithCID = FCQRS.Model.Data.IMessageWithCID;

namespace Server;

public static class Handlers
{
    // -----------------------------------------------------------------------------
    // QUERY HANDLERS (Read Side)
    // -----------------------------------------------------------------------------
    public static Query.Document[] GetDocuments(string connectionString) =>
        ServerQuery.GetDocuments(connectionString).ToArray();

    public static Query.DocumentVersion[] GetDocumentHistory(string connectionString, HttpContext ctx)
    {
        var id = ctx.Request.RouteValues["id"]?.ToString() ?? "";
        return ServerQuery.GetDocumentHistory(connectionString, id).ToArray();
    }

    // -----------------------------------------------------------------------------
    // COMMAND HANDLERS (Write Side)
    // -----------------------------------------------------------------------------
    public static async Task<string> CreateOrUpdateDocument(
        Func<CID> getCid,
        FCQRS.Query.ISubscribe<IMessageWithCID> subs,
        ICommandHandlers commandHandler,
        HttpContext ctx)
    {
        Console.WriteLine(">>> CreateOrUpdateDocument called");
        try
        {
            var form = await ctx.Request.ReadFormAsync();
            var title = form["Title"].ToString();
            var content = form["Content"].ToString();
            var existingId = form["Id"].ToString();
            Console.WriteLine($">>> Title: {title}, Content: {content}");

            var docId = string.IsNullOrEmpty(existingId)
                ? Guid.NewGuid()
                : Guid.Parse(existingId);

            // Create validated domain objects
            var aggregateId = Helpers.CreateAggregateId(docId.ToString());
            Console.WriteLine($">>> DocId: {docId}, AggregateId: {aggregateId}");

            if (!Document.TryCreate(docId, title, content, out var document, out var docError))
            {
                Console.WriteLine($">>> Document validation failed: {docError}");
                return $"Error: {docError}";
            }
            Console.WriteLine(">>> Document validated");

            var correlationId = getCid();
            Console.WriteLine($">>> CorrelationId: {correlationId}");

            // Subscribe to events with this correlation ID BEFORE sending command
            using var awaiter = ISubscribeExtensions.SubscribeFor(subs, e => e.CID.Equals(correlationId), 1);
            Console.WriteLine(">>> Awaiter created");

            // Send command to the actor
            var handler = commandHandler.DocumentHandler;
            Console.WriteLine(">>> About to call handler");
            await handler(
                _ => true,
                correlationId,
                aggregateId,
                new DocumentCommand.CreateOrUpdate(document));
            Console.WriteLine(">>> Handler called");

            // Wait for the event to be projected
            await awaiter.Task;
            Console.WriteLine(">>> Event projected");

            return "Document received!";
        }
        catch (Exception ex)
        {
            Console.WriteLine($">>> Exception: {ex}");
            return $"Error: {ex.Message}";
        }
    }

    // Restores a document to a previous version (time-travel feature)
    public static async Task<string> RestoreVersion(
        string connectionString,
        Func<CID> getCid,
        FCQRS.Query.ISubscribe<IMessageWithCID> subs,
        ICommandHandlers commandHandler,
        HttpContext ctx)
    {
        try
        {
            var form = await ctx.Request.ReadFormAsync();
            var docId = form["Id"].ToString();
            var version = long.Parse(form["Version"].ToString());

            // Look up the historical version from the read model
            var history = ServerQuery.GetDocumentHistory(connectionString, docId);
            var versionData = history.Find(v => v.Version == version);

            if (versionData is null)
                return "Error: Version not found";

            // Recreate the document from historical data
            var guid = Guid.Parse(docId);
            var aggregateId = Helpers.CreateAggregateId(docId);

            if (!Document.TryCreate(guid, versionData.Title, versionData.Body, out var document, out var docError))
                return $"Error: {docError}";

            var correlationId = getCid();

            using var awaiter = ISubscribeExtensions.SubscribeFor(subs, e => e.CID.Equals(correlationId), 1);

            var handler = commandHandler.DocumentHandler;
            await handler(
                _ => true,
                correlationId,
                aggregateId,
                new DocumentCommand.CreateOrUpdate(document));

            await awaiter.Task;

            return "Version restored!";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }
}
