// =============================================================================
// DOMAIN MODEL - Command Side (Write Model)
// =============================================================================
// This module defines the domain types used on the COMMAND side of CQRS.
// These types are used when processing commands and generating events.
//
// Key Patterns Used:
//   1. Value Objects with private constructors (enforce invariants)
//   2. Smart constructors (Create/CreateFrom) for validated creation
//   3. Lens pattern (Value_) for controlled access to wrapped values
// =============================================================================

module Model.Command

open System
open FCQRS.Model.Data
open FsToolkit.ErrorHandling

// -----------------------------------------------------------------------------
// VALUE OBJECTS
// -----------------------------------------------------------------------------
// Value objects wrap primitive types and enforce business rules.
// The 'private' keyword prevents direct construction - must use smart constructors.
// This ensures invalid values can never exist in the system.
// -----------------------------------------------------------------------------

// DocumentId: A validated GUID wrapper
type DocumentId =
    private
    | DocumentId of Guid

    // Lens for getting/setting the inner value (used by FCQRS serialization)
    static member Value_ = (fun (DocumentId id) -> id), (fun (id: Guid) _ -> DocumentId id)

    // Smart constructor: creates a new random ID
    static member Create() = DocumentId(Guid.NewGuid())

    // Smart constructor: parses from string, returns Result for validation
    static member CreateFrom(s: string) : Result<DocumentId, ModelError list> =
        match Guid.TryParse(s) with
        | true, g -> Ok(DocumentId g)
        | false, _ -> Error [ ModelError.InvalidGuid ]

    member _.IsValid = true
    override this.ToString() = (ValueLens.Value this).ToString()

// Title: Wraps ShortString (length-limited string from FCQRS)
type Title =
    private
    | Title of ShortString

    static member Value_ = (fun (Title s) -> s), (fun (s: ShortString) _ -> Title s)

    member this.IsValid = (ValueLens.Value this).IsValid
    override this.ToString() = (ValueLens.Value this).ToString()

// Content: Wraps LongString (larger length limit than ShortString)
type Content =
    private
    | Content of LongString

    static member Value_ = (fun (Content s) -> s), (fun (s: LongString) _ -> Content s)

    member this.IsValid = (ValueLens.Value this).IsValid
    override this.ToString() = (ValueLens.Value this).ToString()

// -----------------------------------------------------------------------------
// AGGREGATE ROOT ENTITY
// -----------------------------------------------------------------------------
// Document is the main entity (aggregate root) in this bounded context.
// It composes multiple value objects and provides a factory method.
// -----------------------------------------------------------------------------
type Document = {
    Id: DocumentId
    Title: Title
    Content: Content
} with
    // Factory method using F# computation expression for Result
    static member Create(docId: Guid, title: string, content: string) =
        result {
            let docId = docId |> ValueLens.Create
            let! title = title |> ValueLens.CreateAsResult    // Fails if title invalid
            let! content = content |> ValueLens.CreateAsResult // Fails if content invalid
            return { Id = docId; Title = title; Content = content }
        }
     member this.IsValid =
        this.Id.IsValid && this.Title.IsValid && this.Content.IsValid
    override this.ToString() = this.IsValid.ToString()


// -----------------------------------------------------------------------------
// COMMANDS AND EVENTS
// -----------------------------------------------------------------------------
// Commands: Intentions to change state (what the user wants to do)
// Events: Facts that happened (immutable history of what occurred)
//
// In Event Sourcing, commands produce events, and events are the source of truth.
// -----------------------------------------------------------------------------
module Document =
    // Commands represent user intentions (including saga commands)
    type Command =
        | CreateOrUpdate of Document
        | SetApprovalCode of string
        | Approve
        | Reject

    // Domain errors (business rule violations)
    type Error =
        | DocumentNotFound

    // Events represent facts that happened
    type Event =
        | CreatedOrUpdated of Document
        | ApprovalCodeSet of string
        | Approved
        | Rejected
        | Error of Error