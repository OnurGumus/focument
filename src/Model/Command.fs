module Model.Command

open System
open FCQRS.Model.Data
open FsToolkit.ErrorHandling

type DocumentId =
    private
    | DocumentId of Guid

    static member Value_ = (fun (DocumentId id) -> id), (fun (id: Guid) _ -> DocumentId id)

    static member Create() = DocumentId(Guid.NewGuid())

    static member CreateFrom(s: string) : Result<DocumentId, ModelError list> =
        match Guid.TryParse(s) with
        | true, g -> Ok(DocumentId g)
        | false, _ -> Error [ ModelError.InvalidGuid ]

    member _.IsValid = true
    override this.ToString() = (ValueLens.Value this).ToString()

type Title =
    private
    | Title of ShortString

    static member Value_ = (fun (Title s) -> s), (fun (s: ShortString) _ -> Title s)

    member this.IsValid = (ValueLens.Value this).IsValid
    override this.ToString() = (ValueLens.Value this).ToString()

type Content =
    private
    | Content of LongString

    static member Value_ = (fun (Content s) -> s), (fun (s: LongString) _ -> Content s)

    member this.IsValid = (ValueLens.Value this).IsValid
    override this.ToString() = (ValueLens.Value this).ToString()

type Document = {
    Id: DocumentId
    Title: Title
    Content: Content
} with
    static member Create(docId: Guid, title: string, content: string) =
        result {
            let docId = docId |> ValueLens.Create
            let! title = title |> ValueLens.CreateAsResult
            let! content = content |> ValueLens.CreateAsResult
            return { Id = docId; Title = title; Content = content }
        }
     member this.IsValid = 
        this.Id.IsValid && this.Title.IsValid && this.Content.IsValid
    override this.ToString() = this.IsValid.ToString()


module Document =
    type Command =
        | CreateOrUpdate of Document

    type Error =
        | DocumentNotFound

    // Events
    type Event =
        | CreatedOrUpdated of Document
        | Error of Error