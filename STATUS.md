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
| WO-40 Command queue | Claude Code | Done on `claude/sharp-shannon-zs2kn3`; merges after CI | `_lock` deleted; engine harness: 15 tests incl. partial cash moves, orders from 8 threads, off-thread saves |
| **Auction pricing defect** | **MD decision** | Found 25 Sep by the engine harness | A well-reserved AAA city fails its Emergency Note auction (cover 0.00x); a thin-budget AA city clears (4.00x). Pinned by a skipped harness test. Blocks Gate B step 1 |
| Horizon 1 (WO-35 … WO-39) | Claude Code | Not started | Waits for WO-40 |
| Horizon 2 (WO-41 … WO-45) | Claude Code | Not started | Waits for Horizon 1 |
| Horizon 3 | None | Held | Feature freeze until 1.0 |

## Daily status

**2026-09-25**
- Merged: nothing new yet; WO-40 (command queue, `_lock` deleted, engine harness) is ready and goes to `master` once CI is green.
- Blocked: auction pricing defect needs an MD decision (it touches pricing, reserved for Gate C); default branch, branch retirement and the release secret still need a repo admin.
- Tomorrow: merge WO-40; start Horizon 1 with the WO-36 issuance ticket, which is where the pricing decision lands.

**2026-09-24**
- Merged: RC-1 … RC-4, WO-33, WO-34, the whole-repo game-style compile check and the cash-settlement fix to `master` (PR #3 and its follow-up).
- Blocked: default branch and branch retirement (repo admin; this session can push only its working branch), release secret (repo admin), Gate B (modding machine).
- Tomorrow: WO-40 command queue and its engine test harness.
