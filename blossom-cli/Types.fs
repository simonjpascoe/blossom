module Types

open Types

type Command =
  // Application management
  | Quit
  | Clear
  // File management
  | Load of string
  | Reload
  // Accounting
  | Balances of string
  | Journal of string
  | BalanceSeries of Tenor * bool * string
  // Meta
  | Meta of MetaRequest