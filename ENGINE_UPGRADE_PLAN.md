# Bond Market Engine — Upgrade Plan

A full read-through of `BondMarketEngine.cs` (2060 lines), `BondMarket.cs`,
`CimDemandEngine.cs`, `BondMarketPanel.cs` (1696 lines), `ResidentialBuildingLog.cs`,
`SaveDataExtension.cs` and `Loading.cs`, with everything that should change before
the next release, ranked by what it costs to leave alone.

Findings cite `file:line` against the current `claude/ecstatic-meitner-os5lhz` head.

---

## Verdict

The architecture is sound. The split between pure math (`BondMarket.cs`,
`CimDemandEngine.cs`), the simulation engine, and the UI layer is the right shape,
and the lock discipline is consistent. The problems are concentrated in three places:

1. **Eight defects that break the economy or crash the game.** Four of them are
   money exploits that a player will find by accident within an hour of play. One
   is a stack overflow on a corrupt save.
2. **A unit error at the heart of the credit model.** Debt burden and DSCR compare
   a per-period number against a per-tick number, so every ratio the rating engine
   consumes is off by a factor of 15. Several hand-tuned fudge constants elsewhere
   in the file exist to paper over the resulting behaviour.
3. **Per-tick work that should be per-period.** The engine re-sums a 60-element
   window three times and revalues the whole portfolio with an O(n) discounting
   loop on every economy tick.

Fixing 1 and 2 is a prerequisite for any realism work — new mechanics layered on
top of a broken rating signal will need the same fudge constants all over again.

---

## P0 — Correctness: crashes and exploits

### P0-1 Infinite recursion on a corrupt save → stack overflow

`BondMarketEngine.cs:1181` and `BondMarketEngine.cs:2013`

`ResetStateInternal` calls `RestoreState(PendingSaveData)` and clears
`PendingSaveData` only *after* the try/catch. `RestoreState` swallows its own
exception and calls `ResetStateInternal()`. A save that fails to parse therefore
loops: reset → restore → throw → reset → restore, with `PendingSaveData` still
non-null on every pass. The stack overflows and takes the game process with it.

**Fix.** Null `PendingSaveData` before calling `RestoreState`, and have
`RestoreState` clear state inline (or set a `_restoreFailed` flag consumed by the
caller) instead of calling back into the reset path. Add a re-entrancy guard as a
belt-and-braces second line.

### P0-2 Defaulting erases the debt

`BondMarketEngine.cs:731-741`, `747-753`, `760-765`

When `TrySpendCash` fails on a coupon or a maturity repayment, the engine calls
`TriggerDefaultInternal` and then *removes the bond from `_issuedBonds`*.
`TriggerDefaultInternal` increments three counters and returns. The obligation is
gone. The penalty is `+12` on `_defaultPenalty`, worth `12 × 0.0025 = 3%` on the
required yield, decaying at 1 point per period — fully paid off in one in-game year.

Deliberately running the treasury dry the period a large bond matures is strictly
better than repaying it. That is the dominant strategy for any issued bond.

**Fix.** A default must keep the liability. Move the bond to a `_defaultedBonds`
list that continues to accrue arrears at a penalty rate, force the rating to `D`
for a lock-out window, suspend new issuance, and require the arrears be cleared
before the rating can recover. Partial payment (pay what cash allows, accrue the
shortfall) is both more realistic and better play than an all-or-nothing check.

### P0-3 Partial paydown creates an infinite money loop

`BondMarketEngine.cs:1409`

`PayDebtPercent`'s partial-paydown branch records the paydown by *lowering
`SoldFraction`*. But `SoldFraction` is also the field that
`SimulateCitizenTradingInternal` reads to compute unsold inventory
(`BondMarketEngine.cs:850`: `totalUnsold += FaceValue * (1 - SoldFraction)`).

So: pay down 50% of a bond → the engine now believes half the issue is unsold →
citizens "buy" it next period → `AddCashToCity` pays the treasury par for principal
it just retired. Repeat indefinitely.

**Root cause.** One field carries two unrelated meanings: *how much of the issue
investors took up* and *how much principal is still outstanding*.

