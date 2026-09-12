# Bond Market — pure-logic tests

xUnit tests for the parts of the mod that have **no** Cities: Skylines or Unity
dependencies: `BondMarket.cs` (pricing, rating table) and `CimDemandEngine.cs`
(demand / pressure / vitals chain). Those two files are linked directly into
this project (see the `<Compile Include>` entries in
`BondMarket.Tests.csproj`), so there is a single source of truth and no copy to
keep in sync.

The mod itself targets `net35` (Unity Mono) and references the game DLLs. This
test project targets `net8.0` only so a stock .NET runner can execute it; the
code under test is framework-agnostic (`System.Math` / `System.Random` only),
so the game is never needed to build or run these tests.

## Running

```bash
dotnet test Tests/BondMarket.Tests.csproj
```

CI runs this automatically (`.github/workflows/ci.yml`) on every push and pull
request.

## Scope

These tests pin the **current, correct** behavior of the pure math so the
Phase 1 defect fixes (which live in `BondMarketEngine.cs`) can land without
silently changing pricing or rating results. Where a later phase deliberately
changes one of these functions, the corresponding test is updated in that
phase's change.
