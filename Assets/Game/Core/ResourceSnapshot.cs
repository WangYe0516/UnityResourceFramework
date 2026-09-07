using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace ResourceFramework
{
    /// <summary>Owns private JSON copies; queries return detached DTOs to prevent shared configuration mutation.</summary>
    public sealed class ResourceSnapshot
    {
        private readonly Dictionary<string, JObject> rows = new Dictionary<string, JObject>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> kinds = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> modules = new HashSet<string>(StringComparer.Ordinal);
        public string Version { get; }
        public int Count { get { return rows.Count; } }
        public string[] LoadedModules { get { return modules.OrderBy(x => x, StringComparer.Ordinal).ToArray(); } }
        public ResourceSnapshot(IEnumerable<ModuleEnvelope> source, bool requireAllReferences = true)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            string version = null;
            foreach (var input in source)
            {
                ModuleEnvelope module = ConfigCodec.ParseModule(ConfigCodec.WriteModule(input));
                if (!modules.Add(module.module)) throw new ConfigException("Duplicate module: " + module.module);
                if (version != null && version != module.contentVersion) throw new ConfigException("Mixed content versions");
                version = module.contentVersion;
                foreach (JObject row in module.items)
                {
                    string id = (string)row["id"];
                    if (rows.ContainsKey(id)) throw new ConfigException("Duplicate ID: " + id);
                    rows.Add(id, (JObject)row.DeepClone()); kinds.Add(id, module.module);
                }
            }
            if (version == null) throw new ConfigException("Empty configuration snapshot");
            Version = version;
            if (requireAllReferences) foreach (string id in rows.Keys) EnsureDependencies(id);
        }
        public bool Contains(string id) { return id != null && rows.ContainsKey(id); }
        public string KindOf(string id)
        {
            if (id == null || !kinds.TryGetValue(id, out string kind))
                throw new ConfigException("Resource/dependency not loaded: " + id);
            return kind;
        }
        public T Get<T>(string id) where T : ResourceData
        {
            string kind = KindOf(id); Type actual = ConfigCodec.TypeFor(kind);
            if (!typeof(T).IsAssignableFrom(actual)) throw new ConfigException("Wrong resource type for " + id);
            return (T)ConfigCodec.ReadRow(rows[id], actual);
        }
        public T[] All<T>() where T : ResourceData
        { return rows.Keys.Where(x => typeof(T).IsAssignableFrom(ConfigCodec.TypeFor(kinds[x]))).OrderBy(x => x, StringComparer.Ordinal).Select(Get<T>).ToArray(); }
        public void EnsureDependencies(string id) { Visit(id, new HashSet<string>(StringComparer.Ordinal)); }
        public void ValidateAllDependencies() { foreach (string id in rows.Keys) EnsureDependencies(id); }
        private void Visit(string id, HashSet<string> visited)
        {
            if (!visited.Add(id)) return;
            ResourceData row = Get<ResourceData>(id);
            foreach (var reference in ResourceValidator.References(row))
            {
                string targetKind = KindOf(reference.Id);
                if (!reference.Kinds.Contains(targetKind)) throw new ConfigException(id + " references wrong type: " + reference.Id);
                Visit(reference.Id, visited);
            }
            if (row is CharacterData character)
            {
                if (character.talentBoardId != null && Get<TalentBoardData>(character.talentBoardId).ownerCharacterId != character.id)
                    throw new ConfigException(character.id + ": talent board owner mismatch");
                var slots = new HashSet<string>(StringComparer.Ordinal);
                foreach (string equipmentId in character.defaultEquipmentIds)
                {
                    EquipmentData equipment = Get<EquipmentData>(equipmentId);
                    if ((equipment.allowedCharacterIds.Length > 0 && !equipment.allowedCharacterIds.Contains(character.id)) || !slots.Add(equipment.slot))
                        throw new ConfigException(character.id + ": incompatible equipment or duplicate slot");
                }
                foreach (string perfumeId in character.defaultPerfumeIds)
                {
                    PerfumeModifierData perfume = Get<PerfumeModifierData>(perfumeId);
                    if (perfume.allowedCharacterIds.Length > 0 && !perfume.allowedCharacterIds.Contains(character.id))
                        throw new ConfigException(character.id + ": incompatible perfume");
                }
            }
        }
    }
    public sealed class ConfigRuntime
    {
        private ResourceSnapshot current;
        private readonly object sync = new object();
        public ResourceSnapshot Current { get { return Volatile.Read(ref current); } }
        public ConfigRuntime(ResourceSnapshot initial) { current = initial ?? throw new ArgumentNullException(nameof(initial)); }
        public void Replace(ResourceSnapshot candidate)
        {
            if (candidate == null) throw new ArgumentNullException(nameof(candidate));
            candidate.ValidateAllDependencies();
            lock (sync)
            {
                foreach (ResourceData existing in Current.All<ResourceData>())
                    if (!candidate.Contains(existing.id)) throw new ConfigException("Hot reload cannot delete an existing resource without a migration: " + existing.id);
                Interlocked.Exchange(ref current, candidate);
            }
        }
        public bool TryReloadJson(string collectionJson, out string error)
        {
            try
            {
                ConfigCollection collection = ConfigCodec.ParseCollection(collectionJson);
                var candidate = new ResourceSnapshot(collection.modules);
                Replace(candidate); error = null; return true;
            }
            catch (Exception ex) when (ex is ConfigException || ex is Newtonsoft.Json.JsonException || ex is OverflowException)
            { error = ex.Message; return false; }
        }
    }
}
