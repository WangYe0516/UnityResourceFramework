using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ResourceFramework;

internal static class Program
{
    private static string root;
    private static string json;
    private static int passed;
    private sealed class TestRandom : IRandomSource
    {
        public long Roll; public int Calls;
        public long NextInt64(long upper) { Calls++; return Roll; }
    }
    private sealed class CapturingResolver : IRewardResolver
    {
        public PlayerState Captured;
        public Action BeforeGrant;
        private readonly IRewardResolver inner;
        public CapturingResolver(ResourceSnapshot snapshot) { inner = DefaultRewardHandlers.StandardRewards(snapshot); }
        public void Validate(ResourceSnapshot snapshot, GachaEntry entry) { inner.Validate(snapshot, entry); }
        public RewardReceipt Grant(PlayerState candidate, GachaEntry entry)
        { Captured = candidate; if (BeforeGrant != null) BeforeGrant(); return inner.Grant(candidate, entry); }
    }
    private static void Assert(bool value, string message = "assertion failed") { if (!value) throw new Exception(message); }
    private static void Throws(Action action)
    { bool failed = false; try { action(); } catch (Exception) { failed = true; } Assert(failed, "Expected failure"); }
    private static void Test(string name, Action action)
    { action(); passed++; Console.WriteLine("PASS " + name); }
    private static ResourceSnapshot Load(string text = null, bool full = true)
    { return new ResourceSnapshot(ConfigCodec.ParseCollection(text ?? json).modules, full); }
    private static JObject Changed(Action<JObject> change)
    { var data = JObject.Parse(json); change(data); return data; }
    private static JObject Row(JObject data, string kind, int i = 0)
    { return (JObject)data["modules"].First(x => (string)x["module"] == kind)["items"][i]; }
    private static PlayerState Seed()
    {
        return new PlayerState { ActionPoints = 10, Balances = new Dictionary<string, long> {
            {"material.herb", 10}, {"material.water", 5}, {"material.healing_potion", 0},
            {"currency.ticket", 2}, {"currency.gold", 0}
        }};
    }
    private static GachaService Gacha(ResourceSnapshot snapshot, PlayerStateStore store, IRandomSource random, IClock clock)
    { return new GachaService(snapshot,store,random,clock,DefaultRewardHandlers.StandardRewards(snapshot)); }
    private static FixedClock Clock() { return new FixedClock(new DateTimeOffset(2026, 9, 7, 0, 0, 0, TimeSpan.Zero)); }
    private static int Main(string[] args)
    {
        try
        {
            root = args.Length > 0 ? Path.GetFullPath(args[0]) : Directory.GetCurrentDirectory();
            json = File.ReadAllText(Path.Combine(root, "ConfigSource", "collection.json"));
            RunCoreTests(); RunCsvTests(); RunFeatureTests(); RunAssetChecks();
            if (args.Contains("--export-schemas")) ExportSchemas();
            Console.WriteLine("RESULT " + passed + " checks passed; real C# sources compiled with C# 7.3.");
            Console.WriteLine("LIMIT: Unity asset import, scene execution and player build require a Unity Editor run.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void RunCoreTests()
    {
        Test("nine modules and twelve resources", () => { var s = Load(); Assert(s.Count == 12 && s.LoadedModules.Length == 9); });
        Test("unknown fields rejected", () => Throws(() => Load(Changed(d => Row(d, "gacha")["weigth"] = 1).ToString())));
        Test("numeric strings rejected", () => Throws(() => Load(Changed(d => Row(d, "gacha")["entries"][0]["weight"] = "100").ToString())));
        Test("duplicate JSON keys rejected", () => Throws(() => ConfigCodec.ParseObject("{\"a\":1,\"a\":2}")));
        Test("duplicate IDs rejected", () => Throws(() => Load(Changed(d => ((JArray)d["modules"][0]["items"]).Add(d["modules"][0]["items"][0].DeepClone())).ToString())));
        Test("missing references rejected", () => Throws(() => Load(Changed(d => Row(d, "recipe")["inputs"][0]["resourceId"] = "material.missing").ToString())));
        Test("wrong reference types rejected", () => Throws(() => Load(Changed(d => Row(d, "recipe")["inputs"][0]["resourceId"] = "currency.ticket").ToString())));
        Test("independent material loading", () => { var m = ConfigCodec.ParseCollection(json).modules.Single(x => x.module == "material"); var s = new ResourceSnapshot(new[] { m }); Assert(s.Count == 3); });
        Test("independent pool indexing but missing execution dependencies", () => {
            var m = ConfigCodec.ParseCollection(json).modules.Single(x => x.module == "gacha");
            var s = new ResourceSnapshot(new[] { m }, false); Assert(s.Get<GachaPoolData>("gacha.standard") != null); Throws(() => s.EnsureDependencies("gacha.standard"));
        });
        Test("query deep-copy isolation", () => { var s = Load(); s.Get<CharacterData>("character.apollo").baseStats[0].value = 999; Assert(s.Get<CharacterData>("character.apollo").baseStats[0].value == 100); });
        Test("source mutation isolation", () => { var c = ConfigCodec.ParseCollection(json); var s = new ResourceSnapshot(c.modules); c.modules[0].items[0]["name"] = "changed"; Assert(s.Get<CharacterData>("character.apollo").name == "阿波罗"); });
        Test("legitimate reciprocal character-board references", () => Assert(Load().Get<TalentBoardData>("talent_board.apollo").ownerCharacterId == "character.apollo"));
        Test("talent prerequisite cycles rejected", () => Throws(() => Load(Changed(d => Row(d, "talent_board")["nodes"][0]["prerequisiteNodeIds"] = new JArray("node_01")).ToString())));
        Test("unsupported craft probability rejected", () => Throws(() => Load(Changed(d => Row(d, "recipe")["outputs"][0]["probabilityPermille"] = 500).ToString())));
        Test("negative zero-sum and overflow weights rejected", () => {
            Throws(() => Load(Changed(d => Row(d, "gacha")["entries"][0]["weight"] = -1).ToString()));
            Throws(() => Load(Changed(d => { foreach (var e in Row(d, "gacha")["entries"]) e["weight"] = 0; }).ToString()));
            Throws(() => Load(Changed(d => Row(d, "gacha")["entries"][0]["weight"] = int.MaxValue).ToString()));
        });
        Test("pity without positive target rejected", () => Throws(() => Load(Changed(d => Row(d, "gacha")["entries"][0]["weight"] = 0).ToString())));
        Test("bad hot reload preserves old snapshot", () => { var r = new ConfigRuntime(Load()); var old = r.Current; Assert(!r.TryReloadJson(Changed(d => Row(d, "recipe")["inputs"][0]["resourceId"] = "material.missing").ToString(), out _)); Assert(ReferenceEquals(old, r.Current)); });
        Test("unsupported capability rejected", () => Throws(() => Load(Changed(d => ((JArray)d["requiredCapabilities"]).Add("execute_arbitrary_code")).ToString())));
        Test("collection format missing numeric and unknown rejected", () => {
            Throws(()=>Load(Changed(d=>d.Remove("format")).ToString())); Throws(()=>Load(Changed(d=>d["format"]=42).ToString())); Throws(()=>Load(Changed(d=>d["format"]="other").ToString()));
        });
        Test("unsupported inventory quality and oversized instances rejected", () => {
            Throws(()=>Load(Changed(d=>Row(d,"recipe")["outputs"][0]["quality"]="epic").ToString()));
            Throws(()=>Load(Changed(d=>Row(d,"gacha")["entries"][0]["amount"]=1001).ToString()));
        });
        Test("valid reload switches new operations only", () => {
            var r = new ConfigRuntime(Load()); var old = r.Current;
            var next = Changed(d => { d["contentVersion"] = "demo-002"; foreach (var m in d["modules"]) m["contentVersion"] = "demo-002"; Row(d, "recipe")["outputs"][0]["amount"] = 2; });
            Assert(r.TryReloadJson(next.ToString(), out _)); Assert(r.Current.Version == "demo-002" && old.Version == "demo-001");
        });
        Test("concurrent hot reload cannot delete newly published IDs", () => {
            var runtime=new ConfigRuntime(Load()); var candidates=new string[2]; var accepted=new bool[2];
            for(int i=0;i<2;i++) { int index=i; candidates[i]=Changed(d=>{ var row=(JObject)Row(d,"material").DeepClone(); row["id"]="material.extra_"+index; ((JArray)d["modules"].First(x=>(string)x["module"]=="material")["items"]).Add(row); }).ToString(); }
            Parallel.For(0,2,i=>accepted[i]=runtime.TryReloadJson(candidates[i],out _)); Assert(accepted.Count(x=>x)==1 && runtime.Current.Count==13);
        });
    }
    private static void RunFeatureTests()
    {
        Test("character construction and instance isolation", () => {
            var factory = new CharacterFactory(Load()); var a = factory.Create("character.apollo"); var b = factory.Create("character.apollo");
            Assert(a.Equipment.Count == 1 && a.AbilityIds.Count == 1 && a.PerfumeIds.Count == 1 && a.TalentBoardId == "talent_board.apollo");
            a.BaseStats["hp"] = 0; Assert(b.BaseStats["hp"] == 100 && a.InstanceId != b.InstanceId);
        });
        Test("all 1000 weighted intervals", () => {
            var counts = new Dictionary<string,int>(); var snapshot = Load();
            for (int i=0; i<1000; i++) {
                var store = new PlayerStateStore(Seed()); var rng = new TestRandom { Roll=i };
                var result = Gacha(snapshot, store, rng, Clock()).Draw("gacha.standard",1,"draw");
                string id = result.Rewards[0].ResourceId; counts[id] = counts.ContainsKey(id) ? counts[id]+1 : 1;
            }
            Assert(counts["character.apollo"] == 100 && counts["equipment.sun_bow"] == 300 && counts["material.herb"] == 600);
        });
        Test("entry physical order does not affect stable ID order", () => {
            var reversed=Changed(d=>Row(d,"gacha")["entries"]=new JArray(Row(d,"gacha")["entries"].Reverse().Select(x=>x.DeepClone())));
            Assert(Gacha(Load(reversed.ToString()),new PlayerStateStore(Seed()),new TestRandom {Roll=150},Clock()).Draw("gacha.standard",1,"draw").Rewards[0].ResourceId=="equipment.sun_bow");
        });
        Test("missing reward handler rejects before fee and RNG", () => {
            var snapshot=Load(); var store=new PlayerStateStore(Seed()); var rng=new TestRandom {Roll=500};
            var service=new GachaService(snapshot,store,rng,Clock(),new RewardRegistry(snapshot));
            Throws(()=>service.Draw("gacha.standard",1,"draw")); Assert(rng.Calls==0 && store.Read().BalanceOf("currency.ticket")==2);
        });
        Test("retained candidate cannot mutate committed state", () => {
            var snapshot=Load(); var store=new PlayerStateStore(Seed()); var resolver=new CapturingResolver(snapshot);
            new GachaService(snapshot,store,new TestRandom {Roll=500},Clock(),resolver).Draw("gacha.standard",1,"draw");
            resolver.Captured.Balances["currency.ticket"]=999; Assert(store.Read().BalanceOf("currency.ticket")==1);
        });
        Test("nested transactions reject and release transaction guard", () => {
            var snapshot=Load(); var store=new PlayerStateStore(Seed()); var resolver=new CapturingResolver(snapshot);
            resolver.BeforeGrant=()=>Gacha(snapshot,store,new TestRandom {Roll=500},Clock()).Draw("gacha.standard",1,"nested");
            Throws(()=>new GachaService(snapshot,store,new TestRandom {Roll=500},Clock(),resolver).Draw("gacha.standard",1,"outer"));
            Assert(store.Read().BalanceOf("currency.ticket")==2); Gacha(snapshot,store,new TestRandom {Roll=500},Clock()).Draw("gacha.standard",1,"normal"); Assert(store.Read().BalanceOf("currency.ticket")==1);
        });
        Test("concurrent retries share a single economic result", () => {
            var store=new PlayerStateStore(Seed()); var rng=new TestRandom {Roll=500}; var service=Gacha(Load(),store,rng,Clock());
            Parallel.For(0,20,i=>service.Draw("gacha.standard",1,"same")); Assert(rng.Calls==1 && store.Read().BalanceOf("material.herb")==15);
        });
        Test("draw atomic cost reward pity and replay", () => {
            var store = new PlayerStateStore(Seed()); var rng = new TestRandom {Roll=500}; var g = Gacha(Load(),store,rng,Clock());
            var result = g.Draw("gacha.standard",1,"draw"); result.Rewards[0].Amount=999;
            Assert(g.Draw("gacha.standard",1,"draw").Rewards[0].Amount == 5 && rng.Calls==1);
            var state=store.Read(); Assert(state.BalanceOf("currency.ticket")==1 && state.BalanceOf("material.herb")==15 && state.PityCounts["gacha.standard"]==1);
            Throws(()=>g.Draw("gacha.standard",2,"draw")); Assert(store.Read().BalanceOf("currency.ticket")==1);
        });
        Test("insufficient draw funds leave state untouched", () => {
            var seed=Seed(); seed.Balances["currency.ticket"]=0; var store=new PlayerStateStore(seed);
            Throws(()=>Gacha(Load(),store,new TestRandom(),Clock()).Draw("gacha.standard",1,"draw")); Assert(store.Read().Characters.Count==0 && store.Read().PityCounts.Count==0);
        });
        Test("reward overflow rolls back fee and pity", () => {
            var seed=Seed(); seed.Balances["material.herb"]=ResourceValidator.MaxAmount; var store=new PlayerStateStore(seed);
            Throws(()=>Gacha(Load(),store,new TestRandom {Roll=500},Clock()).Draw("gacha.standard",1,"draw"));
            Assert(store.Read().BalanceOf("currency.ticket")==2 && store.Read().PityCounts.Count==0);
        });
        Test("pity threshold and natural reset", () => {
            var seed=Seed(); seed.PityCounts["gacha.standard"]=9; var store=new PlayerStateStore(seed);
            var r=Gacha(Load(),store,new TestRandom {Roll=0},Clock()).Draw("gacha.standard",1,"draw");
            Assert(r.Rewards[0].ResourceId=="character.apollo" && store.Read().PityCounts["gacha.standard"]==0);
        });
        Test("craft cost time boundary idempotency", () => {
            var store=new PlayerStateStore(Seed()); var clock=Clock(); var c=new CraftingService(Load(),store,clock);
            var job=c.Start("recipe.healing_potion",1,"start"); Assert(c.Start("recipe.healing_potion",1,"start").JobId==job.JobId);
            Assert(store.Read().BalanceOf("material.herb")==7 && store.Read().BalanceOf("material.water")==4 && store.Read().ActionPoints==8);
            clock.Advance(TimeSpan.FromSeconds(29)); Throws(()=>c.Claim(job.JobId,"claim")); Assert(store.Read().BalanceOf("material.healing_potion")==0);
            clock.Advance(TimeSpan.FromSeconds(1)); Assert(c.Claim(job.JobId,"claim").Completed); Assert(c.Claim(job.JobId,"claim").Completed);
            Throws(()=>c.Claim(job.JobId,"claim-again")); Assert(store.Read().BalanceOf("material.healing_potion")==1);
        });
        Test("insufficient material never partially pays", () => {
            var seed=Seed(); seed.Balances["material.water"]=0; var store=new PlayerStateStore(seed);
            Throws(()=>new CraftingService(Load(),store,Clock()).Start("recipe.healing_potion",1,"start"));
            Assert(store.Read().BalanceOf("material.herb")==10 && store.Read().ActionPoints==10 && store.Read().CraftJobs.Count==0);
        });
        Test("duplicate input costs aggregate", () => {
            var changed=Changed(d=>((JArray)Row(d,"recipe")["inputs"]).Add(new JObject { ["resourceId"]="material.herb",["amount"]=8 }));
            var store=new PlayerStateStore(Seed()); Throws(()=>new CraftingService(Load(changed.ToString()),store,Clock()).Start("recipe.healing_potion",1,"start"));
            Assert(store.Read().BalanceOf("material.herb")==10 && store.Read().CraftJobs.Count==0);
        });
        Test("started craft retains output across reload", () => {
            var store=new PlayerStateStore(Seed()); var clock=Clock(); var original=new CraftingService(Load(),store,clock);
            var job=original.Start("recipe.healing_potion",1,"start");
            var changed=Changed(d=>Row(d,"recipe")["outputs"][0]["amount"]=9);
            var updated=new CraftingService(Load(changed.ToString()),store,clock); clock.Advance(TimeSpan.FromSeconds(30)); updated.Claim(job.JobId,"claim");
            Assert(store.Read().BalanceOf("material.healing_potion")==1);
        });
        Test("state snapshots are detached", () => { var seed=Seed(); var store=new PlayerStateStore(seed); seed.Balances["currency.ticket"]=500; store.Read().Balances["currency.ticket"]=600; Assert(store.Read().BalanceOf("currency.ticket")==2); });
    }
    private static void RunCsvTests()
    {
        Test("all nine CSV tables match JSON source", () => {
            var expected=ConfigCodec.ParseCollection(json);
            var modules=expected.modules.Select(m=>CsvImporter.ParseModule(m.module,File.ReadAllText(Path.Combine(root,"ConfigSource","CSV",m.module+".csv")),1,"demo-001")).ToArray();
            var snapshot=new ResourceSnapshot(modules); Assert(snapshot.Count==12);
            for(int i=0;i<modules.Length;i++) Assert(JToken.DeepEquals(modules[i].items,expected.modules[i].items),modules[i].module+" CSV differs from JSON");
        });
        Test("CSV BOM escaped quotes commas multiline CRLF", () => {
            var rows=CsvImporter.ReadRows("\uFEFFid,name\r\none,\"a,\"\"b\"\"\r\nc\"\r\n");
            Assert(rows.Count==2 && rows[0][0]=="id" && rows[1][1]=="a,\"b\"\r\nc");
        });
        Test("CSV malformed quotes rejected", () => {
            Throws(()=>CsvImporter.ReadRows("a,\"unterminated")); Throws(()=>CsvImporter.ReadRows("a,\"b\"junk")); Throws(()=>CsvImporter.ReadRows("a,b\"c"));
        });
        Test("CSV duplicate unknown missing headers rejected", () => {
            Throws(()=>CsvImporter.ParseModule("material","id,id\na,b",1,"demo-001"));
            Throws(()=>CsvImporter.ParseModule("material","unknown\na",1,"demo-001"));
            Throws(()=>CsvImporter.ParseModule("material","id,name\nmaterial.x,X",1,"demo-001"));
        });
        Test("CSV leading-zero integers rejected", () => {
            var source=File.ReadAllText(Path.Combine(root,"ConfigSource","CSV","material.csv"));
            Assert(source.Contains(",1,")); Throws(()=>CsvImporter.ParseModule("material",source.Replace(",1,",",010,"),1,"demo-001"));
        });
        Test("CSV nested unknown fields rejected", () => {
            var source=File.ReadAllText(Path.Combine(root,"ConfigSource","CSV","material.csv"));
            Throws(()=>CsvImporter.ParseModule("material",source.Replace("sourceType","sourceTypo"),1,"demo-001"));
        });
    }
    private static void ExportSchemas()
    {
        string folder=Path.Combine(root,"ConfigSource","Schemas"); Directory.CreateDirectory(folder);
        foreach (string module in ConfigCodec.ModuleNames) File.WriteAllText(Path.Combine(folder,module+".schema.json"),ConfigCodec.ExportRowSchema(module).ToString()+Environment.NewLine);
    }
    private static void RunAssetChecks()
    {
        string assets=Path.Combine(root,"Assets");
        Test("Unity metadata exists and all serialized GUIDs resolve", () => {
            var files=Directory.GetFiles(assets,"*",SearchOption.AllDirectories).Where(x=>!x.EndsWith(".meta",StringComparison.Ordinal)).ToArray();
            foreach(string path in files.Concat(Directory.GetDirectories(assets,"*",SearchOption.AllDirectories))) Assert(File.Exists(path+".meta"),"Missing meta: "+path);
            var guids=new HashSet<string>();
            foreach(string meta in Directory.GetFiles(assets,"*.meta",SearchOption.AllDirectories)) {
                var match=Regex.Match(File.ReadAllText(meta),@"(?m)^guid: ([a-f0-9]{32})"); Assert(match.Success && guids.Add(match.Groups[1].Value),"Duplicate/invalid GUID: "+meta);
            }
            // Unity's serialized default skybox, lighting and cookie are engine-owned assets.
            var builtInGuids = new HashSet<string> { "0000000000000000f000000000000000", "0000000000000000e000000000000000" };
            foreach(string path in files.Where(x=>x.EndsWith(".asset",StringComparison.Ordinal)||x.EndsWith(".unity",StringComparison.Ordinal)))
                foreach(Match match in Regex.Matches(File.ReadAllText(path),@"guid: ([a-f0-9]{32})")) Assert(guids.Contains(match.Groups[1].Value) || builtInGuids.Contains(match.Groups[1].Value),"Unresolved GUID: "+path);
        });
        Test("nine committed SO payloads match source JSON", () => {
            var expected=ConfigCodec.ParseCollection(json); var modules=new List<ModuleEnvelope>();
            foreach(string path in Directory.GetFiles(Path.Combine(assets,"GameConfig","Versions","seed-demo-001"),"*.asset")) {
                var match=Regex.Match(File.ReadAllText(path),@"(?m)^  canonicalJson: (.+)$"); if(!match.Success) continue;
                var module=ConfigCodec.ParseModule(JsonConvert.DeserializeObject<string>(match.Groups[1].Value)); modules.Add(module);
                Assert(JToken.DeepEquals(module.items,expected.modules.Single(x=>x.module==module.module).items),"SO/source mismatch: "+module.module);
            }
            Assert(modules.Count==9 && new ResourceSnapshot(modules).Count==12);
        });
        Test("assembly definition references resolve", () => {
            var definitions=Directory.GetFiles(assets,"*.asmdef",SearchOption.AllDirectories).Select(x=>JObject.Parse(File.ReadAllText(x))).ToArray();
            var names=new HashSet<string>(definitions.Select(x=>(string)x["name"]));
            foreach(var definition in definitions) foreach(string name in definition["references"].Values<string>()) Assert(names.Contains(name),"Unknown assembly definition: "+name);
        });
    }
}
