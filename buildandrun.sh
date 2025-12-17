#!/bin/bash
set -e

dotnet tool restore
dotnet paket restore
dotnet run --project src/Server/Server.fsproj
