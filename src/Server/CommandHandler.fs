module Command.CommandHandler

open FCQRS.Common
open Model.Command

type ICommandHandlers =
    abstract DocumentHandler: Handler<Document.Command, Document.Event> with get

let api (actorApi: IActor) : ICommandHandlers =
    actorApi.InitializeSagaStarter <| fun _ -> []

    { new ICommandHandlers with
        member _.DocumentHandler = Document.Shard.Handler actorApi
    }

