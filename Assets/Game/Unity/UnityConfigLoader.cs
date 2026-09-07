using System;
using UnityEngine;

namespace ResourceFramework
{
    public static class UnityConfigLoader
    {
        public static ResourceSnapshot LoadActive()
        {
            var manifest = Resources.Load<ManifestAsset>("GameConfig/Active");
            if (manifest == null)
                throw new InvalidOperationException("Missing Resources/GameConfig/Active.asset. Run Tools/Game Config/Import JSON.");
            return manifest.Load();
        }
    }
}
