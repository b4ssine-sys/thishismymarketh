# Municipal Bond Market Mod

A comprehensive municipal bond market simulation for **Cities: Skylines 1**. Issue debt backed by your city's creditworthiness, trade bonds on a simulated secondary market, hedge interest rate risk with swaps, and watch citizens participate as investors in your city's financial instruments.

## Features

- **Bond Issuance** -- Issue general-obligation and revenue bonds in 7 templates ranging from 25K Emergency Notes to 750K Capital Bonds, placed through a uniform-price primary auction with bid-to-cover mechanics.
- **Revenue Bonds** -- Water, Electric, and Transit revenue bonds backed by specific city service income streams, offering yield discounts when the backing service is financially healthy.
- **Credit Ratings** -- AAA through D rating scale driven by debt burden, DSCR, employment, population health, and financial reserves. Rating migration follows a Markov process with mean reversion.
- **Yield Curve** -- Nelson-Siegel term structure with a Vasicek mean-reverting short rate process and business-cycle phase shifts.
- **Debt Lifecycle** -- 4-state lifecycle (Active, Delinquent, Defaulted, Redeemed) with grace periods, arrears accrual, and default lockout windows.
- **Market Friction** -- Bid-ask spreads, market impact, underwriting fees (75bp), and execution price modeling.
- **Interest Rate Swaps** -- Pay-fixed / receive-fixed swaps for hedging floating-rate exposure, with auto-hedge and over-hedge penalty mechanics.
- **Citizen Trading** -- Citizens buy and sell bonds based on demand scoring, absorption capacity, and population-driven market pressure.
- **Quarterly Reports** -- Comprehensive financial health reports covering revenue, expenses, NOI, DSCR, employment, confidence, and credit outlook.
- **Four workspaces** -- Treasury, Borrow, Invest and Risk, with an activity feed along the bottom. The rating explains itself, every deal is previewed before it runs, a 36-month ladder warns of shortfalls, and alerts reach the game's Chirper.
- **Localization** -- All UI strings routed through a centralized `Loc.Get()` string table for translation readiness.
- **Deterministic PRNG** -- xorshift128 random number generator with serializable state, preventing save-scumming of stochastic outcomes.
- **Save/Load** -- Sectioned binary format (v12) with FNV-1a checksums, automatic migration from legacy formats (v1-11).

## Installation

**Players:** subscribe on the Steam Workshop, or download `MyFirstMod.dll` from a
tagged GitHub release and place it (and nothing else) in your Mods folder:

