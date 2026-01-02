// =============================================================================
// COMMAND HANDLER FACTORY
// =============================================================================
// This module creates the command handlers that route commands to actors.
// It serves as the entry point for the command side of CQRS.
// =============================================================================

using System;
using System.Threading.Tasks;
using Model;
using static FCQRS.CSharp;
using CID = FCQRS.Model.Data.CID;
using AggregateId = FCQRS.Model.Data.AggregateId;

namespace Server;

// Handler delegate type - returns Task for easy async/await usage
public delegate Task<FCQRS.Common.Event<TEvent>> Handler<TCmd, TEvent>(
    Func<TEvent, bool> filter,
    CID correlationId,
    AggregateId aggregateId,
    TCmd command)
    where TEvent : notnull;

// -----------------------------------------------------------------------------
// COMMAND HANDLER INTERFACE
// -----------------------------------------------------------------------------
public interface ICommandHandlers
{
    Handler<DocumentCommand, DocumentEvent> DocumentHandler { get; }
}

// -----------------------------------------------------------------------------
// API FACTORY
// -----------------------------------------------------------------------------
public static class CommandHandlerFactory
{
    public static ICommandHandlers Create(FCQRS.Common.IActor actorApi) =>
        new CommandHandlers(actorApi);

    private sealed class CommandHandlers(FCQRS.Common.IActor actorApi) : ICommandHandlers
    {
        public Handler<DocumentCommand, DocumentEvent> DocumentHandler =>
            DocumentShard.Handler(actorApi);
    }
}
