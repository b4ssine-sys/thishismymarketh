# Municipal Bond Market Mod for Cities: Skylines

A municipal bond market simulation for Cities: Skylines 1 that introduces debt issuance, credit ratings, yield curves, interest rate swaps, primary auctions, and citizen-driven trading into the city economy.

---

## Table of Contents

1. [Design Philosophy](#design-philosophy)
2. [Architecture Overview](#architecture-overview)
3. [Core Systems](#core-systems)
   - [Cash Flow Tracking](#cash-flow-tracking)
   - [Credit Model](#credit-model)
   - [Interest Rate Model](#interest-rate-model)
   - [Bond Market](#bond-market)
   - [Primary Auction](#primary-auction)
   - [Bond Portfolio](#bond-portfolio)
   - [Debt Issuance and Lifecycle](#debt-issuance-and-lifecycle)
   - [Market Friction](#market-friction)
   - [Citizen Demand Engine](#citizen-demand-engine)
   - [Interest Rate Swaps](#interest-rate-swaps)
   - [Issuer Credit Model](#issuer-credit-model)
4. [Bond Pricing Model](#bond-pricing-model)
5. [Serialization](#serialization)
6. [Threading Model](#threading-model)
7. [Technical Constraints](#technical-constraints)
8. [UI Architecture](#ui-architecture)
9. [File Structure](#file-structure)
10. [Constants Reference](#constants-reference)
11. [CI and Testing](#ci-and-testing)

---

## Design Philosophy

**Real mechanics, game scale.** Municipal bonds, credit ratings, yield curves, and interest rate swaps follow their real-world counterparts in structure. But time scales, dollar amounts, and complexity are compressed to fit a game where players manage a city, not a trading desk.

**The city IS the issuer.** The player's city occupies both sides of the bond market. Players buy bonds from external issuers (investing surplus cash) and issue their own municipal bonds (raising capital at the cost of future coupon obligations). Issue too much debt and your credit rating drops, making all borrowing more expensive.

**Consequences, not punishment.** Default on a bond and you get a yield spike that decays over time, not a game-over. Missed payments transition through a delinquent grace period before hard default. The system encourages learning through feedback loops rather than hard failure states.

**Everything connects to the real economy.** The mod hooks into the game's actual money flow via `EconomyExtensionBase.OnUpdateMoneyAmount`. Cash flow history, income, expenses, demographics, and the city's bank balance all feed into credit ratings, yield calculations, and citizen demand. The bond market responds to how well you run your city.

**Pure/testable separation.** Domain models, pricing math, credit logic, and the debt book carry no Unity or game dependencies. They compile and test under net8.0 xUnit on CI. Game-coupled files compile against a stub-reference harness so CI type-checks the entire mod without the game DLLs.

---

## Architecture Overview

The mod is built across 46 source files organized by concern:

```
MyFirstMod/
  BondMarket.cs                     Domain models, views (BondView, SwapView), EngineSnapshot, BondPricing
  BondMarketEngine.cs               Simulation engine: tick, orders, cash, metrics, servicing, trading, snapshot, save/load
  CimDemandEngine.cs                Citizen demand, trading volumes, pressure, absorption capacity
  Credit/CreditModel.cs             Annualized credit metrics; FromAnnual shared with previews
  Credit/IssuerModel.cs             Issuer archetypes, migration, default hazard, IssuerView
  Credit/RatingEngine.cs            Rating grid with liquidity notch
  Credit/RatingExplainer.cs         WO-35: distance to the next notch up and down
  DebtBook.cs                       Issued debt lifecycle, pro-rata servicing, repayment, placement, invariants
  Diagnostics/SelfCheck.cs          WO-34: gathers and writes the self-check line
  Diagnostics/StartupReport.cs      WO-34: the self-check line format
  Engine/AlertPolicy.cs             WO-38: one alert per month, by priority
  Engine/CommandQueue.cs            WO-40: lock-free order queue
  Engine/EngineCommand.cs           WO-40: orders and their results
  Engine/IssueTemplates.cs          The issuance menu
  Engine/MaturityLadder.cs          WO-37: 36-month payment ladder and shortfall projection
  Loading.cs                        Level load/unload: creates the window and toolbar button, self-check
  Localization.cs                   String table
  Market/AuctionPricing.cs          Fair and offered yield, clearing spread (engine and ticket)
  Market/DeterministicRandom.cs     Serializable xorshift128 PRNG
  Market/Friction.cs                Bid-ask spread, price impact, depth, underwriting fee
  Market/IssuancePreview.cs         WO-36: what a deal does before it runs
  Market/PrimaryAuction.cs          Uniform-price auction
  Market/RateProcess.cs             Mean-reverting short rate on a business cycle
  Mod.cs                            IUserMod entry point, version
  Presentation/Advisor.cs           Treasury recommendation
  Presentation/Wording.cs           Plain-language labels and the activity feed
  Pricing/SwapPricing.cs            Swap valuation off the curve
  Pricing/YieldCurve.cs             Nelson-Siegel term structure
  ResidentialBuildingLog.cs         Building event observer
  SaveDataExtension.cs              Save/load bridge
  Sim/CashSettlement.cs             What the economy callback returns
  Sim/EconomyReader.cs              Reflection binding to the game ledger
  Sim/TreasuryProbe.cs              Reflection read of the live treasury
  StateSerializer.cs                Sectioned binary format v12, FNV-1a checksums, legacy migration, atomic load
  UI/BondMarketWindow.cs            The window: title, workspace tabs, feed, memory
  UI/BorrowView.cs                  Borrow workspace: ticket, ladder, outstanding bonds
  UI/BriefingView.cs                WO-39: first-run briefing
  UI/ChirperBridge.cs               WO-38: alerts to the Chirper
  UI/InvestView.cs                  Invest workspace: issuer cards, offerings, holdings
  UI/RiskView.cs                    Risk workspace: gauge, regime, swaps, settings
  UI/Theme.cs                       Colour-blind-safe palette
  UI/ToggleButton.cs                Toolbar button; delivers alerts
  UI/TreasuryView.cs                Treasury workspace
  UI/UiPrefs.cs                     Per-player UI memory
  UI/Widgets.cs                     Labels, bars, buttons; BoundLabel (WO-42)
  UI/WorkspaceView.cs               Workspace base: redraw only on change
```

The engine runs on the simulation thread via `OnUpdateMoneyAmount`. The UI runs on Unity's main thread. They share no mutable state: the UI places orders on a lock-free queue and reads immutable snapshots the engine publishes (see [Threading Model](#threading-model)).

**Lifecycle flow:**
1. `Loading.OnLevelLoaded` sets `BondMarketEngine.NeedsReset = true` and creates the UI
2. On the next simulation tick, the engine detects `NeedsReset`, clears all state, and begins tracking cash flow
3. After the first tick, the engine generates the initial market bond offerings
4. Each period (one game month), the engine ages bonds, services debt, settles swaps, runs citizen trading, migrates issuer credit, and generates quarterly reports
5. `Loading.OnLevelUnloading` destroys the UI and clears the runtime bindings

---

## Core Systems

### Cash Flow Tracking

The engine maintains a rolling window of 60 cash flow samples. Each tick records the balance delta, with the mod's own cash movements subtracted out so only organic city revenue and expense enters the window:

```
organic = (currentMoney - prevMoney) - modCashDeltaPending
```

An outlier filter rejects deltas beyond 6 standard deviations of the rolling mean (one-off game grants, desyncs). The window feeds downstream metrics: per-tick operating income/expense (preferring the game ledger via `EconomyReader` when available, falling back to balance-delta proxy), and revenue volatility.

An authoritative cash cursor (`_tickCash`) is seeded from the game's balance each tick, then adjusted by every mod cash operation. This prevents multiple operations in one tick from all reading the same stale `LastCashAmount`. Player orders run inside the tick (WO-40), so they settle against the same cursor.

Large-scale currency accumulators (`_realizedPL`, `_swapPL`, `_totalCitizenProceeds`) use `double` precision internally and cast to `float` only at the public API boundary, preventing drift from millions of small additions.

### Credit Model

Credit assessment uses a two-stage pipeline, both pure and unit-tested:

**CreditModel** (`Credit/CreditModel.cs`) produces annualized metrics from per-tick operating flows and the `DebtBook`:

| Metric | Definition |
|--------|-----------|
| Annual Debt Service | Sum of OutstandingPrincipal x CouponRate across active/delinquent bonds |
| Debt Burden | Annual Debt Service / Annual Operating Revenue |
| DSCR | Annual NOI / Annual Debt Service |
| Months of Reserves | Cash Reserves / Monthly Operating Expense |

**RatingEngine** (`Credit/RatingEngine.cs`) maps these metrics to a rating via a calibrated grid:

| Rating | Min DSCR | Max Debt Burden |
|--------|----------|-----------------|
| AAA    | >= 2.50  | <= 8%           |
| AA     | >= 2.00  | <= 12%          |
| A      | >= 1.50  | <= 18%          |
| BBB    | >= 1.25  | <= 25%          |
| BB     | >= 1.05  | <= 32%          |
| B      | >= 0.90  | <= 40%          |
| CCC    | (else)   | (else)          |
| D      | Hard floor: active arrears or DSCR < 0.2 |

A **liquidity notch** adjusts one grade: 6+ months of reserves upgrades; under 1 month downgrades. The base/notch path caps at CCC; D is reserved for the hard floor (active arrears or coverage collapse).

### Interest Rate Model

The exogenous interest rate environment is driven by a **mean-reverting short rate** (Vasicek / Ornstein-Uhlenbeck):

```
dr = kappa * (theta - r) * dt + sigma * sqrt(dt) * Z
```

- `kappa = 0.15` (mean-reversion speed per year)
- `theta` drifts on a slow business cycle: `baseTheta + amplitude * sin(phase)`
- `sigma = 0.006` (volatility per sqrt-year), scaled by the player's Rate Volatility setting
- Floor at 0.1%, ceiling at 50%

The short rate feeds a **Nelson-Siegel yield curve**:

```
y(t) = Level + Slope * (1 - e^-x)/x + Curvature * ((1 - e^-x)/x - e^-x)
```

where `x = t / Lambda`. The short end equals `Level + Slope` (the short rate); the long end approaches `Level` (short rate + term premium). This gives a spot rate for any maturity, so duration has a real price consequence and the curve can invert, steepen, or flatten with the rate cycle.

Nothing the city does moves the exogenous rate. The city's **borrowing rate** layers its own fiscal adjustment (`debtBurden * 2%`) and over-hedge penalty on top.

### Bond Market

The market offers bonds from six fictional municipal issuers (Regional Water District, Clean Power Grid, State Transit Auth, Port Authority, County Health System, District School Board). Each issuer has its own migrating credit rating, so market bonds price off the **issuer's** spread, not the city's.

When the market drops below 6 active bonds, new ones are generated with randomly selected face values (10K-250K) and terms (4-16 periods), priced near par at the issuer's current yield.

Initial market offerings provide a fixed set of six starter bonds for a gentle introduction before randomized bonds appear.

### Primary Auction

When the city issues a bond, placement runs through a **uniform-price auction**:

- **Bid-to-cover** = `clamp(exp(concession / 40) * demandScore, 0, 4.0)`
  - Concession = (offered yield - fair yield) in basis points
  - Fair yield = spot rate from the yield curve at the bond's tenor
- **Minimum cover** = 0.75: below this the deal fails
- Between 0.75 and 1.0: partial fill at the offered yield
- Above 1.0: fully subscribed

The UI shows estimated bid-to-cover before the player commits (pre-trade indication). A 75bp underwriting fee is deducted from net proceeds.

### Bond Portfolio

Purchased bonds move from the market list to the portfolio. Each period:

1. Remaining periods decrement
2. At maturity: face value credited, realized P/L recorded, bond removed
3. Otherwise: periodic coupon credited

Buy/sell prices include bid-ask spread scaled by issuer rating and duration. Bulk purchases additionally pay square-root price impact against the issue's depth.

### Debt Issuance and Lifecycle

The city can issue up to 5 bonds from 7 templates (general-obligation and revenue-backed):

| Template               | Face Value | Term    | Revenue Source     |
|------------------------|-----------|---------|-------------------|
| Emergency Note         | 25,000    | 2 years | General Obligation |
| Municipal Note         | 75,000    | 3 years | General Obligation |
| Water Revenue Bond     | 150,000   | 4 years | Water              |
| Electric Revenue Bond  | 200,000   | 5 years | Electricity        |
| Transit Revenue Bond   | 300,000   | 6 years | Public Transport   |
| Infrastructure Bond    | 400,000   | 7 years | General Obligation |
| Capital Bond           | 750,000   | 10 years| General Obligation |

Revenue bonds (Phase 6, WO-11) are backed by a specific city service's income stream. When the backing service has positive net revenue, the bond's offered yield is reduced by 50bp; when the service is losing money, yield increases by 100bp. Revenue source is tracked via the `RevenueSource` enum (`None`, `Water`, `Electricity`, `PublicTransport`) and persisted on each bond.

Issuance is gated by: bond count cap, credit rating (not D), no active defaults or arrears, lockout window cleared, minimum demand score (0.10), and absorption capacity.

**Bond lifecycle** follows a four-state model managed by the `DebtBook`:

```
Active --> Delinquent --> Defaulted
  |            |
  v            v
  Redeemed   (arrears cleared --> Active)
```

- **Active**: coupon paid on time
- **Delinquent**: missed payment rolls into arrears (which compound at coupon + 300bp). Grace period of 2 periods before hard default
- **Defaulted**: triggered by grace expiry or a missed maturity principal payment. Issuance suspended, lockout window of 12 periods
- **Redeemed**: terminal state (term elapsed, all amounts cleared)

The `DebtBook` owns servicing via **two-pass pro-rata allocation**: pass 1 ages bonds and computes per-bond dues (arrears + coupon + maturing principal); pass 2 allocates the cash budget proportionally to each bond's share of total dues, then applies payments in priority order (arrears, coupon, principal). This ensures service outcomes are independent of array insertion order. `PlacedFraction` tracks primary take-up; `OutstandingPrincipal` tracks what's owed. These are strictly separated to prevent the infinite-money loop that occurred when one field carried both meanings.

Each bond tracks cumulative `InterestPaid` (arrears + coupon payments) and `PrincipalRepaid` separately for reporting. Early-retired bonds (via `RemoveBond` or `RepayByBudget`) enter the redeemed history with proper state cleanup.

The servicing triad (`GRACE_PERIODS`, `ARREARS_SPREAD`, `DEFAULT_LOCKOUT_PERIODS`) is coupled: grace sets the delinquency window, arrears spread penalizes during grace, and lockout blocks issuance after resolution. Changing one without the others skews the default lifecycle.

### Market Friction

Secondary-market trading carries realistic costs (`Market/Friction.cs`):

| Component | Formula | Purpose |
|-----------|---------|---------|
| Underwriting fee | 75bp of par | Deducted from issuance proceeds |
| Bid-ask half-spread | `BaseHalfSpread(rating) * (1 + duration/10)` | Scales with credit risk and maturity |
| Price impact | `50bp * sqrt(orderSize / depth)` | Large orders move the price |
| Depth per period | 20% of outstanding face | Tradeable liquidity |

### Citizen Demand Engine

`CimDemandEngine` models citizen participation in the bond market using city demographics:

**Demand Score** = weighted sum of Financial Health (35%), Citizen Confidence (30%), and Bond Appeal (35%), adjusted by a momentum multiplier. Feeds into yield adjustment and absorption capacity.

**City demographics** are sampled from the game's district and citizen managers: population, happiness, health, education, employment rate, land value, crime rate. When game data is unavailable (early load), fiscal-state fallback demographics are synthesized.

**Citizen trading** runs once per period: buy/sell volumes are computed from population, demand, and appeal. Buy volume absorbs unplaced primary inventory via the `DebtBook`; sell volume creates market pressure that adjusts yields. A transaction log records each period's activity.

**Absorption capacity** caps total issuable face based on population, wealth factors, and demand score.

### Interest Rate Swaps

Up to 5 interest rate swaps allow hedging against rate fluctuations on issued debt:

- Swaps settle against the **exogenous market floating rate** (the short rate), not the city's borrowing rate
- Pay-fixed: settlement = (floating - fixed) * notional / 12
- Receive-fixed: settlement = (fixed - floating) * notional / 12
- Negative settlements the city cannot afford force-terminate the swap
- Over-hedging (hedge notional > debt face) incurs a borrowing rate penalty

Swap valuation uses textbook single-curve pricing off the Nelson-Siegel term structure (`Pricing/SwapPricing.cs`): annuity, par swap rate, and mark-to-market.

### Issuer Credit Model

Each of the six market issuers has a **sector archetype** with a home rating and recovery rate:

| Archetype | Home Rating | Recovery |
|-----------|-------------|----------|
| Water District | AA | 70% |
| Power Grid | A | 70% |
| School Board | AA | 65% |
| Health System | A | 65% |
| Transit Authority | BBB | 50% |
| Port Authority | BBB | 45% |

**Annual migration** (A-2): each issuer's rating drifts one notch per year via a Markov process with mean-reversion toward home. Base migration probability 8% per direction, biased 10% toward home.

**Default hazard** (A-4): `BaseAnnualDefaultProb(rating) * hazardMultiplier`. The multiplier is player-configurable: Historical (x1), Standard (x25), Volatile (x60). On default, portfolio holders receive recovery x par; the issuer's market paper is pulled and the entity restructures back to its home rating.

A **deterministic PRNG** (`DeterministicRandom`, xorshift128 deriving from `System.Random`) ensures stochastic outcomes survive save/load and cannot be save-scummed. Its 4-uint state is serialized. The engine maintains two RNG streams: a persisted sim-stream (`_rng`) for all simulation-thread stochastic decisions (rate process, migrations, market bond generation, citizen activity), and a cosmetic `_cosmeticRng` for UI-thread operations (buy buttons) that do not affect determinism.

---

## Bond Pricing Model

Present value uses the closed-form annuity formula (O(1)):

```
d  = (1 + r)^-n
PV = C * (1 - d) / r  +  F * d
```

Where `r` = annual yield / 12, `C` = periodic coupon, `n` = remaining periods.

**Required yield** for city bonds:

```
requiredYield = GetRequiredYield(cityBorrowingRate, rating)
              + defaultSpike
              + wealthAdjustment
              + demandAdjustment
              + pressureAdjustment
```

Credit spreads by rating:

| Rating | Spread (bp) |
|--------|-------------|
| AAA    | 20          |
| AA     | 45          |
| A      | 90          |
| BBB    | 160         |
| BB     | 275         |
| B      | 450         |
| CCC    | 800         |
| D      | 1500        |

The required yield is rate-limited to 50bp/period to prevent snapping, and capped at 50%.

---

## Serialization

`StateSerializer` uses a **sectioned binary format** (v12) with per-section FNV-1a checksums:

```
[version byte]
[scalars section: length | checksum | payload]
[cashFlowHistory section]
[pressureHistory section]
[portfolio bonds section]
[issued bonds section]
[redeemed bonds section]
[market bonds section]
[swaps section]
[transactions section]
[reports section]
[issuers section]        (v8+)
```

Deserialization is atomic: the entire state is validated against invariants (I1-I9) in a staging object before being applied, including redeemed bonds. All deserialize counts (bonds, swaps, transactions, reports, issuers) are bounded to reject corrupt saves early. Legacy saves (v1-6) are migrated through `ReadLegacyFlat`; v7 saves load through the same sectioned reader with newer fields defaulted. v9 adds the `RevenueSource` field per bond; v12 adds per-bond `InterestPaid` and `PrincipalRepaid` tracking. No exception ever escapes `TryDeserialize`.

**Invariants validated on load:**
- I1: PlacedFraction in [0, 1]
- I2: OutstandingPrincipal >= 0
- I3: OutstandingPrincipal <= FaceValue
- I4: OutstandingPrincipal <= FaceValue * PlacedFraction
- I5: RemainingPeriods >= 0
- I6: Redeemed bonds have zero outstanding
- I7: PlacedFraction only modified by PlacePrimary (structural guarantee)
- I8: Arrears >= 0
- I9: PeriodsInArrears >= 0

---

## Threading Model

Cities: Skylines runs simulation logic and UI on separate threads. Since WO-40
they share no mutable state and there is no lock:

- **Orders in.** Every player action is an `EngineCommand` placed on a lock-free
  queue (`Engine/CommandQueue.cs`) with `BondMarketEngine.Submit`. The simulation
  thread drains the queue at the start of its next tick and carries the orders
  out against the balance the game has just handed it. Nothing reads the game's
  balance outside the tick. While the game is paused, orders wait.
- **Snapshots out.** At the end of any tick where something changed, the engine
  publishes a new immutable `EngineSnapshot`: every figure the UI shows, the
  market, portfolio, issued, redeemed, swap, report and activity lists as fresh
  arrays of view objects, the yield curve, the credit metrics, the settings and
  the results of recent orders. The UI reads the latest one and never touches
  engine state. Idle ticks publish nothing and allocate nothing.
- **Results.** Each order's outcome (success, count, cash moved, message) comes
  back in `Snapshot.RecentResults`; `LastProcessedSequence` tells the UI when its
  order has run.
- **City change.** Orders still queued when a city is reset or restored are
  discarded, so they never run in the next city.
- **Saving.** `SerializeState` captures directly on the simulation thread. From
  any other thread it retries until no tick ran during the capture (a sequence
  counter bumped at the start and end of each tick), so a save never records half
  a tick.

Heavy per-period work (portfolio revaluation, credit model, rate pipeline, demand chain, demographic sampling) runs **once per period**, not every tick.

The engine harness (`Tests.Engine/`) compiles the real engine against the stub
game types with a fake treasury and drives whole ticks, orders, partial cash
moves and saves on CI.

---

## Technical Constraints

### .NET Framework 3.5

Cities: Skylines 1 runs on Unity Mono targeting .NET 3.5:

- No string interpolation, auto-property initializers, expression-bodied members
- No null-conditional operators, LINQ, nameof, async/await
- Use `string.Format`, explicit backing fields, full property syntax, manual loops

### Integer Overflow Protection

The game's `AddResource`/`FetchResource` accept `int`. Large bond values (multiplied by the internal scale of 100) can exceed `int.MaxValue`. All money operations use `long` arithmetic and chunk into `int.MaxValue`-sized pieces.

### Cash Cursor

The engine maintains an authoritative `_tickCash` cursor seeded from the balance the game hands `OnUpdateMoneyAmount` each tick, decremented/incremented by every mod cash operation, player orders included. Multiple operations in one tick see accurate running balances.

Because those moves happen inside the economy callback, the callback returns what it was handed plus the change it measured in the game's live treasury field (`Sim/TreasuryProbe.cs`, `Sim/CashSettlement.cs`). That keeps every move exactly once whether the game assigns the return value to its treasury or ignores it. The self-check line reports `cash=live-field`, or `cash=unbound` if the field could not be found.

### Mod Cash Isolation

The rolling cash flow window must reflect only the city's organic revenue and expense. A `_modCashDeltaPending` accumulator tracks the net cash the mod itself moved since the last sample, subtracted from the raw balance delta so coupon payments, maturities, placement proceeds, and swap settlements never distort the credit model.

---

## UI Architecture

The window (`UI/BondMarketWindow.cs`, 860x566) has four workspaces with an activity
feed along the bottom (Directive 03 section 3.1). It reads only the engine's
published `EngineSnapshot` and places orders with `BondMarketEngine.Submit`
(WO-40).

| Workspace | Replaces | Shows first |
|---|---|---|
| Treasury | Report, summary line | Rating badge with DSCR, burden and reserves bars and the distance to the next notch up and down (WO-35); cash runway; next three payments; one recommendation (`Presentation/Advisor.cs`) |
| Borrow | Debt | Issuance ticket with a live preview (WO-36, `Market/IssuancePreview.cs`); 36-month maturity ladder (WO-37); outstanding bonds |
| Invest | Market, Portfolio, Positions | Issuer cards with rating trend; offerings; holdings with unrealized P&L |
| Risk | Hedging, Settings | Rate-exposure gauge; market regime; swaps; settings |

- **Preview equals result.** The ticket and the engine share `AuctionPricing` (fair
  and offered yield), `PrimaryAuction`, `Friction.UnderwritingFee` and
  `CreditModel.FromAnnual`. The engine harness checks that a previewed deal fills
  exactly as shown.
- **Redraw only what changed (WO-42).** A workspace redraws only when the snapshot
  version or its own controls changed. `BoundLabel` rewrites a label only when its
  value changed, formatting through one shared `StringBuilder`. An idle city
  publishes no snapshot, so the window writes no labels. The harness counts the
  writes to prove it.
- **Alerts (WO-38).** The engine raises candidates where they happen, and
  `Engine/AlertPolicy.cs` delivers at most one per in-game month. The toolbar button,
  which is always on screen, posts new alerts to the Chirper through
  `UI/ChirperBridge.cs`. The Chirper is bound at runtime; if it can't be found, the
  alert goes to Debug Output and the feed.
- **Player memory (WO-39).** Window position, last workspace, text size and
  "briefing seen" are kept in Unity `PlayerPrefs` (`UI/UiPrefs.cs`), so they follow
  the player across cities. Ratings use the Okabe-Ito colour-blind-safe palette and
  always print their letters. Every figure has a tooltip.

---

## File Structure

| File | Lines | Purpose |
|------|-------|---------|
| `BondMarket.cs` | 543 | Domain models, views (BondView, SwapView), EngineSnapshot, BondPricing |
| `BondMarketEngine.cs` | 2506 | Simulation engine: tick, orders, cash, metrics, servicing, trading, snapshot, save/load |
| `CimDemandEngine.cs` | 211 | Citizen demand, trading volumes, pressure, absorption capacity |
| `Credit/CreditModel.cs` | 83 | Annualized credit metrics; FromAnnual shared with previews |
| `Credit/IssuerModel.cs` | 192 | Issuer archetypes, migration, default hazard, IssuerView |
| `Credit/RatingEngine.cs` | 43 | Rating grid with liquidity notch |
| `Credit/RatingExplainer.cs` | 214 | WO-35: distance to the next notch up and down |
| `DebtBook.cs` | 476 | Issued debt lifecycle, pro-rata servicing, repayment, placement, invariants |
| `Diagnostics/SelfCheck.cs` | 48 | WO-34: gathers and writes the self-check line |
| `Diagnostics/StartupReport.cs` | 26 | WO-34: the self-check line format |
| `Engine/AlertPolicy.cs` | 92 | WO-38: one alert per month, by priority |
| `Engine/CommandQueue.cs` | 57 | WO-40: lock-free order queue |
| `Engine/EngineCommand.cs` | 104 | WO-40: orders and their results |
| `Engine/IssueTemplates.cs` | 54 | The issuance menu |
| `Engine/MaturityLadder.cs` | 64 | WO-37: 36-month payment ladder and shortfall projection |
| `Loading.cs` | 77 | Level load/unload: creates the window and toolbar button, self-check |
| `Localization.cs` | 109 | String table |
| `Market/AuctionPricing.cs` | 38 | Fair and offered yield, clearing spread (engine and ticket) |
| `Market/DeterministicRandom.cs` | 84 | Serializable xorshift128 PRNG |
| `Market/Friction.cs` | 69 | Bid-ask spread, price impact, depth, underwriting fee |
| `Market/IssuancePreview.cs` | 65 | WO-36: what a deal does before it runs |
| `Market/PrimaryAuction.cs` | 67 | Uniform-price auction |
| `Market/RateProcess.cs` | 49 | Mean-reverting short rate on a business cycle |
| `Mod.cs` | 37 | IUserMod entry point, version |
| `Presentation/Advisor.cs` | 75 | Treasury recommendation |
| `Presentation/Wording.cs` | 89 | Plain-language labels and the activity feed |
| `Pricing/SwapPricing.cs` | 49 | Swap valuation off the curve |
| `Pricing/YieldCurve.cs` | 53 | Nelson-Siegel term structure |
| `ResidentialBuildingLog.cs` | 194 | Building event observer |
| `SaveDataExtension.cs` | 44 | Save/load bridge |
| `Sim/CashSettlement.cs` | 17 | What the economy callback returns |
| `Sim/EconomyReader.cs` | 199 | Reflection binding to the game ledger |
| `Sim/TreasuryProbe.cs` | 78 | Reflection read of the live treasury |
| `StateSerializer.cs` | 638 | Sectioned binary format v12, FNV-1a checksums, legacy migration, atomic load |
| `UI/BondMarketWindow.cs` | 272 | The window: title, workspace tabs, feed, memory |
| `UI/BorrowView.cs` | 325 | Borrow workspace: ticket, ladder, outstanding bonds |
| `UI/BriefingView.cs` | 81 | WO-39: first-run briefing |
| `UI/ChirperBridge.cs` | 95 | WO-38: alerts to the Chirper |
| `UI/InvestView.cs` | 198 | Invest workspace: issuer cards, offerings, holdings |
| `UI/RiskView.cs` | 192 | Risk workspace: gauge, regime, swaps, settings |
| `UI/Theme.cs` | 51 | Colour-blind-safe palette |
| `UI/ToggleButton.cs` | 50 | Toolbar button; delivers alerts |
| `UI/TreasuryView.cs` | 191 | Treasury workspace |
| `UI/UiPrefs.cs` | 63 | Per-player UI memory |
| `UI/Widgets.cs` | 192 | Labels, bars, buttons; BoundLabel (WO-42) |
| `UI/WorkspaceView.cs` | 60 | Workspace base: redraw only on change |

**Total: 46 files, 8,514 lines.**

Pure files (no game dependencies) compile into the unit-test project; everything else compiles against the stub shims, and the engine harness (`Tests.Engine/`) runs the whole engine and UI against them.

---

## Constants Reference

| Constant | Value | Location | Purpose |
|----------|-------|----------|---------|
| WINDOW_SIZE | 60 | Engine | Cash flow history samples |
| TICKS_PER_PERIOD | 15 | Engine | Fallback ticks per bond period |
| MIN_MARKET_BONDS | 6 | Engine | Market regeneration threshold |
| INTERNAL_UNIT_SCALE | 100 | Engine | Game money units per display unit |
| MAX_ISSUED_BONDS | 5 | Engine | Maximum city bonds outstanding |
| MAX_ACTIVE_SWAPS | 5 | Engine | Maximum interest rate swaps |
| GRACE_PERIODS | 2 | Engine | Delinquent periods before default |
| ARREARS_SPREAD | 3% | Engine | Penalty rate on arrears |
| DEFAULT_LOCKOUT_PERIODS | 12 | Engine | Issuance lock-out after default |
| DEFAULT_PENALTY_PER_EVENT | 12 | Engine | Penalty points per default |
| MAX_DEFAULT_PENALTY | 60 | Engine | Penalty cap |
| PeriodsPerYear | 12 | BondPricing | Monthly bond periods |
| RATE_KAPPA | 0.15 | Engine | Mean-reversion speed |
| RATE_BASE_THETA | 4% | Engine | Long-run mean short rate |
| RATE_SIGMA | 0.6% | Engine | Short-rate volatility |
| RATE_LAMBDA | 2.0 | Engine | Nelson-Siegel decay (years) |
| ConcessionScaleBp | 40 | PrimaryAuction | Auction sensitivity |
| MinCover | 0.75 | PrimaryAuction | Minimum bid-to-cover |
| CoverCap | 4.0 | PrimaryAuction | Maximum bid-to-cover |
| UnderwritingFeeRate | 75bp | Friction | Issuance fee |
| ImpactK | 50bp | Friction | Impact at full-depth order |
| DepthFraction | 20% | Friction | Depth as fraction of outstanding |
| HAZARD_STANDARD | 25x | IssuerModel | Default hazard multiplier (default setting) |
| FORMAT_VERSION | 12 | StateSerializer | Current save format |
| REFRESH_INTERVAL | 4.0 | Panel | UI auto-refresh (seconds) |
| MAX_ROWS | 6 | Panel | Visible rows in bond list |

---

## CI and Testing

The project has two CI jobs (`.github/workflows/ci.yml`):

**pure-logic-tests**: Builds and runs the xUnit test project (`Tests/`) against pure source files (BondMarket.cs, DebtBook.cs, CimDemandEngine.cs, StateSerializer.cs, Credit/*, Market/*, Pricing/*) on net8.0. These tests cover the debt lifecycle, credit model, auction mechanics, serialization round-trips, and invariant validation.

**compile-check**: Links all 21 mod source files into `CI/CompileCheck.csproj` and builds against stub declarations in `Stubs/` (GameStubs.csproj). The stubs declare minimal API surface for ICities, ColossalFramework, ColossalFramework.UI, and UnityEngine — just enough for the compiler to resolve every member the mod touches. No .NET SDK runs in the dev environment; tests run exclusively on GitHub Actions.
