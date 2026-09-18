# Directive 02 — Ship Readiness

**To:** Engineering **From:** Managing Director **Date:** 2026-09-18
**Supersedes:** nothing in Directive 01; extends its register with WO-17 … WO-31
**Verified against:** `integration` @ `b9e1e11` · CI run #39 green (compile-check + pure-logic-tests)

---

## 1. Position

**Thirteen of seventeen work orders landed in four days.** The trunk was cut as directed and
`master` held at `05a65ef`. WO-1 wired the auction. WO-2/WO-3 built the stub harness —
`Stubs/ICities.cs`, `Stubs/ColossalFramework.cs`, `Stubs/UnityEngine.cs`, `CI/CompileCheck.csproj`
— and the `compile-check` job is green on the current head. WO-4, 5, 6, 7, 8, 9, 10, 12 and 13
are in. The G-2 diagnostic was re-cut to three-point sampling at runs 2, 10 and 60. That is
disciplined execution against a register the team received four days ago, and it should be said
before anything else.

**Gate A is MET.** CI compiles 100% of shipped source on every push. Record it in the WO-14
file and retire "inspection-verified" as a category, as Directive 01 required.

**And we are not shipping on this head.** The internal review returns five P0 findings. I have
confirmed every one of them in the code myself rather than pass them on as assertions. Two are
money exploits, one is an unbounded money printer, and two together mean the credit model has
never once produced a realistic number. The engine rating moves **BBB → B**: the P0 count went
up between reviews, and the exploit class the rebuild was commissioned to eliminate is back at
the call sites.

**The headline finding is compounding.** P0-1 pins DSCR high whenever the ledger binds; P0-2
pins it to a D floor whenever it does not. The credit model therefore reports AAA or D and
nothing in between. Every system downstream of it — required yield, issuer spreads, auction
clearing, the rating grid, the quarterly report — has been built and inspected against a signal
that has never taken a realistic value. That is why nothing downstream can be considered
validated today, regardless of how carefully it was written.

## 2. Verified findings

I checked each against `integration` @ `b9e1e11`.

| ID | Finding | Verified at |
|---|---|---|
| **P0-1** | Ledger flow sampled per-period, annualised per-tick. `SampleTickOperatingFlow` is reachable only from the per-period metrics block, so `di` spans 15 ticks; `CreditModel` multiplies by `ticksPerPeriod × periodsPerYear` = 180 where 12 is correct. Revenue, expense, NOI and DSCR overstated 15×; `DebtBurden` understated 15×. The two paths feeding `_avgIncomePerTick` disagree by exactly 15×, which is the proof it is a defect rather than a calibration choice. Second order: the EMA α = 1/`WINDOW_SIZE` now steps once per period, moving its time constant from ~4 periods to ~60 periods (about five in-game years). | `BondMarketEngine.cs:908-923` · `CreditModel.cs:42-44` |
| **P0-2** | A debt-free city is rated D on boot and cannot issue. The unlevered branch sets `DSCR = AnnualNOI > 0f ? 20f : 0f`, and the rating engine's hard floor is `DSCR < 0.2f → D`. On the first metrics pass both flow EMAs are `0f`, so NOI is 0, DSCR is 0, and a city with no debt is rated D. `CanIssueBonds` and `IssueBond` both hard-reject on D and `_requiredYield` picks up the 1500bp D spread. The same fires for any debt-free city that runs one deficit period. | `CreditModel.cs:58-63` · `RatingEngine.cs:14` |
| **P0-3** | Debt reduced before the cash move is confirmed. `SpendCashUpTo` returns what the game actually moved and can return 0 (`em == null`) or a partial amount (`if (got < chunk) break;`). `TrySpendCash` discards that return and returns `true` unconditionally. Every debt-reducing call site mutates the book first and ignores the result. Any partial `FetchResource` extinguishes real liabilities for free — the P0-3/P0-7 class of the original audit, reintroduced at the call sites. | `BondMarketEngine.cs:1506-1543`, `:1007-1013`, `:1913-1917`, `:2282-2285` |
| **P0-4** | Failing to pay a swap deletes the swap. On a failed settlement the engine does `_activeSwaps.RemoveAt(i); continue;`, skipping `LastSettlement`, `CumulativePL` and `_swapPL` entirely. An underwater swap the city cannot fund is cancelled at zero cost with the loss recorded nowhere. Running the treasury dry becomes the cheapest exit from a losing hedge, inverting the over-hedge penalty. `DebtBook.ServicePeriod` already handles the equivalent bond case correctly. | `BondMarketEngine.cs:1047-1054` |
| **P0-5** | Unbounded synthetic supply with guaranteed positive carry. The three bulk-buy paths fabricate a `Bond` instead of lifting one from `_marketBonds`, then set `b.CouponRate = IssuerYieldFor(b)` — coupon equal to the discount rate, so it prices at par by construction. Entry cost is friction only (~1.5% all-in); hold to maturity returns roughly +23% nominal over five years, at arbitrary size, repeatably, bypassing `_absorptionCapacity` and `MIN_ISSUABLE_DEMAND`. Same block: a second cash cursor seeded from `em.LastCashAmount` while `TrySpendCash` settles against `_tickCash`. | `BondMarketEngine.cs:1926-2015`, `:1956`, `:1990` |
| **P1-6** | `MigrateAnnual` downgrades AAA on an upgrade roll. At `cur == 0` a roll in `[0, pUp)` fails the `cur > 0` guard, falls through, satisfies `roll < pUp + pDown` and downgrades. For an AAA issuer with home AA the realised downgrade probability is 0.26 against an intended 0.18 — 44% inflation. The bands are mutually exclusive only when both branches are reachable. | `IssuerModel.cs:136-139` |

