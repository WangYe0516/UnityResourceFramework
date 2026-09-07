using System;
using UnityEngine;

namespace ResourceFramework
{
    public sealed class ManifestAsset : ScriptableObject
    {
        // The active pointer stores references only. Old version assets are never edited.
        public ConfigTableAsset[] tables = new ConfigTableAsset[0];

        public ResourceSnapshot Load()
        {
            if (tables == null || tables.Length == 0)
                throw new InvalidOperationException("The active configuration manifest contains no tables.");
            var modules = new ModuleEnvelope[tables.Length];
            for (int i = 0; i < tables.Length; i++)
            {
                if (tables[i] == null) throw new InvalidOperationException("Missing table reference at index " + i);
                modules[i] = tables[i].Load();
            }
            return new ResourceSnapshot(modules);
        }
    }
}
