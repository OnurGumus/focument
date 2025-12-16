open System
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http

let builder = WebApplication.CreateBuilder()
let app = builder.Build()

app.UseDefaultFiles() |> ignore
app.UseStaticFiles() |> ignore

app.MapPost("/document", Func<HttpContext, _>(fun ctx ->
    task {
        let! form = ctx.Request.ReadFormAsync()
        let title = form["Title"].ToString()
        let content = form["Content"].ToString()
        printfn "Received: Title=%s, Content=%s" title content
        return $"Document received: {title}"
    }
)) |> ignore

app.Run()