**Fix.** Split them. `Bond` needs `PlacedFraction` (primary market take-up, only
ever moved by citizen trading) and `OutstandingPrincipal` (amortising balance, only
ever moved by repayment). Debt service, `SubscribedFace`, and the capacity checks
all key off `OutstandingPrincipal`.

### P0-4 Citizen sell-backs forgive debt for free

`BondMarketEngine.cs:893-907`

`SoldFraction` is decremented first, `actualRedeemed` is computed from the change,
and *then* `TrySpendCash(cost)` is called — with its return value discarded. When
the city cannot afford the buy-back, the reduction stands anyway and the debt
shrinks at no cost. No default fires either.

**Fix (mechanical).** Compute the affordable redemption first, then move the
fraction by the amount actually paid.

**Fix (model).** The deeper issue is that citizens selling in the *secondary*
market should never touch the issuer's treasury. An investor sells to another
investor; the issuer keeps paying the same coupon to whoever holds the paper. Sell
pressure belongs in the price, not in the cash account. Route sell volume into the
yield/pressure channel that already exists (`AdjustYieldForPressure`) and remove
the cash leg entirely. This also deletes a whole class of "citizens drained my
budget" bug reports.

### P0-5 The over-hedge penalty pays you to over-hedge

`BondMarketEngine.cs:438` and `BondMarketEngine.cs:769`, `1523`

`RecalculateMetricsInternal` adds the over-hedge penalty (up to +300bp) into
`_benchmarkRate`. `SettleSwapsInternal` then uses `_benchmarkRate` as the *floating
index* the swaps settle against. A pay-fixed swap earns `(floating − fixed) × N`.

Over-hedging raises floating, which raises the payout on the very swaps that caused
the over-hedge. The penalty is inverted into a reward, and the loop is self-reinforcing.

**Fix.** Separate two rates that are currently one:
- `_marketFloatingRate` — the exogenous index (swaps and floating-rate debt settle
  against this; nothing the city does moves it).
- `_cityBorrowingRate` — `_marketFloatingRate` + fiscal adjustment + over-hedge
  penalty + credit spread (this prices what the city *issues*).

Every use site needs auditing; `_benchmarkRate` is currently read for both purposes.

### P0-6 `LastCashAmount` is stale across a single tick

`BondMarketEngine.cs:1082`

`TrySpendCash` gates on `em.LastCashAmount`, which the design doc itself
(`MUNICIPAL_BOND_MARKET.md`, "Stale LastCashAmount") documents as reflecting the balance at the *start*
of the tick. The bulk-buy paths were fixed for this; the per-period servicing path
was not. In one `AgeBondsInternal` call the engine can run up to five coupon
payments, five swap settlements, and a round of citizen redemptions — every one of
them reading the same stale figure.

Consequences run both ways: the city overdraws (every spend sees the pre-tick
balance and approves), and phantom defaults fire (a spend is refused against a
balance that was already spent, when in fact the mod's own inflows this tick would
have covered it).

**Fix.** `OnUpdateMoneyAmount` receives the authoritative balance. Hold a
`_tickCash` cursor seeded from that argument at the top of the tick, and settle
every mod cash operation against the cursor, updating it on each. `LastCashAmount`
should not be read anywhere in the servicing path.

### P0-7 Resource call return values are discarded

`BondMarketEngine.cs:1088` (`FetchResource`), `BondMarketEngine.cs:1105` (`AddResource`)

Both game calls return the amount actually moved, which can be less than requested.
The engine assumes the full amount moved and adjusts `_prevMoney` by the requested
figure. Any shortfall permanently desyncs the cash-flow baseline, and the phantom
delta is written into `_cashFlowHistory` on the next tick, where it corrupts gross
income, expenses, volatility, DSCR, debt burden and the credit rating — with no
mechanism to ever re-sync.

**Fix.** Accumulate the *returned* amounts into a `_modCashDeltaThisTick` field,
subtract that from the raw balance delta in `UpdateCashFlowHistory`, and reset it
each tick. That is self-correcting by construction and removes the `_prevMoney`
fixups scattered through `TrySpendCash`/`AddCashToCity`. Add a sanity clamp: if a
recorded delta exceeds, say, 6σ of the rolling window, re-baseline rather than
record it.

### P0-8 UI acts on stale indices

