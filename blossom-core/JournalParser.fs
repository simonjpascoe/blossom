module JournalParser

open System

open FParsec

open Shared
open Types
open Definitions
open ParserShared

type UserState =
  {
    IndentSize : int
    IndentCount : int
    AccountConvention: AccountConvention option
  }
  with
    static member Default = {
      IndentCount = 0
      IndentSize = 2
      AccountConvention = None
    }

(*
  Journal parsing returns a data in a raw journal (RJournal) format which is the bottom
  of a processing pipeline. This is not widely checked / validated / expanded but is tagged
  with meta items, such as position on major elements, to help users with locating errors
  in their journal files.

  It is close to a tokenized format. Some types are similar to upper types which makes the
  code a little longer, but simpler to read and process.

  The number of types here is attempted to be minimised, and the parser kept as simple as possible.
  Note in particular that account hierarchies are not exploded into lists here, and utilise Account itself
  instead of the list version.
*)

type DAssertion = {
  Account: Account
  Value: Value
}

type DPrice = {
  Commodity: Commodity
  Price: Value
}

type DSplit = {
  Commodity: Commodity
  K1: int
  K2: int
}

type Contra = CS | CV of Account

type DTransferEntry =
  | Posting of account:Account * value:Value option * contra:Contra option
  | PComment of Comment
  | PCommented of DTransferEntry * Comment

type DTransfer = {
  Payee: string option
  Narrative: string
  Tags: string Set
  Entries: DTransferEntry list
}

type DDividend = {
  Account: Account
  Asset: Commodity
  PerUnitValue: Value
  PayDate: DateTime option
  Settlement: Account option
  Receivable: Account option
  Income: Account option
}

type DTrade = {
  Account: Account
  Settlement: Account option
  CapitalGains: Account option
  Asset: Commodity
  Quantity: decimal
  PerUnitPrice: Value
  LotName: string list
  Reference: string option
  Expenses: (Account * Value * Contra option) list
}

type RElement =
  | Comment2 of text:string
  | Assertion of DAssertion
  | Price of DPrice
  | Split of DSplit
  | Transfer of DTransfer
  | Dividend of DDividend
  | Trade of DTrade

type RJournalElement =
  // Structural
  | Indent of int
  | StartRegion of name:string
  | EndRegion
  | Comment1 of text:string
  // Operational
  | Header of JournalMeta
  | Import of string
  // Declarations and defintions
  | Alias of string * Account
  | Account of AccountDecl
  | Commodity of CommodityDecl
  // Core elements
  | Item of sequence:SQ * flagged:bool * element:RElement
  | Prices of commodity:Commodity * measure:Commodity * xs:(DateTime * decimal) list

type private RSubElement =
  | SAccount of string * Account
  | SAccountConvention of AccountConvention
  | SComment of Comment
  | SCommodity of string * Commodity
  | SCommodityClass of CommodityClass
  | SExpensePosting of Account * Value * Contra option
  | SExternalIdent of string * string
  | SLotNames of string list
  | SMTM
  | SMultiplier of int
  | SName of string
  | SNote of string
  | SPaydate of DateTime
  | SPropagate
  | SReference of string
  | SQuoteDP of int
  | SValuationMode of ValuationMode

// TODO Add line number to parser for the root items

// Basic parser helpers
// To get comment association correct, we are precise on whitespace and layout (to borrow)
//  the Haskell terminology
let nSpaces0 = skipMany (skipChar ' ')
let nSpaces1 = skipMany1 (skipChar ' ')
let rol b = restOfLine b |>> fun s -> s.TrimEnd()

let str = pstring
let sstr = skipString
let sstr1 s = skipString s >>. nSpaces1
let p0 p = p .>> nSpaces0
let p1 p = p .>> nSpaces1

let indented p =
  let sp n m = skipArray (n*m) (skipChar ' ') >>? p
  getUserState >>= fun s -> sp s.IndentCount s.IndentSize

let increaseIndent p =
  getUserState >>= fun st -> let st2 = {st with IndentCount = st.IndentCount+1}
                             setUserState st2 >>. p .>> setUserState st

// Primitive parsers
let pdate = tuple3 (pint32 .>> pchar '-') (pint32 .>> pchar '-') pint32 |>> DateTime

