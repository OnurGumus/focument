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
    printfn ">>> F# handleEvent: event type = %s, state = %A" (event.GetType().FullName) sagaState.State
    match event, sagaState.State with
    | :? Event<Event> as e, _ ->
        printfn ">>> F# handleEvent: EventDetails = %A" e.EventDetails
        match e.EventDetails, sagaState.State with
        | CreatedOrUpdated _, None ->
            printfn ">>> F# handleEvent: CreatedOrUpdated -> GeneratingCode"
            GeneratingCode |> StateChangedEvent
        | ApprovalCodeSet code, Some GeneratingCode ->
            printfn ">>> F# handleEvent: ApprovalCodeSet -> SendingNotification"
            SendingNotification code |> StateChangedEvent
        | Event.Approved, _ ->
            printfn ">>> F# handleEvent: Approved -> Approved"
            State.Approved |> StateChangedEvent
        | Event.Rejected, _ ->
            printfn ">>> F# handleEvent: Rejected -> Rejected"
            State.Rejected |> StateChangedEvent
        | _ ->
            printfn ">>> F# handleEvent: unhandled event details"
            UnhandledEvent
    | _ ->
        printfn ">>> F# handleEvent: unhandled event type"
        UnhandledEvent

let applySideEffects
    (originatorFactory: string -> Akkling.Cluster.Sharding.IEntityRef<obj>)
    (sagaState: SagaState<SagaData, State>)
    (recovering: bool)
    =
    printfn ">>> F# applySideEffects: state = %A, recovering = %b" sagaState.State recovering
    let originator =
        FactoryAndName {
            Factory = originatorFactory
            Name = Originator
        }

    match sagaState.State with
    | GeneratingCode ->
        let code = System.Random.Shared.Next(100000, 999999).ToString()
        printfn ">>> F# applySideEffects: GeneratingCode -> SetApprovalCode %s" code
        Stay,
        [
            {
                TargetActor = originator
                Command = Command.SetApprovalCode code  // Use aggregate's Command type
                DelayInMs = None
            }
        ]

    | SendingNotification code ->
        // Simulate sending notification, then auto-approve
        printfn ">>> F# applySideEffects: SendingNotification -> Approve"
        if recovering then
            Stay, []
        else
            NextState (WaitingForApproval code), []

    | WaitingForApproval _ ->
        printfn ">>> F# applySideEffects: WaitingForApproval -> Approve"
        Stay,
        [
            {
                TargetActor = originator
                Command = Command.Approve  // Use aggregate's Command type
                DelayInMs = None
            }
        ]

    | State.Approved
    | State.Rejected ->
        printfn ">>> F# applySideEffects: Completed -> StopSaga"
        StopSaga, []

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
