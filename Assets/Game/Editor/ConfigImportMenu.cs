using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ResourceFramework
{
    public static class ConfigImportMenu
    {
        private const string ActivePath = "Assets/Resources/GameConfig/Active.asset";
        private const string VersionsPath = "Assets/GameConfig/Versions";
        private const string DemoScenePath = "Assets/Scenes/DemoScene.unity";
        private static string ProjectRoot { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }

        [MenuItem("Tools/Game Config/Import JSON")]
        public static void ImportJson()
        {
            string path = Path.Combine(ProjectRoot, "ConfigSource", "collection.json");
            var collection = ConfigCodec.ParseCollection(File.ReadAllText(path, Encoding.UTF8));
            Publish(collection.modules, collection);
        }

        [MenuItem("Tools/Game Config/Import CSV")]
        public static void ImportCsv()
        {
            var collection = ConfigCodec.ParseCollection(File.ReadAllText(
                Path.Combine(ProjectRoot, "ConfigSource", "collection.json"), Encoding.UTF8));
            string csvDirectory = Path.Combine(ProjectRoot, "ConfigSource", "CSV");
            var expected = new HashSet<string>(collection.modules.Select(item => item.module), StringComparer.Ordinal);
            foreach (string path in Directory.GetFiles(csvDirectory, "*.csv"))
                if (!expected.Contains(Path.GetFileNameWithoutExtension(path)))
                    throw new ConfigException("CSV file has no corresponding collection module: " + path);
            var modules = new List<ModuleEnvelope>();
            foreach (var source in collection.modules)
            {
                RequireSafeModuleName(source.module);
                string path = Path.Combine(csvDirectory, source.module + ".csv");
                modules.Add(CsvImporter.ParseModule(source.module, File.ReadAllText(path, Encoding.UTF8),
                    source.schemaVersion, collection.contentVersion));
            }
            Publish(modules.ToArray(), collection);
        }

        public static void Publish(ModuleEnvelope[] modules, ConfigCollection sourceCollection = null)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play mode before publishing configuration assets.");
            if (modules == null) throw new ArgumentNullException(nameof(modules));
            // Nothing on disk changes until every table and cross-module reference has passed.
            var validated = new ResourceSnapshot(modules);
            var sorted = modules.OrderBy(item => item.module, StringComparer.Ordinal).ToArray();
            foreach (var item in sorted) RequireSafeModuleName(item.module);
            var exportCollection = new ConfigCollection {
                format = sourceCollection == null ? "reference-module-collection" : sourceCollection.format,
                contentVersion = validated.Version,
                requiredCapabilities = sourceCollection == null ? ConfigCodec.SupportedCapabilities.ToArray() : sourceCollection.requiredCapabilities,
                modules = sorted,
                demo = sourceCollection == null || sourceCollection.demo == null ? new JObject() : (JObject)sourceCollection.demo.DeepClone()
            };
            string collectionJson = JsonConvert.SerializeObject(exportCollection, Formatting.Indented);
            ConfigCodec.ParseCollection(collectionJson);
            string fingerprint = Hash(collectionJson);
            string version = new string(validated.Version.Select(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' ? ch : '_').ToArray());
            if (version.Length > 64) version = version.Substring(0, 64);
            string folder = VersionsPath + "/v-" + version + "-" + fingerprint.Substring(0, 12);
            string versionManifest = folder + "/Manifest.asset";
            bool created = false, switchAttempted = false, preserveBackup = false;
            bool hadActive = File.Exists(Absolute(ActivePath));
            string temporaryDirectory = Path.Combine(ProjectRoot, "Temp", "GameConfigPublication");
            string operation = Guid.NewGuid().ToString("N");
            string temporary = Path.Combine(temporaryDirectory, operation + ".new");
            string backup = Path.Combine(temporaryDirectory, operation + ".backup");
            try
            {
                if (!AssetDatabase.IsValidFolder(folder))
                {
                    EnsureFolder(folder); created = true;
                    var tables = new ConfigTableAsset[sorted.Length];
                    for (int i = 0; i < sorted.Length; i++)
                    {
                        var asset = ScriptableObject.CreateInstance<ConfigTableAsset>();
                        asset.name = sorted[i].module;
                        asset.From(sorted[i]);
                        string tablePath = folder + "/" + sorted[i].module + ".asset";
                        AssetDatabase.CreateAsset(asset, tablePath);
                        if (AssetDatabase.GetAssetPath(asset) != tablePath)
                            throw new IOException("Could not create configuration asset: " + tablePath);
                        AssetDatabase.SaveAssetIfDirty(asset);
                        string moduleJsonPath = folder + "/" + sorted[i].module + ".json";
                        File.WriteAllText(Absolute(moduleJsonPath), ConfigCodec.WriteModule(sorted[i]), new UTF8Encoding(false));
                        AssetDatabase.ImportAsset(moduleJsonPath, ImportAssetOptions.ForceSynchronousImport);
                        tables[i] = asset;
                    }
                    File.WriteAllText(Absolute(folder + "/collection.json"), collectionJson, new UTF8Encoding(false));
                    AssetDatabase.ImportAsset(folder + "/collection.json", ImportAssetOptions.ForceSynchronousImport);
                    var manifest = ScriptableObject.CreateInstance<ManifestAsset>();
                    manifest.name = "Active"; manifest.tables = tables;
                    AssetDatabase.CreateAsset(manifest, versionManifest);
                    AssetDatabase.SaveAssetIfDirty(manifest);
                }
                // Reuse is allowed only when the existing immutable directory is exactly this release.
                var staged = AssetDatabase.LoadAssetAtPath<ManifestAsset>(versionManifest);
                if (staged == null) throw new ConfigException("Incomplete immutable version directory: " + folder);
                staged.Load();
                var stored = staged.tables.Select(table => table.Load()).OrderBy(item => item.module, StringComparer.Ordinal).ToArray();
                if (Fingerprint(stored) != Fingerprint(sorted))
                    throw new ConfigException("Existing version assets do not match their content fingerprint: " + folder);
                for (int i = 0; i < stored.Length; i++)
                {
                    string moduleJson = File.ReadAllText(Absolute(folder + "/" + stored[i].module + ".json"), Encoding.UTF8);
                    if (ConfigCodec.WriteModule(ConfigCodec.ParseModule(moduleJson)) != ConfigCodec.WriteModule(stored[i]))
                        throw new ConfigException("Generated JSON and SO disagree: " + stored[i].module);
                }
                string storedCollection = File.ReadAllText(Absolute(folder + "/collection.json"), Encoding.UTF8);
                ConfigCodec.ParseCollection(storedCollection);
                if (Hash(storedCollection) != fingerprint)
                    throw new ConfigException("Generated collection JSON does not match this release: " + folder);

                EnsureFolder("Assets/Resources/GameConfig");
                // Copy the fully saved manifest, then replace the single active file atomically.
                // Its .meta stays in place so existing GUID references remain valid.
                Directory.CreateDirectory(temporaryDirectory);
                File.Copy(Absolute(versionManifest), temporary, true);
                if (hadActive) File.Copy(Absolute(ActivePath), backup, false);
                switchAttempted = true;
                if (hadActive) File.Replace(temporary, Absolute(ActivePath), null);
                else File.Move(temporary, Absolute(ActivePath));
                AssetDatabase.ImportAsset(ActivePath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                var active = AssetDatabase.LoadAssetAtPath<ManifestAsset>(ActivePath);
                if (active == null || active.Load().Version != validated.Version)
                    throw new ConfigException("Published active manifest could not be reloaded.");
                var activeModules = active.tables.Select(table => table.Load()).OrderBy(item => item.module, StringComparer.Ordinal);
                if (Fingerprint(activeModules) != Fingerprint(sorted))
                    throw new ConfigException("Published active manifest does not reference the candidate release.");
                Debug.Log("[Config] Published " + validated.Version + " (" + sorted.Length + " modules, SHA256 " + fingerprint + "). Active: " + ActivePath);
            }
            catch (Exception publicationError)
            {
                try
                {
                    // Even failure during the final Unity reimport restores the previous pointer.
                    if (switchAttempted)
                    {
                        if (hadActive)
                        {
                            File.Copy(backup, temporary, true);
                            if (File.Exists(Absolute(ActivePath))) File.Replace(temporary, Absolute(ActivePath), null);
                            else File.Move(temporary, Absolute(ActivePath));
                            AssetDatabase.ImportAsset(ActivePath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                        }
                        else
                        {
                            AssetDatabase.DeleteAsset(ActivePath);
                            if (File.Exists(Absolute(ActivePath))) File.Delete(Absolute(ActivePath));
                            if (File.Exists(Absolute(ActivePath + ".meta"))) File.Delete(Absolute(ActivePath + ".meta"));
                        }
                    }
                    if (created) AssetDatabase.DeleteAsset(folder);
                }
                catch (Exception rollbackError)
                {
                    // Preserve both release assets and any backup when recovery itself fails.
                    preserveBackup = true;
                    throw new AggregateException("Configuration publication and rollback both failed. Recovery backup, if still present: " + backup,
                        publicationError, rollbackError);
                }
                throw;
            }
            finally
            {
                CleanupTemporary(temporary);
                if (!preserveBackup) CleanupTemporary(backup);
            }
        }

        [MenuItem("Tools/Game Config/Create Demo Scene")]
        public static void CreateDemoScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play mode before creating the Demo scene.");
            EnsureFolder("Assets/Scenes");
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                var root = new GameObject("Resource Framework Demo");
                root.AddComponent<DemoBootstrap>();
                var cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                var camera = cameraObject.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.06f, 0.08f, 0.12f);
                cameraObject.transform.position = new Vector3(0, 0, -10);
                if (!EditorSceneManager.SaveScene(scene, DemoScenePath))
                    throw new IOException("Could not save " + DemoScenePath);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
            var scenes = EditorBuildSettings.scenes.ToList();
            if (!scenes.Any(item => item.path == DemoScenePath))
                scenes.Add(new EditorBuildSettingsScene(DemoScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("[Config] Demo saved. Open " + DemoScenePath + " and press Play; results appear in Console and the DemoBootstrap inspector.");
        }

        // Unity -batchmode -quit -projectPath <path> -executeMethod ResourceFramework.ConfigImportMenu.BatchImportAndCreateDemo
        public static void BatchImportAndCreateDemo()
        {
            ImportJson();
            CreateDemoScene();
        }

        [MenuItem("Tools/Game Config/Validate And Run Demo")]
        public static void BatchValidateAndRunDemo()
        {
            ImportCsv();
            ResourceSnapshot snapshot = UnityConfigLoader.LoadActive();
            if (snapshot.Count == 0) throw new ConfigException("Imported active snapshot is empty.");
            var instance = new GameObject("Batch Resource Framework Validation");
            instance.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var demo = instance.AddComponent<DemoBootstrap>();
                demo.runOnStart = false;
                demo.RunDemo();
                Debug.Log("[Batch Validation] PASS: CSV -> JSON/SO -> active manifest -> Demo including runtime hot reload.");
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }

        [MenuItem("Tools/Game Config/Build Demo Windows")]
        public static void BatchBuildDemoWindows()
        {
            const BuildTarget target = BuildTarget.StandaloneWindows64;
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, target))
                throw new InvalidOperationException("Install Unity Windows Build Support before building the Demo.");
            if (EditorUserBuildSettings.activeBuildTarget != target)
                throw new InvalidOperationException("Select Windows x86_64 first, or invoke Unity with -buildTarget Win64.");
            BatchValidateAndRunDemo();
            CreateDemoScene();
            string output = Path.Combine(ProjectRoot, "Builds", "Windows", "ResourceFrameworkDemo.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { DemoScenePath }, locationPathName = output,
                target = target, options = BuildOptions.StrictMode
            });
            if (report == null || report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Windows Demo build failed: " + (report == null ? "missing BuildReport" : report.summary.result.ToString()));
            Debug.Log("[Batch Build] PASS: " + output + " (" + report.summary.totalSize + " bytes).");
        }

        private static string Fingerprint(IEnumerable<ModuleEnvelope> modules)
        {
            string text = string.Join("\n", modules.Select(ConfigCodec.WriteModule));
            return Hash(text);
        }
        private static string Hash(string text)
        {
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
        }
        private static string Absolute(string assetPath)
        { return Path.Combine(ProjectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar)); }
        private static void CleanupTemporary(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            { Debug.LogWarning("[Config] Temporary file cleanup deferred: " + path + ". " + ex.Message); }
        }
        private static void RequireSafeModuleName(string module)
        {
            if (string.IsNullOrEmpty(module) || module.Any(ch => !(ch >= 'a' && ch <= 'z') && !(ch >= '0' && ch <= '9') && ch != '_'))
                throw new ConfigException("Module name must contain only a-z, 0-9 and underscore: " + module);
        }
        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            if (current != "Assets") throw new ArgumentException("Expected an Assets-relative path.");
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
