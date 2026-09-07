using System;
using Newtonsoft.Json.Linq;

namespace ResourceFramework
{
    [Serializable] public class ResourceData
    {
        public string id;
        public string name;
        public string description = "";
        public string iconKey = "";
        public string[] tags = Array.Empty<string>();
        public ExtensionField[] extensions = Array.Empty<ExtensionField>();
    }
    [Serializable] public sealed class ExtensionField { public string key; public string valueType; public string value; }
    [Serializable] public sealed class ResourceAmount { public string resourceId; public long amount; }
    [Serializable] public sealed class StatValue { public string statId; public float value; }
    [Serializable] public sealed class StatModifier { public string statId; public string operation; public float value; }
    [Serializable] public sealed class EffectDescriptor { public string effectType; public ExtensionField[] parameters; }
    [Serializable] public sealed class SourceDescriptor { public string sourceType; public string sourceId; }
    [Serializable] public sealed class ModifierDescriptor
    { public string targetType; public string targetId; public string property; public string operation; public float value; }
    [Serializable] public sealed class TalentNode
    { public string nodeId; public string[] prerequisiteNodeIds; public ResourceAmount[] unlockCosts; public string[] grantedAbilityIds; }
    [Serializable] public sealed class CraftOutput
    { public string resourceId; public long amount; public int probabilityPermille; public string quality; }
    [Serializable] public sealed class GachaEntry
    { public string entryId; public string resourceId; public long amount; public int weight; public bool isPityTarget; }
    [Serializable] public sealed class PityRule
    { public string ruleType; public int guaranteeAt; public string counterKey; public bool resetOnTarget; }

    [Serializable] public sealed class CharacterData : ResourceData
    {
        public StatValue[] baseStats;
        public string[] defaultEquipmentIds;
        public string[] defaultAbilityIds;
        public string talentBoardId;
        public string[] defaultPerfumeIds;
    }
    [Serializable] public sealed class EquipmentData : ResourceData
    {
        public string slot; public int rarity; public string[] allowedCharacterIds;
        public string[] grantedAbilityIds; public StatModifier[] statModifiers;
    }
    [Serializable] public sealed class AbilityData : ResourceData
    { public string abilityType; public float cooldownSeconds; public ResourceAmount[] costs; public EffectDescriptor[] effects; }
    [Serializable] public sealed class TalentBoardData : ResourceData
    { public string ownerCharacterId; public TalentNode[] nodes; }
    [Serializable] public sealed class PerfumeModifierData : ResourceData
    { public string[] allowedCharacterIds; public ModifierDescriptor[] modifiers; public string stackRule; }
    [Serializable] public sealed class MaterialData : ResourceData
    { public int rarity; public SourceDescriptor[] sources; }
    [Serializable] public sealed class RecipeData : ResourceData
    { public ResourceAmount[] inputs; public CraftOutput[] outputs; public int durationSeconds; public int actionPointCost; }
    [Serializable] public sealed class CurrencyData : ResourceData
    { public string[] acquisitionMethods; public bool purchasable; public ResourceAmount[] purchaseCosts; }
    [Serializable] public sealed class GachaPoolData : ResourceData
    {
        public ResourceAmount[] drawCosts; public GachaEntry[] entries;
        public string startsAtUtc; public string endsAtUtc; public PityRule pity;
    }
    public sealed class ModuleEnvelope
    { public string module; public int schemaVersion; public string contentVersion; public JArray items; }
    public sealed class ConfigCollection
    {
        public string format; public string contentVersion; public string[] requiredCapabilities;
        public ModuleEnvelope[] modules; public JObject demo;
    }
    public sealed class ConfigException : Exception
    { public ConfigException(string message) : base(message) { } }
}
