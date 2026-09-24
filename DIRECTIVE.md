# Directive 01 — Municipal Bond Market

**To:** Engineering **From:** Managing Director **Date:** 2026-09-14
**Verified against:** `a8ea5dc` · CI run #22 green · 77 pure-logic tests · save format v8

---

## 1. Position

Phases 0–5 are committed and CI is green on exactly the head the status report names. The
P0 class is closed on inspection: the within-tick cash cursor replaced the stale balance
read, placement and outstanding principal are separate fields, a default keeps the
liability as accruing arrears, and UI actions resolve by stable ID. The pure modules exist
and are tested — curve, swap pricing, short-rate process, issuer credit, friction,
deterministic PRNG, staged checksummed serializer.

The correction is to the measure. The report tracks phases shipped, which is work
performed. I need product delivered. On that measure: a tested module can have no callers,
a fixed defect can be missing from the plan entirely, and 4,492 of 8,084 shipped lines have
never been through a compiler.

**Engine rating this review: BBB.** Investment grade, not release grade.

## 2. Findings

Four of these nine appear in no plan document I have been given; they are marked
*undisclosed*.

| ID | Finding | Severity | Evidence |
|---|---|---|---|
| F-1 | **The auction is dead code** — `PrimaryAuction.cs` is 67 lines, tested, green, and called from nowhere. P1-7 is still live: issuance clears at par. The report files this as "6-C auction UI", which turns an unbuilt mechanic into a UI task. | Critical *(undisclosed)* | 0 call sites outside the file itself |
| F-2 | **Most of the product has never been compiled** — 4,492 of 8,084 lines are "inspection-verified only". Graded "low risk" in the report; that grade is not supportable after six phases of refactoring. The engine's entire Unity/Colossal surface is six `Debug.Log` calls plus `Singleton<T>`, `ItemClass` enums, two `EconomyManager.Resource` members and the ICities bases — a stub shim puts it under a compiler in a day. | Critical | measured surface |
| F-3 | **Coupons pay 3× faster on fast-forward** — P1-10. `TICKS_PER_PERIOD = 15` counts economy callbacks; no `SimulationManager` reference exists anywhere. Worse now that Phase 4 put a business cycle on the period clock. | High *(undisclosed)* | `BondMarketEngine.cs:17` |
| F-4 | **Absorption capacity keys off the treasury** — P1-11. Still `population × 500` scaled by a function of the bank balance, so a rich treasury raises public appetite for city debt, and the ceiling never binds against shipped templates. | High *(undisclosed)* | `CimDemandEngine.cs:149,153` |
| F-5 | **Phase 5's central mechanic is invisible** — the panel references `IssuerName`/`IssuerRating` zero times. The diversification decision the phase was built to create cannot be made. | Medium | `BondMarketPanel.cs` |
| F-6 | **Bond flows mislabelled in the city budget** — P1-12. Every outflow books as `LoanPayment`, every inflow as `PublicIncome`. | Medium *(undisclosed)* | `BondMarketEngine.cs:1419,1455` |
| F-7 | **The engine survives level unload** — `OnLevelUnloading` destroys the UI and leaves `Instance` standing; `NeedsReset` is set only for `LoadMode.NewGame`. Blast radius grew with Phase 5's persistent PRNG and issuer roster. | Medium | `Loading.cs:41-55,19-24` |
| F-8 | **Documentation describes a product we no longer have** — 460 lines, last touched pre-Phase-0. Still says four files, an 888-line engine, the deleted 10%-of-expenses proxy. | Medium | `MUNICIPAL_BOND_MARKET.md` |
| F-9 | **One index-based entry point remains** — `SellSwapTranche(int index, …)` still resolves by list position. Uncalled by the panel today, so latent rather than live. | Low | `BondMarketEngine.cs:1965` |

## 3. Rulings on the five open decisions

1. **G-2 — approved, resequenced.** Run the smoke test, but not on its own. It needs a
   compiled mod, a running game and a person watching; spending that session on one
   reflection binding wastes it. It happens after the compile harness lands, with a full
   checklist.
