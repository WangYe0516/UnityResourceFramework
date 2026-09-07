using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace ResourceFramework
{
    public sealed class ResourceReference
    {
        public readonly string Id;
        public readonly string[] Kinds;
        public ResourceReference(string id, params string[] kinds) { Id = id; Kinds = kinds; }
    }
    public static class ResourceValidator
    {
        public const long MaxAmount = 9007199254740991L;
        private static void Require(bool value, string error) { if (!value) throw new ConfigException(error); }
        private static void Amount(long value, string path) { Require(value > 0 && value <= MaxAmount, path + ": amount out of range"); }
        private static void NonEmpty(string value, string path) { Require(!string.IsNullOrWhiteSpace(value), path + ": empty value"); }
        private static void Unique(IEnumerable<string> values, string path)
        { var list = values.ToArray(); Require(list.All(x => !string.IsNullOrWhiteSpace(x)) && list.Distinct(StringComparer.Ordinal).Count() == list.Length, path + ": empty/duplicate value"); }
        private static void Extensions(ExtensionField[] fields, string path)
        {
            Unique(fields.Select(x => x.key), path);
            foreach (var field in fields)
            {
                Require(field.value != null, path + ": null extension value");
                switch (field.valueType)
                {
                    case "string": break;
                    case "int": Require(long.TryParse(field.value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _), path + ": invalid int extension"); break;
                    case "float":
                        Require(double.TryParse(field.value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && !double.IsNaN(number) && !double.IsInfinity(number), path + ": invalid float extension"); break;
                    case "bool": Require(field.value == "true" || field.value == "false", path + ": invalid bool extension"); break;
                    default: throw new ConfigException(path + ": unknown valueType " + field.valueType);
                }
            }
        }
        private static void Costs(ResourceAmount[] costs, string path)
        {
            var sums = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var c in costs)
            {
                NonEmpty(c.resourceId, path); Amount(c.amount, path);
                long previous = sums.TryGetValue(c.resourceId, out long v) ? v : 0;
                long total;
                try { total = checked(previous + c.amount); }
                catch (OverflowException) { throw new ConfigException(path + ": aggregated amount overflow"); }
                Amount(total, path); sums[c.resourceId] = total;
            }
        }
        public static void ValidateLocal(ResourceData row, string kind)
        {
            NonEmpty(row.id, kind); NonEmpty(row.name, row.id);
            Require(Regex.IsMatch(row.id, "^[a-z][a-z0-9_]*\\.[a-z0-9_]+$") && row.id.StartsWith(kind + ".", StringComparison.Ordinal), row.id + ": invalid resource ID namespace");
            Unique(row.tags, row.id + ".tags"); Extensions(row.extensions, row.id + ".extensions");
            // extensions are descriptive metadata; executable semantics are never dispatched from arbitrary keys.
            Require(row.extensions.All(x => x.key.StartsWith("ui.", StringComparison.Ordinal)), row.id + ": only ui.* metadata extensions are supported");
            if (row is CharacterData character)
            {
                Unique(character.baseStats.Select(x => x.statId), row.id + ".baseStats");
                Unique(character.defaultEquipmentIds, row.id); Unique(character.defaultAbilityIds, row.id); Unique(character.defaultPerfumeIds, row.id);
            }
            else if (row is EquipmentData equipment)
            {
                NonEmpty(equipment.slot, row.id); Require(equipment.rarity >= 0, row.id + ": negative rarity");
                Unique(equipment.allowedCharacterIds, row.id); Unique(equipment.grantedAbilityIds, row.id);
                foreach (var modifier in equipment.statModifiers) { NonEmpty(modifier.statId, row.id); Operation(modifier.operation, row.id); }
            }
            else if (row is AbilityData ability)
            {
                Require(ability.abilityType == "active" || ability.abilityType == "passive", row.id + ": invalid abilityType");
                Require(ability.cooldownSeconds >= 0, row.id + ": negative cooldown"); Costs(ability.costs, row.id);
                foreach (var effect in ability.effects)
                {
                    Require(effect.effectType == "damage_descriptor", row.id + ": unsupported effect descriptor");
                    Extensions(effect.parameters, row.id + ".effects");
                }
            }
            else if (row is TalentBoardData board)
            {
                NonEmpty(board.ownerCharacterId, row.id); Unique(board.nodes.Select(x => x.nodeId), row.id + ".nodes");
                var nodes = board.nodes.ToDictionary(x => x.nodeId, StringComparer.Ordinal);
                var done = new HashSet<string>(); var active = new HashSet<string>();
                foreach (var node in board.nodes)
                {
                    Unique(node.prerequisiteNodeIds, row.id); Costs(node.unlockCosts, row.id); Unique(node.grantedAbilityIds, row.id);
                    VisitNode(node.nodeId, nodes, done, active, row.id);
                }
            }
            else if (row is PerfumeModifierData perfume)
            {
                Unique(perfume.allowedCharacterIds, row.id); Require(perfume.stackRule == "unique", row.id + ": unsupported perfume stackRule");
                foreach (var m in perfume.modifiers)
                {
                    Require(m.targetType == "ability", row.id + ": only ability modifiers are described");
                    NonEmpty(m.targetId, row.id); NonEmpty(m.property, row.id); Operation(m.operation, row.id);
                }
            }
            else if (row is MaterialData material)
            {
                Require(material.rarity >= 0, row.id + ": negative rarity");
                foreach (var source in material.sources)
                {
                    Require(new[] {"drop", "plant", "purchase", "craft"}.Contains(source.sourceType), row.id + ": unknown sourceType");
                    NonEmpty(source.sourceId, row.id);
                }
            }
            else if (row is RecipeData recipe)
            {
                Require(recipe.inputs.Length > 0 && recipe.outputs.Length > 0, row.id + ": empty recipe");
                Costs(recipe.inputs, row.id); Require(recipe.durationSeconds >= 0 && recipe.actionPointCost >= 0, row.id + ": negative recipe cost/time");
                foreach (var output in recipe.outputs)
                {
                    Amount(output.amount, row.id); NonEmpty(output.quality, row.id);
                    Require(output.probabilityPermille == 1000, row.id + ": only fixed craft outputs are implemented");
                    Require(output.quality == "normal", row.id + ": quality-specific inventories are not implemented; use normal");
                }
            }
            else if (row is CurrencyData currency)
            {
                Costs(currency.purchaseCosts, row.id); Unique(currency.acquisitionMethods, row.id);
                Require(currency.purchasable == (currency.purchaseCosts.Length > 0), row.id + ": inconsistent purchase price");
                Require(currency.purchaseCosts.All(x => x.resourceId != row.id), row.id + ": self-priced currency");
            }
            else if (row is GachaPoolData pool)
            {
                Require(pool.drawCosts.Length > 0 && pool.entries.Length > 0, row.id + ": empty pool/cost");
                Costs(pool.drawCosts, row.id); Unique(pool.entries.Select(x => x.entryId), row.id);
                long total = 0;
                foreach (var entry in pool.entries)
                {
                    Amount(entry.amount, row.id); Require(entry.weight >= 0, row.id + ": negative weight"); total = checked(total + entry.weight);
                    if (entry.resourceId != null && (entry.resourceId.StartsWith("character.", StringComparison.Ordinal) || entry.resourceId.StartsWith("equipment.", StringComparison.Ordinal)))
                        Require(entry.amount <= 1000, row.id + ": instance rewards must contain at most 1000 instances");
                }
                Require(total > 0 && total <= int.MaxValue, row.id + ": weight sum must fit positive Int32");
                DateTimeOffset? start = ParseUtc(pool.startsAtUtc, row.id), end = ParseUtc(pool.endsAtUtc, row.id);
                Require(!start.HasValue || !end.HasValue || start < end, row.id + ": invalid availability range");
                if (pool.pity != null)
                {
                    Require(pool.pity.ruleType == "hard_pity" && pool.pity.guaranteeAt >= 1 && pool.pity.resetOnTarget && pool.pity.counterKey == pool.id,
                        row.id + ": unsupported pity rule (requires independent hard pity with reset)");
                    Require(pool.entries.Any(x => x.isPityTarget && x.weight > 0), row.id + ": no positive pity target");
                }
            }
        }
        private static void Operation(string op, string path) { Require(op == "add" || op == "multiply", path + ": unsupported modifier operation"); }
        private static void VisitNode(string id, Dictionary<string, TalentNode> nodes, HashSet<string> done, HashSet<string> active, string path)
        {
            Require(nodes.ContainsKey(id), path + ": missing prerequisite " + id);
            if (done.Contains(id)) return;
            Require(active.Add(id), path + ": prerequisite cycle");
            foreach (string prerequisite in nodes[id].prerequisiteNodeIds) VisitNode(prerequisite, nodes, done, active, path);
            active.Remove(id); done.Add(id);
        }
        public static DateTimeOffset? ParseUtc(string input, string path)
        {
            if (input == null) return null;
            Require(input.EndsWith("Z", StringComparison.Ordinal) || input.EndsWith("+00:00", StringComparison.Ordinal), path + ": timestamp must explicitly use UTC");
            Require(DateTimeOffset.TryParse(input, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset result), path + ": invalid timestamp");
            return result;
        }
        public static IEnumerable<ResourceReference> References(ResourceData row)
        {
            if (row is CharacterData c)
            {
                foreach (string id in c.defaultEquipmentIds) yield return new ResourceReference(id, "equipment");
                foreach (string id in c.defaultAbilityIds) yield return new ResourceReference(id, "ability");
                foreach (string id in c.defaultPerfumeIds) yield return new ResourceReference(id, "perfume");
                if (c.talentBoardId != null) yield return new ResourceReference(c.talentBoardId, "talent_board");
            }
            else if (row is EquipmentData e)
            {
                foreach (string id in e.allowedCharacterIds) yield return new ResourceReference(id, "character");
                foreach (string id in e.grantedAbilityIds) yield return new ResourceReference(id, "ability");
            }
            else if (row is AbilityData a) { foreach (var x in a.costs) yield return new ResourceReference(x.resourceId, "material", "currency"); }
            else if (row is TalentBoardData b)
            {
                yield return new ResourceReference(b.ownerCharacterId, "character");
                foreach (var n in b.nodes)
                {
                    foreach (var x in n.unlockCosts) yield return new ResourceReference(x.resourceId, "material", "currency");
                    foreach (string id in n.grantedAbilityIds) yield return new ResourceReference(id, "ability");
                }
            }
            else if (row is PerfumeModifierData p)
            {
                foreach (string id in p.allowedCharacterIds) yield return new ResourceReference(id, "character");
                foreach (var m in p.modifiers) yield return new ResourceReference(m.targetId, "ability");
            }
            // Acquisition sources describe provenance, not activation dependencies on other business systems.
            else if (row is RecipeData r)
            {
                foreach (var x in r.inputs) yield return new ResourceReference(x.resourceId, "material");
                foreach (var x in r.outputs) yield return new ResourceReference(x.resourceId, "material");
            }
            else if (row is CurrencyData u) { foreach (var x in u.purchaseCosts) yield return new ResourceReference(x.resourceId, "currency"); }
            else if (row is GachaPoolData g)
            {
                foreach (var x in g.drawCosts) yield return new ResourceReference(x.resourceId, "currency");
                foreach (var x in g.entries) yield return new ResourceReference(x.resourceId, "character", "equipment", "material");
            }
        }
    }
}
