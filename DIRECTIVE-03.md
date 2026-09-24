# Directive 03 — Forward Plan: Legible, Fast, Alive

**To:** Engineering **From:** Managing Director **Date:** 2026-09-24
**Extends:** Directive 01 and Directive 02. Nothing here overrides their gates, holds or the RC ladder.
**Written against:** `claude/optimistic-hawking-b7fqka` @ `25f7438`, with the RC-1 … RC-4 work on
`claude/sharp-shannon-zs2kn3` @ `015fe3e`

---

## 1. Position

The engine is built around a small, well-tested finance core: yield curve, swap pricing,
auction, market friction, credit model and debt lifecycle. A large game-facing layer wraps it:
a 2,600-line engine and a panel that calls into it about 140 times. The core is stronger than
what the player sees.

RC-1 through RC-4 fixed the number that drives every other system. Before them, the credit
model reported only AAA or D. What the player sees has not kept up: 8 text tabs, 6 rows at a
time, and a rating shown as a single letter with no explanation.

This directive sets the plan past 1.0. It makes the finance core visible and easy to act on,
makes the engine cheap enough that nobody notices it, and then adds what makes a city's debt
feel like part of the city.

**Four principles hold throughout:**

1. **Every number answers a question the player has.** If a figure doesn't change a decision,
   it moves to the Report tab.
2. **Every action shows its effect before it runs.** The pure modules already make "what if"
   calculations cheap.
3. **Zero allocations per game tick, and no noticeable frame cost.**
4. **The game never compiles our source on a player's machine.**

## 2. Step 0 — One trunk, one install path (about 1 week)