`BondMarketPanel.cs:1335-1444`, engine snapshot methods at `1193`, `1208`, `1722`, `1732`

Every UI→engine call passes a list index (`SellBond(portfolioIdx)`,
`RepaySingleBond(issuedIdx)`, `TerminateSwap(swapIdx)`, `BuyBond(index)`) resolved
against a snapshot that can be up to 4 seconds old. The engine ages bonds, removes
matured issues and drops market bonds every period in between. Click "Sell" on row 3
after a maturity and you sell a different bond.

The tab-4 path is worse: it derives sub-ranges from `_cachedBonds.Count` and
`_cachedIssuedBonds.Count` — counts from the *previous* refresh — to decide whether
an index means a holding, a debt, or a swap. A shifted boundary terminates a swap
when the player clicked "Sell" on a bond.

**Fix.** Pass the stable `Id` string, not the index. `SellBond(string bondId)` and
friends look up under the lock and return false when the instrument is gone. While
in there: `GetMarketSnapshot` and the other snapshot methods hand the UI thread
*live references* to `Bond` and `InterestRateSwap` objects that the sim thread keeps
mutating — the design doc claims otherwise at `MUNICIPAL_BOND_MARKET.md`, "Threading Model". Only
`CimTransaction` is deep-copied. Introduce an immutable `BondView` DTO and copy into it.

---

## P1 — Realism: the model itself

### P1-1 The credit ratios are off by 15×

`BondMarketEngine.cs:399-410`

```
avgIncome            = _grossIncome / WINDOW_SIZE       // per TICK
scheduledDebtService = Σ (face × coupon) / 12           // per PERIOD (= 15 ticks)
_debtBurden          = scheduledDebtService / avgIncome // mixed units
_dscr                = _noi / scheduledDebtService      // mixed units
```

Debt burden is overstated 15×, DSCR understated 15×. A city comfortably covering
its debt reads as `D`. This is very likely the reason the file carries hand-tuned
corrections that have no analytical basis:

- `BondMarketEngine.cs:416-417` — the absolute cash thresholds (+1.0 DSCR above
  500k, −0.5 below 10k) that rescue the number after the fact;
- the `vitalFloor` term in `CalculateDemandScore` (`CimDemandEngine.cs:134`) that
  stops demand collapsing to zero.

**Fix.** Normalise to one horizon. Recommend annualising both sides: income and
expenses become per-year run-rates (`avg per tick × TICKS_PER_PERIOD × 12`), debt
service becomes annual debt service. Then `DSCR = annual NOI / annual debt service`
matches the textbook definition the rating table (`BondMarket.cs:145`) was written
against, and the table's thresholds become meaningful as written.

Delete the cash-threshold fudge and replace it with a defensible liquidity metric:
*months of debt service held in reserve*, folded in as a rating notch adjustment
rather than a DSCR edit. That scales correctly for a 2k village and a 200k metro.

### P1-2 Bad credit makes your investments more profitable

`BondMarketEngine.cs:1202`, `1217`, `1230`, `1251`; `BondMarketEngine.cs:1419-1490`

Every market bond is priced by discounting at `_requiredYield` — *the city's own*
credit-adjusted yield. Port Authority paper gets discounted at your junk spread.

The consequences are backwards throughout:
- Wreck your credit and your required yield rises, so every bond you buy is cheaper
  and yields more. Bad governance improves investment returns.
- Coupons on newly generated market bonds are set to `_requiredYield ± small spread`
  (`BondMarketEngine.cs:1064`), so they price at ~par and pay your own junk yield
  back to you with zero credit risk. `Buy1BBond` (`BondMarketEngine.cs:1419`) is
  therefore a risk-free carry trade with a billion-unit notional.
- Your portfolio's mark-to-market is driven by your own city's finances rather than
  by rates or by the issuers' health.

**Fix.**
- Give each market issuer its own `CreditRating` and spread, and a slow credit
  migration process between periods.
- Price holdings off `benchmark curve + issuer spread`. The city's own spread
  applies only to `_issuedBonds`.
- Add issuer default risk, so holding paper carries real risk and diversification
  becomes a decision rather than a formality.
