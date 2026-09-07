using System;
using System.Collections.Generic;

namespace ResourceFramework
{
    public sealed class EquipmentInstance
    {
        public string InstanceId;
        public string DefinitionId;
        public string Slot;
        public EquipmentInstance Copy() { return (EquipmentInstance)MemberwiseClone(); }
    }

    public sealed class CharacterInstance
    {
        public string InstanceId;
        public string DefinitionId;
        public string ConfigVersion;
        public int Level = 1;
        public double CurrentHp;
        public Dictionary<string, double> BaseStats = new Dictionary<string, double>(StringComparer.Ordinal);
        public List<EquipmentInstance> Equipment = new List<EquipmentInstance>();
        public List<string> AbilityIds = new List<string>();
        public string TalentBoardId;
        public List<string> PerfumeIds = new List<string>();

        public CharacterInstance Copy()
        {
            var result = (CharacterInstance)MemberwiseClone();
            result.BaseStats = new Dictionary<string, double>(BaseStats, StringComparer.Ordinal);
            result.Equipment = new List<EquipmentInstance>();
            foreach (var item in Equipment) result.Equipment.Add(item.Copy());
            result.AbilityIds = new List<string>(AbilityIds);
            result.PerfumeIds = new List<string>(PerfumeIds);
            return result;
        }
    }

    public sealed class RewardReceipt
    {
        public string ResourceId;
        public string ResourceKind;
        public long Amount;
        public string Quality;
        public List<string> InstanceIds = new List<string>();
        public RewardReceipt Copy()
        {
            var result = (RewardReceipt)MemberwiseClone();
            result.InstanceIds = new List<string>(InstanceIds);
            return result;
        }
    }

    public sealed class CraftJob
    {
        public string JobId;
        public string RecipeId;
        public string ConfigVersion;
        public int Batches;
        public DateTimeOffset ReadyAtUtc;
        public bool Claimed;
        // Final quantities are captured at Start; a subsequent config release cannot change them.
        public List<RewardReceipt> Outputs = new List<RewardReceipt>();
        public CraftJob Copy()
        {
            var result = (CraftJob)MemberwiseClone();
            result.Outputs = CopyRewards(Outputs);
            return result;
        }
        internal static List<RewardReceipt> CopyRewards(List<RewardReceipt> items)
        {
            var copy = new List<RewardReceipt>();
            foreach (var item in items) copy.Add(item.Copy());
            return copy;
        }
    }

    /// <summary>Owned mutable state. Store constructor and Read always deep copy this object.</summary>
    public sealed class PlayerState
    {
        public Dictionary<string, long> Balances = new Dictionary<string, long>(StringComparer.Ordinal);
        public long ActionPoints;
        public Dictionary<string, CharacterInstance> Characters = new Dictionary<string, CharacterInstance>(StringComparer.Ordinal);
        public Dictionary<string, EquipmentInstance> Equipment = new Dictionary<string, EquipmentInstance>(StringComparer.Ordinal);
        public Dictionary<string, CraftJob> CraftJobs = new Dictionary<string, CraftJob>(StringComparer.Ordinal);
        public Dictionary<string, int> PityCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        public long BalanceOf(string resourceId)
        {
            long value;
            return Balances.TryGetValue(resourceId, out value) ? value : 0L;
        }

        internal PlayerState Copy()
        {
            var result = new PlayerState();
            result.Balances = new Dictionary<string, long>(Balances, StringComparer.Ordinal);
            result.ActionPoints = ActionPoints;
            result.PityCounts = new Dictionary<string, int>(PityCounts, StringComparer.Ordinal);
            foreach (var pair in Characters) result.Characters.Add(pair.Key, pair.Value.Copy());
            foreach (var pair in Equipment) result.Equipment.Add(pair.Key, pair.Value.Copy());
            foreach (var pair in CraftJobs) result.CraftJobs.Add(pair.Key, pair.Value.Copy());
            return result;
        }
    }

