// Test-only: compiled solely by Tests.Engine, which defines
// BOND_MARKET_ENGINE_TESTS. The game compiles every .cs under Source\, so
// without this guard a full repo copy fails in-game.
#if BOND_MARKET_ENGINE_TESTS
using Xunit;

// The engine and the game singletons are process-wide statics.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
#endif
