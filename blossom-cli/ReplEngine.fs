module ReplEngine

open System
open System.IO

open Types
open Renderers
open ReplParser
open ParserShared
open SubParsers
open Journal

open Reports

type GlobalOptions =
  {
    PerformanceReporting: bool
  }
  with
    static member Default = {
      PerformanceReporting = false
    }

type State =
  {
    Filename: string option
    Journal: Journal option
    GlobalOptions: GlobalOptions
  }
  with
    static member Default = {
      Filename = None
      Journal = None
      GlobalOptions = GlobalOptions.Default
    }

let time action =
  let timer = Diagnostics.Stopwatch.StartNew()
  let result = action()
  let elapsed = timer.Elapsed
  result, elapsed

let getFilter = FromString >> runParser pFilter JournalParser.UserState.Default

let set state value =
  Some <| match value with
            | None -> printfn "%A" state.GlobalOptions; state
            | Some (GPerformanceReporting v)
                -> {state with GlobalOptions = {state.GlobalOptions with PerformanceReporting = v}}

let load state filename =
  try
    let parsed = loadJournal filename
    Some {state with Journal = Some parsed; Filename = Some filename}
  with
    | :? FileNotFoundException as ex ->
           printfn "Couldn't find file %A" ex.FileName
           Some state

let reload state =
  match state.Filename with
    | Some fn -> load state fn
    | None    -> printfn "Cannot reload if no file loaded"
                 Some state

let showHelp state =
  printfn "  Filters"
  printfn "    date: >/>=/</<="
  printfn "    payee: @"
  printfn "    narrative: ?"
  printfn "    commodity: %%"
  printfn "    hashtag: #"
  printfn "    account: no symbol"
  Some state

let execute state input =
  let withJournal op =
    match state.Journal with
      | Some j -> op j
      | None   -> printfn "You must load a journal first."
    Some state

  let action = function
    | Quit                 -> None
    | Clear                -> Console.Clear()
                              Some state
    | Set value            -> set state value
    | Load filename        -> load state filename
    | Reload               -> reload state
    | Balances (fs, query) -> withJournal <| balances HumanReadable.renderTable (getFilter query) fs
    | Journal (fs, query)  -> withJournal <| journal HumanReadable.renderTable (getFilter query) fs
    | BalanceSeries (fs, tenor, cumulative, query)
                           -> withJournal <| balanceSeries HumanReadable.renderTable tenor cumulative (getFilter query) fs
    | Check request        -> withJournal <| checkJournal HumanReadable.renderTable request
    | Meta request         -> withJournal <| meta HumanReadable.renderMetaResult request
    | Help                 -> showHelp state

  try
    let result = runParser parse () (FromString input)
    printfn "=> %A" result
    let output, duration = time (fun () -> action result)
    match state.GlobalOptions.PerformanceReporting with
      | true -> printfn "=> %A elapsed." duration
      | false -> ()
    output
  with
    | ex -> printfn "=> error detected %A" ex.Message
            Some state

let rec repl state =
  printf "] "
  let input = Console.ReadLine()
  match execute state input with
    | Some state2 -> repl state2
    | None        -> ()

let rec repl1 filename =
  let state = load State.Default filename
  match state with
    | Some s -> repl s
    | None -> failwith "Unexpected load error"