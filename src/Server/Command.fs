module Command.Document

open FCQRS.Common
open Model.Command
open Model.Command.Document

type State = {
    Document: Document option
    Version: int64
}

let initialState = { Document = None; Version = 0L }

type Shard =

    static member ApplyEvent(event: Event<Event>, state: State) =
        match event.EventDetails with
        | CreatedOrUpdated doc -> {
            state with
                Document = Some doc
                Version = state.Version + 1L
          }
        | Error _ -> state

    static member HandleCommand(cmd: Command<Command>, state: State) =
        match cmd.CommandDetails, state.Document with
        | CreateOrUpdate doc, None -> CreatedOrUpdated doc |> PersistEvent
        | CreateOrUpdate doc, Some existing when existing.Id = doc.Id -> CreatedOrUpdated doc |> PersistEvent
        | CreateOrUpdate _, Some _ -> Error DocumentNotFound |> DeferEvent

    static member Init(actorApi: IActor, entityName) =
        actorApi.InitializeActor
            initialState
            entityName
            (Curry.curry Shard.HandleCommand)
            (Curry.curry Shard.ApplyEvent)

    static member Factory actorApi =
        Shard.Init(actorApi, "Document").RefFor DEFAULT_SHARD

    static member Handler actorApi = commandHandler<Shard, _, _, _> actorApi
