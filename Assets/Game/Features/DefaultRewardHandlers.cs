using System;

namespace ResourceFramework
{
    /// <summary>Composition bridge. GachaService itself does not reference any concrete reward system.</summary>
    public static class DefaultRewardHandlers
    {
        public static IRewardResolver StandardRewards(ResourceSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException("snapshot");
            return new RewardRegistry(snapshot)
                .Register("material", new MaterialRewards())
                .Register("equipment", new EquipmentRewards(snapshot))
                .Register("character", new CharacterRewards(new CharacterFactory(snapshot)));
        }

        private static RewardReceipt Receipt(GachaEntry entry, string kind)
        {
            return new RewardReceipt { ResourceId = entry.resourceId, ResourceKind = kind, Amount = entry.amount };
        }

        private static void ValidateInstanceAmount(GachaEntry entry)
        {
            if (entry.amount < 1 || entry.amount > 1000)
                throw new InvalidOperationException("Instance rewards must contain 1..1000 instances.");
        }

        private sealed class MaterialRewards : IRewardHandler
        {
            public void Validate(GachaEntry entry) { }
            public RewardReceipt Grant(PlayerState candidate, GachaEntry entry)
            {
                PlayerStateStore.Credit(candidate, entry.resourceId, entry.amount);
                return Receipt(entry, "material");
            }
        }

        private sealed class EquipmentRewards : IRewardHandler
        {
            private readonly ResourceSnapshot snapshot;
            public EquipmentRewards(ResourceSnapshot snapshot) { this.snapshot = snapshot; }
            public void Validate(GachaEntry entry)
            {
                ValidateInstanceAmount(entry);
                snapshot.Get<EquipmentData>(entry.resourceId);
            }
            public RewardReceipt Grant(PlayerState candidate, GachaEntry entry)
            {
                var definition = snapshot.Get<EquipmentData>(entry.resourceId);
                var receipt = Receipt(entry, "equipment");
                for (long i = 0; i < entry.amount; i++)
                {
                    var equipment = new EquipmentInstance
                    {
                        InstanceId = Guid.NewGuid().ToString("N"), DefinitionId = entry.resourceId, Slot = definition.slot
                    };
                    candidate.Equipment.Add(equipment.InstanceId, equipment);
                    receipt.InstanceIds.Add(equipment.InstanceId);
                }
                return receipt;
            }
        }

        private sealed class CharacterRewards : IRewardHandler
        {
            private readonly CharacterFactory factory;
            public CharacterRewards(CharacterFactory factory) { this.factory = factory; }
            public void Validate(GachaEntry entry) { ValidateInstanceAmount(entry); }
            public RewardReceipt Grant(PlayerState candidate, GachaEntry entry)
            {
                var receipt = Receipt(entry, "character");
                for (long i = 0; i < entry.amount; i++)
                {
                    var character = factory.Create(entry.resourceId);
                    candidate.Characters.Add(character.InstanceId, character);
                    foreach (var equipment in character.Equipment) candidate.Equipment.Add(equipment.InstanceId, equipment.Copy());
                    receipt.InstanceIds.Add(character.InstanceId);
                }
                return receipt;
            }
        }
    }
}
