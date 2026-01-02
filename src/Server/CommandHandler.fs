// =============================================================================
// COMMAND HANDLER FACTORY
// =============================================================================
// This module creates the command handlers that route commands to actors.
// It serves as the entry point for the command side of CQRS.
//
// Architecture:
//   HTTP Handler → CommandHandler → Actor (Shard) → Event Store
// =============================================================================

module Command.CommandHandler

open FCQRS.Common
open Model.Command

// -----------------------------------------------------------------------------
// COMMAND HANDLER INTERFACE
// -----------------------------------------------------------------------------
// Defines all available command handlers in the system.
// Each handler knows how to route commands to the appropriate actor.
// -----------------------------------------------------------------------------
type ICommandHandlers =
    abstract DocumentHandler: Handler<Document.Command, Document.Event> with get

// -----------------------------------------------------------------------------
// API FACTORY
// -----------------------------------------------------------------------------
// Creates the command handler implementations.
// InitializeSagaStarter: Required by FCQRS even if not using sagas (pass empty list)
// -----------------------------------------------------------------------------
let api (actorApi: IActor) : ICommandHandlers =
    // Note: Saga starter is initialized in Program.fs
    // Return object expression implementing ICommandHandlers
    { new ICommandHandlers with
        member _.DocumentHandler = Document.Shard.Handler actorApi
    }