2. **Sequencing — approved with one change.** 6-A and 6-B begin immediately. 6-C does not
   begin: per F-1 there is no auction in the engine for it to expose. WO-1 builds the
   mechanic; the ladder follows it.
3. **`master` — hold at `05a65ef`.** It is the release branch from today and advances only
   to code that has been compiled and run. It moves to the validated head after Gate B and
   not one commit sooner.
4. **Tuning — accepted as defaults, not frozen.** The constants stand as shipped. Nobody
   hand-tunes before we have data; they change at Gate C on evidence from real cities.
   Every constant stays a named documented value so a calibration change is a one-line diff.
5. **Auction behaviour — Option A confirmed as specified.** Soft partial fills, pro-rata
   above the 0.75 cover floor, quiet under-fill below it, no penalty, no lockout, no abort.

## 4. Gates

**Gate A — Prove it builds.** *Blocks everything.* A stub-reference project declaring the
game types we touch, plus a CI job compiling real mod sources against it: engine first,
panel second. Nothing ships to players from the shim.
*Exit:* CI compiles 100% of shipped source on every push; "inspection-verified" retires.

**Gate B — Run it in the game, once, thoroughly.** *Needs the modding machine and a person.*
Confirm which `GetIncomeAndExpenses` overload resolves and record it. Confirm
`TryReadService` matches the game's own budget panel for a pledged utility — that is G-2.
Round-trip a v8 save, load a genuine legacy save, force the reflection fallback and confirm
it logs. Output is a validation record committed to the repo, naming the game build.
*Exit:* mod loads, plays an in-game year, saves. G-2 answered in writing. `master`
fast-forwards.

**Gate C — Calibrate against real cities.** Roughly 5k / 50k / 200k, several in-game years,
engine logging its own ratios. Checking the rating grid separates well-run from badly-run at
every size, that absorption binds mid-game once WO-5 lands, and that the auction fails a
deal priced too tight. Constants move here and only here.
*Exit:* calibration record for all three sizes; constant changes committed with evidence.

**Gate D — Ship 1.0.** Version bump across the project file and `Mod.Version`, docs
rewritten, Workshop packaging, migration note for the Phase 2 rating change.
*Exit:* the §7 checklist complete, no item waived.

## 5. Work order register

A work order is done when its acceptance criterion is demonstrable, not when the code is
written.

| WO | Order | Stream | Closes | Gate |
|---|---|---|---|---|
| WO-1 | Wire `PrimaryAuction` into issuance and placement; clearing yield sets the coupon, cover below 0.75 under-fills quietly. *Accept when a deal priced 200bp tight raises measurably less than par.* | ENG-B | F-1 · P1-7 | Start now |
| WO-2 | Stub-reference shim + CI job compiling engine, ledger reader and entry points. *Accept when CI fails on a deliberately introduced type error.* | ENG-A | F-2 | Start now |
| WO-3 | Extend the shim to the Colossal UI surface; bring `BondMarketPanel.cs` under the same job. | ENG-A | F-2 | After WO-2 |
| WO-4 | Drive the period boundary off `m_currentGameTime` month boundaries. *Accept when a coupon lands on the same in-game date at speed 1 and speed 3.* | ENG-B | F-3 · P1-10 | Start now |
| WO-5 | Rebase absorption capacity on citizen wealth (land value, education, employment, population); drop the treasury term. *Accept when capacity binds before the largest template mid-game.* | ENG-B | F-4 · P1-11 | Start now |
| WO-6 | Map each cash operation to its closest `EconomyManager.Resource`. | ENG-B | F-6 · P1-12 | Start now |
| WO-7 | Clear `Instance` on level unload; set `NeedsReset` on every load, let `PendingSaveData` override. *Accept when loading city B after city A shows no state from A.* | ENG-A | F-7 | Start now |
| WO-8 | **6-A.** Scrollable panel; issuer name and rating on every market and portfolio row; four-state lifecycle and arrears legible; friction shown at the point of trade. | ENG-C | F-5 · P2-6 | Start now |
| WO-9 | **6-C.** Auction ticket with live cover indication and depth-aware order ladder, replacing the fixed buy buttons. | ENG-C | P1-8 surface | After WO-1 |
| WO-10 | **6-B.** Settings page (hazard multiplier, rate volatility, citizen-trading toggle); localization scaffolding; keyboard shortcut. | ENG-C | P3 | Start now |
| WO-11 | **6-D.** Revenue bonds rated on a pledged service's net revenue; refuse issuance when the pledge read returns unknown. | ENG-B | P1-9 ext. | **Held — Gate B** |
| WO-12 | Rewrite `MUNICIPAL_BOND_MARKET.md` against the code as built, incl. the Phase 2 rating migration note. | ENG-D | F-8 · P3 | After WO-1…8 |
| WO-13 | `SellSwapTranche` to an ID; `PayDebtPercent` returns a result struct in place of the −1 sentinel. | ENG-B | F-9 · P1-12 | Start now |
| WO-14 | The Gate B validation session and its committed record. | ENG-A + MD | G-2 | After Gate A |
| WO-15 | Calibration across three city sizes; rating and cover distributions recorded. | ENG-B + ENG-D | R4 | Gate C |
| WO-16 | Version bump, Workshop packaging, migration note, release. | ENG-D | Gate D | Gate D |

