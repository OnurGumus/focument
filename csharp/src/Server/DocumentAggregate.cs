// =============================================================================
// DOCUMENT AGGREGATE (Actor-based Event Sourcing)
// =============================================================================

using System;
using Akkling.Cluster.Sharding;
using Model;
using static FCQRS.Common;
using static FCQRS.CSharp;

namespace Server;

// -----------------------------------------------------------------------------
// STATE: The in-memory representation of a Document aggregate
// -----------------------------------------------------------------------------
public record DocumentState(
    Document? Document,
    long Version,
    string? ApprovalCode = null,
    bool? IsApproved = null)
{
    public static readonly DocumentState Initial = new(null, 0L);
}

// -----------------------------------------------------------------------------
// SHARD: The actor implementation following CQRS/ES patterns
// -----------------------------------------------------------------------------
public static class DocumentShard
{
    // Stored factory for saga access
    private static Func<string, IEntityRef<object>>? _factory;
    public static Func<string, IEntityRef<object>> OriginatorFactory =>
        _factory ?? throw new InvalidOperationException("DocumentShard not initialized");

    // -------------------------------------------------------------------------
    // EVENT APPLICATION (Pure Function)
    // -------------------------------------------------------------------------
    public static DocumentState ApplyEvent(Event<DocumentEvent> evt, DocumentState state) =>
        evt.EventDetails switch
        {
            DocumentEvent.CreatedOrUpdated e => state with
            {
                Document = e.Document,
                Version = state.Version + 1L
            },
            DocumentEvent.ApprovalCodeSet e => state with
            {
                ApprovalCode = e.Code,
                Version = state.Version + 1L
            },
            DocumentEvent.Approved => state with
            {
                IsApproved = true,
                Version = state.Version + 1L
            },
            DocumentEvent.Rejected => state with
            {
                IsApproved = false,
                Version = state.Version + 1L
            },
            DocumentEvent.Error => state,
            _ => state
        };

    // -------------------------------------------------------------------------
    // COMMAND HANDLING (Decision Function)
    // -------------------------------------------------------------------------
    public static EventAction<DocumentEvent> HandleCommand(
        Command<DocumentCommand> cmd,
        DocumentState state) =>
        (cmd.CommandDetails, state.Document) switch
        {
            // Create new document (no existing document)
            (DocumentCommand.CreateOrUpdate c, null) =>
                EventActions.Persist<DocumentEvent>(new DocumentEvent.CreatedOrUpdated(c.Document)),

            // Update existing document (IDs must match)
            (DocumentCommand.CreateOrUpdate c, { } existing) when existing.Id == c.Document.Id =>
                EventActions.Persist<DocumentEvent>(new DocumentEvent.CreatedOrUpdated(c.Document)),

            // Reject: trying to update with wrong ID
            (DocumentCommand.CreateOrUpdate, _) =>
                EventActions.Defer<DocumentEvent>(new DocumentEvent.Error(new DocumentError.DocumentNotFound())),

            // Set approval code (saga command)
            (DocumentCommand.SetApprovalCode c, _) =>
                EventActions.Persist<DocumentEvent>(new DocumentEvent.ApprovalCodeSet(c.Code)),

            // Approve document (saga command)
            (DocumentCommand.Approve, _) =>
                EventActions.Persist<DocumentEvent>(new DocumentEvent.Approved()),

            // Reject document (saga command)
            (DocumentCommand.Reject, _) =>
                EventActions.Persist<DocumentEvent>(new DocumentEvent.Rejected()),

            _ => EventActions.Ignore<DocumentEvent>()
        };

    // -------------------------------------------------------------------------
    // ACTOR INITIALIZATION - Using C#-friendly helper from FCQRS.CSharp
    // -------------------------------------------------------------------------
    public static EntityFac<object> Init(IActor actorApi, string entityName) =>
        IActorExtensions.InitActor<DocumentState, DocumentCommand, DocumentEvent>(
            actorApi,
            DocumentState.Initial,
            entityName,
            HandleCommand,
            ApplyEvent);

    // Factory: Creates a reference to a specific document actor by entity ID
    public static Func<string, IEntityRef<object>> Factory(IActor actorApi)
    {
        var entityFac = Init(actorApi, "Document");
        _factory = entityId => entityFac.RefFor(DEFAULT_SHARD, entityId);
        return _factory;
    }

    // Handler: Creates a command handler that routes commands to the right actor
    public static Handler<DocumentCommand, DocumentEvent> Handler(IActor actorApi)
    {
        var factory = Factory(actorApi);
        return (filter, cid, aggregateId, command) =>
            AsyncExtensions.ToTask(
                IActorExtensions.CreateCommand<DocumentEvent, DocumentCommand>(
                    actorApi,
                    factory,
                    cid,
                    aggregateId,
                    command,
                    filter));
    }
}