    /// <summary>
    /// In-memory unit of work: state and successful operation receipts share one lock/commit.
    /// Different threads serialize writes; nested writes from injected callbacks are rejected.
    /// Initial state, public reads, callback candidates, and operation results remain detached.
    /// Durable storage/server authority are intentionally outside this offline sample.
    /// </summary>
    public sealed class PlayerStateStore
    {
        public const long MaxAmount = 9007199254740991L;
        private sealed class Operation
        {
            public string Signature;
            public object Result;
        }
        private readonly object sync = new object();
        private readonly Dictionary<string, Operation> operations = new Dictionary<string, Operation>(StringComparer.Ordinal);
        private PlayerState state;
        private bool transactionActive;

        public PlayerStateStore(PlayerState initial)
        {
            if (initial == null) throw new ArgumentNullException("initial");
            var owned = initial.Copy();
            if (owned.ActionPoints < 0 || owned.ActionPoints > MaxAmount) throw new ArgumentException("Action points are outside the supported range.");
            foreach (var pair in owned.Balances)
                if (pair.Value < 0 || pair.Value > MaxAmount) throw new ArgumentException("Balance is outside the supported range: " + pair.Key);
            foreach (var pair in owned.PityCounts)
                if (pair.Value < 0) throw new ArgumentException("Pity counter cannot be negative: " + pair.Key);
            state = owned;
        }

        public PlayerState Read() { lock (sync) return state.Copy(); }

        internal T Execute<T>(string operationId, string signature, Func<PlayerState, T> action, Func<T, T> copy)
        {
            if (String.IsNullOrWhiteSpace(operationId)) throw new ArgumentException("operationId is required.", "operationId");
            lock (sync)
            {
                Operation prior;
                if (operations.TryGetValue(operationId, out prior))
                {
                    if (prior.Signature != signature || !(prior.Result is T))
                        throw new InvalidOperationException("operationId is already bound to a different request.");
                    return copy((T)prior.Result);
                }
                // Monitor locks are reentrant; a reward callback must not start a nested write.
                if (transactionActive) throw new InvalidOperationException("Nested player-state transactions are not supported.");
                transactionActive = true;
                try
                {
                    var candidate = state.Copy();
                    T result = action(candidate); // Any exception discards every state modification.
                    // An injected handler may retain its candidate for diagnostics. Keep ownership private.
                    PlayerState committed = candidate.Copy();
                    T saved = copy(result);
                    T returned = copy(result);
                    operations.Add(operationId, new Operation { Signature = signature, Result = saved });
                    state = committed;
                    return returned;
                }
                finally { transactionActive = false; }
            }
        }

        internal static string Signature(string verb, string resourceId, int count)
        {
            if (String.IsNullOrWhiteSpace(resourceId)) throw new ArgumentException("Resource/job ID is required.");
            return verb.Length + ":" + verb + ":" + resourceId.Length + ":" + resourceId + ":" + count;
        }

        internal static void Pay(PlayerState state, Dictionary<string, long> costs, long actionPoints)
        {
            if (actionPoints < 0 || actionPoints > MaxAmount) throw new InvalidOperationException("Action point cost is outside the supported range.");
            if (state.ActionPoints < actionPoints) throw new InvalidOperationException("Insufficient action points.");
            foreach (var pair in costs)
                if (state.BalanceOf(pair.Key) < pair.Value) throw new InvalidOperationException("Insufficient balance: " + pair.Key);
            foreach (var pair in costs) state.Balances[pair.Key] = checked(state.BalanceOf(pair.Key) - pair.Value);
            state.ActionPoints = checked(state.ActionPoints - actionPoints);
        }

        internal static Dictionary<string, long> AggregateCosts(ResourceAmount[] items, int count)
        {
            var costs = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var item in items)
            {
                if (item.amount <= 0) throw new InvalidOperationException("Cost must be positive.");
                long previous;
                costs.TryGetValue(item.resourceId, out previous);
                costs[item.resourceId] = checked(previous + checked(item.amount * count));
                if (costs[item.resourceId] > MaxAmount) throw new OverflowException("Aggregated cost exceeds the supported maximum.");
            }
            return costs;
        }

        internal static void Credit(PlayerState state, string id, long amount)
        {
            if (amount <= 0) throw new InvalidOperationException("Reward must be positive.");
            long balance = checked(state.BalanceOf(id) + amount);
            if (balance > MaxAmount) throw new OverflowException("Balance exceeds the supported maximum: " + id);
            state.Balances[id] = balance;
        }
    }
}
