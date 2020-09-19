module Journal

open System
open Shared
open Types
open JournalParser

let marketAccount = Types.Account "_Market"
let conversionsAccount = Types.Account "_Conversions"

let internalDefaultCommodity = Types.Commodity "$"  // this is not a parsable value

let stripComments = function
  | Commented (elt, _) -> elt
  | elt -> elt

let balanceEntry gdc acctDecls commodDecls = function
  | Entry (dt, py, na, xs) ->
      // Helpers
      let measureOf commodity = commodDecls |> Map.tryFind commodity
                                            |> Option.bind (fun c -> c.Measure)
                                            |> function Some c -> c | None -> internalDefaultCommodity
      let multiplierOf commodity = commodDecls |> Map.tryFind commodity
                                               |> Option.bind (fun c -> c.Multiplier)
                                               |> Option.defaultValue 1m
      let tryCommodityOf account = acctDecls |> Map.tryFind account
                                             |> Option.bind (fun a -> a.Commodity)
      let orGlobalCommodity = Option.orElse gdc >> function Some c -> c | None -> internalDefaultCommodity

      let contraRAmountV = function | Un d                -> Un (-d)
                                    | Ve (q, c)           -> Ve (-q, c)
                                    | Tf ((q, c), (p, m)) -> Ve (-q*p*multiplierOf c, m)
                                    | Th ((q, c), p)      -> Ve (-q*p*multiplierOf c, measureOf c)
                                    | Cr (_, b)           -> Ve b
                                    | Cl (a, _)           -> Ve a

      // is there a blank account for auto contra?
      let blanks, nonBlanks = xs |> List.partition (snd3 >> Option.isNone)
                                 |> (List.map fst3) *** (List.map (second3 Option.get))

      let collectWeightings (account, value, contraAccount) =
        // if this leg has it's own contra account, it automatically balances, it has zero weight to return
        match contraAccount with
          | NoCAccount ->
              match value with
                | Un q               -> [q, tryCommodityOf account |> orGlobalCommodity]
                | Ve (q, c)          -> [q, c]
                | Tf ((q,c), (p, m)) -> [q*p*multiplierOf c, m]
                | Th ((q,c), p)      -> [q*p*multiplierOf c, measureOf c]
                | Cr ((q,c), _)      -> [q, c]
                | Cl (_, (p, m))     -> [p, m]
          | _ -> []
      let ys = nonBlanks |> List.collect collectWeightings
      // now group up and check the result balancing (or not)
      let residual = ys |> List.groupBy snd
                        |> List.map (second (List.sumBy fst))
                        |> List.filter (fun (_,d) -> d <> 0M)

      let convertXs (account, value, contraAccount) =
        let cvalue =
          function | Un q -> V (q, tryCommodityOf account |> orGlobalCommodity)
                   | Ve v -> V v
                   | Tf (a, b) -> T (a, b)
                   | Th ((q, c), p) -> T ((q, c), (p, measureOf c))
                   | Cr (a, b) -> X (b, a)
                   | Cl (a, b) -> X (a, b)
        let cca = function | NoCAccount -> None | Self -> Some account | CAccount c -> Some c
        (account, value |> cvalue, cca contraAccount)

      let defaultContraAccount = List.tryHead blanks
      Some <| match List.length blanks, defaultContraAccount, List.length residual with
                | 0, _, 0       -> {Date = dt; Payee = py; Narrative = na; Postings = xs |> List.map (second3 Option.get >> convertXs)} |> Choice1Of2
                | 0, _, _       -> Choice2Of2 "Entry doesn't balance! Need a contra account but none specified."
                | 1, Some _ , 0 -> Choice2Of2 "Entry balances, but a contra account has been specified."
                | 1, Some ca, _ -> let zs = residual |> List.map (fun (c, v) -> (ca, Ve (-v, c), NoCAccount))
                                   {Date = dt; Payee = py; Narrative = na; Postings = nonBlanks @ zs |> List.map convertXs} |> Choice1Of2
                | _, _, _       -> Choice2Of2 "Entry has more than one default contra account, there should only be one."
  | _ -> None

let loadJournal filename =
  let elts = loadRJournal filename |> List.map stripComments

  // handle imports here, we will need to rec the load function and combine results
  // will have to split this function later to handle
  let imports = elts |> List.choose (function Import i -> Some i | _ -> None)

  let header = elts |> List.choose (function Header h -> Some h | _ -> None)
                    |> List.tryHead
                    |> Option.defaultValue {Name = "Untitled"; Commodity = None; CapitalGains = None; Note = None}

  let accountDecls = elts |> List.choose (function Account a -> Some (a.Account, a) | _ -> None)
                          |> Map.ofList

  let commodityDecls = elts |> List.choose (function RJournalElement.Commodity c -> Some (c.Symbol, c) | _ -> None)
                            |> Map.ofList

  let register = elts |> List.choose (balanceEntry header.Commodity accountDecls commodityDecls)
                      |> List.choose (function | Choice1Of2 x -> Some x
                                               | Choice2Of2 s -> printfn "%s" s; None)
                      |> List.groupBy (fun e -> e.Date)
                      |> Map.ofList

  let prices = elts |> List.choose (function Prices (c, m, xs) -> Some ((c, m), xs) | _ -> None)
                    |> List.groupByApply fst snd
                    |> List.map (second (List.collect id >> Map.ofList))
                    |> Map.ofList

  let splits = elts |> List.choose (function Split (d, c, k1, k2) -> Some (c, (d, k1, k2)) | _ -> None)
                    |> List.groupByApply fst snd
                    |> Map.ofList

  let assertions = elts |> List.choose (function Assertion (d,a,v) -> Some (d,a,v) | _ -> None)

  // Avengers... assemble!
  {
    Meta = header
    AccountDecls = accountDecls
    CommodityDecls = commodityDecls
    Register = register
    Prices = prices
    Splits = splits
    Assertions = assertions
  }