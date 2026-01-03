module DocumentSaga

open FCQRS.Common
open FCQRS.Common.SagaBuilder
open Model.Command
open Model.Command.Document
open Command.Document

type State =
    | GeneratingCode
    | SendingNotification of string
    | WaitingForApproval of string
    | Approved
    | Rejected

type SagaData = { ApprovalCode: string option }

let handleEvent (event: obj) (sagaState: SagaState<SagaData, State option>) =
    match event, sagaState.State with
    | :? Event<Event> as e, _ ->
        match e.EventDetails, sagaState.State with
        | CreatedOrUpdated _, None -> GeneratingCode |> StateChangedEvent
        | ApprovalCodeSet code, Some GeneratingCode -> SendingNotification code |> StateChangedEvent
        | Event.Approved _, _ -> State.Approved |> StateChangedEvent
        | Event.Rejected _, _ -> State.Rejected |> StateChangedEvent
        | _ -> UnhandledEvent
    | _ -> UnhandledEvent

let applySideEffects
    (originatorFactory: string -> Akkling.Cluster.Sharding.IEntityRef<obj>)
    (sagaState: SagaState<SagaData, State>)
    (recovering: bool)
    =
    let originator =
        FactoryAndName {
            Factory = originatorFactory
            Name = Originator
        }

    match sagaState.State with
    | GeneratingCode ->
        let code = System.Random.Shared.Next(100000, 999999).ToString()
        Stay,
        [
            {
                TargetActor = originator
                Command = Command.SetApprovalCode code
                DelayInMs = None
            }
        ]

    | SendingNotification code ->
        if recovering then
            Stay, []
        else
            NextState (WaitingForApproval code), []

    | WaitingForApproval _ ->
        Stay,
        [
            {
                TargetActor = originator
                Command = Command.Approve
                DelayInMs = None
            }
        ]

    | State.Approved
    | State.Rejected -> StopSaga, []

let apply (sagaState: SagaState<SagaData, SagaStateWrapper<State, Event>>) =
    match sagaState.State with
    | UserDefined (SendingNotification code) ->
        { sagaState with Data = { sagaState.Data with ApprovalCode = Some code } }
    | _ -> sagaState

let mutable private sagaFac: Akkling.Cluster.Sharding.EntityFac<obj> option = None

let init (actorApi: IActor) originatorFactory =
    let fac =
        SagaBuilder.init<SagaData, State, Event>
            actorApi
            { ApprovalCode = None }
            handleEvent
            (applySideEffects originatorFactory)
            apply
            originatorFactory
            "DocumentSaga"
    sagaFac <- Some fac
    fac

let factory (actorApi: IActor) originatorFactory entityId =
    let fac =
        match sagaFac with
        | Some f -> f
        | None -> init actorApi originatorFactory
    fac.RefFor DEFAULT_SHARD entityId