## 6. Order of operations

1. **Compile harness first (WO-2).** Until a compiler reads the engine, every review any of
   us does is an opinion about code that may not build.
2. **Then WO-1, the auction** — the largest gap between what we have told ourselves we
   shipped and what a player can do. The module is written; this is wiring it into
   `IssueBond`, `IssueBondPercent` and the placement path, and deleting the par assumption.
3. **ENG-B takes WO-4, WO-5, WO-6, WO-13 in parallel.** The audit items that fell off the
   plan close before anyone writes a new feature.
4. **ENG-C starts WO-8 and WO-10 immediately.** Issuer identity on every row is the first
   commit. WO-9 stays parked until WO-1 merges.
5. **ENG-A follows WO-2 with WO-3 and WO-7,** so Gate B tests the corrected lifecycle.
6. **Gate A closes; book the machine.** One session, full checklist, G-2 answered in
   writing. Validation record committed and `master` fast-forwarded that day.
7. **G-2's answer decides WO-11.** Yes and revenue bonds are in the release; no and we ship
   GO-only with the seam in place and the reason recorded. Either outcome delays nothing.
8. **WO-12 lands once the engine stops moving.** Docs written against moving code are
   written twice.
9. **Gate C, then ship.**

## 7. Release gate — definition of done

Any item open blocks the release. A waiver is a conversation with me and gets recorded.

- [ ] Every shipped line compiles in CI, pure-logic suite still green
- [ ] Mod has loaded, played an in-game year and saved on the target build, record committed
- [ ] G-2 answered in writing; revenue bonds implemented or explicitly excluded with reason
- [ ] Issuance runs through the auction; a badly priced deal under-fills
- [ ] A coupon lands on the same in-game date at every simulation speed
- [ ] Absorption capacity binds in a mid-game city
- [ ] Loading a second city in one session inherits nothing from the first
- [ ] Issuer name and rating visible everywhere paper is shown
- [ ] Bond flows labelled correctly in the city budget panel
- [ ] No public engine entry point resolves an instrument by list position
- [ ] Calibration record exists for three city sizes; constants match it
- [ ] Documentation describes the code as built, with the rating-migration note
- [ ] `master` points at the validated release head

---

**On the record:** the five phases were the right call and the P0 work is what made this
project salvageable. The corrections in §2 are to the reporting, not the engineering — four
audit items went missing between documents and a tested module shipped with no callers.
Both are failures of tracking that a completion checklist prevents. From here the register
in §5 is the plan of record. Bring exceptions early; I would rather move a gate than
discover it was quietly waived.
