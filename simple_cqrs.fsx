module CQRS

// ══════════════════════════════════════════════════════════════════
//                           TYPES
// ══════════════════════════════════════════════════════════════════

type Command =
    | Deposit of amount: decimal
    | Withdraw of amount: decimal

type Event =
    | Deposited of amount: decimal
    | Withdrawn of amount: decimal

type State = { Balance: decimal }

// ══════════════════════════════════════════════════════════════════
//                         COMMAND SIDE
// ══════════════════════════════════════════════════════════════════

let execute state = function
    | Deposit amount when amount > 0m ->
        Ok [ Deposited amount ]
    | Withdraw amount when amount > 0m && state.Balance >= amount ->
        Ok [ Withdrawn amount ]
    | Withdraw _ ->
        Error "Insufficient funds"
    | _ ->
        Error "Invalid amount"

// ══════════════════════════════════════════════════════════════════
//                         QUERY SIDE
// ══════════════════════════════════════════════════════════════════

let init = { Balance = 0m }

let evolve state = function
    | Deposited amount -> { state with Balance = state.Balance + amount }
    | Withdrawn amount -> { state with Balance = state.Balance - amount }

let fold = List.fold evolve

// ══════════════════════════════════════════════════════════════════
//                         QUERY AGENT (READ SIDE)
// ══════════════════════════════════════════════════════════════════

type QueryMessage =
    | Project of Event list

let createQueryAgent () =
    MailboxProcessor.Start(fun inbox ->
        let rec loop () = async {
            let! msg = inbox.Receive()
            match msg with
            | Project events ->
                printfn "Projecting: %A" events
                return! loop ()
        }
        loop ()
    )

// ══════════════════════════════════════════════════════════════════
//                         COMMAND AGENT (WRITE SIDE)
// ══════════════════════════════════════════════════════════════════

type CommandMessage =
    | Execute of Command * AsyncReplyChannel<Result<Event list, string>>

let createCommandAgent (queryAgent: MailboxProcessor<QueryMessage>) =
    MailboxProcessor.Start(fun inbox ->
        let rec loop state = async {
            let! msg = inbox.Receive()
            match msg with
            | Execute(cmd, reply) ->
                match execute state cmd with
                | Ok newEvents ->
                    let newState = fold state newEvents
                    queryAgent.Post(Project newEvents)
                    reply.Reply(Ok newEvents)
                    return! loop newState
                | Error err ->
                    reply.Reply(Error err)
                    return! loop state
        }
        loop init
    )

// ══════════════════════════════════════════════════════════════════
//                           USAGE (Fable-compatible)
// ══════════════════════════════════════════════════════════════════

let queryAgent = createQueryAgent ()
let commandAgent = createCommandAgent queryAgent

let commands = [ Deposit 100m; Withdraw 30m; Deposit 50m; Withdraw 200m ]

let run = async {
    for cmd in commands do
        let! result = commandAgent.PostAndAsyncReply(fun reply -> Execute(cmd, reply))
        match result with
        | Ok _ -> ()
        | Error err -> printfn "Rejected: %s" err
}

Async.StartImmediate run


