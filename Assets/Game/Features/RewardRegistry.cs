using System;
using System.Collections.Generic;

namespace ResourceFramework
{
    /// <summary>
    /// Validation has no side effects; Grant modifies only the supplied candidate state.
    /// A handler must not start another write transaction on the same store: nested writes are rejected.
    /// The store detaches the committed state, so retaining a candidate cannot mutate it after commit.
    /// </summary>
    public interface IRewardResolver
    {
        void Validate(ResourceSnapshot snapshot, GachaEntry entry);
        RewardReceipt Grant(PlayerState candidate, GachaEntry entry);
    }

    public interface IRewardHandler
    {
        void Validate(GachaEntry entry);
        RewardReceipt Grant(PlayerState candidate, GachaEntry entry);
    }

    /// <summary>Composition-time registration keeps gacha independent of concrete reward systems.</summary>
    public sealed class RewardRegistry : IRewardResolver
    {
        private readonly ResourceSnapshot snapshot;
        private readonly Dictionary<string, IRewardHandler> handlers = new Dictionary<string, IRewardHandler>(StringComparer.Ordinal);
        public RewardRegistry(ResourceSnapshot snapshot)
        {
            this.snapshot = snapshot ?? throw new ArgumentNullException("snapshot");
        }

        public RewardRegistry Register(string resourceKind, IRewardHandler handler)
        {
            if (String.IsNullOrWhiteSpace(resourceKind)) throw new ArgumentException("Resource kind is required.", "resourceKind");
            if (handler == null) throw new ArgumentNullException("handler");
            if (handlers.ContainsKey(resourceKind)) throw new InvalidOperationException("Reward handler is already registered: " + resourceKind);
            handlers.Add(resourceKind, handler);
            return this;
        }

        public void Validate(ResourceSnapshot operationSnapshot, GachaEntry entry)
        {
            if (!Object.ReferenceEquals(operationSnapshot, snapshot))
                throw new InvalidOperationException("Reward resolver must use the operation's configuration snapshot.");
            if (entry == null) throw new ArgumentNullException("entry");
            if (entry.amount < 1 || entry.amount > PlayerStateStore.MaxAmount)
                throw new InvalidOperationException("Reward amount is outside the supported range.");
            snapshot.EnsureDependencies(entry.resourceId);
            Resolve(entry.resourceId).Validate(entry);
        }

        public RewardReceipt Grant(PlayerState candidate, GachaEntry entry)
        {
            if (candidate == null) throw new ArgumentNullException("candidate");
            return Resolve(entry.resourceId).Grant(candidate, entry);
        }

        private IRewardHandler Resolve(string resourceId)
        {
            string kind = snapshot.KindOf(resourceId);
            IRewardHandler handler;
            if (!handlers.TryGetValue(kind, out handler)) throw new InvalidOperationException("No reward handler is registered for: " + kind);
            return handler;
        }
    }
}
