module DocumentSaga

open FCQRS.Common
open FCQRS.Model.Data
open Model.Command.Document

type State =
    | GeneratingCode
    | SendingNotification of string
    | WaitingForApproval of string
    | Approved
    | Rejected

type SagaData = { ApprovalCode: ShortString option }

// This is a self approving dummy saga. Only for demo purposes.

let handleEvent (event: obj) sagaState =
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
    originatorFactory
    sagaState
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

let apply (sagaState: SagaState<SagaData, State>) =
    // Update cross-cutting data based on current state
    match sagaState.State with
    | SendingNotification code ->
        { sagaState with Data.ApprovalCode = ValueLens.TryCreate code |> Result.toOption }
    | _ -> sagaState

let init actorApi originatorFactory =
    SagaBuilder.initSimple<SagaData, State, Event>
        actorApi
        { ApprovalCode = None }
        handleEvent
        (applySideEffects originatorFactory)
        apply
        originatorFactory
        "DocumentSaga"

let factory actorApi originatorFactory entityId =
    let fac = init actorApi originatorFactory
    fac.RefFor DEFAULT_SHARD entityId