- Re-scale or remove the 1B/10×10M buttons; with correct pricing they become a
  liquidity question, which leads directly to the next item.

### P1-3 No yield curve

`BondMarket.cs:112-131`

`PresentValue` discounts a 4-period note and a 120-period bond at the same single
rate. Duration has no price consequence, so there is no reason to ever prefer a
short bond, and none of the interesting rates play (riding the curve, duration
positioning, curve inversion as a recession signal) is expressible.

**Fix.** Introduce a term structure. A three-factor Nelson-Siegel curve (level,
slope, curvature) is about 20 lines, needs no allocation, and gives a spot rate for
any maturity. `PresentValue` then discounts each cash flow at its own tenor's spot
rate. Swap par rates and the bond ladder both fall out of the same curve.

### P1-4 The benchmark rate is a constant

`BondMarketEngine.cs:424-430`

`fedFundsProxy = 0.04f`, hard-coded, plus a term premium driven only by the city's
own volatility and a fiscal adjustment. There is no macro environment: no cycle, no
rate shocks, no regime changes. A player's entire rates experience is a function of
their own balance sheet, which makes hedging pointless — there is nothing exogenous
to hedge against.

**Fix.** Drive the short rate with a mean-reverting stochastic process (Vasicek /
Ornstein-Uhlenbeck): `dr = κ(θ − r)dt + σ dW`, one line per tick, with `θ` itself
drifting slowly on a business cycle. Persist `r` and the cycle phase in the save.
This makes swaps, duration and refinancing timing genuinely meaningful, and gives
quarterly reports something to narrate.

### P1-5 The yield rate-limiter is applied per tick

`BondMarketEngine.cs:504-512`

The 2%-per-step limiter runs inside `RecalculateMetricsInternal`, which runs every
tick. That permits 30 percentage points of movement per period — the limiter
effectively never binds, which defeats its purpose, while still hiding genuine
step-changes from the player between refreshes.

**Fix.** Move rate evolution into the per-period path and limit to something like
25-50bp per period. Rates should visibly *trend*, so a player can read the market.

### P1-6 Swaps are priced by a linear approximation

`BondMarketEngine.cs:1521-1530`

`CalculateSwapMTM` returns `(floating − fixed) × notional × remainingYears` with no
discounting, no convexity, and an assumption that today's floating rate holds for
the whole remaining life. `AutoHedge` (`BondMarketEngine.cs:1678-1681`) then sets the
swap's fixed leg to `_requiredYield` — the city's credit-adjusted borrowing rate —
where a real swap's fixed leg is the par swap rate off the risk-free curve.

**Fix.** Once the curve from P1-3 exists: MTM = PV(floating leg) − PV(fixed leg),
both discounted on the curve; the fair fixed rate is the par swap rate. Also,
`SellSwapTranche` (`BondMarketEngine.cs:1583`) scales `CumulativePL` by the
remaining fraction, rewriting already-realised history — realised P/L should be
immutable and only the notional should scale.

### P1-7 Primary issuance always clears at par

`BondMarketEngine.cs:846-910`, `BondMarketEngine.cs:1303`

The coupon is fixed at `_requiredYield` at issuance, and the unsold portion is
placed with citizens at face value in later periods regardless of where yields have
moved since. An issue floated at 4% that the market now demands 9% for still raises
100 cents on the dollar.

**Fix.** Model the primary market as an auction. Citizen demand at a given offered
yield gives a bid-to-cover ratio; the clearing yield is where cover reaches 1.0.
Issue below the clearing yield and the deal is undersubscribed — the player either
prices it wider or pulls it. This turns issuance from a button press into the most
interesting decision in the mod, and it reuses `CalculateCitizenActivity` as the
demand curve with one added argument.

### P1-8 Zero transaction friction

`BondMarketEngine.cs:1223` / `1244` (buy and sell both at `PresentValue`)

Buy and sell execute at the same price with no bid-ask, no commission, no price
impact and no settlement lag. Round-tripping is free, and a 1B-notional order moves
the market not at all.

**Fix.** A bid-ask spread that widens with issuer risk and order size, plus square-root
price impact for large blocks, plus a per-issue depth limit. This also quietly fixes
the bulk-buy buttons without special-casing them.

### P1-9 The credit model infers income from balance deltas

