# Unity 多系统数据驱动框架

工程包含九类配置、完整 CSV/JSON 导入工具、ScriptableObject 资产、角色构建、抽卡、定时合成、数据热更新及 Console 示例场景。所有源文件和真实提交历史保存在 [WangYe0516/UnityResourceFramework](https://github.com/WangYe0516/UnityResourceFramework)。

**2026-09-10 验证通过：47 项 C# 检查、Unity 全部程序集编译、真实 CSV→JSON/SO 导入、DemoScene Play Mode、Windows x64 Player 构建及启动运行。Editor 和 Player 均输出预期最终余额。**逐项覆盖见 [原题交付对照](Docs/REQUIREMENTS.md)，实际结果见 [验证记录](Docs/VALIDATION.md)。

工程已使用本机实际安装的 **Unity 6000.6.0f1** 导入并验证。Unity 自动升级后的 Newtonsoft 包为 **3.2.2**，依赖锁文件已提交；该包提供自动引用的预编译 DLL。首次打开需允许下载依赖。命令行测试使用 .NET 8 SDK 和 NuGet Newtonsoft.Json 13.0.3。旧提交的 2022.3 兼容目标未实测，本交付以实际验证版本为准。

## 1. 如何运行

### Unity 场景

1. 用 Unity Hub 将**本 README 所在目录**添加为项目，使用上述编辑器打开。
2. 等待包解析和脚本编译，打开 `Assets/Scenes/DemoScene.unity`。
3. 点击 Play，查看 Console，或选中场景中的 `Resource Framework Demo` 查看 `lastReport`。
4. 工程已提交初始 Active manifest、九类 SO 和对应 JSON，无需先手动拖拽资源引用。
5. 如需由本机 Unity 重新生成初始资源，先执行 `Tools > Game Config > Import CSV`，再执行 `Create Demo Scene`，重新打开场景。

Demo 不需要 UI、操作输入、动画或技能效果。它按固定随机值和可推进时钟执行：

```text
SO → Runtime，加载 9 类模块、12 条资源。
构建阿波罗：1 件装备、1 项去重后的能力、天赋板与香氛。
抽卡 roll=500：扣 1 张券，获得 5 草药；重复同请求不再扣费。
创建 30 秒合成任务；第 29 秒领取失败。
通过 JSON → 临时 SO → Runtime 热更，将新配方产量从 1 改成 2。
旧任务领取 1 药剂，新任务领取 2 药剂。
含坏引用的热更被拒绝，当前快照保留。
预期最终：herb=9, water=3, potion=3, ticket=1, actionPoints=6。
```

上述结果已在真实 Editor Play Mode 和 Windows Player 执行，日志摘录见验证记录。示例玩家状态由 Demo 初始化；框架的配方、消耗、掉落和挂载来自配置。改变示例配方/卡池后，Demo 中针对原样例的断言也要相应调整；业务服务不依赖这些样例 ID。

### 不依赖 Unity 的真实 C# 检查

安装 .NET 8 SDK 后，在项目根目录执行：

```powershell
dotnet run --project .\Tools\Checks\Framework.Checks.csproj -- .
```

在 .NET 8 SDK 已加入 PATH 的环境中，也可以运行脚本：

```powershell
.\Tools\verify.ps1
```

如果 SDK 未加入 PATH，可用 `-Dotnet` 参数指定实际的 dotnet 可执行文件路径。检查失败时进程非零退出，成功输出 `RESULT 47 checks passed`。标准检查不会修改配置或资产。

### 一条命令做真实 Unity 验收

关闭已打开的同一项目后，在项目根目录执行以下脚本。它按顺序执行导入、真实 Play Mode、Windows 构建和 Player 自动运行，同时检查退出码与成功标记。需有效 Unity 许可和 Windows Build Support，日志保存在 `Logs/Verification`：

```powershell
.\Tools\verify-unity.ps1 -UnityEditor 'C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Unity.exe'
```

可加 `-Mode Import`、`-Mode PlayMode` 或 `-Mode Player` 单独验收。以下是直接入口：

在已安装且授权的 Unity 编辑器环境中执行；`$unityEditor` 改为实际路径：

```powershell
$unityEditor = 'C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Unity.exe'
$projectRoot = (Get-Location).Path
& $unityEditor -batchmode -nographics -quit -projectPath $projectRoot `
  -executeMethod ResourceFramework.ConfigImportMenu.BatchValidateAndRunDemo `
  -logFile (Join-Path $projectRoot 'unity-validation.log')
if ($LASTEXITCODE -ne 0) { throw 'Unity validation failed; inspect unity-validation.log' }
```

该入口实际执行 CSV 导入、生成 JSON/SO、加载活动入口、调用 Demo，包括运行时临时 SO 热更。它在 Editor 批处理环境运行，不代替 Player 验证。要构建 Windows Player，需安装 Windows Build Support，然后运行：

```powershell
& $unityEditor -batchmode -quit -buildTarget Win64 -projectPath $projectRoot `
  -executeMethod ResourceFramework.ConfigImportMenu.BatchBuildDemoWindows `
  -logFile (Join-Path $projectRoot 'unity-build.log')
if ($LASTEXITCODE -ne 0) { throw 'Unity build failed; inspect unity-build.log' }
```

输出路径是 `Builds/Windows/ResourceFrameworkDemo.exe`，上述入口均已执行。Player 不含 UI，正常启动显示空背景，结果写入 Unity Player 日志；自动验收增加 `-batchmode -nographics --smoke-test -logFile <日志路径>`，完成后以 0/1 退出。

## 2. 框架架构

```text
ConfigSource/CSV + collection.json
       ↓ CsvImporter / ConfigCodec：类型、范围、字段与引用校验
Editor ConfigImportMenu
       ↓ 版本目录：每模块 JSON + ConfigTableAsset + ManifestAsset
Resources/GameConfig/Active.asset
       ↓ UnityConfigLoader / ManifestAsset.Load
ResourceSnapshot：私有索引和查询副本
       ├─ CharacterFactory
       ├─ CraftingService → PlayerStateStore
       └─ GachaService → IRewardResolver → PlayerStateStore
                            ↑
             Composition Root 注册默认奖励处理器
```

程序集按 Core、Features、Unity、Editor 分层。Core 与 Features 不依赖 UnityEngine；Editor 程序集仅在 Editor 中编译。GachaService 不引用 CharacterFactory 或具体角色/装备状态，发奖通过注册接口完成；默认奖励桥接在组合根组装。

SO 保存 `module/schemaVersion/contentVersion/canonicalJson`，是实际的 Unity 配置资产，不存不可序列化的 JObject/Dictionary。Runtime 再将其中的 JSON 解析成已注册的强类型 DTO。采用一个通用 ConfigTableAsset 承载九类表，避免九套重复导入代码；每一类仍有独立 DTO、Schema、JSON、SO 资产和索引。

ResourceSnapshot 私有保存配置副本，`Get<T>()` 每次返回脱离内部状态的 DTO；调用者无法通过修改返回值污染配置。代价是查询时产生解析和分配，适合本题的小型框架。大型项目可将内部存储换成不可变 Definition，查询接口和业务边界不变。玩家余额、保底、任务、角色实例始终独立于 SO。

## 3. 九类数据结构

公共字段：`id`、`name`、`description`、`iconKey`、`tags`、`extensions`。完整结构见 [DataModels.cs](Assets/Game/Core/DataModels.cs) 与 [机器可读 Schema](ConfigSource/Schemas)。

| 类型 / 模块 | 核心字段 |
|---|---|
| CharacterData / character | baseStats、defaultEquipmentIds、defaultAbilityIds、talentBoardId、defaultPerfumeIds |
| EquipmentData / equipment | slot、rarity、allowedCharacterIds、grantedAbilityIds、statModifiers |
| AbilityData / ability | abilityType、cooldownSeconds、costs、effects |
| TalentBoardData / talent_board | ownerCharacterId、nodes：前置节点、解锁消耗与能力 ID |
| PerfumeModifierData / perfume | allowedCharacterIds、modifiers、stackRule |
| MaterialData / material | rarity、sources；名称/图标/描述来自公共字段 |
| RecipeData / recipe | inputs、outputs、durationSeconds、actionPointCost |
| CurrencyData / currency | acquisitionMethods、purchasable、purchaseCosts |
| GachaPoolData / gacha | drawCosts、entries、startsAtUtc、endsAtUtc、pity |

ID 使用 `module.name` 格式，例如 `material.herb`，区分大小写，全局唯一。ID 与展示名称、数组位置、Unity InstanceID 无关。角色/装备的玩家实例使用独立 `InstanceId`。配置之间只有 ID 引用；manifest 对表 SO 的引用属于基础设施，不是业务资源之间的硬引用。

天赋节点前置图禁止循环；角色指向天赋板、板子记录所属角色的双向关系合法。装备限制、槽位冲突、香氛适用角色在依赖校验时检查。来源 `sourceId` 描述掉落/种植/商店等来源，不强制启动这些业务系统。

## 4. 数据生产与导入

原题允许 CSV。本项目支持 **Excel 另存 UTF-8 CSV → JSON → SO → Runtime**，不直接解析 `.xlsx`。

- `ConfigSource/CSV`：九张示例表，一行一个资源，列名与 DTO 字段一致。
- 标量使用明确类型；数组/嵌套对象用 JSON 单元格，CSV 负责引号转义。空数组写 `[]`，空可选对象写 `null`。
- Parser 支持 BOM、逗号、转义引号和多行字段；拒绝重复/未知/缺失列、类型错配与非法数值。
- `ConfigSource/collection.json`：独立 JSON 导入入口，同时提供 CSV 导入的模块列表、结构版本、内容版本和能力声明。CSV 导入以 CSV 中的记录为准，不使用集合文件中的旧记录覆盖它。
- 先完整校验所有候选表和引用，再写入新版本目录，最后替换 `Active.asset`。旧版本不被覆盖；重导失败尝试恢复旧指针，恢复失败则保留备份并明确报错。
- 每个版本目录包含各模块 `.json`、`.asset`、`collection.json` 和 `Manifest.asset`；目录哈希绑定该次发布内容。

修改 CSV 后执行 `Import CSV`；修改集合 JSON 后执行 `Import JSON`。`Create Demo Scene` 可以重新生成场景并登记到 Build Settings。

## 5. 查询、独立加载和角色构建

```csharp
var character = snapshot.Get<CharacterData>("character.apollo");
var equipment = snapshot.Get<EquipmentData>("equipment.sun_bow");
var material = snapshot.Get<MaterialData>("material.herb");
var instance = new CharacterFactory(snapshot).Create("character.apollo");
```

可以只构建某个模块的快照。需要保留外部引用、稍后再加载依赖时，使用 `new ResourceSnapshot(modules, requireAllReferences: false)`；查询本表可用，业务执行前调用 `EnsureDependencies(id)`。缺失依赖时操作失败，不过滤掉落条目、不修改概率。

角色工厂解析装备、能力、天赋板和香氛 ID，创建独立实例。角色默认能力与装备授予能力按 ID 去重，天赋板只挂载、不默认解锁所有节点。能力/香氛仅加载描述，本题不执行战斗效果。

## 6. 抽卡与合成

```csharp
var state = new PlayerState { ActionPoints = 10 };
state.Balances["currency.ticket"] = 2;
state.Balances["material.herb"] = 10;
state.Balances["material.water"] = 5;
var store = new PlayerStateStore(state);
var clock = new FixedClock(DateTimeOffset.UtcNow);
var gacha = new GachaService(snapshot, store, new SeededRandomSource(123), clock,
    DefaultRewardHandlers.StandardRewards(snapshot));
var reward = gacha.Draw("gacha.standard", 1, "request-draw-001");
var craft = new CraftingService(snapshot, store, clock);
var job = craft.Start("recipe.healing_potion", 1, "request-start-001");
clock.Advance(TimeSpan.FromSeconds(30));
var claim = craft.Claim(job.JobId, "request-claim-001");
```

代码置于已加载 `snapshot` 的调用处，并引用 `System`、`ResourceFramework`。

**抽卡：**按稳定 entryId 排序，整数权重构造 `[0,total)` 区间；负权重、总和为零或超出 Int32 上限被拒绝，零权重不命中。扣费、奖励与保底在同一玩家状态事务中提交。保底可为 null；当前支持每池独立 hard_pity、命中目标清零。`guaranteeAt=10` 表示前九次未出时第十次必出。时间边界为 UTC 的 `[startsAtUtc,endsAtUtc)`。

**合成：**开始时一次扣除聚合后的材料和行动力，保存确定产物及完成时间；未到期不发奖，到期只领取一次。多产物已支持；当前只支持必定产出和 normal 品质，概率/不同品质配置会被明确拒绝。批量 1..1000，按并行作业解释：费用与产物乘批量，耗时不乘；零耗时任务立即结算并标记已领取。

**事务：**一个 PlayerStateStore 串行提交整个状态副本；提交前异常不改余额、任务、保底和幂等记录。成功请求用 operationId 绑定请求参数，重试返回原回执；相同 ID 搭配不同参数被拒绝。同 Store 的嵌套写事务明确拒绝，候选状态提交时再次隔离，避免扩展处理器保留引用后修改已提交数据。

**发奖：**材料增加数量，装备/角色生成独立实例。允许重复角色；示例角色的默认装备随角色实例初始化。角色重复转碎片、背包容量及装备更换不是本题实现范围。每条角色/装备奖励最多 1000 实例。通用数量上限为 2^53−1，倍数与加减有溢出检查。

## 7. 数据热更新

`UnityConfigSession.TryReloadJson` 走 JSON → 临时 ScriptableObject → 候选快照 → 原子切换；SO 创建/销毁在所属 Unity 主线程。可调用 `TryReloadFromJsonPath`，或在 DemoBootstrap Inspector 填 `hotfixJsonPath`，执行组件菜单 `Reload Config From JSON Path`。

每次操作捕获一份快照，奖励注册器也绑定同一份快照；新版本生效后，为新操作创建使用新快照的服务。旧任务按保存的产物领取，旧角色维持原配置版本。失败保留旧快照；没有迁移实现时，禁止热更删除现有 ID。Core 的替换检查和提交使用同一锁，防止并发覆盖刚发布的资源。

本版本演示本地文件及内存内容热更新，不包含 CDN、下载重试、签名、离线缓存恢复或 Addressables 发布。新增记录、修改已实现字段可数据热更；新增执行语义需要代码实现。结构版本当前只支持 1，未知能力被拒绝。

## 8. AI 配表与扩展

将 `ConfigSource/Schemas`、已有 ID 目录、[数据字段](Assets/Game/Core/DataModels.cs)、[业务校验规则](Assets/Game/Core/ResourceValidator.cs) 及集合示例一起交给 AI。生成的配置必须经过相同导入器；Schema 是结构约束，存在性、保底、范围、金额及关系约束还要通过业务校验。

```text
根据提供的 Schema 和现有 ID 目录生成配置，使用指定的 schemaVersion/contentVersion。
只使用已实现的能力类型；不添加未声明的核心字段，不输出任何执行脚本。
所有 resourceId 必须已存在，或在本次模块集合中同时定义。
资源数量写整数，时间明确单位，UTC 时间带 Z；复杂 CSV 单元格写合法 JSON。
不要输出玩家余额、已领取状态或当前保底计数。
核心字段错误必须修复；extensions 目前仅允许 ui.* 描述元数据。
无法表达的需求返回缺口，不自行编造新玩法字段。
```

扩展步骤：

| 需求 | 操作 |
|---|---|
| 新角色/装备/材料/配方/卡池 | 新增配置记录并重新导入 |
| 新资源类型 | 新增 ResourceData 派生类型，ConfigCodec.RegisterModule，加入类型对应本地规则与引用规则，增加表/集合模块/Schema；可发奖时注册 IRewardHandler |
| 新字段 | 修改 DTO、Schema 与验证；业务有含义则补消费代码；制定默认值/迁移与客户端能力版本，不能假定旧客户端能解释 |
| Buff 等新系统 | 新增服务，注入查询快照、时钟和状态边界，避免让抽卡/合成直接调用它 |
| 概率合成/品质背包/新保底 | 实现策略与相应状态模型，扩展能力声明和验证，然后才接受相应配置 |

重新生成结构 Schema：

```powershell
dotnet run --project .\Tools\Checks\Framework.Checks.csproj -- . --export-schemas
```

这个显式导出命令会更新 Schema 文件；普通检查不更新它们。

## 9. 已知限制与验收范围

- 玩家状态、任务和幂等回执仅在内存中，关闭后不保留；不宣称支持崩溃恢复或跨服务器事务。
- 随机序列不属于玩家事务；失败交易不扣钱，但已使用的随机数可能推进。成功重试不会重抽。固定种子是本地演示能力，不是可信付费抽卡服务。
- 已执行 Unity 6000.6.0f1 Editor/Play Mode/Windows x64 Mono Player 验证；当前 Active 指向 Unity 生成的版本资产，DemoScene 已由 Editor 保存。未验证 IL2CPP、其他平台或旧编辑器。
- 没有参考 Excel 原件，因此示例为自拟数据。
- SO 的 canonical JSON 可扩展且容易检查，但不是面向超大规模数据的零分配数据库。

## 10. Git 提交与远程交付

查看真实历史和工作区：

```powershell
git log --oneline --reverse
git status --short
```

历史按实际工作形成：工程初始化、配置契约与样例、事务业务及测试、Unity 接入及资产、README 与验收，以及公开交付说明。没有改写原有提交历史。

公开仓库地址：https://github.com/WangYe0516/UnityResourceFramework 。默认分支为 `main`。克隆项目：

```powershell
git clone https://github.com/WangYe0516/UnityResourceFramework.git
cd UnityResourceFramework
```

## 官方参考

- [Unity ScriptableObject](https://docs.unity3d.com/2022.3/Documentation/Manual/class-ScriptableObject.html)：配置资产和玩家存档的边界。
- [Unity JSON Serialization](https://docs.unity3d.com/2022.3/Documentation/Manual/JSONSerialization.html)：本版本内置序列化限制，因此 SO 不直接存字典。
- [Unity Newtonsoft JSON 包 3.2](https://docs.unity3d.com/Packages/com.unity.nuget.newtonsoft-json@3.2/manual/index.html)：JSON 库依赖。
- [Unity Editor 命令行](https://docs.unity3d.com/2022.3/Documentation/Manual/EditorCommandLineArguments.html)：批处理与 executeMethod。
- [BuildPipeline.BuildPlayer](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/BuildPipeline.BuildPlayer.html)：Windows 构建入口。
