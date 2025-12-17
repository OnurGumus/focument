@echo off
dotnet tool restore
dotnet paket restore
dotnet run --project src/Server/Server.fsproj
