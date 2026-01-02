// =============================================================================
// SAGA TYPES - Document Approval Saga
// =============================================================================
// This module defines the saga types for document approval workflow.
// The saga is triggered when a document is created and manages the approval process.
// =============================================================================

using System.Text.Json.Serialization;

namespace Model;

// -----------------------------------------------------------------------------
// SAGA DATA (cross-cutting state persisted across the saga lifecycle)
// -----------------------------------------------------------------------------

/// <summary>
/// Data that persists across saga state transitions
/// </summary>
public record ApprovalSagaData
{
    public int RetryCount { get; init; } = 0;
    public string? ApprovalCode { get; init; }
    public string? DocumentId { get; init; }
    public string? DocumentTitle { get; init; }
}

// -----------------------------------------------------------------------------
// SAGA STATES (user-defined states - framework adds NotStarted/Started)
// -----------------------------------------------------------------------------

/// <summary>
/// States for the document approval saga
/// </summary>
[JsonPolymorphic]
[JsonDerivedType(typeof(ApprovalState.GeneratingCode), nameof(GeneratingCode))]
[JsonDerivedType(typeof(ApprovalState.SendingNotification), nameof(SendingNotification))]
[JsonDerivedType(typeof(ApprovalState.WaitingForApproval), nameof(WaitingForApproval))]
[JsonDerivedType(typeof(ApprovalState.Approved), nameof(Approved))]
[JsonDerivedType(typeof(ApprovalState.Rejected), nameof(Rejected))]
public abstract record ApprovalState
{
    private ApprovalState() { }

    /// <summary>Generating approval code</summary>
    public sealed record GeneratingCode : ApprovalState;

    /// <summary>Sending notification (simulated)</summary>
    public sealed record SendingNotification(string ApprovalCode) : ApprovalState;

    /// <summary>Waiting for approval decision</summary>
    public sealed record WaitingForApproval(string ApprovalCode) : ApprovalState;

    /// <summary>Document was approved</summary>
    public sealed record Approved : ApprovalState;

    /// <summary>Document was rejected</summary>
    public sealed record Rejected : ApprovalState;
}

// -----------------------------------------------------------------------------
// SAGA EVENTS (events that the saga listens to and produces)
// -----------------------------------------------------------------------------

/// <summary>
/// Events related to document approval (extends the main DocumentEvent)
/// </summary>
[JsonPolymorphic]
[JsonDerivedType(typeof(ApprovalEvent.ApprovalCodeGenerated), nameof(ApprovalCodeGenerated))]
[JsonDerivedType(typeof(ApprovalEvent.NotificationSent), nameof(NotificationSent))]
[JsonDerivedType(typeof(ApprovalEvent.DocumentApproved), nameof(DocumentApproved))]
[JsonDerivedType(typeof(ApprovalEvent.DocumentRejected), nameof(DocumentRejected))]
public abstract record ApprovalEvent
{
    private ApprovalEvent() { }

    public sealed record ApprovalCodeGenerated(string Code) : ApprovalEvent;
    public sealed record NotificationSent(string Code) : ApprovalEvent;
    public sealed record DocumentApproved(string DocumentId) : ApprovalEvent;
    public sealed record DocumentRejected(string DocumentId) : ApprovalEvent;
}

// -----------------------------------------------------------------------------
// SAGA COMMANDS (commands the saga sends to aggregates)
// -----------------------------------------------------------------------------

/// <summary>
/// Commands that the saga sends
/// </summary>
[JsonPolymorphic]
[JsonDerivedType(typeof(ApprovalCommand.SetApprovalCode), nameof(SetApprovalCode))]
[JsonDerivedType(typeof(ApprovalCommand.ApproveDocument), nameof(ApproveDocument))]
[JsonDerivedType(typeof(ApprovalCommand.RejectDocument), nameof(RejectDocument))]
public abstract record ApprovalCommand
{
    private ApprovalCommand() { }

    public sealed record SetApprovalCode(string Code) : ApprovalCommand;
    public sealed record ApproveDocument : ApprovalCommand;
    public sealed record RejectDocument : ApprovalCommand;
}
