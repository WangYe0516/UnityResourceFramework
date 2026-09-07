using System;
using UnityEngine;

namespace ResourceFramework
{
    // Unity serializes the canonical module payload; runtime DTOs remain engine independent.
    public sealed class ConfigTableAsset : ScriptableObject
    {
        public string module;
        public int schemaVersion;
        public string contentVersion;
        [TextArea(4, 20)] public string canonicalJson;

        public void From(ModuleEnvelope source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            canonicalJson = ConfigCodec.WriteModule(source);
            var parsed = ConfigCodec.ParseModule(canonicalJson);
            module = parsed.module;
            schemaVersion = parsed.schemaVersion;
            contentVersion = parsed.contentVersion;
        }

        public ModuleEnvelope Load()
        {
            var parsed = ConfigCodec.ParseModule(canonicalJson);
            if (parsed.module != module || parsed.schemaVersion != schemaVersion || parsed.contentVersion != contentVersion)
                throw new InvalidOperationException("SO metadata and payload disagree: " + name);
            return parsed;
        }
    }
}
