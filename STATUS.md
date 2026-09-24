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
| Gate B | **Unassigned: needs the modding machine** | Not started | `validation/GATE-B-CHECKLIST.md` ready |
| First Workshop build | **Unassigned: needs Steam publisher** | Blocked on WO-33 secret and Gate B | Release workflow produces the Workshop folder |
| WO-40 Command queue | Claude Code | In progress | Starts after the trunk merge, before any Horizon 1 screen |
| Horizon 1 (WO-35 … WO-39) | Claude Code | Not started | Waits for WO-40 |
| Horizon 2 (WO-41 … WO-45) | Claude Code | Not started | Waits for Horizon 1 |
| Horizon 3 | None | Held | Feature freeze until 1.0 |

## Daily status

**2026-09-24**
- Merged: RC-1 … RC-4, WO-33, WO-34 and the whole-repo game-style compile check to `master`.
- Blocked: default branch (repo admin), release secret (repo admin), Gate B (modding machine).
- Tomorrow: WO-40 command queue and its engine test harness.