P1-7 through P1-13 and P2-14 through P2-25 are accepted as written and carried into the register
below. I have not re-derived each one; the six above were the sample, and the review's accuracy
on all six is why I am taking the rest on its word.

## 3. Governance — one breach, and its consequence

**WO-11 was held and it shipped.** Directive 01 placed revenue bonds behind Gate B, and I
reaffirmed the hold twice, in writing, with the reason: *a pledge rating built on a miscalibrated
reader repeats the error at an altitude with no obviously solvent city to contradict it.* WO-11
landed at `54d69bc`. Gate B has not been run. **WO-17 — which I numbered specifically as the
prerequisite — was never opened.** So revenue bonds now sit on top of the uncorrected ledger
seam, which is the exact outcome the hold existed to prevent.

I am not treating this as bad faith and I am not reverting the work. The consequence is
proportionate:

**Ruling — WO-11 is quarantined, not reverted.** Gate revenue bonds behind a settings flag
defaulted **off**, shipping disabled, until Block 1 lands *and* Gate B confirms the per-service
read against the game's own budget panel. The code stays, the tests stay, the feature leaves the
1.0 surface. Re-enabling is my call and it needs both conditions.

**Standing rule from today:** a work order marked *Held* does not start, and its dependency is
not assumed satisfied because the blocking gate looks likely to pass. If a hold is wrong, say so
and I will move it. Shipping through a hold removes my ability to sequence risk, which is the
only thing a gate does.

## 4. Release-candidate ladder

Feature work is frozen from now. Nothing new enters the 1.0 surface. Four blocks, in order —
each is a release candidate that must be green before the next begins, because each one's
acceptance depends on the previous one's numbers being real.

### RC-1 — Make the measurement true
*Nothing else in the engine means anything until this lands.*

