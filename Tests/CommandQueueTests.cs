// Test-only: compiled solely by Tests/BondMarket.Tests.csproj, which defines
// BOND_MARKET_TESTS. The game compiles every .cs under Source\, so without
// this guard a full repo copy fails in-game (C# 7 syntax, xUnit refs).
#if BOND_MARKET_TESTS
using System.Collections.Generic;
using System.Threading;
using Xunit;

namespace MyFirstMod.Tests
{
    // WO-40: the order queue between the UI and the simulation thread.
    public class CommandQueueTests
    {
        private sealed class Item
        {
            public int Producer;
            public int N;
        }

        [Fact]
        public void SingleProducer_DrainsInEnqueueOrder()
        {
            var q = new CommandQueue<Item>();
            for (int i = 0; i < 5; i++) q.Enqueue(new Item { N = i });

            var got = new List<Item>();
            Assert.Equal(5, q.DrainTo(got));
            for (int i = 0; i < 5; i++) Assert.Equal(i, got[i].N);
            Assert.True(q.IsEmpty);
        }

        [Fact]
        public void EmptyDrain_AddsNothing()
        {
            var q = new CommandQueue<Item>();
            var got = new List<Item> { new Item() };
            Assert.Equal(0, q.DrainTo(got));
            Assert.Single(got);
        }

        [Fact]
        public void DrainAppends_AfterExistingItems()
        {
            var q = new CommandQueue<Item>();
            q.Enqueue(new Item { N = 1 });
            q.Enqueue(new Item { N = 2 });
            var got = new List<Item> { new Item { N = 0 } };
            q.DrainTo(got);
            Assert.Equal(new[] { 0, 1, 2 }, got.ConvertAll(x => x.N).ToArray());
        }

        // Many producers race one consumer that drains while they enqueue.
        // Every item arrives exactly once, and each producer's items arrive in
        // the order that producer enqueued them.
        [Fact]
        public void ManyProducers_OneConsumer_ExactlyOnce_PerProducerOrder()
        {
            const int producers = 8;
            const int perProducer = 20000;
            var q = new CommandQueue<Item>();
            var threads = new Thread[producers];
            int finished = 0;
            for (int p = 0; p < producers; p++)
            {
                int id = p;
                threads[p] = new Thread(() =>
                {
                    for (int n = 0; n < perProducer; n++) q.Enqueue(new Item { Producer = id, N = n });
                    Interlocked.Increment(ref finished);
                });
            }
            foreach (var t in threads) t.Start();

            var got = new List<Item>(producers * perProducer);
            while (Volatile.Read(ref finished) < producers || !q.IsEmpty)
                q.DrainTo(got);
            foreach (var t in threads) t.Join();
            q.DrainTo(got);

            Assert.Equal(producers * perProducer, got.Count);
            var next = new int[producers];
            foreach (var item in got)
            {
                Assert.Equal(next[item.Producer], item.N);
                next[item.Producer]++;
            }
        }
    }
}

#endif
