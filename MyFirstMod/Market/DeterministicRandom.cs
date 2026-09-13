using System;

namespace MyFirstMod
{
    // Phase 5 (G-1): a small, explicit, serializable PRNG. System.Random can't be
    // serialized on net35, so stochastic outcomes (issuer defaults, auctions) would
    // re-roll on reload - save-scumming. This xorshift128 generator has 4-uint state
    // that persists in the save, and derives from System.Random (overriding both the
    // legacy Sample() path and Next/NextDouble directly) so it drops into every
    // existing consumer unchanged, deterministically on both net35 and net8.
    public sealed class DeterministicRandom : Random
    {
        private uint _x, _y, _z, _w;

        public DeterministicRandom(int seed)
        {
            uint s = unchecked((uint)seed);
            if (s == 0u) s = 0x9E3779B9u;
            // SplitMix-style expansion so a small seed fills all four words.
            _x = s;
            _y = unchecked(_x * 747796405u + 2891336453u);
            _z = unchecked(_y * 747796405u + 2891336453u);
            _w = unchecked(_z * 747796405u + 2891336453u);
            if ((_x | _y | _z | _w) == 0u) _w = 0x9E3779B9u;
        }

        public DeterministicRandom(uint x, uint y, uint z, uint w)
        {
            _x = x; _y = y; _z = z; _w = w;
            if ((_x | _y | _z | _w) == 0u) _w = 0x9E3779B9u;
        }

        private uint NextUInt()
        {
            uint t = _x ^ (_x << 11);
            _x = _y; _y = _z; _z = _w;
            _w = _w ^ (_w >> 19) ^ (t ^ (t >> 8));
            return _w;
        }

        // 24 random bits mapped to [0, 1).
        protected override double Sample()
        {
            return (NextUInt() >> 8) * (1.0 / 16777216.0);
        }

        public override int Next()
        {
            return (int)(Sample() * int.MaxValue);
        }

        public override int Next(int maxValue)
        {
            if (maxValue <= 0) return 0;
            return (int)(Sample() * maxValue);
        }

        public override int Next(int minValue, int maxValue)
        {
            if (maxValue <= minValue) return minValue;
            long range = (long)maxValue - minValue;
            return (int)(minValue + (long)(Sample() * range));
        }

        public override double NextDouble()
        {
            return Sample();
        }

        public uint[] GetState()
        {
            return new uint[] { _x, _y, _z, _w };
        }

        public void SetState(uint[] s)
        {
            if (s != null && s.Length >= 4)
            {
                _x = s[0]; _y = s[1]; _z = s[2]; _w = s[3];
                if ((_x | _y | _z | _w) == 0u) _w = 0x9E3779B9u;
            }
        }
    }
}