| WO | Order |
|---|---|
| **WO-17** | P0-1. Rename the sampler's outputs to per-period and say so; annualise on the cadence actually sampled (`× periodsPerYear`). Convert the fallback at `:580-581` to per-period (`totalPositive / WINDOW_SIZE × TICKS_PER_PERIOD`). Re-tune the EMA α for a per-period step (α ≈ 0.25, ~4-period horizon). Per Directive 01's E2 ruling, derive the annualisation factor from elapsed cadence the sampler measures itself, so WO-4's clock cannot stale it again. |
| **WO-18** | P0-2. Coverage collapse gates on there being something to cover: `if (hasActiveArrears) return D; if (m.AnnualDebtService > 1f && m.DSCR < 0.2f) return D;`. Treat the unlevered case as unrated-but-solvent and let the liquidity notch carry it. |

**RC-1 acceptance.** A solvent growing city reads investment grade. A debt-free city is never D.
A levered city running a real deficit reads sub-investment. Then re-run the three-point G-2
diagnostic and bring me the readout — the readings that motivated it are almost certainly these
two defects, and I want that confirmed rather than assumed.

### RC-2 — Money integrity
*Fixed as a set, per the review's own recommendation. These three are one class.*

| WO | Order |
|---|---|
| **WO-19** | P0-3. `TrySpendCash` returns `SpendCashUpTo(amount) == amount`. Audit every debt-reducing call site: spend first and pass the confirmed amount as the budget, or reconcile `requested − moved` back into `Arrears` so the liability survives a failed transfer. No call site discards a spend return. |
| **WO-20** | P0-4. An unfundable settlement carries as `UnpaidSettlement` on the swap, penalised and blocking new swaps, with `LastSettlement`, `CumulativePL` and `_swapPL` accounted either way. Mirror `DebtBook.ServicePeriod`'s lifecycle treatment. |
| **WO-21** | P0-5. Bulk buys draw against real `_marketBonds` inventory and depth, or price the coupon off a curve independent of the discount rate used to value it. Remove the second cash cursor; one cursor per balance. |

**RC-2 acceptance.** A short-filled or refused transfer never reduces a liability. An unpayable
swap leaves a recorded, penalised obligation. No purchase path returns positive carry by
construction, and none bypasses absorption capacity.

### RC-3 — Model integrity

| WO | Order |
|---|---|
| **WO-22** | P1-6. Clamp instead of falling through on both migration branches. |
| **WO-23** | P1-7. One coupon base. `PresentValue` and `AgeBondsInternal` key off `OutstandingPrincipal`, matching `PeriodCoupon` and `ServicePeriod`. |
| **WO-24** | P1-8. Pro-rata servicing across `due` under a constrained budget, so the default set is order-independent. |
| **WO-25** | P1-9. Split `InterestPaid` from `PrincipalRepaid`; the quarterly report's coupon line carries interest only. |
| **WO-26** | P1-10. `EconomyReader` reads the `MethodInfo` into a local once; resolve is locked or idempotent under `volatile`; `Reset()` clears `_servicesCached`/`_services` too; the blanket catch logs. |
| **WO-27** | P1-11. One immutable snapshot published by the sim thread via a single reference assignment; the UI reads that reference and takes no lock. This retires the 136-access-per-refresh tearing class wholesale rather than patching getters. |
| **WO-28** | P1-12. Split the RNG into a persisted simulation stream and a separate cosmetic stream, so UI clicks cannot steer migrations or rate shocks. |
| **WO-29** | P1-13. Currency arithmetic in `double`; convert only at the `long` boundary. Applies to `_realizedPL` accumulation as well as the 1e9 notional. |

### RC-4 — Ship-blocking hygiene

| WO | Order |
|---|---|
| **WO-30** | P2-14 (one capacity base, and the button must not report enabled while every click fails), P2-18 (bound every deserialize count the way the float array already is), P2-19 (`ValidateState` covers `Redeemed`), P2-20 (make the `Service.None` coupling explicit and documented — it is load-bearing and one parameter change from opening a self-reinforcing rating loop), P2-21 (`IssueBondPercent` seeds the cash cursor), P2-22 (early-retired bonds enter redemption history), P2-23 (delete or obsolete `BondPricing.CalculateRating` and stop testing it). |

