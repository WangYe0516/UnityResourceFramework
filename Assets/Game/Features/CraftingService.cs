using System;
using System.Collections.Generic;

namespace ResourceFramework
{
    public sealed class CraftResult
    {
        public string JobId;
        public bool Completed;
        public DateTimeOffset ReadyAtUtc;
        public string ConfigVersion;
        public List<RewardReceipt> Outputs = new List<RewardReceipt>();
        public CraftResult Copy()
        {
            var result = (CraftResult)MemberwiseClone();
            result.Outputs = CraftJob.CopyRewards(Outputs);
            return result;
        }
    }

    public sealed class CraftingService
    {
        private readonly ResourceSnapshot snapshot;
        private readonly PlayerStateStore store;
        private readonly IClock clock;
        public CraftingService(ResourceSnapshot snapshot, PlayerStateStore store, IClock clock)
        {
            this.snapshot = snapshot ?? throw new ArgumentNullException("snapshot");
            this.store = store ?? throw new ArgumentNullException("store");
            this.clock = clock ?? throw new ArgumentNullException("clock");
        }

        public CraftResult Start(string recipeId, int batches, string operationId)
        {
            if (batches < 1 || batches > 1000) throw new ArgumentOutOfRangeException("batches", "Batch count must be 1..1000.");
            return store.Execute(operationId, PlayerStateStore.Signature("craft.start", recipeId, batches), state =>
            {
                snapshot.EnsureDependencies(recipeId);
                var recipe = snapshot.Get<RecipeData>(recipeId);
                var costs = PlayerStateStore.AggregateCosts(recipe.inputs, batches);
                long actionPoints = checked((long)recipe.actionPointCost * batches);
                var outputs = new List<RewardReceipt>();
                foreach (var output in recipe.outputs)
                {
                    if (output.probabilityPermille != 1000) throw new InvalidOperationException("Only fixed craft outputs are supported.");
                    snapshot.Get<MaterialData>(output.resourceId);
                    long amount = checked(output.amount * batches);
                    if (amount <= 0 || amount > PlayerStateStore.MaxAmount) throw new OverflowException("Craft output is outside the supported range.");
                    outputs.Add(new RewardReceipt
                    {
                        ResourceId = output.resourceId, ResourceKind = "material",
                        Amount = amount, Quality = output.quality
                    });
                }
                // Batches execute in parallel; duration is per job, costs/output scale by batches.
                var readyAt = clock.UtcNow.ToUniversalTime().AddSeconds(recipe.durationSeconds);
                var job = new CraftJob
                {
                    JobId = Guid.NewGuid().ToString("N"), RecipeId = recipeId,
                    ConfigVersion = snapshot.Version, Batches = batches,
                    ReadyAtUtc = readyAt, Outputs = outputs
                };
                PlayerStateStore.Pay(state, costs, actionPoints);
                if (recipe.durationSeconds == 0)
                {
                    Grant(state, job.Outputs);
                    job.Claimed = true;
                }
                state.CraftJobs.Add(job.JobId, job);
                return Result(job);
            }, result => result.Copy());
        }

        public CraftResult Claim(string jobId, string operationId)
        {
            return store.Execute(operationId, PlayerStateStore.Signature("craft.claim", jobId, 1), state =>
            {
                CraftJob job;
                if (!state.CraftJobs.TryGetValue(jobId, out job)) throw new InvalidOperationException("Unknown craft job: " + jobId);
                if (job.Claimed) throw new InvalidOperationException("Craft job has already been claimed.");
                if (clock.UtcNow < job.ReadyAtUtc) throw new InvalidOperationException("Craft job is not ready.");
                // Use captured reward amounts; do not re-read the current recipe.
                Grant(state, job.Outputs);
                job.Claimed = true;
                return Result(job);
            }, result => result.Copy());
        }

        private static void Grant(PlayerState state, List<RewardReceipt> outputs)
        {
            foreach (var output in outputs) PlayerStateStore.Credit(state, output.ResourceId, output.Amount);
        }
        private static CraftResult Result(CraftJob job)
        {
            return new CraftResult
            {
                JobId = job.JobId, Completed = job.Claimed, ReadyAtUtc = job.ReadyAtUtc,
                ConfigVersion = job.ConfigVersion, Outputs = CraftJob.CopyRewards(job.Outputs)
            };
        }
    }
}