let psequence : Parser<SQ, UserState> = pdate .>>. opt (sstr "/" >>. puint32)

let pnumber = pfloat |>> decimal

let pCommodity =
  let first = letter <|> digit <|> anyOf "."
  many1Chars2 first (letter <|> digit <|> anyOf ".:-()_") |>> Types.Commodity

let pValue = pnumber .>> nSpaces1 .>>. pCommodity |>> Value

let pSubItems ss = (choice ss .>> nSpaces0 .>> skipNewline) |> indented |> many

let pWordPlus = many1Chars2 letter (letter <|> digit <|> anyOf "._-")

let pCommodityClass : Parser<CommodityClass, UserState> =
  choice [stringReturn "Currency" Currency
          stringReturn "Equity"   Equity
          stringReturn "Option"   Option
          stringReturn "Future"   Future]

let pValuationMode : Parser<ValuationMode, UserState> =
  choice [stringReturn "Latest" Latest
          stringReturn "Historical" Historical]

// Account name elements and parsing (Note, accounts can no longer have spaces inside)
let pAccountConvention : Parser<AccountConvention, UserState> =
  choice [stringReturn "F5" Financial5
          stringReturn "F7" Financial7]

let pAccount =
  let accountValidChars = letter <|> digit <|> anyOf "()[]{}"
  let pAccountElt = many1Chars2 upper accountValidChars
  let hierarchy = sepBy1 pAccountElt (pchar ':') .>>. opt (skipChar '/' >>. pAccountElt)
  // Parsing depends upon the convention. If no convention, anything goes.
  // A convention mandates that the stub is part of a hierarchy (no 1 level accounts)
  let parser =
    getUserState >>=
      fun st -> match st.AccountConvention with
                  | None    -> hierarchy
                  | Some ac -> let stub = ac |> getAccountConventionStubs |> List.map (fun x -> pstring x .>> skipChar ':') |> choice
                               pipe2 stub hierarchy (fun a (b,c) -> [a] @ b, c)
  parser |>> fun (xs, v) -> Types.Account (String.concat ":" xs, v)

let pPosting =
  let contra = sstr "~" >>. opt pAccount
  p0 pAccount .>>. opt (pValue .>>. opt (nSpaces1 >>. contra)) .>> skipNewline
    |>> fun (a, vx) -> match vx with
                         | None                    -> (a, None, None)
                         | Some (v, None)          -> (a, Some v, None)
                         | Some (v, Some None)     -> (a, Some v, Some CS)
                         | Some (v, Some (Some x)) -> (a, Some v, Some (CV x))

let pPostingM =
  let contra = sstr "~" >>. opt pAccount
  p0 pAccount .>>. pValue .>>. opt (nSpaces1 >>. contra)
    |>> fun ((a, v), c) -> match c with
                             | None          -> (a, v, None)
                             | Some None     -> (a, v, Some CS)
                             | Some (Some x) -> (a, v, Some (CV x))

// RSubElement Parsers
let private spCommodity ctag = sstr1 ctag >>. pCommodity |>> curry SCommodity ctag
let private spAccount atag = sstr1 atag >>. pAccount |>> curry SAccount atag
let private spNote = sstr1 "note" >>. rol false |>> SNote
let private spConvention = sstr1 "convention" >>. pAccountConvention |>> SAccountConvention
let private spName = sstr1 "name" >>. rol false |>> SName
let private spCommodityClass = sstr1 "class" >>. pCommodityClass |>> SCommodityClass
let private spValuationMode = sstr1 "valuation" >>. pValuationMode |>> SValuationMode
let private spQuoteDP = sstr1 "dp" >>. pint32 |>> SQuoteDP
let private spMultiplier = sstr1 "multiplier" >>. pint32 |>> SMultiplier
let private spExternalIdent = sstr1 "externalid" >>. many1CharsTill (letter <|> digit <|> anyOf ".") (pchar ' ')
                                                 .>> nSpaces0
                                                 .>>. rol false
                                                 |>> SExternalIdent
