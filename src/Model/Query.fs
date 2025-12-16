module Query

open System

type Document = {
    Id: string
    Title: string
    Body: string
    Version: int64
    CreatedAt: DateTime
    UpdatedAt: DateTime
}