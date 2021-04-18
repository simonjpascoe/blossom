module Types

open System

type SQ = DateTime * uint32 option

type AccountConvention = Financial5 | Financial7

// "NewType" style definitions
type Comment = Comment of string
type Commodity = Commodity of string
type LotName = AutoLotName of string | CustomLotName of string
type Account = Account of string

type Value = decimal * Commodity

type Tenor = Y | M | H | Q | W of DayOfWeek | D

type LotType = Open | Extend | Reduce | Close

type ValuationMode =
  | Latest
  | Historical

type CommodityClass =
  | Currency
  | Equity
  | Option
  | Future

type JournalMeta = {
  Name: string
  Commodity: Commodity option
  Note: string option
  Convention: AccountConvention option
}

type AccountDecl = {
  Account: Account
  ValuationMode: ValuationMode
  Commodity: Commodity option
  Note: string option
  Propagate: bool
}

type CommodityDecl = {
  Symbol: Commodity
  Measure: Commodity option
  QuoteDP: int option
  Underlying: Commodity option
  Name: string option
  Klass: CommodityClass option
  Multiplier: decimal option
  Mtm: bool
  ExternalIdents: Map<string, string>
}

type ClosingTrade = {
  Date: SQ
  Settlement: Account
  CapitalGains: Account option
  Quantity: decimal
  PerUnitPrice: Value
  UnadjustedPnL: Value
  LotName: string
  Reference: string option
  Expenses: (Account * Value * Account) list
}

type OpeningTrade = {
  Date: SQ
  Account: Account
  Settlement: Account
  Asset: Commodity
  Quantity: decimal
  PerUnitPrice: Value
  LotName: string
  Reference: string option
  Expenses: (Account * Value * Account) list
  Closings: ClosingTrade list
}

type Entry = {
  Flagged : bool
  Date: SQ
  Payee: string option
  Narrative: string
  Tags: string Set
  Postings: (Account * Value * Account) list
}

type Journal = {
  Meta: JournalMeta
  AccountDecls: Map<Account, AccountDecl>
  CommodityDecls: Map<Commodity, CommodityDecl>
  Register: Map<SQ, Entry list>
  InvestmentAnalysis: OpeningTrade list
  Prices: Map<Commodity * Commodity, Map<DateTime, decimal>>
  Splits: Map<Commodity, (DateTime * int * int) list>
  Assertions: (DateTime * Account * Value) list
}

type Filter = {
  Timespan: ((bool * DateTime) option * (bool * DateTime) option) option
  Accounts: string list
  Payees: string list
  Narrative: string option
  Commodities: string list
  Denominations: string list
  Tags: string list
}

type BalancesRequest = {
  GroupToTop: bool
  HideZeros: bool
  Flex: bool
}

type JournalRequest = {
  HideZeros: bool
  Flex: bool
  FlaggedOnly: bool
}

type SeriesRequest = {
  GroupToTop: bool
  HideZeros: bool
  Flex: bool
  Cumulative: bool
  Tenor: Tenor
}

type LotRequest = {
  Consolidated: bool
  OpenOnly: bool
  ClosedOnly: bool
}

type CheckRequest = Assertions

type MetaRequestType = Statistics | Accounts | Commodities | Payees | Tags_
type MetaRequest = {
  RequestType: MetaRequestType
  Regex: string option
}

type MetaStatistics = {
  Range: DateTime * DateTime
  Transactions: int * int
  Accounts: int
  Commodities: int
  Payees: int
  Tags: int
  Assertions: int
  Prices: int
}
type MetaResult =
  | Statistics of MetaStatistics
  | MetaResultSet of string Set