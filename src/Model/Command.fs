module Model.Command

open System
open FCQRS.Model.Data

type DocumentId =
    private
    | DocumentId of Guid

    static member Value_ = (fun (DocumentId id) -> id), (fun (id: Guid) _ -> DocumentId id)

    static member Create() = DocumentId(Guid.NewGuid())
    member _.IsValid = true
    override this.ToString() = (ValueLens.Value this).ToString()

type Title =
    private
    | Title of ShortString

    static member Value_ = (fun (Title s) -> s), (fun (s: ShortString) _ -> Title s)

    member _.IsValid = true
    override this.ToString() = (ValueLens.Value this).ToString()

type Content =
    private
    | Content of LongString

    static member Value_ = (fun (Content s) -> s), (fun (s: LongString) _ -> Content s)

    member _.IsValid = true
    override this.ToString() = (ValueLens.Value this).ToString()

type Document = {
    Id: DocumentId
    Title: Title
    Content: Content
}


module Document =
    type Command =
        | CreateOrUpdate of Document

    type Error =
        | DocumentNotFound

    // Events
    type Event =
        | CreatedOrUpdated of Document
        | Error of Error