**Deferred to 1.1, recorded not forgotten:** P2-15, P2-16, P2-17, P2-24, P2-25. All are
performance or tidiness with no player-visible incorrectness.

## 5. The five test gaps are mandatory

These are not a nice-to-have appendix. The review's own framing is the point: the suite is good
on pure modules, so the uncovered seams are exactly where three consecutive units-and-cadence
defects have now shipped green. Each lands **with** its block, not after.

1. **Cadence contract.** Assert the relationship between sampling cadence and annualisation.
   Ships with WO-17. This is the test whose absence let P0-1 through.
2. **Unlevered and deficit.** Unlevered-with-deficit and unlevered-at-zero, asserting not-D.
   Ships with WO-18.
3. **Partial cash moves.** A fake `EconomyManager` seam that short-fills. Ships with WO-19. The
   entire P0-3 class is invisible to CI today.
4. **Migration distribution.** Chi-square over ~100k `MigrateAnnual` calls from AAA. Ships with
   WO-22. Would have caught P1-6 immediately.
5. **Service-order independence.** Same default set regardless of `_bonds` insertion order.
   Ships with WO-24.

Plus the reader↔`CreditModel` contract assertion already agreed: each side declares its horizon
and scale, and the other asserts it. Three defects of this class is enough.

## 6. Gates, restated

- **Gate A — MET.** Compile harness green on every push, CI #39. Commit the record.
- **Gate B — still owed, and now larger.** It must cover WO-11's per-service read, since revenue
  bonds shipped ahead of it. Runs after RC-1 so the session tests a credit model that can produce
  a realistic number. `master` fast-forwards only on Gate B sign-off, unchanged from R3.
- **Gate C — cannot start before RC-1.** Calibrating constants against a 15× signal would encode
  the defect as intended behaviour. Credit constants stay frozen, as ruled.
- **Gate D — ship.** Behind RC-1 … RC-4, the five tests, Gates B and C, and §7.

**On the ship date:** I am not naming one until RC-1's diagnostic readout is in front of me.
Naming a date across five unfixed P0s would be theatre, and the team has earned better than being
held to a number I invented. Bring me RC-1 and I will set the date in the same conversation.

## 7. Release gate — revised

Directive 01's §7 stands, with these added. Any item open blocks the release; a waiver is a
conversation with me and gets recorded.

- [ ] All five P0 findings closed, each with the test from §5 that would have caught it
- [ ] A solvent city reads investment grade; a debt-free city is never D; a deficit city reads sub-investment
- [ ] No spend path reduces a liability without confirming the cash moved
- [ ] No purchase path returns positive carry by construction
- [ ] An unpayable swap settlement leaves a recorded, penalised obligation
- [ ] Revenue bonds either enabled on Gate B evidence or shipped disabled by default
- [ ] Issuer migration frequencies match their intended bands under a distribution test
- [ ] One coupon base, one capacity base, one cash cursor, one rating grid
- [ ] `EconomyReader` is safe against concurrent `Reset()`, and its fallback logs when it fires
- [ ] UI reads one immutable snapshot; no torn or mutually inconsistent reads
- [ ] Every deserialize count is bounded
- [ ] Documentation re-verified against the **fixed** engine, not the intended one — WO-12 predates every finding above
- [ ] `master` points at the Gate-B-validated head

---

**On the record.** Thirteen work orders in four days is the best stretch of delivery this project
has had, and the compile harness in particular changes what we can promise: the review that found
these five P0s was possible because the code now builds and can be reasoned about as a whole. I
would rather receive this review now than a support queue later.

The correction is narrow and I want it understood as such. The pace was right. The one thing that
went wrong is that a held item shipped and its prerequisite was skipped, and the cost is that
revenue bonds must now come out of the 1.0 surface after being built. Hold the line on the gates
and we ship something defensible. Bring me RC-1.
