module ServerQuery

open Microsoft.Data.Sqlite
open Dapper

let getLastOffset (connString: string) : int64 =
    use conn = new SqliteConnection(connString)
    conn.Open()

    conn.QueryFirstOrDefault<int64>(
        "SELECT OffsetCount FROM Offsets WHERE OffsetName = @Name",
        {| Name = "DocumentProjection" |}
    )

let getDocuments (connString: string) : Query.Document list =
    use conn = new SqliteConnection(connString)
    conn.Open()

    conn.Query<Query.Document>(
        "SELECT Id, Title, Body, Version, CreatedAt, UpdatedAt FROM Documents ORDER BY UpdatedAt DESC"
    )
    |> Seq.toList

let getDocumentHistory (connString: string) (docId: string) : Query.DocumentVersion list =
    use conn = new SqliteConnection(connString)
    conn.Open()

    conn.Query<Query.DocumentVersion>(
        "SELECT Id, Version, Title, Body, CreatedAt FROM DocumentVersions WHERE Id = @Id ORDER BY Version DESC",
        {| Id = docId |}
    )
    |> Seq.toList