let private spPaydate = sstr1 "paydate" >>. pdate |>> SPaydate
let private spMTM = sstr "mtm" >>% SMTM
let private spPropagate = sstr "propagate" >>% SPropagate
let private spLotNames = sstr1 "lot" >>. sepBy1 pWordPlus (skipChar ',' >>. nSpaces0) |>> SLotNames
let private spReference = sstr1 "reference" >>. pWordPlus |>> SReference
let private spExpensePosting = sstr1 "expense" >>. pPostingM |>> SExpensePosting

// TODO Support detecting comments on restOfLine items (or parse till ';' etc)
// let wrapCommented c elt = match c with | Some c -> Commented(elt, c)
//                                        | None   -> elt

// let pComment0 xs = anyOf xs >>. restOfLine false |>> Types.Comment
// let pComment = pComment0 ";*" |>> Comment

// let pOptLineComment p = p .>> nSpaces0 .>>. opt (pComment0 ";").>> optional newline

// RJournalElement Parsers

let pIndent =
  let pval = sstr1 ".indent" >>. pint32 .>> nSpaces0 .>> skipNewline
  pval >>= fun i -> (updateUserState (fun u -> {u with IndentSize = i}) >>. preturn (Indent i))

let pStartRegion = sstr1 "#region" >>. rol true |>> StartRegion
let pEndRegion = stringReturn "#endregion" EndRegion
let pComment1 = skipAnyOf ";*" >>. rol true |>> Comment1

let pHeader =
  let getConvention = List.tryPick (function (SAccountConvention x) -> Some x | _ -> None)
  let subitems = [spCommodity "commodity"; spNote; spConvention]
  sstr1 "journal" >>. rol true .>>. increaseIndent (pSubItems subitems)
    >>= fun (t, ss) -> (updateUserState (fun u -> {u with AccountConvention = getConvention ss}) >>. preturn (t, ss))
    |>> fun (t, ss) ->
          Header {Name = t
                  Commodity = ss |> List.tryPick (function SCommodity ("commodity", x) -> Some x | _ -> None)
                  Note = ss |> List.tryPick (function SNote x -> Some x | _ -> None)
                  Convention = getConvention ss}

let pImport = sstr1 "import" >>. rol true |>> Import

let pAlias = sstr1 "alias" >>. p1 (many1Chars2 letter (letter <|> digit)) .>>. pAccount .>> newline |>> Alias

let pAccountDecl =
  let subitems = [spCommodity "commodity"; spNote; spValuationMode; spPropagate]
  sstr1 "account" >>. pAccount .>> skipNewline .>>. increaseIndent (pSubItems subitems)
    |>> fun (a, ss) -> Account {Account = a
                                Commodity = ss |> List.tryPick (function SCommodity ("commodity", x) -> Some x | _ -> None)
                                Note = ss |> List.tryPick  (function SNote x -> Some x | _ -> None)
                                ValuationMode = ss |> List.tryPick (function SValuationMode x -> Some x | _ -> None)
                                                   |> Option.defaultValue Historical
                                Propagate = List.contains SPropagate ss}

let pCommodityDecl =
  let subitems = [spName; spCommodity "measure"; spQuoteDP; spCommodity "underlying"; spCommodityClass; spMultiplier; spMTM; spExternalIdent]
  sstr1 "commodity" >>. pCommodity .>> skipNewline .>>. increaseIndent (pSubItems subitems)
    |>> fun (t, ss) -> Commodity {Symbol = t
                                  Measure = ss |> List.tryPick  (function SCommodity ("measure", m) -> Some m | _ -> None)
                                  QuoteDP = ss |> List.tryPick (function SQuoteDP i -> Some i | _ -> None)
                                  Underlying = ss |> List.tryPick (function SCommodity ("underlying", m) -> Some m | _ -> None)
                                  Name = ss |> List.tryPick  (function SName n -> Some n | _ -> None)
                                  Klass = ss |> List.tryPick  (function SCommodityClass c -> Some c | _ -> None)
                                  Multiplier = ss |> List.tryPick (function SMultiplier m -> Some (decimal m) | _ -> None)
                                  ExternalIdents = ss |> List.choose (function SExternalIdent (a,b) -> Some (a,b) | _ -> None) |> Map.ofList
                                  Mtm = List.contains SMTM ss}

