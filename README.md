# blossom
Double entry plain text accounting for traders

Blossom is a _yet another_ plain text acount application cli similar to [Ledger-cli](https://github.com/ledger/), [Hledger](https://github.com/simonmichael/hledger) and [Beancount](https://github.com/beancount). As with the other similar implementations:
- blossom works locally, without interacting with webs servers, banks or the like, your data stays with you.
- As it's plain text, you can store your files in whatever source control repository or document management system you like. I **don't** recommend storing them on github.com!
- blossom only reads your data, it doesn't know how to write and won't smash up your data.
- a small eco-system of helper utilities is in the planning (pretty-print, price import, etc - rather custom)

## But this breaks IFRS Rule 123-R!
Yes. But then you should get a real accountant if you need to follow those rules to the letter. It tries to strike a balance between real accounting and common sense. You can raise a ticket if you like.

## Does it work yet?
Yes and no. It works, but it doesn't do a whole lot and is not overly optimised. For "toy" inputs, it works fine, and I have successfully converted a ~2500 line input into the tool. But on the flip side, not all the features are there and the outputs are rather basic.

I am personally using this to capture my accounts, starting with 2020. Here's a statistics output snapshot (`meta stats`) showing what's inside my core file so far:
| Item         | Detail |
| ------------ | ------ |
| Range        | 2020-01-01 -> 2020-12-06 |
| Transactions | 915 |
| Accounts     | 113 |
| Commodities  | 5   |
| Payees       | 222 |
| Hashtags     | 11  |
| Prices       | 0   |


## Can I contribute?
There's not much to contribute to right now, star the project and come back later.

## Why is there _another_ clone of ledger?
1. I find that the other systems don't cater well for more advanced trading strategies such as options or futures, which large numbers of trading assets (100+ can soon accrue).
1. My use of MS Money for the last 15 years didn't really cut it for trading and multicurrency handling; it's long dead and I needed another solution.
1. I want to customize reports inside the app rather than have to write on top of results from others.
1. I felt like a challenge!

## Differences to others
After the initial accounting portions which are fairly standard across pta software (balance checking, validations, reporting etc), there is a focus on trading support:
- Enhanced PnL reporting taking into account expenses, transfers, cross currency impact
- Support for non-nav assets such as mtm futures
- Possible future support for risk, stress reports (option evaluation, derivatives linkage to underlyings, volatility surfaces etc) for OTC products.

Most of the "standard" formatting works in blossom, although there some extra helpers and formatting supported to cut down on boilerplate and monotonous copy/paste. You can see some of the format ideas at https://github.com/blossom-hub/blossom/blob/master/documentation/JournalFormat.md.

## Plans
I'm currently migrating a bigger codebase from my initial implementation into this repository and upgrading several features.
1. Migrate existing infrastructure
1. Code up and improve original accouting
1. Add more checks / validation
1. Focus trading expanding features
1. Add a [VSCode](https://code.visualstudio.com/) extension for both _editing_ and _processing_ the data.
