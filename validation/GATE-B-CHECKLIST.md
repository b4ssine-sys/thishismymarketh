# Gate B: run it in the game, once, thoroughly

Directive 01, Gate B, plus the checks Directive 03 and the 24 September memo add.
Needs the modding machine and one person, in one session. Copy this file to
`validation/GATE-B-<yyyy-mm-dd>.md`, fill in every **Record** line, and commit it
the same day. A step that cannot be done is written down with the reason; it is
never skipped silently.

## 0. Setup

- [ ] Build: `.\deploy.ps1` from the repo root at the commit under test.
      **Record:** commit hash.
- [ ] `Mods\MyFirstMod\` holds `MyFirstMod.dll` and **no** `Source\` folder.
- [ ] **Record:** game build (main menu, bottom-right), OS, DLCs enabled.

## 1. Load and first look

- [ ] Start a **new** city. Open Debug Output (F7).
      **Record:** the `[MyFirstMod] SELF-CHECK` line, verbatim.
      Expect `ui=OK` and `save=v12`. `ledger=` answers Gate B's first question:
      which `GetIncomeAndExpenses` overload resolved (the shape in brackets).
- [ ] The toolbar icon (top-left) and Shift+B both open the panel.
- [ ] A debt-free new city is **not** rated D, and the Emergency Note can be
      issued (RC-1 / WO-18). **Record:** rating shown.

## 2. Money moves exactly once (critical)

The engine moves cash inside the game's economy callback. Each action below must
change the treasury by the stated amount **once**: not zero times, not twice.

- [ ] Note the treasury. Issue an Emergency Note.
      **Record:** treasury before, after, and the proceeds the panel reports.
      Expect: after = before + proceeds, give or take organic income that tick.
- [ ] The budget panel books the proceeds under loans, not public income (WO-6).
- [ ] Buy one market bond. **Record:** treasury before/after and the price.
- [ ] Pay 25%. **Record:** treasury before/after and the amount reported.
- [ ] Let one month pass with the bond outstanding.
      **Record:** the coupon paid and the treasury change.

## 3. G-2: pledged-service revenue

- [ ] With water service built and running for a month, compare the
      `G-2 sample` lines in Debug Output with the game's own budget panel for
      water income and expense. **Record:** both sets of numbers, and yes/no:
      do they match? This answer decides WO-11 (revenue bonds).

## 4. Time

- [ ] Note the in-game date of a coupon at speed 1. Reload, run at speed 3.
      **Record:** both dates. They must be the same (WO-4).

## 5. Save and load

- [ ] Save, quit to menu, reload. Bonds, swaps, rating and reports persist.
      **Record:** SELF-CHECK line after reload.
- [ ] Load a genuine save from before RC-1 (format v11 or older).
      **Record:** loads yes/no, any errors.
- [ ] Load city A, then city B in the same session. City B shows nothing from
      city A (WO-7).
- [ ] Reflection fallback: if `ledger=BOUND`, record that the fallback path
      could not be forced on this build. If `ledger=FALLBACK`, confirm the
      "ledger API unavailable" line appears exactly once.

## 6. One in-game year

- [ ] Play one full in-game year at normal speed with at least one issued bond.
      **Record:** coupons paid (expect 12), quarterly reports (expect 4), any
      exceptions in `output_log.txt` (expect none).
- [ ] Save at the end of the year.

## Outcome

- [ ] **Record:** pass / fail, and every deviation above with its step number.
- [ ] If pass: `master` is the validated head (Directive 01 §7).
