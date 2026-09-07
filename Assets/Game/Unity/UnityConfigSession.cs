using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Newtonsoft.Json;
using UnityEngine;

namespace ResourceFramework
{
    /// <summary>Unity main-thread adapter: JSON -> transient SOs -> validated snapshot -> atomic pointer swap.</summary>
    public sealed class UnityConfigSession
    {
        private readonly ConfigRuntime runtime;
        private readonly int mainThread;
        public ResourceSnapshot Current { get { return runtime.Current; } }

        public UnityConfigSession(ResourceSnapshot initial)
        {
            runtime = new ConfigRuntime(initial);
            mainThread = Thread.CurrentThread.ManagedThreadId;
        }

        public bool TryReloadJson(string json, out string error)
        {
            var transient = new List<ConfigTableAsset>();
            try
            {
                if (Thread.CurrentThread.ManagedThreadId != mainThread)
                    throw new InvalidOperationException("Unity configuration reload must run on its owning main thread.");
                ConfigCollection collection = ConfigCodec.ParseCollection(json);
                var modules = new ModuleEnvelope[collection.modules.Length];
                for (int i = 0; i < modules.Length; i++)
                {
                    var asset = ScriptableObject.CreateInstance<ConfigTableAsset>();
                    transient.Add(asset);
                    asset.hideFlags = HideFlags.DontSave;
                    asset.From(collection.modules[i]);
                    modules[i] = asset.Load();
                }
                var candidate = new ResourceSnapshot(modules);
                runtime.Replace(candidate);
                error = null;
                return true;
            }
            catch (Exception ex) when (ex is ConfigException || ex is JsonException || ex is OverflowException
                || ex is ArgumentException || ex is InvalidOperationException)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                // Snapshot owns detached data, so no transient SO survives the reload operation.
                foreach (var asset in transient)
                {
                    if (Application.isPlaying) UnityEngine.Object.Destroy(asset);
                    else UnityEngine.Object.DestroyImmediate(asset);
                }
            }
        }

        public bool TryReloadFromJsonPath(string path, out string error)
        {
            try { return TryReloadJson(File.ReadAllText(path), out error); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            { error = ex.Message; return false; }
        }
    }
}
