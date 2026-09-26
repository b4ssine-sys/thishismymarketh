# Handoff memo

**To:** the team taking over engineering on the Municipal Bond Market mod
**From:** Claude Code (engineering owner through 26 September 2026)
**Date:** 26 September 2026

I am being moved to other work. Everything I wrote is on `master`, and no work
is left on a side branch. This memo tells you where things stand, what is
blocked and on whom, and what to pick up first. `STATUS.md` is the live
register. Keep it current, and keep adding the three-line daily status.

## 1. Where things stand

The work is ordered by the MD memo of 24 September.

| Priority | State |
|---|---|
| One trunk | Done in code: everything is merged to `master`. **Still needs a repo admin** to make `master` the default branch and to retire the other branches (section 3). |
| Gate B, Monday 29 September | **Not run.** I have no machine with the game installed, so I could not run it. The checklist is ready at `validation/GATE-B-CHECKLIST.md`. Someone with a modding machine must own it. |
| First Workshop build, Wednesday 1 October | **Blocked** until a repo admin adds the `CS_MANAGED_ZIP_URL` secret. After that, pushing a `v1.0.0` tag produces the DLL, the zip and a SHA-256 on the release. |
| WO-40 command queue | Merged (PR #5). |
| Horizon 1 (WO-35 … WO-39) | Merged (PR #6 logic, PR #7 four-workspace UI). |
| Horizon 2 | WO-41, WO-42, WO-43 and WO-44 are merged. WO-45 is a first cut and has not met its acceptance criterion (section 4). |

The checks on the handoff commit all pass:
- both compile gates build with 0 errors and 0 warnings;
- 184 unit tests pass;
- 34 engine-harness tests pass, with 1 skipped on purpose (section 2).

## 2. Decisions and actions needed from people

1. **Auction pricing: needs an MD decision.** The engine harness found this
   defect on 25 September.
   - **The defect.** The fair yield is the risk-free spot at the bond's tenor. The offered yield is the short-end required yield, which the wealth and demand adjustments then pull down further.
   - **What the player sees.** A well-reserved AAA city gets 0.00x cover on an Emergency Note, while a thin-budget AA city gets 4.00x.
   - **Current state.** A skipped test pins the defect: `EmergencyNote_ClearsForWellReservedAaaCity` in `Tests.Engine`. The issuance ticket works around it by opening at the clearing spread, so the player pays the concession.
   - **Why it needs the MD.** Pricing changes are reserved for Gate C.
   - **Once decided.** Fix it in `Market/AuctionPricing.cs`, which is the single definition of fair and offered yield, then un-skip the test.
   - **Why it matters now.** It blocks Gate B step 1.
2. **Repo admin (GitHub settings):**
   - Set `master` as the default branch. It is still `claude/cities-skylines-options-trading-03eri0`.
   - Delete the fully merged branches: `bond-engine-percentage-buttons`, `cities-skylines-options-trading-03eri0`, `issue-debt-bond-demand`, `liquidation-logic-bug`, `new-session`, `optimistic-hawking`, `practical-volta`, `ecstatic-meitner`, `integration` and `sharp-shannon-zs2kn3` (all `claude/…`).
   - `claude/liquidation-event` holds an abandoned August feature that was never merged. I recommend retiring it too. Look at it first if you want to keep anything.
   - Add the secrets `CS_MANAGED_ZIP_URL` (required), plus `CS_MANAGED_ZIP_TOKEN` and `CS_MANAGED_ZIP_SHA256` (optional). `CI/fetch-game-assemblies.sh` explains what each one is.
   - Push the tag `v1.0.0`. The session proxy refused my tag push.
3. **Gate B owner (modding machine):**
   - Run `validation/GATE-B-CHECKLIST.md`.
   - The startup self-check line must read `cash=live-field`. If it reads `cash=unbound`, the treasury probe could not find `m_cashAmount`, and cash settlement falls back to the return value.
   - Also confirm in game:
     - Chirper delivery (WO-38);
     - the look of the four workspaces and the charts (WO-39, WO-43);
     - the performance log line (`PERF`, once a year).
4. **A new player:** run a timed first-play session. The Horizon 1 target is a first bond in under 2 minutes. Engineering cannot measure this alone.

## 3. How to work in this repo

- **Build and test** (any machine with the .NET 8 SDK; in a cloud container, `apt-get update && apt-get install -y dotnet-sdk-8.0`):
  ```
  dotnet build CI/CompileCheck.csproj      # stub harness, net35
  dotnet build CI/GameStyleCompile.csproj  # every .cs in the repo, C# 5, no defines: how the game compiles
  dotnet test  Tests                       # pure-logic unit tests
  dotnet test  Tests.Engine                # real engine against stub game types
  ```
  CI (`.github/workflows/ci.yml`) runs these jobs: `compile-check`, `game-style-compile`, `pure-logic-tests`, `engine-tests`, `performance-budget` and `mod-dll`. The `mod-dll` job is skipped until the secret exists. `release.yml` runs on `vX.Y.Z` tags.
- **The game compiles every `.cs` under the mod folder.** Any file that is not
  mod code must be wrapped in a define guard:
  - `BOND_MARKET_TESTS` for unit tests;
  - `BOND_MARKET_ENGINE_TESTS` for the harness;
  - `BOND_MARKET_HEADLESS` for the simulator.

  Mod code must stay at C# 5 and net35. The `game-style-compile` job enforces both.
- **Threading model (WO-40).**
  - The UI never touches engine state. It submits `EngineCommand`s to a lock-free queue and reads the immutable `EngineSnapshot`.
  - The simulation thread drains the queue inside `OnUpdateMoneyAmount` and publishes a new snapshot only on ticks where something changed.
  - Do not reintroduce a lock.
- **Cash settlement.** `Sim/CashSettlement.cs` and `Sim/TreasuryProbe.cs` make cash moved inside the callback count exactly once, whether the game assigns the return value or ignores it. The engine harness tests both behaviours.
- **Engine harness.** `Tests.Engine/Harness` provides:
  - `FakeEconomy`, which has the private `m_cashAmount`, fetch caps, deferred application and an income ledger;
  - `GameHarness`, with `Tick`, `QuietTick`, `Months`, `Boot`, `AlignToPeriodStart` and a seed.

  Runs are deterministic through `BondMarketEngine.SeedForNextReset`. Test parallelisation is off because the game singletons are static.
- **Performance (WO-44).** `Engine/EnginePerf.cs` times every tick. CI fails if either of these breaks:
  - an ordinary tick allocates any bytes;
  - a tick takes more than 50µs, or a period boundary more than 1ms.

  If you add work to the tick, keep it allocation-free. Work that allocates goes in the period rebuild.
- **Merging to `master`.** Work on a branch, wait for green CI, open a PR, and merge. In the cloud sessions, the proxy allowed pushes only to the session branch. I merged through the GitHub API, passing the full 40-character head SHA.

## 4. Pick up first: WO-45 headless simulator

Code: `Tools/HeadlessSim/`.
- `CityTrace.cs` builds synthetic cities or loads a CSV trace.
- `Strategy.cs` holds the Passive, Prudent and Aggressive scripted players.
- `Program.cs` is the runner.

It runs the real engine through the harness. To run it:
```
dotnet run -c Release --project Tools/HeadlessSim -- -cities 12 -years 10 -out calibration
```
It writes `runs.csv` (one row per city and strategy) and `summary.md`.

**Do not trust its numbers yet.** A first run (12 cities, 10 years) gave:

| Strategy | Default rate | Median final rating |
|---|---|---|
| Passive | 0% | AAA |
| Prudent | 67% | AAA |
| Aggressive | 92% | CCC |

A 67% default rate combined with a median AAA rating is not believable. Suspects, in the order I would check them:
1. The auction pricing defect in section 2, which prices issues oddly.
2. The `Defaulted` flag in `Program.Run`, which latches the first time any bond shows `InDefault`. It should be checked against `BondState`.
3. The synthetic budget model in `CityTrace.Synthetic`, which is a placeholder: 3 display units of income per resident per month, and an expense ratio between 0.88 and 1.06.
4. The Prudent player, which may be issuing when it should be repaying.

Remaining to meet WO-45:
- Validate the runner against a hand-checked city.
- Add distribution statistics: rating transition matrix, default rate by size class and strategy, time to first default.
- Load recorded budget traces, which come from the Gate C recordings.
- Add a nightly CI workflow that runs about 1,000 cities for 50 years and uploads the report.

Everything in `Tools/` is guarded by `BOND_MARKET_HEADLESS` and does not affect the game build.

## 5. After WO-45

- Horizon 3 is held until 1.0. Keep the feature freeze.
- Before any pricing change, get the MD's decision on section 2.1.
- The directive documents are in the repo root:
  - `DIRECTIVE.md`, `DIRECTIVE-02.md` and `DIRECTIVE-03.md`, which set the current plan;
  - `ENGINE_UPGRADE_PLAN.md`;
  - `MUNICIPAL_BOND_MARKET.md`, the player-facing design.

Bad news travels fast. If Gate B turns up a failure, write it into `STATUS.md` the same day.
