// =============================================================================
// DOMAIN MODEL - Command Side (Write Model)
// =============================================================================
// This module defines the domain types used on the COMMAND side of CQRS.
// These types are used when processing commands and generating events.
// =============================================================================

using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using static FCQRS.Model.CSharp;

using ShortString = FCQRS.Model.Data.ShortString;
using LongString = FCQRS.Model.Data.LongString;

namespace Model;

// -----------------------------------------------------------------------------
// VALUE OBJECTS
// -----------------------------------------------------------------------------

/// <summary>
/// DocumentId: A validated GUID wrapper
/// </summary>
public readonly record struct DocumentId(Guid Value)
{
    public static DocumentId Create() => new(Guid.NewGuid());

    public static DocumentId CreateFrom(Guid g) => new(g);

    public static bool TryParse(string s, [NotNullWhen(true)] out DocumentId result)
    {
        if (Guid.TryParse(s, out var g))
        {
            result = new DocumentId(g);
            return true;
        }
        result = default;
        return false;
    }

    public override string ToString() => Value.ToString();
}

/// <summary>
/// Title: Wraps ShortString (length-limited string from FCQRS)
/// </summary>
public readonly record struct Title(ShortString Value)
{
    public static bool TryCreate(string s, [NotNullWhen(true)] out Title result)
    {
        if (StringTypes.TryCreateShortString(s, out var shortString))
        {
            result = new Title(shortString);
            return true;
        }
        result = default;
        return false;
    }

    public override string ToString() => Value.ToString();
}

/// <summary>
/// Content: Wraps LongString (larger length limit than ShortString)
/// </summary>
public readonly record struct Content(LongString Value)
{
    public static bool TryCreate(string s, [NotNullWhen(true)] out Content result)
    {
        if (StringTypes.TryCreateLongString(s, out var longString))
        {
            result = new Content(longString);
            return true;
        }
        result = default;
        return false;
    }

    public override string ToString() => Value.ToString();
}

// -----------------------------------------------------------------------------
// AGGREGATE ROOT ENTITY
// -----------------------------------------------------------------------------

/// <summary>
/// The Document aggregate root
/// </summary>
public sealed record Document(DocumentId Id, Title Title, Content Content)
{
    /// <summary>
    /// Try to create a validated Document
    /// </summary>
    public static bool TryCreate(
        Guid docId,
        string title,
        string content,
        [NotNullWhen(true)] out Document? result,
        [NotNullWhen(false)] out string? error)
    {
        var documentId = DocumentId.CreateFrom(docId);

        if (!Title.TryCreate(title, out var titleValue))
        {
            result = null;
            error = "Invalid title";
            return false;
        }

        if (!Content.TryCreate(content, out var contentValue))
        {
            result = null;
            error = "Invalid content";
            return false;
        }

        result = new Document(documentId, titleValue, contentValue);
        error = null;
        return true;
    }
}

// -----------------------------------------------------------------------------
// COMMANDS AND EVENTS
// -----------------------------------------------------------------------------

/// <summary>
/// Commands represent user intentions
/// </summary>
[JsonPolymorphic]
[JsonDerivedType(typeof(DocumentCommand.CreateOrUpdate), nameof(CreateOrUpdate))]
[JsonDerivedType(typeof(DocumentCommand.SetApprovalCode), nameof(SetApprovalCode))]
[JsonDerivedType(typeof(DocumentCommand.Approve), nameof(Approve))]
[JsonDerivedType(typeof(DocumentCommand.Reject), nameof(Reject))]
public abstract record DocumentCommand
{
    private DocumentCommand() { }

    public sealed record CreateOrUpdate(Document Document) : DocumentCommand;
    public sealed record SetApprovalCode(string Code) : DocumentCommand;
    public sealed record Approve : DocumentCommand;
    public sealed record Reject : DocumentCommand;
}

/// <summary>
/// Domain errors (business rule violations)
/// </summary>
[JsonPolymorphic]
[JsonDerivedType(typeof(DocumentError.DocumentNotFound), nameof(DocumentNotFound))]
public abstract record DocumentError
{
    private DocumentError() { }

    public sealed record DocumentNotFound : DocumentError;
}

/// <summary>
/// Events represent facts that happened
/// </summary>
[JsonPolymorphic]
[JsonDerivedType(typeof(DocumentEvent.CreatedOrUpdated), nameof(CreatedOrUpdated))]
[JsonDerivedType(typeof(DocumentEvent.Error), nameof(Error))]
[JsonDerivedType(typeof(DocumentEvent.ApprovalCodeSet), nameof(ApprovalCodeSet))]
[JsonDerivedType(typeof(DocumentEvent.Approved), nameof(Approved))]
[JsonDerivedType(typeof(DocumentEvent.Rejected), nameof(Rejected))]
public abstract record DocumentEvent
{
    private DocumentEvent() { }

    public sealed record CreatedOrUpdated(Document Document) : DocumentEvent;
    public sealed record Error(DocumentError ErrorDetails) : DocumentEvent;
    public sealed record ApprovalCodeSet(string Code) : DocumentEvent;
    public sealed record Approved : DocumentEvent;
    public sealed record Rejected : DocumentEvent;
}
