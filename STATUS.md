# Status register

One owner per work order. A work order is closed when its acceptance criterion
has been demonstrated, and code on an unmerged branch counts as not shipped.
Trunk: `master`.

| Item | Owner | State | Evidence / blocker |
|---|---|---|---|
| RC-1 … RC-4 (WO-17 … WO-30) | Claude Code | Merged to `master` | CI green; 131 unit tests |
| WO-32 One trunk | Claude Code | Merged to `master` | Needs repo admin: set `master` as the GitHub default branch |
| WO-33 DLL on tagged release | Claude Code | Merged to `master`; blocked | Needs the `CS_MANAGED_ZIP_URL` secret, then a `v1.0.0` tag |
| WO-34 Startup self-check | Claude Code | Merged to `master` | Line format unit-tested; in-game line is a Gate B record |
| Cash settlement in the economy callback | Claude Code | Merged to `master` | 5 unit tests; confirmed in game by Gate B step 2 |
| Gate B | **Unassigned: needs the modding machine** | Not started | `validation/GATE-B-CHECKLIST.md` ready |
| First Workshop build | **Unassigned: needs Steam publisher** | Blocked on WO-33 secret and Gate B | Release workflow produces the Workshop folder |
| WO-40 Command queue | Claude Code | Merged to `master` (PR #5) | `_lock` deleted; engine harness: partial cash moves, orders from 8 threads, off-thread saves |
| **Auction pricing defect** | **MD decision** | Found 25 Sep by the engine harness | A well-reserved AAA city fails its Emergency Note auction (cover 0.00x); a thin-budget AA city clears (4.00x). Pinned by a skipped harness test. Blocks Gate B step 1 |
| WO-35 Rating explains itself | Unassigned (handoff) | Merged (PR #6, #7) | Grid test: every rating shows distance up and down, each gap verified |
| WO-36 Issuance ticket | Unassigned (handoff) | Merged (PR #6, #7) | Harness: 200bp tight previews low cover and fails; preview equals the deal |
| WO-37 Maturity ladder | Unassigned (handoff) | Merged (PR #6, #7) | Harness: warning two periods before the first missed payment |
| WO-38 Chirper alerts | Unassigned (handoff) | Merged (PR #6, #7) | Harness: every type fires, one per month. In-game Chirper delivery is a Gate B check |
| WO-39 Quality of life | Unassigned (handoff) | Merged (PR #7) | Position, last workspace, text size, colour-blind palette, tooltips, briefing. In-game look is a Gate B check |
| Four workspaces | Unassigned (handoff) | Merged (PR #7) | UI smoke test renders every workspace through a city's life; idle window writes no labels |
| Horizon 1 target: first bond in under 2 minutes | **Needs a new player** | Not measured | A timed first-play session; cannot be done by engineering alone |
| WO-41 Curve table | Unassigned (handoff) | Merged (handoff PR) | Unit test: table equals direct evaluation; harness: zero curve evaluations between period rebuilds |
| WO-42 Idle redraw | Unassigned (handoff) | Merged (PR #7) | Harness: idle window writes zero labels |
| WO-43 Charts | Unassigned (handoff) | Merged (handoff PR) | Rating-history and curve charts; harness: one texture upload per period, none on ordinary ticks |
| WO-44 Performance budget | Unassigned (handoff) | Merged (handoff PR) | CI `performance-budget` job: 0 bytes per ordinary tick, ~2µs per tick, ~100–175µs per period (budgets 50µs / 1ms) |
| WO-45 Headless simulator | **Unassigned: next owner** | First cut merged; not accepted | Runner builds and runs; results not yet trusted (see HANDOFF.md). Needs statistics, recorded traces, nightly job |
| Horizon 3 | None | Held | Feature freeze until 1.0 |

## Daily status

**2026-09-26**
- Merged: WO-41, WO-43, WO-44 and the WO-45 first cut to `master` (handoff PR), plus HANDOFF.md.
- Blocked: unchanged. Auction pricing (MD), default branch and branch retirement (repo admin), release secret (repo admin), Gate B (modding machine).
- Tomorrow: the engineering owner changes. The next team starts from HANDOFF.md.

**2026-09-25**
- Merged: WO-40 command queue (PR #5), Horizon 1 logic (PR #6); the four-workspace UI goes next once CI is green.
- Blocked: auction pricing defect needs an MD decision (the ticket works around it by letting the player pay the concession); default branch, branch retirement and the release secret need a repo admin; Gate B needs the modding machine.
- Tomorrow: Horizon 2, starting with WO-41 (discount-factor cache) and WO-44 (performance budget in CI).

**2026-09-24**
- Merged: RC-1 … RC-4, WO-33, WO-34, the whole-repo game-style compile check and the cash-settlement fix to `master` (PR #3 and its follow-up).
- Blocked: default branch and branch retirement (repo admin; this session can push only its working branch), release secret (repo admin), Gate B (modding machine).
- Tomorrow: WO-40 command queue and its engine test harness.