- **Windows**: `%LOCALAPPDATA%\Colossal Order\Cities_Skylines\Addons\Mods\MyFirstMod\`
- **macOS**: `~/Library/Application Support/Colossal Order/Cities_Skylines/Addons/Mods/MyFirstMod/`
- **Linux**: `~/.local/share/Colossal Order/Cities_Skylines/Addons/Mods/MyFirstMod/`

There must be no `Source\` folder next to the DLL: the game compiles any source it
finds there and would load the mod twice. Enable "Municipal Bond Market" in the
Content Manager, then press **Shift+B** in-game or click the toolbar icon.

**Developers:** from the repo root in PowerShell, run `.\deploy.ps1`. It builds
`MyFirstMod.dll` against your installed game (`-GameDir` if Steam is not in the
default place), copies only the DLL, and removes any old `Source\` folder.
`.\deploy.ps1 -FromSource` is the fallback for machines without a .NET SDK.

**Diagnosing a missing UI:** every level load writes one line to the Debug Output
(F7) starting with `[MyFirstMod] SELF-CHECK`, carrying the mod version, save format,
whether the game-ledger reader bound, the game build, the load mode and whether
the UI was created. Include that line in any bug report.

## Releasing

1. Bump the version in `MyFirstMod/Mod.cs` and `MyFirstMod/MyFirstMod.csproj` (CI
   fails if they disagree).
2. Push a tag `vX.Y.Z` matching that version. `.github/workflows/release.yml`
   runs the tests, builds the DLL against the real game assemblies and attaches
   `MyFirstMod.dll`, a zip of the Workshop folder and a SHA-256 to the release.
3. Publish that folder to the Workshop from the in-game Content Manager.

The game's assemblies cannot be committed, so the release build fetches them from
repository secrets: `CS_MANAGED_ZIP_URL` (a zip of `Assembly-CSharp.dll`,
`ColossalManaged.dll`, `ICities.dll` and `UnityEngine.dll` from
`Cities_Data/Managed`), plus optional `CS_MANAGED_ZIP_TOKEN` and
`CS_MANAGED_ZIP_SHA256`. The stub-built CI assembly never ships.

## Usage

Open the window with the toolbar icon or **Shift+B**. The first time, a three-card
briefing walks through the treasury, borrowing, and a first move. Every figure has
a tooltip. Every action is an order that runs on the next simulation tick, so
nothing happens while the game is paused. The title bar shows each order's outcome.

### Treasury
The rating badge and the three things that decide it: debt service coverage (DSCR),
debt burden and months of reserves. Each has a bar with a green tick where the next
notch up begins and an amber tick where the rating would drop, plus a line such as
*"+0.18 DSCR or +1.2 months of reserves to reach AA"*. Also here: cash runway, the
next three payments due, one recommendation with a button that acts on it, and the
latest quarterly report.

### Borrow
The **issuance ticket**: pick a bond type, then set the yield you offer with the
slider or the 5bp buttons. The ticket updates live with the expected bid-to-cover,
the fill, the proceeds after the 75bp underwriting fee, the annual coupon cost, and
the city's coverage and rating after the deal. It opens at the clearing price (cover
1.0x). A deal priced too tight shows low cover and fails. **Clear** returns to the
clearing price.

| Template | Face Value | Term | Type |
|---|---|---|---|
| Emergency Note | 25,000 | 2yr | General Obligation |
| Municipal Note | 75,000 | 3yr | General Obligation |
| Water Revenue Bond | 150,000 | 4yr | Revenue (Water) |
| Electric Revenue Bond | 200,000 | 5yr | Revenue (Electricity) |
| Transit Revenue Bond | 300,000 | 6yr | Revenue (Transit) |
| Infrastructure Bond | 400,000 | 7yr | General Obligation |
| Capital Bond | 750,000 | 10yr | General Obligation |

Revenue bonds stay switched off until Gate B validates the per-service revenue read.
Beside the ticket, the **maturity ladder** shows 36 months of coupon and principal
due. A month whose projected cash falls short turns amber two months ahead. Below it
are the outstanding bonds, with Repay and Pay down 25%/50%.

### Invest
Cards for the six market issuers, showing rating, last move and spread. The market's
offerings can be bought at the ask. Your holdings show unrealized P&L and can be
sold at the bid, and there are bulk lots (10 x 1M, 10 x 10M, 1B) that pay price
impact.

### Risk
The rate-exposure gauge (hedged share of debt, with the over-hedge zone), the market
regime (short rate, 2- and 10-year spot, curve shape, where the cycle is heading),
the city's swaps (Auto-hedge, Sell 25%/50%, Exit all) and settings: issuer default
frequency, rate volatility, citizen trading, revenue bonds, text size, and replaying
the briefing.

### Alerts
At most one alert per in-game month, highest priority first: a missed coupon, a
shortfall projected within two months, a downgrade, a failed auction, a big rate
move. Alerts post to the Chirper, so you hear about them with the window closed.

## Architecture

The mod separates pure financial logic from game-dependent code for testability:

```
MyFirstMod/
  BondMarket.cs          -- Domain models (Bond, BondView, SwapView, enums)
  BondMarketEngine.cs    -- Simulation engine (EconomyExtensionBase)
  UI/                    -- Window, four workspaces, briefing, Chirper bridge
  Presentation/          -- Pure wording, feed and recommendation logic
  Engine/                -- Command queue, orders, templates, ladder, alerts
  DebtBook.cs            -- Pure debt lifecycle manager
  CimDemandEngine.cs     -- Citizen demand scoring
  StateSerializer.cs     -- Binary save/load with checksums
  Localization.cs        -- String table for UI text
  Credit/
    CreditModel.cs       -- Annualized credit metrics
    RatingEngine.cs      -- Rating evaluation grid
    IssuerModel.cs       -- Issuer archetypes and migration
  Market/
    PrimaryAuction.cs    -- Uniform-price auction
    Friction.cs          -- Spread, impact, underwriting
    RateProcess.cs       -- Vasicek rate model
    DeterministicRandom.cs -- Serializable xorshift128 PRNG
  Pricing/
    YieldCurve.cs        -- Nelson-Siegel yield curve
    SwapPricing.cs       -- Interest rate swap valuation
  Sim/
    EconomyReader.cs     -- Reflection-bound game ledger reader
```

Pure logic files (DebtBook, Credit/, Market/, Pricing/) compile against .NET 8 for unit testing. Game-dependent files compile against stub shims in `Stubs/` for CI type-checking.

## CI

GitHub Actions jobs on every push (`.github/workflows/ci.yml`):
- **pure-logic-tests** -- xUnit tests for the pure modules (debt book, credit model, auction, friction, curve, swaps, serializer)
- **compile-check** -- every file under `MyFirstMod/` (by glob) type-checked against stub shims, targeting the game's .NET 3.5 runtime
- **mod-dll** -- builds the real DLL against the game assemblies when the secret is configured, so a stub/real API mismatch shows up before a release

## Technical Details

See [MUNICIPAL_BOND_MARKET.md](MUNICIPAL_BOND_MARKET.md) for the complete technical specification covering all systems, constants, threading model, and serialization format.

## License

This project is provided as-is for educational and modding purposes.
