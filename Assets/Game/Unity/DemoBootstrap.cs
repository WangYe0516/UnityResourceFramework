using System;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ResourceFramework
{
    public sealed class DemoBootstrap : MonoBehaviour
    {
        public bool runOnStart = true;
        public string characterId = "character.apollo";
        public string poolId = "gacha.standard";
        public string recipeId = "recipe.healing_potion";
        public string hotfixJsonPath = "";
        [TextArea(6, 24)] public string lastReport;
        private UnityConfigSession session;

        private sealed class DemoRandom : IRandomSource
        {
            public long NextInt64(long exclusiveMax)
            {
                const long value = 500;
                if (value >= exclusiveMax) throw new InvalidOperationException("Demo expects a pool weight sum greater than 500.");
                return value;
            }
        }

        private void Start()
        {
            if (!runOnStart) return;
            bool smokeTest = !Application.isEditor && Array.IndexOf(Environment.GetCommandLineArgs(), "--smoke-test") >= 0;
            try
            {
                RunDemo();
                if (smokeTest) Application.Quit(0);
            }
            catch
            {
                if (smokeTest) Application.Quit(1);
                else throw;
            }
        }

        [ContextMenu("Run Full Demo")]
        public void RunDemo()
        {
            var report = new StringBuilder();
            try
            {
                var manifest = Resources.Load<ManifestAsset>("GameConfig/Active");
                if (manifest == null) throw new InvalidOperationException("Import configuration before running DemoScene.");
                session = new UnityConfigSession(manifest.Load());
                ResourceSnapshot original = session.Current;
                report.AppendLine("1. SO -> Runtime: " + original.Version + ", " + original.LoadedModules.Length + " modules, " + original.Count + " resources.");

                var character = new CharacterFactory(original).Create(characterId);
                report.AppendLine("2. Character: " + character.DefinitionId + "; equipment=" + character.Equipment.Count
                    + "; abilities=" + character.AbilityIds.Count + "; talents=" + character.TalentBoardId + "; perfume=" + character.PerfumeIds.Count);
                Require(character.Equipment.Count > 0 && character.AbilityIds.Count > 0 && character.PerfumeIds.Count > 0,
                    "The example character must assemble equipment, abilities and perfume.");

                var player = new PlayerState { ActionPoints = 10 };
                player.Balances["material.herb"] = 10;
                player.Balances["material.water"] = 5;
                player.Balances["material.healing_potion"] = 0;
                player.Balances["currency.ticket"] = 2;
                player.Balances["currency.gold"] = 0;
                player.Characters.Add(character.InstanceId, character);
                var store = new PlayerStateStore(player);
                var clock = new FixedClock(new DateTimeOffset(2026, 9, 7, 0, 0, 0, TimeSpan.Zero));
                var gacha = new GachaService(original, store, new DemoRandom(), clock, DefaultRewardHandlers.StandardRewards(original));
                var draw = gacha.Draw(poolId, 1, "demo-draw-1");
                gacha.Draw(poolId, 1, "demo-draw-1");
                Require(store.Read().BalanceOf("currency.ticket") == 1, "Repeated draw operation must not charge again.");
                Require(store.Read().BalanceOf("material.herb") == 15, "Roll 500 must grant five herbs with the shipped data.");
                report.AppendLine("3. Gacha: " + draw.Rewards[0].ResourceId + " x" + draw.Rewards[0].Amount
                    + "; ticket=1; repeated operation charged once.");

                var crafting = new CraftingService(original, store, clock);
                CraftResult oldJob = crafting.Start(recipeId, 1, "demo-craft-start-1");
                clock.Advance(TimeSpan.FromSeconds(29));
                bool tooEarly = false;
                try { crafting.Claim(oldJob.JobId, "demo-craft-claim-1"); }
                catch (InvalidOperationException) { tooEarly = true; }
                Require(tooEarly && store.Read().BalanceOf("material.healing_potion") == 0, "Craft cannot complete at 29 seconds.");
                report.AppendLine("4. Craft at 29s: rejected; no output granted.");

                ConfigCollection hotfix = MakeHotfix(manifest, original.Version + "-runtime-demo-2");
                var recipeModule = hotfix.modules.Single(module => module.module == "recipe");
                var recipe = recipeModule.items.Cast<JObject>().Single(row => (string)row["id"] == recipeId);
                recipe["outputs"][0]["amount"] = 2;
                string error;
                Require(session.TryReloadJson(JsonConvert.SerializeObject(hotfix), out error), "Valid hot reload failed: " + error);
                Require(original.Get<RecipeData>(recipeId).outputs[0].amount == 1, "Hot reload must not mutate an existing snapshot.");

                clock.Advance(TimeSpan.FromSeconds(1));
                crafting.Claim(oldJob.JobId, "demo-craft-claim-1");
                Require(store.Read().BalanceOf("material.healing_potion") == 1, "An existing job retains its original output.");
                var updatedCrafting = new CraftingService(session.Current, store, clock);
                var newJob = updatedCrafting.Start(recipeId, 1, "demo-craft-start-2");
                clock.Advance(TimeSpan.FromSeconds(30));
                updatedCrafting.Claim(newJob.JobId, "demo-craft-claim-2");
                Require(store.Read().BalanceOf("material.healing_potion") == 3, "A new job uses the new two-item output.");
                report.AppendLine("5. JSON -> transient SO -> Runtime hot reload: old job grants 1; new job grants 2; total potion=3.");

                ResourceSnapshot beforeInvalid = session.Current;
                recipe["inputs"][0]["resourceId"] = "material.missing";
                Require(!session.TryReloadJson(JsonConvert.SerializeObject(hotfix), out error), "Dangling-reference hotfix must fail.");
                Require(ReferenceEquals(session.Current, beforeInvalid), "A rejected hotfix must preserve the current snapshot.");
                report.AppendLine("6. Invalid hotfix rejected; current version preserved. Reason: " + error);
                var final = store.Read();
                Require(final.BalanceOf("material.herb") == 9 && final.BalanceOf("material.water") == 3
                    && final.BalanceOf("material.healing_potion") == 3 && final.BalanceOf("currency.ticket") == 1
                    && final.ActionPoints == 6, "Unexpected final balances.");
                report.AppendLine("PASS: herb=9, water=3, potion=3, ticket=1, actionPoints=6.");
                lastReport = report.ToString();
                Debug.Log(lastReport, this);
            }
            catch (Exception ex)
            {
                lastReport = report + "FAIL: " + ex.Message;
                Debug.LogError(lastReport, this);
                throw;
            }
        }

        public void ReloadFromJsonPath(string path)
        {
            if (session == null) session = new UnityConfigSession(UnityConfigLoader.LoadActive());
            string error;
            if (!session.TryReloadFromJsonPath(path, out error)) throw new ConfigException(error);
            Debug.Log("[Config] Runtime version switched to " + session.Current.Version, this);
        }

        [ContextMenu("Reload Config From JSON Path")]
        private void ReloadConfiguredPath() { ReloadFromJsonPath(hotfixJsonPath); }

        private static ConfigCollection MakeHotfix(ManifestAsset manifest, string version)
        {
            var modules = manifest.tables.Select(table => table.Load()).ToArray();
            foreach (var module in modules) module.contentVersion = version;
            return new ConfigCollection {
                format = "reference-module-collection", contentVersion = version,
                requiredCapabilities = ConfigCodec.SupportedCapabilities.ToArray(), modules = modules, demo = new JObject()
            };
        }
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
