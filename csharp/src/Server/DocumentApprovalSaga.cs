// =============================================================================
// DOCUMENT APPROVAL SAGA
// =============================================================================
// This saga is triggered when a document is created. It:
// 1. Generates an approval code
// 2. Simulates sending a notification
// 3. Auto-approves the document (for demo purposes)
// =============================================================================

using System;
using System.Collections.Generic;
using Akkling.Cluster.Sharding;
using Model;
using static FCQRS.Common;
using static FCQRS.CSharp;

namespace Server;

public static class DocumentApprovalSaga
{
    // -------------------------------------------------------------------------
    // INITIAL SAGA DATA
    // -------------------------------------------------------------------------
    public static readonly ApprovalSagaData InitialData = new();

    // -------------------------------------------------------------------------
    // EVENT HANDLER - Reacts to events and produces state changes
    // -------------------------------------------------------------------------
    private static EventAction<ApprovalState> HandleEvent(
        Event<DocumentEvent> evt,
        ApprovalSagaData data,
        ApprovalState? currentState) =>
        (evt.EventDetails, currentState) switch
        {
            // Document was created - start approval workflow
            (DocumentEvent.CreatedOrUpdated, null) =>
                SagaEventActions.StateChanged<ApprovalState>(new ApprovalState.GeneratingCode()),

            // Approval code was set - move to sending notification
            (DocumentEvent.ApprovalCodeSet codeSet, ApprovalState.GeneratingCode) =>
                SagaEventActions.StateChanged<ApprovalState>(new ApprovalState.SendingNotification(codeSet.Code)),

            // Document was approved
            (DocumentEvent.Approved, _) =>
                SagaEventActions.StateChanged<ApprovalState>(new ApprovalState.Approved()),

            // Document was rejected
            (DocumentEvent.Rejected, _) =>
                SagaEventActions.StateChanged<ApprovalState>(new ApprovalState.Rejected()),

            // Unhandled
            _ => SagaEventActions.Unhandled<ApprovalState>()
        };

    // -------------------------------------------------------------------------
    // SIDE EFFECTS - Sends commands when entering states
    // -------------------------------------------------------------------------
    private static SagaSideEffectResult<ApprovalState> ApplySideEffects(
        ApprovalSagaData data,
        ApprovalState state,
        bool recovering) =>
        state switch
        {
            // Generate and set approval code
            ApprovalState.GeneratingCode => new()
            {
                Transition = SagaTransitions.Stay<ApprovalState>(),
                Commands = [SagaCommands.ToOriginator(
                    DocumentShard.OriginatorFactory,
                    new DocumentCommand.SetApprovalCode(GenerateApprovalCode()))]
            },

            // "Send" notification and auto-approve for demo
            ApprovalState.SendingNotification sending => new()
            {
                Transition = recovering
                    ? SagaTransitions.Stay<ApprovalState>()
                    : SagaTransitions.NextState<ApprovalState>(new ApprovalState.WaitingForApproval(sending.ApprovalCode)),
                Commands = []
            },

            // Auto-approve after waiting (for demo)
            ApprovalState.WaitingForApproval => new()
            {
                Transition = SagaTransitions.Stay<ApprovalState>(),
                Commands = [SagaCommands.ToOriginator(
                    DocumentShard.OriginatorFactory,
                    new DocumentCommand.Approve())]
            },

            // Saga completed - stop
            ApprovalState.Approved or ApprovalState.Rejected => new()
            {
                Transition = SagaTransitions.StopSaga<ApprovalState>(),
                Commands = []
            },

            _ => new()
            {
                Transition = SagaTransitions.Stay<ApprovalState>(),
                Commands = []
            }
        };

    // -------------------------------------------------------------------------
    // APPLY - Update saga data when states change
    // -------------------------------------------------------------------------
    private static ApprovalSagaData Apply(ApprovalSagaData data, ApprovalState state) =>
        state is ApprovalState.SendingNotification sending
            ? data with { ApprovalCode = sending.ApprovalCode }
            : data;

    // -------------------------------------------------------------------------
    // INITIALIZATION
    // -------------------------------------------------------------------------
    private static EntityFac<object>? _sagaFac;

    public static EntityFac<object> Init(IActor actorApi)
    {
        _sagaFac = SagaBuilderCSharp.InitSimple<DocumentEvent, ApprovalSagaData, ApprovalState>(
            actorApi,
            InitialData,
            HandleEvent,
            ApplySideEffects,
            Apply,
            DocumentShard.OriginatorFactory,
            "DocumentApprovalSaga");
        return _sagaFac;
    }

    public static Func<string, IEntityRef<object>> Factory(IActor actorApi)
    {
        var fac = _sagaFac ?? Init(actorApi);
        return entityId => fac.RefFor(DEFAULT_SHARD, entityId);
    }

    // -------------------------------------------------------------------------
    // HELPERS
    // -------------------------------------------------------------------------
    private static string GenerateApprovalCode() =>
        Random.Shared.Next(100_000, 999_999).ToString();
}