let pElement =
  // Fairly simple entries
  let pComment = sstr1 "comment" >>. rol false |>> Comment2
  let pAssertion = sstr1 "assert" >>. p1 pAccount .>>. pValue |>> fun (a, v) -> Assertion {Account = a; Value = v}
  let pPrice = sstr1 "price" >>. p1 pCommodity .>>. pValue |>> fun (c, v) -> Price {Commodity = c; Price = v}
  let pSplit = sstr1 "split" >>. tuple3 (p1 pCommodity) (p1 pint32 ) pint32 |>> fun (c, k1, k2) -> Split {Commodity = c; K1 = k1; K2 = k2}

  // Composite entries
  let pTransfer =
    let spn (n:string) = n.Split([|'|'|], 2) |> List.ofArray
                                             |> function | [x] -> (None, x) | x::xs -> (Some (x.Trim()), List.head xs) | _ -> (None, n)

    let subitems = choice [pPosting |>> Posting] |> indented |> many
    rol true .>>. increaseIndent subitems
      |>> fun (header, entries) -> let payee, narrative = spn header
                                   Transfer {Payee = payee; Narrative = narrative; Tags = Set.empty; Entries = entries}

  let pDividend =
    let subitems = [spPaydate; spAccount "account"; spAccount "settlement"; spAccount "receivable"; spAccount "income"]
    sstr1 "dividend" >>. tuple3 (p1 pAccount) (p1 pCommodity) pValue .>> skipNewline .>>. increaseIndent (pSubItems subitems)
      |>> fun ((a, c, v), ss) -> let paydate = ss |> List.tryPick (function SPaydate d -> Some d | _ -> None)
                                 let s = ss |> List.tryPick (function SAccount ("settlement", d) -> Some d | _ -> None)
                                 let r = ss |> List.tryPick (function SAccount ("receivable", d) -> Some d | _ -> None)
                                 let i =  ss |> List.tryPick  (function SAccount ("income", d) -> Some d | _ -> None)
                                 Dividend {Account = a; Asset = c; PerUnitValue = v; PayDate = paydate;
                                           Settlement = s; Receivable = r; Income = i}

  let pTrade =
    let subitems = [spLotNames; spReference; spAccount "settlement"; spAccount "cg"; spExpensePosting]
    sstr1 "trade" >>. (p1 pAccount) .>>. (p1 pValue .>> sstr1 "@" .>>. pValue) .>> skipNewline .>>. increaseIndent (pSubItems subitems)
      |>> fun ((account, ((q, c), price)), ss) -> let lns = ss |> List.tryPick (function SLotNames xs -> Some xs | _ -> None)
                                                               |> Option.defaultValue []
                                                  let reference = ss |> List.tryPick (function SReference r -> Some r | _ -> None)
                                                  let s = ss |> List.tryPick (function SAccount ("settlement", d) -> Some d | _ -> None)
                                                  let cg = ss |> List.tryPick (function SAccount ("cg", d) -> Some d | _ -> None)
                                                  let expenses = ss |> List.choose (function SExpensePosting (a,b,c) -> Some (a,b,c) | _ -> None)
                                                  Trade {Account = account
                                                         Settlement = s
                                                         CapitalGains = cg
                                                         Asset = c
                                                         Quantity = q
                                                         PerUnitPrice = price
                                                         LotName = lns
                                                         Reference = reference
                                                         Expenses = expenses}

  choice [pComment; pAssertion; pPrice; pSplit; pDividend; pTrade; pTransfer]

let pItem = p1 psequence .>>. pElement |>> fun (s, e) -> Item (s, false, e)

let pPrices =
  let subitems = (p1 pdate .>>. p0 pnumber .>> skipNewline) |> indented |> many
  sstr1 "prices" >>. p1 pCommodity .>>. pCommodity .>> skipNewline .>>. increaseIndent subitems
    |>> fun ((c, m), xs) -> Prices (commodity = c, measure = m, xs = xs)

let pRJournal =
  let parsers = [
    pIndent; pStartRegion; pEndRegion; pComment1;
    pHeader; pImport; pAlias; pAccountDecl; pCommodityDecl;
    pItem; pPrices
  ]
  spaces >>. many (getPosition .>>. choice parsers .>> skipMany newline) .>> eof

let loadRJournal filename =
  let result = runParser pRJournal UserState.Default (FromFile filename)
  result