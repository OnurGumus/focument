module Query

[<CLIMutable>]
type Document = {
    Id: string
    Title: string
    Body: string
    Version: int64
    CreatedAt: string
    UpdatedAt: string
}

[<CLIMutable>]
type DocumentVersion = {
    Id: string
    Version: int64
    Title: string
    Body: string
    CreatedAt: string
}
