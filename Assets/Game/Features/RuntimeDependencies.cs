using System;

namespace ResourceFramework
{
    public interface IClock { DateTimeOffset UtcNow { get; } }
    public interface IRandomSource { long NextInt64(long exclusiveMax); }

    public sealed class SystemClock : IClock
    {
        public DateTimeOffset UtcNow { get { return DateTimeOffset.UtcNow; } }
    }

    /// <summary>Demo/test clock; game services never sleep or read Unity time.</summary>
    public sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; private set; }
        public FixedClock(DateTimeOffset initial) { UtcNow = initial.ToUniversalTime(); }
        public void Advance(TimeSpan amount)
        {
            if (amount < TimeSpan.Zero) throw new ArgumentOutOfRangeException("amount");
            UtcNow = UtcNow.Add(amount);
        }
    }

    /// <summary>Repeatable local demonstration RNG, not an authoritative paid-gacha RNG.</summary>
    public sealed class SeededRandomSource : IRandomSource
    {
        private readonly Random random;
        private readonly object sync = new object();
        public SeededRandomSource(int seed) { random = new Random(seed); }

        public long NextInt64(long exclusiveMax)
        {
            if (exclusiveMax <= 0) throw new ArgumentOutOfRangeException("exclusiveMax");
            lock (sync)
            {
                // Sample uniformly from [0, 2^63), rejecting its incomplete final bucket.
                ulong range = 1UL << 63;
                ulong bound = (ulong)exclusiveMax;
                ulong limit = range - range % bound;
                var bytes = new byte[8];
                ulong sample;
                do
                {
                    random.NextBytes(bytes);
                    sample = BitConverter.ToUInt64(bytes, 0) & 0x7fffffffffffffffUL;
                } while (sample >= limit);
                return (long)(sample % bound);
            }
        }
    }
}