| WO | Order | Accept when |
|---|---|---|
| **WO-32** | Merge `sharp-shannon` (RC-1 … RC-4) and `optimistic-hawking` (compile guards, `deploy.ps1`, scenario UI fix) into `integration`. The work has split across five branches, and a player has already downloaded a branch without the compile fix. | One branch carries every fix; CI green on it |
| **WO-33** | CI builds the mod DLL and attaches it to every tagged release. Ship the prebuilt DLL through the Steam Workshop, so players never see the in-game compiler. | A tagged release has a DLL attached; a fresh install loads with no `Source\` folder |
| **WO-34** | Startup self-check: on load, write one line to Debug Output with mod version, save format version, whether the game-ledger reader connected, and the game build. | "UI missing" reports can be diagnosed from that one line |

Gates B and C from Directives 01 and 02 still apply and run alongside Step 0.

## 3. Horizon 1 — "Legible": the player experience (1.1)

### 3.1 Four workspaces replace eight tabs

| Workspace | Replaces | What the player sees first |
|---|---|---|
| **Treasury** | Report and summary line | Rating badge, cash runway, next 3 payments due, one recommendation |
| **Borrow** | Debt tab and issuance | Issuance ticket and maturity ladder |
| **Invest** | Market, Portfolio, Positions | Issuer cards with rating trend, holdings, unrealized P&L |
| **Risk** | Hedging, Settings | Rate exposure gauge, swaps, market regime |

Activity becomes a feed along the bottom edge instead of a separate tab.

### 3.2 Work orders

| WO | Order | Accept when |
|---|---|---|
| **WO-35** | **The rating explains itself.** Next to the badge, show the three factors that decide it (DSCR, debt burden, months of reserves) as bars with the next-notch threshold marked. Example: *"+0.18 DSCR or +1.2 months of reserves to reach AA."* `RatingEngine` is pure, so this is a few extra evaluations per period. | Every rating shows the distance to the next notch up and down |
| **WO-36** | **Issuance ticket with a what-if preview.** A slider sets the offered yield. As it moves, the ticket updates live: expected bid-to-cover, proceeds after the 75bp underwriting fee, annual coupon cost, and the city's DSCR and rating *after* the deal. This completes WO-1: the player picks the price, so a deal priced too tight can fail the way Directive 01 intended. | A deal priced 200bp tight shows low cover before commit and under-fills after |
| **WO-37** | **Maturity ladder.** A 36-month bar strip of coupon and principal payments due. Any month where projected cash falls short turns amber two periods ahead. | A default is never the first warning the player gets |
| **WO-38** | **Chirper alerts**, rate-limited to one per in-game month: downgrades, failed auctions, coupon shortfalls, big rate moves. They show in the game's own UI, so the player doesn't need the panel open. | Each event type fires once in a scripted test city |
| **WO-39** | **Small quality-of-life changes:** remember panel position and last tab; color-blind-safe rating colors; a text-scale option; a tooltip on every figure; a three-card "Treasury Briefing" the first time the panel opens, ending with a suggested first action (an Emergency Note). | Checklist complete |

**Horizon 1 target:** a new player issues their first bond within 2 minutes of opening the
panel, without reading the README.

## 4. Horizon 2 — "Fast": optimization (1.2)

| WO | Order | Accept when |
|---|---|---|
| **WO-40** | **Route all player actions through a command queue.** Today, button clicks change engine state from the UI thread under `_lock`, and fund themselves by reading `LastCashAmount` outside the game tick. That is where every cash-cursor bug so far has come from. Instead, the UI places orders on a queue, and the simulation thread carries them out at the start of its next tick, against the balance the game has just confirmed. Together with the read-only snapshot WO-27 already publishes, this removes `_lock` completely: the UI and simulation never share mutable state. | `_lock` deleted; partial-cash-move tests green |
| **WO-41** | **Cache the discount factors each period.** The yield curve changes once per period. Compute a 120-entry table of discount factors when it changes (about 1 microsecond). Every bond valuation, swap annuity and auction fair-yield lookup then becomes a table read instead of repeated `Math.Exp` calls. | Pricing tests unchanged; no `Math.Exp` in per-tick paths |
| **WO-42** | **Redraw only what changed.** Give each workspace a dirty flag tied to the snapshot version. Only reformat a label when its value has actually changed, using one reusable `StringBuilder`. Today about 69 `string.Format` calls run every 4 seconds whether anything changed or not. | Close to zero UI work while the city is idle |
| **WO-43** | **Charts drawn directly into small textures.** Rating history and the yield curve are drawn into small `Texture2D` images once per period, one sprite each, with no UI component per data point. | Charts update once per period |
| **WO-44** | **Performance budget enforced in CI:** under 50 microseconds of engine time per tick, 0 bytes allocated per tick, under 1 ms for the once-per-period metrics block. A debug build records these numbers, and a benchmark test fails if they regress. | Benchmark job in CI, red on regression |
| **WO-45** | **Headless city simulator.** Run the pure engine without the game: 50 in-game years × 1,000 seeded cities, driven by recorded budget traces from real saves. This turns Gate C calibration from a manual play session into an overnight CI run, producing rating distributions, auction cover distributions and default rates by city size. | Calibration report generated from CI; constants cite it |

## 5. Horizon 3 — "Alive": new features (2.0)

One feature per release, in this order:

1. **Project bonds.** Link a bond to a specific building. Placing a hospital offers a "Hospital
   Bond". The building's upkeep and service revenue then back the debt, and an info-view
   overlay tints bond-funded buildings. Debt becomes something the player can see on the map.
2. **Citizens as bondholders.** Cims who buy city paper get a small happiness bonus while
   coupons are paid. A default makes them unhappy. Borrowing gains a political cost and not
   just a financial one.
3. **Callable bonds and refinancing.** The yield curve already moves over a business cycle.
   Callable bonds let a disciplined player refinance when rates fall, which rewards watching the
   market instead of spamming issuance.
4. **Green bonds.** Parks, renewable power and transit projects qualify for a 15–25bp discount.
   The discount is bigger with the Green Cities DLC.
5. **Rating agency review.** Once a year, a short scripted review with a narrative verdict and a
   preview: *"We'd downgrade if reserves drop below 2 months."* It gives the player a deadline
   to act on.
6. **Market regimes as challenges.** Named rate scenarios with goals: "1979 Volcker Shock",
   "2008 Credit Freeze", "Zero-Rate Decade". For example: stay investment grade through the
   2008 scenario. Workshop achievements come from these.
7. **Neighbour-city issuers.** The market's issuers become the cities at your outside
   connections. Their credit rises and falls with trade volume, so your transport network
   affects your bond portfolio.

Each Horizon 3 feature needs its own acceptance criterion before work starts. Directive 02's
feature freeze holds until 1.0 ships.

## 6. Order of work

| Order | Milestone | Done when |
|---|---|---|
| 1 | Step 0 plus Gates B and C | Workshop DLL live; calibration record for 3 city sizes |
| 2 | WO-40 command queue (moved ahead of the UX work) | `_lock` deleted; partial-cash-move tests green |
| 3 | Horizon 1 | First bond issued in under 2 minutes by a new player; zero "where is the UI" reports |
| 4 | Rest of Horizon 2 | Performance budget enforced in CI |
| 5 | Horizon 3, one feature per release | Project bonds first, since they tie debt to the map |

**Why the command queue goes first:** every new button in Horizon 1 would otherwise add another
cross-thread write path. Building the queue first means the new UI is built on a safe foundation
from day one.

---

**On the record:** Directives 01 and 02 made the numbers true. This one makes them useful to
the person playing. The standing rules still apply: a work order is done when its acceptance
criterion is demonstrable, and a *Held* item does not start.
