using System;
using System.Collections.Generic;
using System.Globalization;

namespace ResourceFramework
{
    public sealed class GachaResult
    {
        public string PoolId;
        public string ConfigVersion;
        public int PityCount;
        public List<RewardReceipt> Rewards = new List<RewardReceipt>();
        public GachaResult Copy()
        {
            var result = (GachaResult)MemberwiseClone();
            result.Rewards = CraftJob.CopyRewards(Rewards);
            return result;
        }
    }

    public sealed class GachaService
    {
        private readonly ResourceSnapshot snapshot;
        private readonly PlayerStateStore store;
        private readonly IRandomSource random;
        private readonly IClock clock;
        private readonly IRewardResolver rewards;
        public GachaService(ResourceSnapshot snapshot, PlayerStateStore store, IRandomSource random, IClock clock, IRewardResolver rewards)
        {
            this.snapshot = snapshot ?? throw new ArgumentNullException("snapshot");
            this.store = store ?? throw new ArgumentNullException("store");
            this.random = random ?? throw new ArgumentNullException("random");
            this.clock = clock ?? throw new ArgumentNullException("clock");
            this.rewards = rewards ?? throw new ArgumentNullException("rewards");
        }

        public GachaResult Draw(string poolId, int drawCount, string operationId)
        {
            if (drawCount < 1 || drawCount > 1000) throw new ArgumentOutOfRangeException("drawCount", "Draw count must be 1..1000.");
            return store.Execute(operationId, PlayerStateStore.Signature("gacha.draw", poolId, drawCount), state =>
            {
                snapshot.EnsureDependencies(poolId);
                var pool = snapshot.Get<GachaPoolData>(poolId);
                Array.Sort(pool.entries, (left, right) => StringComparer.Ordinal.Compare(left.entryId, right.entryId));
                var now = clock.UtcNow;
                if (!String.IsNullOrEmpty(pool.startsAtUtc) && now < ParseUtc(pool.startsAtUtc))
                    throw new InvalidOperationException("Gacha pool has not opened.");
                if (!String.IsNullOrEmpty(pool.endsAtUtc) && now >= ParseUtc(pool.endsAtUtc))
                    throw new InvalidOperationException("Gacha pool is closed.");
                // Reject every unsupported candidate before paying or consuming random values.
                foreach (var entry in pool.entries) rewards.Validate(snapshot, entry);
                var costs = PlayerStateStore.AggregateCosts(pool.drawCosts, drawCount);
                PlayerStateStore.Pay(state, costs, 0);
                var result = new GachaResult { PoolId = poolId, ConfigVersion = snapshot.Version };
                int counter = 0;
                if (pool.pity != null)
                {
                    if (pool.pity.ruleType != "hard_pity" || pool.pity.guaranteeAt < 1)
                        throw new InvalidOperationException("Unsupported pity rule.");
                    state.PityCounts.TryGetValue(pool.pity.counterKey, out counter);
                }
                for (int i = 0; i < drawCount; i++)
                {
                    bool guaranteed = pool.pity != null && counter >= pool.pity.guaranteeAt - 1;
                    var entry = Select(pool.entries, guaranteed);
                    result.Rewards.Add(rewards.Grant(state, entry));
                    if (pool.pity != null)
                    {
                        if (entry.isPityTarget && pool.pity.resetOnTarget) counter = 0;
                        else counter = (int)Math.Min((long)pool.pity.guaranteeAt, (long)counter + 1);
                    }
                }
                if (pool.pity != null) state.PityCounts[pool.pity.counterKey] = counter;
                result.PityCount = counter;
                return result;
            }, result => result.Copy());
        }

        private GachaEntry Select(GachaEntry[] entries, bool guaranteed)
        {
            long total = 0;
            foreach (var entry in entries)
                if (entry.weight > 0 && (!guaranteed || entry.isPityTarget)) total = checked(total + entry.weight);
            if (total <= 0) throw new InvalidOperationException("No selectable gacha entries.");
            long roll = random.NextInt64(total);
            if (roll < 0 || roll >= total) throw new InvalidOperationException("RNG returned a value outside its contract.");
            long boundary = 0;
            foreach (var entry in entries)
            {
                if (entry.weight <= 0 || (guaranteed && !entry.isPityTarget)) continue;
                boundary = checked(boundary + entry.weight);
                if (roll < boundary) return entry;
            }
            throw new InvalidOperationException("Gacha weight interval is invalid.");
        }

        private static DateTimeOffset ParseUtc(string value)
        {
            DateTimeOffset parsed = DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            if (parsed.Offset != TimeSpan.Zero) throw new InvalidOperationException("Pool time must use UTC.");
            return parsed;
        }
    }
}
