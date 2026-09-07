using System;
using System.Collections.Generic;

namespace ResourceFramework
{
    public sealed class CharacterFactory
    {
        private readonly ResourceSnapshot snapshot;
        public CharacterFactory(ResourceSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException("snapshot");
            this.snapshot = snapshot;
        }

        public CharacterInstance Create(string characterId)
        {
            snapshot.EnsureDependencies(characterId);
            CharacterData definition = snapshot.Get<CharacterData>(characterId);
            var instance = new CharacterInstance
            {
                InstanceId = Guid.NewGuid().ToString("N"), DefinitionId = characterId,
                ConfigVersion = snapshot.Version, TalentBoardId = definition.talentBoardId
            };
            foreach (var stat in definition.baseStats) instance.BaseStats.Add(stat.statId, stat.value);
            double hp;
            if (instance.BaseStats.TryGetValue("hp", out hp)) instance.CurrentHp = hp;

            var abilities = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in definition.defaultAbilityIds)
            {
                snapshot.Get<AbilityData>(id);
                if (abilities.Add(id)) instance.AbilityIds.Add(id);
            }
            var slots = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in definition.defaultEquipmentIds)
            {
                var equipment = snapshot.Get<EquipmentData>(id);
                RequireAllowed(equipment.allowedCharacterIds, characterId, id);
                if (!slots.Add(equipment.slot)) throw new InvalidOperationException("Duplicate equipment slot: " + equipment.slot);
                instance.Equipment.Add(new EquipmentInstance
                {
                    InstanceId = Guid.NewGuid().ToString("N"), DefinitionId = id, Slot = equipment.slot
                });
                foreach (string abilityId in equipment.grantedAbilityIds)
                {
                    snapshot.Get<AbilityData>(abilityId);
                    if (abilities.Add(abilityId)) instance.AbilityIds.Add(abilityId);
                }
            }
            if (!String.IsNullOrEmpty(definition.talentBoardId))
            {
                var talentBoard = snapshot.Get<TalentBoardData>(definition.talentBoardId);
                if (talentBoard.ownerCharacterId != characterId)
                    throw new InvalidOperationException("Talent board belongs to a different character.");
                // Locked nodes do not grant abilities just because a board is attached.
            }
            foreach (string id in definition.defaultPerfumeIds)
            {
                var perfume = snapshot.Get<PerfumeModifierData>(id);
                RequireAllowed(perfume.allowedCharacterIds, characterId, id);
                instance.PerfumeIds.Add(id);
            }
            return instance;
        }

        private static void RequireAllowed(string[] allowed, string characterId, string definitionId)
        {
            if (allowed.Length == 0 || Array.IndexOf(allowed, characterId) >= 0) return;
            throw new InvalidOperationException(definitionId + " cannot be attached to " + characterId);
        }
    }
}
