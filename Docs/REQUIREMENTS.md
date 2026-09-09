# 原题逐项交付对照

依据：用户提供的《Unity 程序员框架测试题：设计能力测试（数据驱动 × 多系统资源）》六页。本文区分题目要求与实现选择；运行证据见 [VALIDATION.md](VALIDATION.md)。

## 一、项目要求

| 原题要求 | 实现位置与验收方式 |
|---|---|
| 战斗装备/能力：Character、Equipment、Ability、TalentBoard、PerfumeModifier | `Assets/Game/Core/DataModels.cs` 定义全部五类，`ConfigSource/CSV`、`ConfigSource/Schemas` 各有独立表与 Schema；不是只实现 README 简表中的三类 |
| 配置生成角色，按配置挂载装备/能力/角色专属天赋板/香氛 | `CharacterFactory` 通过 ID 查询组装独立实例；校验归属与重复槽位，Demo 输出装配结果 |
| 不要求战斗或技能效果，不写死技能逻辑 | 能力与香氛保存描述数据；没有战斗循环、动画、特效或技能执行器 |
| 材料 ID、名称、稀有度、来源、图标、描述 | `MaterialData` 及 `material.csv`；来源类型包括 drop、plant、purchase、craft；图标为资源键，不是 SO 硬引用 |
| 配方输入、输出、耗时、行动力、产量，可扩展品质/概率 | `RecipeData`、`CraftingService`；按表扣费、定时领取，支持多产物；质量与概率预留字段，当前仅接受 normal/必定产出，其他值明确拒绝，扩展步骤见 README |
| 通用货币及价格/获取/是否购买 | `CurrencyData` 的 acquisitionMethods、purchasable、purchaseCosts；价格表达为货币 ID 与数量，VIP 等新货币可新增记录 |
| 卡池货币消耗、角色/装备/材料掉落、概率、可选保底 | `GachaPoolData`、`GachaService`、默认发奖注册器；整数权重、可空保底，扣费/发奖/保底原子提交 |
| 卡池/配方不硬编码，可扩展限时池 | 服务读取配置；卡池 UTC 起止时间已实现；样例 ID 和固定随机值仅出现在 Demo/检查中 |

## 二、框架要求

| 原题要求 | 实现位置与验收方式 |
|---|---|
| Excel/CSV→JSON→SO→Runtime | 采用原题明确允许的 CSV 路径：Excel 编辑并另存 UTF-8 CSV，`CsvImporter`→`ConfigImportMenu`→九个 `ConfigTableAsset`→`ManifestAsset.Load`→`ResourceSnapshot`。不宣称直接解析 .xlsx |
| Editor 导入工具 | `Tools > Game Config > Import CSV / Import JSON`，实际生成模块 JSON、原生 SO、版本清单和 Active 入口；发布前校验所有数据 |
| 清晰映射、字段可扩展、AI 可生成 | 强类型 DTO、九份 JSON Schema、严格字段校验、CSV 嵌套字段采用 JSON；README 给出 AI 提示、字段/类型/系统扩展步骤 |
| 统一模块管理和查询，模块可独立加载 | `ResourceSnapshot` 提供 LoadedModules、Get<T>、All<T>、KindOf、Contains；可局部加载并在执行前检查依赖，命令行检查覆盖材料独立加载和缺依赖拒绝 |
| 资源 ID 统一、模块依赖通过 ID | 所有业务配置引用采用命名空间 ID；只有资源装载清单引用模块 SO，业务数据之间没有 Unity 对象硬引用 |
| 系统解耦、后续 Buff 等扩展 | Core / Features / Unity / Editor 四程序集；业务服务依赖查询、时钟、随机与玩家事务；抽卡通过奖励接口分发 |
| 数据热更新 | `UnityConfigSession` 实际执行 JSON→临时 SO→新快照；坏引用拒绝，旧合成任务保留原产物，新任务采用新版本 |
| DemoScene 加载四类数据、一次抽卡与合成、Console 输出 | `Assets/Scenes/DemoScene.unity` 的 `DemoBootstrap.Start` 实际执行角色、装备、材料/配方、卡池加载；额外演示幂等、时间边界与热更 |

## 三、交付内容

| 原题要求 | 交付 |
|---|---|
| Git 仓库、合理提交历史 | [公开仓库](https://github.com/WangYe0516/UnityResourceFramework)，main 分支；按实际工作追加提交，保留原历史 |
| README 架构/模块/数据流/扩展/AI | README 第 2、4、5、7、8 节 |
| README 数据结构 | README 第 3 节列出全部九类，附 DTO 和 Schema 链接 |
| README 演示运行与系统接口 | README 第 1、6 节；`Tools/verify-unity.ps1` 自动导入、Play Mode、构建并启动 Player |
| README 新资源/字段/Buff 系统 | README 第 8 节 |
| 可选架构图/流程图/工具截图 | README 已提供文本数据流图；截图不是必交项 |

原题提及的参考 Excel 未随消息提供，当前 12 条样例是自拟配置；不声称复刻未收到的表格。玩家存档、服务端可信付费抽卡、CDN 发布与完整战斗不在此框架骨架交付中。
