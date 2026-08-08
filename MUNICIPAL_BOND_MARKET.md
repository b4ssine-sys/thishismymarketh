# Municipal Bond Market Mod for Cities: Skylines

A comprehensive financial derivatives system for Cities: Skylines 1 that introduces municipal bond trading, debt issuance, credit ratings, and interest rate swap hedging into the city simulation.

---

## Table of Contents

1. [Design Philosophy](#design-philosophy)
2. [Architecture Overview](#architecture-overview)
3. [Core Systems](#core-systems)
   - [Cash Flow Tracking](#cash-flow-tracking)
   - [Credit Rating Engine](#credit-rating-engine)
   - [Bond Market](#bond-market)
   - [Bond Portfolio](#bond-portfolio)
   - [Debt Issuance](#debt-issuance)
   - [Interest Rate Swaps](#interest-rate-swaps)
   - [Volatility Tracking](#volatility-tracking)
   - [Auto-Hedge System](#auto-hedge-system)
4. [Bond Pricing Model](#bond-pricing-model)
5. [Threading Model](#threading-model)
6. [Technical Constraints](#technical-constraints)
7. [UI Architecture](#ui-architecture)
8. [File Structure](#file-structure)
9. [Constants Reference](#constants-reference)
10. [Iterative Development Process](#iterative-development-process)

---

## Design Philosophy

The mod adapts real-world fixed-income financial instruments into a game context, balancing realism with playability:

**Real mechanics, game scale.** Municipal bonds, credit ratings, yield curves, and interest rate swaps follow their real-world counterparts in structure. A bond pays periodic coupons and returns face value at maturity. Credit ratings derive from debt burden and debt service coverage ratio (DSCR). Interest rate swaps exchange fixed for floating payments on a notional amount. But the time scales, dollar amounts, and complexity are compressed to fit a game where players manage a city, not a trading desk.

**The city IS the issuer.** Unlike a typical bond market game, the player's city occupies both sides of the bond market. Players can buy bonds from external issuers (investing surplus cash for returns) AND issue their own municipal bonds (raising capital at the cost of future coupon obligations). This dual role creates a natural tension: issue too much debt and your credit rating drops, making all borrowing more expensive.

**Consequences, not punishment.** Default on a bond and you get a yield spike penalty that decays over time, not a game-over. Over-leverage yourself and your credit rating drops from AAA toward D, widening spreads and making new issuance more expensive. The system encourages learning through feedback loops rather than hard failure states.

**Everything connects to the real economy.** The mod hooks into the game's actual money flow via `EconomyExtensionBase.OnUpdateMoneyAmount`. Cash flow history, income, expenses, and the city's actual bank balance all feed into the credit rating and yield calculations. The bond market isn't a separate mini-game; it responds to how well you run your city.

---

## Architecture Overview

The mod is built on four files with clear separation of concerns:

```
BondMarket.cs        Domain models and pricing math (pure logic, no game dependencies)
BondMarketEngine.cs  Simulation engine (EconomyExtensionBase, runs on simulation thread)
BondMarketPanel.cs   UI panel and toggle button (UIPanel, runs on main thread)
Loading.cs           Lifecycle management (LoadingExtensionBase)
```

The engine runs on the simulation thread via the `OnUpdateMoneyAmount` callback. The UI runs on Unity's main thread. A `lock(_lock)` object synchronizes all shared state between them. The engine exposes snapshot methods that copy data under the lock, so the UI never holds a reference into mutable simulation state.

**Lifecycle flow:**
1. `Loading.OnLevelLoaded` sets `BondMarketEngine.NeedsReset = true` and creates the UI components
2. On the next simulation tick, the engine detects `NeedsReset`, clears all state, and begins tracking cash flow
3. After the first tick, the engine generates the initial market bond offerings
4. Every `TICKS_PER_PERIOD` (15) ticks, the engine ages all bonds, services issued debt, settles swaps, and decays default penalties
5. `Loading.OnLevelUnloading` destroys the UI components

---

## Core Systems

### Cash Flow Tracking

The engine maintains a rolling window of 60 cash flow samples. Each simulation tick, it records the delta between the current and previous internal money amount:

```
_cashFlowHistory[_windowIndex] = (float)(currentMoney - _prevMoney)
_windowIndex = (_windowIndex + 1) % WINDOW_SIZE
```

This circular buffer feeds all downstream metrics: gross income (sum of positive deltas), total expenses (sum of negative deltas), net operating income, and revenue volatility. The window represents roughly 4 bond periods of history, giving the metrics enough smoothing to be stable while still responding to changes in city finances.

The internal unit scale factor of 100 converts between the game's internal money representation and display currency (display 10,000 = 1,000,000 internal units).

### Credit Rating Engine

Credit ratings follow a dual-metric system inspired by real municipal credit analysis:

| Rating | Max Debt Burden | Min DSCR |
|--------|----------------|----------|
| AAA    | < 5%           | > 3.0    |
| AA     | < 10%          | > 2.0    |
| A      | < 15%          | > 1.5    |
| BBB    | < 25%          | > 1.2    |
| BB     | < 35%          | > 0.9    |
| B      | (any)          | > 0.8    |
| CCC    | (any)          | > 0.5    |
| D      | (any)          | <= 0.5   |

**Debt Burden** = scheduled debt service / average income. Scheduled debt service is the sum of per-period coupon payments across all issued bonds. If no bonds are issued, 10% of average expenses is used as a proxy.

**DSCR** (Debt Service Coverage Ratio) = net operating income / scheduled debt service. A DSCR above 1.0 means the city generates enough surplus to cover its debt payments. Below 1.0 means it's running a deficit relative to debt obligations.

Two cash-reserve adjustments apply:
- If the city holds over 500,000 in display currency and DSCR is below 3.0, DSCR gets a +1.0 boost (capped at 10.0)
- If the city holds under 10,000 and DSCR is above 0.5, DSCR takes a -0.5 penalty (floored at 0.0)

These adjustments recognize that large cash reserves provide a buffer against temporary cash flow problems, while dangerously low balances signal real distress.

### Bond Market

The market offers bonds from 6 fictional municipal issuers (State Transit Auth, Regional Water District, County Health System, Port Authority, Clean Power Grid, District School Board). When the market drops below 6 active bonds, new ones are generated with:

- Face values: 10K, 25K, 50K, 75K, 100K, or 250K (randomly selected)
- Terms: 4 to 16 periods (randomly selected)
- Coupon rates: required yield +/- a random spread of up to 2%, clamped to 2%-25%

The initial market offers a fixed set of 6 starter bonds ranging from a 10K City Infrastructure Note (3%, 2 periods) to a 200K Capital Improvement Bond (6.5%, 12 periods), providing a gentle introduction before randomized bonds appear.

Market bonds age each period. When a market bond's remaining periods hit zero, it's silently removed (the issuer repaid it).

Additionally, three bulk purchase buttons allow rapid portfolio scaling:
- **Buy 10x 1M 5yr**: purchases up to ten 1,000,000 face value bonds with 60-period terms
- **Buy 10x 10M 5yr**: purchases up to ten 10,000,000 face value bonds
- **Buy 1B 5yr**: purchases a single 1,000,000,000 face value Institutional Sovereign Note

Bulk buy loops track remaining cash locally to avoid the stale `LastCashAmount` problem (see Technical Constraints).

### Bond Portfolio

Purchased bonds move from the market list to the portfolio list. Each period:

1. The bond's remaining periods decrement
2. If remaining periods hit zero: face value is credited to the city, realized P/L is recorded, and the bond is removed
3. Otherwise: the periodic coupon payment (face * couponRate / 12) is credited to the city and tracked in `CouponsReceived`

The portfolio view shows each bond's name, coupon rate, purchase price, unrealized P/L (current PV + coupons received - purchase price), and days until maturity. A **Sell All** button liquidates the entire portfolio at current market prices.

Lifetime P/L tracks both realized gains (from bonds that matured or were sold) and unrealized gains (mark-to-market on current holdings).

### Debt Issuance

The city can issue up to 5 bonds simultaneously (MAX_ISSUED_BONDS). Five templates are available, scaled by face value and maturity:

| Template            | Face Value | Term      | Periods |
|---------------------|-----------|-----------|---------|
| Emergency Note      | 25,000    | 2 years   | 24      |
| Municipal Note      | 75,000    | 3 years   | 36      |
| Revenue Bond        | 200,000   | 5 years   | 60      |
| Infrastructure Bond | 400,000   | 7 years   | 84      |
| Capital Bond        | 750,000   | 10 years  | 120     |

When a bond is issued:
- The face value is immediately credited to the city (proceeds)
- The coupon rate locks at the current required yield
- Each period, the city must pay a coupon of (face * rate / 12)
- At maturity, the city must repay the full face value

If the city cannot afford a coupon payment or maturity repayment, a **default** is triggered:
- Default penalty increases by 3
- Total defaults counter increments
- The bond is removed (written off)
- The yield spike from penalties makes all future borrowing more expensive

Default penalties decay by 1 per period, so a single default adds a temporary yield spike that wears off over time.

The City Debt tab displays issued bonds first (showing name, face value, rate, months remaining, total coupons paid, per-period cost, and a **Repay** button), followed by issuance templates below. **Pay 25%** and **Pay 50%** buttons allow early retirement of bonds by face value budget, retiring the smallest bonds first that fit within the budget.

### Interest Rate Swaps

Interest rate swaps allow the city to hedge against rate fluctuations on its issued debt. The system supports up to 5 active swaps (MAX_ACTIVE_SWAPS).

**How swaps work:**

A swap exchanges fixed-rate payments for floating-rate payments on a notional amount. Each period, the settlement is calculated:

- **If pay-fixed**: settlement = (floating rate - fixed rate) * notional / 12
  - Positive when floating > fixed (you receive money)
  - Negative when floating < fixed (you pay money)
- **If receive-fixed**: settlement = (fixed rate - floating rate) * notional / 12
  - Positive when fixed > floating (you receive money)
  - Negative when fixed < floating (you pay money)

The floating rate is the current benchmark rate, which moves with the city's financial health. The fixed rate locks at entry.

**Settlement mechanics:**
- Positive settlements: cash is added to the city
- Negative settlements: cash is deducted from the city
- If the city cannot afford a negative settlement, the swap is force-terminated (removed from active swaps, no P/L recorded for that period)
- Expired swaps (remaining periods hit zero) are removed after their final settlement

Swap P/L is tracked both per-swap (CumulativePL) and globally (_swapPL).

### Volatility Tracking

Revenue volatility is calculated at the end of each metrics recalculation using the cash flow history window:

```
mean = average of all cash flow samples
stddev = sqrt(sum of squared deviations from mean / window size)
volatility = stddev / average positive flow
```

Volatility is clamped to the range [0.0, 2.0]. A volatility above 0.5 (50%) is considered high risk for hedging purposes. The volatility metric feeds into the hedge recommendation system and is displayed in the Hedging tab summary.

### Auto-Hedge System

The **Auto-Hedge** button calculates the city's unhedged debt exposure and enters a single pay-fixed swap to cover it:

1. Sum the face values of all issued bonds (total debt face)
2. Sum the notional amounts of all active swaps (hedged notional)
3. Unhedged exposure = total debt face - hedged notional
4. If unhedged > 0, enter a new pay-fixed swap with:
   - Notional = unhedged amount
   - Fixed rate = current required yield
   - Term = weighted-average remaining periods of issued bonds (minimum 6)

The **hedge recommendation** system evaluates the current position:
- "No debt to hedge" - no issued bonds
- "Fully hedged" - hedge ratio >= 100%
- "HIGH RISK: Hedge X (Y% exposed)" - volatility > 50% and hedge ratio < 50%
- "Recommend: Hedge X unhedged" - positive unhedged exposure
- "Position balanced" - everything else

---

## Bond Pricing Model

Bond present value uses a standard discounted cash flow calculation:

```
PV = sum(coupon / (1+r)^t, t=1..n) + face / (1+r)^n
```

Where:
- `r` = annual yield / 12 (periodic rate)
- `coupon` = face * couponRate / 12 (periodic coupon)
- `n` = remaining periods

The required yield for the city's bonds is:

```
requiredYield = benchmarkRate + creditSpread + defaultSpike
```

**Benchmark rate** = 2% + (debtBurden * 8%), clamped to [1%, 15%]. This creates a feedback loop: more debt raises the benchmark, which raises borrowing costs.

**Credit spread** varies by rating:

| Rating | Spread |
|--------|--------|
| AAA    | 0.5%   |
| AA     | 1.2%   |
| A      | 2.2%   |
| BBB    | 3.8%   |
| BB     | 6.0%   |
| B      | 9.0%   |
| CCC    | 14.0%  |
| D      | 30.0%  |

**Default spike** = defaultPenalty * 0.048%. The total required yield is capped at 50%.

---

## Threading Model

Cities: Skylines runs simulation logic and UI on separate threads. This mod crosses that boundary:

- **Simulation thread**: `OnUpdateMoneyAmount` is called by the game's economy simulation. All internal state mutations (aging bonds, servicing debt, settling swaps, recording cash flow) happen here, inside `lock(_lock)`.
- **Main thread**: UI event handlers (button clicks, scroll events) call public methods on the engine. All public methods that touch shared collections acquire `lock(_lock)`.
- **Snapshot pattern**: `GetMarketSnapshot`, `GetPortfolioSnapshot`, `GetIssuedBondsSnapshot`, and `GetActiveSwapsSnapshot` copy data into caller-provided lists under the lock. The UI works with its own copies, never holding references into the engine's live lists.

Private methods called within the lock (suffixed with `Internal`) do not re-acquire the lock to avoid deadlock.

---

## Technical Constraints

### .NET Framework 3.5

Cities: Skylines 1 runs on Unity Mono targeting .NET 3.5. This means:

- No string interpolation (`$"..."`) - use `string.Format`
- No auto-property initializers - explicit backing fields and constructors
- No expression-bodied members - use full property syntax with `get { return ...; }`
- No null-conditional operators (`?.`) - explicit null checks
- No LINQ - manual loops for all collection operations
- No `nameof` operator
- No `async`/`await`

### Integer Overflow Protection

The game's `EconomyManager.AddResource` and `FetchResource` methods accept `int` parameters. When dealing with large bond values (1M, 10M, 1B face values), the internal representation (multiplied by 100) can exceed `int.MaxValue` (2,147,483,647). Casting a `long` above this threshold to `int` wraps to a negative number, which would catastrophically crash the city's bank balance.

Solution: all money operations use `long` arithmetic and chunk into `int.MaxValue`-sized pieces:

```csharp
while (internalAmount > 0)
{
    int chunk = (int)Math.Min(internalAmount, (long)int.MaxValue);
    em.AddResource(..., chunk, ...);
    internalAmount -= chunk;
}
```

### Stale LastCashAmount

`EconomyManager.LastCashAmount` is stale within a single tick; it reflects the balance at the start of the tick, not after intermediate operations. Bulk buy loops that check `LastCashAmount` repeatedly within one lock acquisition will see the same value and overspend.

Solution: bulk buy methods (`Buy10x1MBonds`, `Buy10x10MBonds`) read `LastCashAmount` once into a local `remaining` variable and decrement it after each purchase. The actual spend still goes through `TrySpendCash`, but the local tracking prevents attempting purchases the city can't afford.

### EconomyExtensionBase Limitations

The modding API only allows overriding `OnUpdateMoneyAmount(long)`. The methods `OnAddResource` and `OnFetchResource` are not virtual and cannot be overridden. All economy interactions must go through direct calls to `EconomyManager.AddResource` and `EconomyManager.FetchResource`.

---

## UI Architecture

The panel (`BondMarketPanel`) is an 800x520 `UIPanel` with four sections:

1. **Title bar** (40px): draggable via `UIDragHandle`, close button
2. **Summary** (56px): context-sensitive financial metrics, changes per active tab
3. **Tab bar** (30px): Market, Portfolio, City Debt, Hedging tabs + context buttons
4. **Bond list** (6 rows x 36px): scrollable via mouse wheel, three columns per row:
   - Info label (460px): bond/swap details
   - Price label (120px): right-aligned value
   - Action button (80px): context-sensitive (Buy/Sell/Issue/Repay/Exit)
5. **Footer** (30px): aggregate statistics

The panel auto-refreshes every 4 seconds when visible. Each tab has its own refresh method that populates the same 6 row slots with different data. Scroll state is per-tab and resets on tab switch.

**Context-sensitive buttons** in the tab bar:
- Market tab: Buy 10x 1M 5yr, Buy 10x 10M 5yr, Buy 1B 5yr
- Portfolio tab: Sell All
- City Debt tab: Pay 25%, Pay 50%
- Hedging tab: Auto-Hedge, Exit All

A small toggle button (`BondToggleButton`, 36x36px) in the top-left corner of the screen opens/closes the panel.

---

## File Structure

### BondMarket.cs (126 lines)

Pure domain models with no game dependencies:

- `CreditRating` enum: AAA through D
- `Bond` class: Id, Name, FaceValue, CouponRate, TotalPeriods, RemainingPeriods, PurchasePrice, CouponsReceived
- `InterestRateSwap` class: Id, NotionalAmount, FixedRate, TotalPeriods, RemainingPeriods, PayFixed, CumulativePL, LastSettlement
- `BondPricing` static class: PresentValue (DCF), GetRequiredYield (benchmark + spread), CalculateRating (debt burden + DSCR), RatingLabel

### BondMarketEngine.cs (888 lines)

The simulation engine, subclassing `EconomyExtensionBase`:

- Singleton via static `Instance`, set on each tick
- State: cash flow window, market/portfolio/issued bond lists, active swaps, financial metrics
- Public API: buy/sell bonds, issue bonds, repay debt, enter/terminate swaps, auto-hedge, snapshot methods
- Internal: cash flow tracking, metrics recalculation, bond aging, debt servicing, swap settlement

### BondMarketPanel.cs (944 lines)

The UI layer, subclassing `UIPanel`:

- `BondToggleButton`: 36x36 icon at (60, 6), toggles panel visibility
- `BondMarketPanel`: 800x520 centered panel with 4 tabs, 6-row scrollable list, summary, and footer
- All game sprite references use built-in ColossalFramework sprites (ButtonMenu, MenuPanel2, InfoIconLevel, buttonclose)

### Loading.cs (48 lines)

Lifecycle management via `LoadingExtensionBase`:

- `OnLevelLoaded`: sets reset flag, creates UI components
- `OnLevelUnloading`: destroys UI components

### MyFirstMod.csproj (50 lines)

Build configuration targeting `net35` with references to ICities, ColossalManaged, Assembly-CSharp, and UnityEngine. Post-build deploy copies the DLL to the game's Mods folder.

---

## Constants Reference

| Constant | Value | Purpose |
|----------|-------|---------|
| WINDOW_SIZE | 60 | Cash flow history samples |
| TICKS_PER_PERIOD | 15 | Simulation ticks per bond period |
| MIN_MARKET_BONDS | 6 | Minimum market offerings before regeneration |
| INTERNAL_UNIT_SCALE | 100 | Game money units per display currency unit |
| MAX_ISSUED_BONDS | 5 | Maximum simultaneous issued city bonds |
| DEFAULT_YIELD_SPIKE | 0.012 | Base yield penalty per default event |
| DEFAULT_DECAY_PER_PERIOD | 1 | Penalty decay rate per period |
| MAX_ACTIVE_SWAPS | 5 | Maximum simultaneous interest rate swaps |
| PeriodsPerYear | 12 | Monthly bond periods |
| REFRESH_INTERVAL | 4.0 | UI auto-refresh interval (seconds) |
| MAX_ROWS | 6 | Visible rows in bond list |

---

## Iterative Development Process

The mod evolved over 27 commits from an initial options trading concept to a full municipal bond market with derivatives. Here is the chronological development history:

### Phase 1: Options Trading Prototype (Commits 1-10)

The mod started as a stock options trading system for Cities: Skylines:

1. **Initial scaffold** - Options trading mod with basic call/put positions, resolving a duplicate mod-entry conflict with another IUserMod in the project
2. **README** - Basic project documentation
3. **API fix** - Fixed `AddResource` overload mismatch (CS1502) caused by incorrect parameter types
4. **UI polish** - Shrunk the toggle button to a 36x36 icon in the top-left corner
5. **Portfolio tracking** - Added live portfolio value tracker with PriceFeed diagnostics
6. **Feature expansion** - Resized and centered the panel, switched to city currency, added expiry selector and short-selling
7. **Position locking** - Locked strike and expiry on open, added auto-measure layout
8. **Bug fixes** - Fixed expiry day math, button highlight sync, and portfolio display issues
9. **Economy integration** - Wired live price feed to the city economy, prevented selling into negative balance
10. **API upgrade** - Switched to the official IEconomy API for the live stock price feed

### Phase 2: Municipal Bond Market (Commits 11-16)

A fundamental pivot from options trading to municipal bonds:

11. **Complete rewrite** - Replaced the entire options market with a municipal bond market system. Introduced the `EconomyExtensionBase` hook, cash flow tracking, credit ratings, yield curves, and a three-tab UI (Market, Portfolio, City Debt)
12. **API compliance** - Removed non-overridable `OnAddResource`/`OnFetchResource` methods that caused compile errors, working within the constraint that only `OnUpdateMoneyAmount` is virtual
13. **Maturity tuning** - Shortened bonds to half a game year for faster gameplay feedback, added lifetime P/L tracking to the portfolio
14. **Portfolio UX** - Added Sell All button and scrollable portfolio list to handle larger portfolios
15. **City Debt tab** - Added the City Debt tab with issuance templates, persistent lifetime P/L display, and days-until-maturity countdown
16. **Balance tuning** - Reduced default penalty by 90%, extended bonds to 1 year, increased issue prices by 50%, general UI cleanup

### Phase 3: Scaling Up (Commits 17-22)

Responding to player demand for larger-scale bond operations:

17. **1M bonds** - Added Buy 1M 5yr bond button to the market tab
18. **State management** - Added state reset on new game, buy 10x 1M bonds batch button, reduced default penalty further, increased issue capital limits
19. **10M bonds** - Added 10x 10M 5yr treasury bond buy button
20. **Critical bug fix** - Fixed negative balance bug where bulk buy loops read stale `LastCashAmount`, causing overspending. Solution: track remaining cash locally in the buy loop
21. **1B bonds** - Added Buy 1B 5yr treasury bond button for massive-scale investing
22. **Critical bug fix** - Fixed `int` overflow causing catastrophic bank balance crash when selling/maturing large bonds. Values exceeding `int.MaxValue` (2.1B) wrapped negative when cast to `int`. Solution: all money operations use `long` arithmetic with chunking

### Phase 4: Hardening and Polish (Commits 23-25)

23. **API restoration** - After a major engine rewrite, the panel referenced properties and methods that no longer existed. Restored the full API surface (PortfolioCount, MarketCount, IssuedCount, all snapshot methods, etc.)
24. **Term correction** - Fixed treasury bond terms from 8/12 periods (months) to 60 periods (5 years) to match the "5yr" label
25. **Code review fixes** - Fixed 1B bond term to 60 periods, added `TrySpendCash` return value checks on issued bond coupon/maturity payments, moved GC-allocating arrays (`ISSUE_NAMES`, `MARKET_ISSUERS`, etc.) to `static readonly` fields to avoid allocation on the hot path

### Phase 5: Derivatives and Visibility (Commits 26-27)

26. **Interest Rate Swaps** - Added the complete IRS system: `InterestRateSwap` domain model, swap settlement logic, volatility tracking, auto-hedge recommendation, Hedging Desk UI tab with individual swap management. Scaled debt maturity templates to face value (25K=2yr through 750K=10yr), added Pay 25%/50% early repayment buttons
27. **Issued bond visibility** - Made issued bond stats visible in the City Debt tab. Issued bonds now show name, face value, rate, months remaining, coupons paid, and per-period cost, each with an individual Repay button. The tab displays issued bonds first, then issuance templates below, with proper scroll support across the mixed list

### Key Lessons from the Iteration

**Start with the hook, not the UI.** The options trading prototype proved that `EconomyExtensionBase.OnUpdateMoneyAmount` is the only reliable entry point. Everything else in the economy API is sealed. This constraint shaped the entire architecture.

**Int overflow is silent and catastrophic.** The .NET 3.5 runtime doesn't throw on overflow; it wraps. A city with 2 billion in bond value would see its balance go negative in a single tick. This class of bug only manifests at scale and is invisible in normal testing.

**Stale reads in tight loops.** `LastCashAmount` doesn't update mid-tick, so reading it repeatedly in a buy loop gives the same answer. The fix (local `remaining` tracker) is simple but the bug is subtle and only appears during bulk operations.

**Thread safety is non-negotiable.** The simulation thread and UI thread run concurrently. Without the lock, race conditions on the bond lists cause index-out-of-range exceptions, duplicate entries, and data corruption. The snapshot pattern keeps the UI responsive without holding the lock during rendering.

**Scale reveals design flaws.** The mod started with 10K-100K bonds. Adding 1M, 10M, and 1B bonds revealed the int overflow, stale-read, and API surface issues. Each scale jump was a stress test that found a new category of bug.
