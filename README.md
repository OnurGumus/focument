# Focument

A document history tool that tracks every change to your documents, allowing you to view and restore any previous version. Built with F# using CQRS and Event Sourcing patterns.

![Focument Screenshot](image.png)

## What It Does

- Create and edit documents with title and content
- Automatically tracks every change as a new version
- View complete history of all document revisions
- Restore any previous version with one click

## Patterns Demonstrated

- **CQRS (Command Query Responsibility Segregation)** - Separating read and write operations
- **Event Sourcing** - Storing state changes as a sequence of events
- **Projections** - Building read models from event streams
- **Version History** - Full document history with restore capability

## Architecture

```
┌─────────────┐     ┌──────────────┐     ┌─────────────┐
│   Browser   │────▶│  ASP.NET     │────▶│  Commands   │
│             │     │  Handlers    │     │  (Write)    │
└─────────────┘     └──────────────┘     └──────┬──────┘
                           │                    │
                           ▼                    ▼
                    ┌──────────────┐     ┌─────────────┐
                    │   Queries    │     │   Events    │
                    │   (Read)     │     │   Store     │
                    └──────┬───────┘     └──────┬──────┘
                           │                    │
                           ▼                    ▼
                    ┌──────────────┐     ┌─────────────┐
                    │  SQLite DB   │◀────│ Projections │
                    │  (Read Model)│     │             │
                    └──────────────┘     └─────────────┘
```

## Tech Stack

- **F#** - Functional-first programming language
- **FCQRS** - F# CQRS/Event Sourcing framework
- **Akka.NET** - Actor model for event persistence
- **Dapper** - Lightweight ORM for projections
- **SQLite** - Embedded database
- **ASP.NET Core** - Web framework

## Getting Started

### Prerequisites

- .NET 10 SDK
- Paket package manager

### Running the Application

```bash
# Restore dependencies
dotnet paket install

# Run the server
dotnet run --project src/Server/Server.fsproj
```

Open http://localhost:5000 in your browser.

## Project Structure

```
src/
├── Model/
│   ├── Command.fs      # Domain types and commands
│   └── Query.fs        # Query DTOs
└── Server/
    ├── Query.fs        # Database queries
    ├── Projection.fs   # Event projections
    ├── Command.fs      # Command definitions
    ├── CommandHandler.fs
    ├── Handlers.fs     # HTTP handlers
    └── Program.fs      # Application entry point
```

## Features

- Create and update documents
- View complete version history
- Restore any previous version
- Real-time event projection

## Workshop Topics

1. Understanding CQRS pattern
2. Event Sourcing fundamentals
3. Building projections with FCQRS
4. Handling eventual consistency
5. Domain modeling with F# types

## License

MIT