`BondMarketEngine.cs:382-395`

"Gross income" is the sum of positive balance deltas, which conflates tax revenue,
loan drawdowns, milestone rewards, refunds and the mod's own inflows. Real municipal
credit analysis keys off the *composition* of revenue, and the game already exposes it.

**Fix.** Read the actual books from `EconomyManager` (per-`ItemClass.Service` income
and expenses — verify the exact accessor against the game's assembly) instead of
differencing the bank balance. That unlocks the single best realism feature available
here:

> **General obligation vs revenue bonds.** A GO bond is backed by the tax base and
> rated on total revenue. A revenue bond is backed by one service — water, power,
> transit — and rated on *that service's* net revenue, exactly as in reality. With
> per-service income available, revenue-bond DSCR is a direct calculation, and the
> player gets a genuine reason to care which utility they pledge.

It also fixes P0-7 at the root: mod cash flows stop being indistinguishable from
tax revenue.

### P1-10 Bond periods drift with game speed

`BondMarketEngine.cs:19`, `349-353`

`TICKS_PER_PERIOD = 15` counts economy callbacks, which arrive faster at simulation
speed 3. A "monthly" coupon is therefore three times more frequent on fast-forward,
and `BondPricing.PeriodsPerYear = 12` describes a year that has no relationship to
the in-game calendar.

**Fix.** Drive periods off `SimulationManager.instance.m_currentGameTime` month
boundaries. Coupons then land on the same in-game date regardless of speed, the
"120 periods" on a Capital Bond becomes a real ten years, and the quarterly report
lines up with actual quarters.

### P1-11 Absorption capacity keys off the treasury

`CimDemandEngine.cs:145-152`

`CalculateAbsorptionCapacity` multiplies population × 500 by a factor derived from
`cashReserves` — the *city's* bank balance standing in for how much bond paper the
citizenry can absorb. A rich treasury should, if anything, reduce the need to borrow.
At `population × 500`, a 100k city has a ~150M ceiling against a largest template of
750k, so the constraint never binds.

**Fix.** Base capacity on citizen wealth: land value, education level, employment and
population, all of which `ReadCityDemographicsInternal` already samples. Scale so it
binds for real at mid-game, and let the existing `_absorptionCapacity` plumbing carry it.

### P1-12 Assorted model notes

- `ServiceIssuedBondsInternal` charges coupons on `SubscribedFace`, while
  `CanIssueBonds` / `RemainingCapacity` (`BondMarketEngine.cs:148-162`, `265-271`) measure
  capacity on `FaceValue`. Pick one convention per concept, per P0-3.
- `PayDebtPercent` returns `-1` as a sentinel for "partial paydown" mixed into an
  otherwise-a-count return (`BondMarketEngine.cs:1410`). Return a small result struct.
- `SellBond` deletes the instrument rather than returning it to market inventory,
  so selling permanently shrinks the market until `RegenerateBondsInternal` tops it up.
- `AddCashToCity` books every inflow as `PublicIncome` and `TrySpendCash` books every
  outflow as `LoanPayment` (`BondMarketEngine.cs:1088`, `1105`). Bond *purchases*
  showing up as loan payments in the city budget panel is misleading; map to the
  closest matching `EconomyManager.Resource` per operation type.
- The default penalty decays at 1/period (`BondMarketEngine.cs:24`) — one in-game
  year to erase a default entirely. Real downgrades persist for years. Once P0-2
  lands, slow this considerably and gate recovery on clearing arrears.
- `_rng` (`BondMarketEngine.cs:36`) is neither seeded deterministically nor
  serialised, so reloading a save re-rolls the market. Seed it from the save and
  persist the state.

---

## P2 — Performance

### P2-1 `PresentValue` is an O(n) loop in the hot path

`BondMarket.cs:112-131`

The discounting loop runs once per remaining period. It is called for every
portfolio bond inside `RecalculateMetricsInternal` (`BondMarketEngine.cs:419-421`),
which runs **every economy tick**, and again for every row on every UI refresh. A
portfolio of 40 bonds at 120 periods is ~4,800 float divisions per tick for a number
the player sees once every 4 seconds.

**Fix.** Closed form:

```
d   = pow(1 + r, -n)
PV  = C × (1 − d) / r  +  F × d
```

O(1), one `Math.Pow`. Guard `r == 0`. Keep a per-period memoised portfolio value
rather than recomputing it per tick; the inputs only change per period anyway.

### P2-2 The whole metrics block runs every tick

`BondMarketEngine.cs:382-516`

Every tick: three passes over the 60-element window (positive/negative sums, mean,
variance), a full portfolio revaluation, the demand/vitals/appeal chain, and the
yield pipeline. Only `UpdateCashFlowHistory` genuinely needs per-tick cadence.

**Fix.**
- Maintain running sums incrementally — when a slot is overwritten, subtract the old
  value and add the new one. Sum, sum-of-squares and the positive/negative splits all
  update in O(1), and variance comes from `E[x²] − E[x]²`.
- Move the rest to the per-period path. Fifteen-fold reduction in engine work for no
  observable behavioural change.

### P2-3 Allocations on the simulation thread

`BondMarketEngine.cs:842` allocates `new float[_issuedBonds.Count]` every period.
`BondMarketEngine.cs:912-937` builds the transaction detail string by repeated
concatenation inside a loop, plus `string.Format` per changed bond.
`_transactionLog.RemoveAt(0)` (`BondMarketEngine.cs:948`) and
`_reportHistory.RemoveAt(0)` (`BondMarketEngine.cs:1015`) shift the whole list.

Individually small; collectively this is avoidable Mono GC pressure on the
simulation thread, which is where stutter comes from.

**Fix.** Preallocate the fractions array at `MAX_ISSUED_BONDS`; a reused
`StringBuilder` for the detail string; ring buffers for both logs.

### P2-4 Exceptions used as control flow

`BondMarketEngine.cs:532-628`

`ReadCityDemographicsInternal` wraps both game-data reads in `try`/`catch` and
computes fallback values *inside the catch blocks*. Mono exception handling is
expensive, and a blanket `catch` hides real bugs — a null-reference in the sampling
loop silently degrades into "fabricate plausible demographics" with no log line.

**Fix.** Explicit null checks and a `_gameDataAvailable` flag. Keep one narrow
`try`/`catch` around the raw buffer walk if you want belt-and-braces, and log when
it fires.

### P2-5 `ResidentialBuildingLog` logs on every building event

`ResidentialBuildingLog.cs:99-101`, `122-124`

`Debug.Log` with a `string.Format` payload fires on *every* residential building
created or released. In a growing 100k city that is a continuous stream of formatted
strings and file I/O. `Debug.Log` is not cheap on the Unity Mono runtime, and this
runs on the simulation thread.

Worth noting: this class currently feeds nothing into the engine. It counts buildings
and logs them.

**Fix.** Drop the per-event logging (or gate it behind a debug flag, off by default).
Then either wire the counts into the demand model — residential density and
composition are a genuinely good proxy for the citizen bond-buying base described in
P1-11 — or remove the class.

### P2-6 UI refresh rebuilds everything unconditionally

`BondMarketPanel.cs:522-648`

Every 4 seconds the panel reformats all six rows plus summary and footer whether or
not anything changed. Six rows is cheap, so this is a polish item rather than a
performance emergency, but the refresh interval is also why displayed figures feel
stale and then jump.

**Fix.** Refresh on a ~1s cadence, dirty-check against a cached value per label, and
only assign `.text` when it differs (assignment triggers layout in the Colossal UI
framework). Consider a proper `UIScrollablePanel` in place of the fixed six rows and
the manual `_scrollOffset` arithmetic — it would delete a good chunk of
`BondMarketPanel.cs` and all of the index-mapping bugs that come with it.

---

## P3 — Infrastructure and hygiene

- **No tests, no CI.** `BondMarket.cs` and `CimDemandEngine.cs` have zero game
  dependencies and are pure functions — they are testable today. Add a `net48`
  xUnit project that links the same source files and covers pricing, the rating
  table, the demand chain, and the fixed ratio units from P1-1. Every P0 above
  would have been caught by a test of the arithmetic. Wire it to GitHub Actions;
  the game DLLs are not needed for the pure-logic project.
- **`BondMarketEngine.Instance` is never cleared** on level unload
  (`Loading.cs:38-55` destroys the UI but leaves the static). A stale engine
  reference survives into the next city load.
- **`NeedsReset` is only set for `NewGame`** (`Loading.cs:19-24`). Loading a city
  that has no saved mod data leaves whatever state the statics carry. Set the reset
  flag on every load and let `PendingSaveData` override it.
- **The save format is positional with no guards.** `SerializeState` writes raw
  fields; `RestoreState` reads them back in order. A version mismatch mid-stream
  leaves state half-restored (`BondMarketEngine.cs:1883-1888` returns early, having
  already mutated fields). Add a length prefix and a checksum per section, restore
  into a *staging* object and swap it in only on success.
- **No settings UI.** `Mod.OnSettingsUI` is still commented out (`Mod.cs:28-36`).
  The realism work above wants player-facing toggles: difficulty, rate volatility,
  whether citizen trading is on, starting benchmark.
- **No localization**, no keyboard shortcut for the panel, fixed 800×520 layout that
  ignores UI scale.
- **`MUNICIPAL_BOND_MARKET.md` has drifted materially** and now misdescribes the code:
  - "four files" / `BondMarketEngine.cs (888 lines)` / `BondMarketPanel.cs (944 lines)`
    — there are nine files at 2060 and 1696 lines.
  - Constants table says `DEFAULT_YIELD_SPIKE 0.012`; the code has
    `DEFAULT_YIELD_SPIKE_PER_POINT = 0.0025`.
  - "coupon rates: required yield ± a random spread of up to 2%, clamped to 2%-25%";
    the code uses −0.3%/+0.9% clamped to 2.5%-10% (`BondMarketEngine.cs:1064-1067`).
  - "If no bonds are issued, 10% of average expenses is used as a proxy" — not
    implemented; the code sets burden to 0 (`BondMarketEngine.cs:411-412`).
  - Initial bond table ("3%, 2 periods") does not match `GenerateInitialBondsInternal`.
  - The threading section claims the UI never holds references into simulation state;
    it does, for bonds, swaps and reports (P0-8).

---

## Suggested sequencing

Each phase is independently shippable and leaves the mod in a working state.

**Phase 1 — Stop the bleeding.** P0-1 through P0-8. No new mechanics, no tuning
changes. Add the test project first so the fixes are pinned. This is the release
that makes the mod safe to play.

**Phase 2 — Fix the measurements.** P1-1 (unit normalisation) and P1-9 (read the
real books from `EconomyManager`). Delete the fudge constants they were masking.
Re-tune the rating thresholds against real cities of several sizes. Expect visible
rating changes for existing saves — worth a migration note.

**Phase 3 — Performance.** P2-1 through P2-5. Mechanical, well-contained, and the
per-tick → per-period move is the one the player feels.

**Phase 4 — A real rates market.** P1-3 (curve), P1-4 (stochastic short rate),
P1-5 (per-period rate evolution), P1-6 (proper swap pricing). This is the phase that
makes hedging a decision rather than a formality.

**Phase 5 — A real bond market.** P1-2 (issuer credit), P1-7 (auction-based
issuance), P1-8 (friction and depth), plus GO vs revenue bonds on top of Phase 2's
per-service revenue.

**Phase 6 — Surface.** UI rework (scrollable panel, ID-based actions, faster
dirty-checked refresh), settings page, localization, doc rewrite.

---

## Proposed file layout

The engine file is 2060 lines and carries market simulation, credit analysis, cash
plumbing, serialisation and the public API. The phases above will add to all five.
A split along the seams that already exist:

```
Domain/        Bond, InterestRateSwap, CreditRating, BondView (DTO)
Pricing/       YieldCurve, BondPricing, SwapPricing      — pure, unit-tested
Market/        IssuerBook, PrimaryAuction, SecondaryMarket, RateProcess
Credit/        CreditModel, RatingEngine, DefaultEngine
Sim/           BondMarketEngine (tick orchestration + cash plumbing only)
Persistence/   StateSerializer (staged restore, versioned, checksummed)
UI/            BondMarketPanel + per-tab view classes
```

`Pricing/` and `Credit/` stay free of Unity and Colossal references, which keeps the
test project able to link them directly.
