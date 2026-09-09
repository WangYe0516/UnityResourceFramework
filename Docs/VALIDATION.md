# 验证记录

日期：2026-09-10（Asia/Shanghai）。本次以实际 Unity 验收结果更新 2026-09-07 的历史状态。

## Unity 实机验收：全部通过

| 验收层 | 环境与结果 |
|---|---|
| Unity 编译/包解析 | Unity 6000.6.0f1 (f7f8ed4d1e24)，官方 Newtonsoft 3.2.2；四个项目程序集实际编译 |
| Editor 导入 | CSV→九模块 JSON/原生 SO→Active manifest→运行时查询；完整 Demo 与临时 SO 热更通过 |
| 场景 Play Mode | 真正打开 DemoScene、进入 Play Mode，经场景组件 Start 执行全部断言；独立验证进程退出 0 |
| Windows 构建 | Windows x64、Mono 后端、StrictMode；BuildReport 成功，输出 96,557,882 字节 |
| 独立 Player | 启动构建出的 exe，以 --smoke-test 执行真实场景；余额断言通过，进程退出 0 |

复现（先关闭同项目的编辑器）：

```powershell
.\Tools\verify-unity.ps1 -UnityEditor 'C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Unity.exe'
```

真实脚本输出：

```text
[Batch Validation] PASS:
[Play Mode Validation] PASS:
[Batch Build] PASS:
PASS: herb=9, water=3, potion=3, ticket=1, actionPoints=6.
Unity verification completed: All.
```

完整本地日志在 `Logs/Verification`（忽略入 Git，含本机路径）；公开结果摘录及构建文件 SHA-256 在 `Docs/Evidence`。

## 实机发现并修复

首次批处理打开项目时，Unity 默认提供未保存的空场景。原先一律追加新场景会报 `Cannot create a new scene additively with an untitled scene unsaved`。现在仅在唯一场景是未修改的无名空场景时替换；存在未保存工作时明确拒绝并保留现场。修复后重新执行完整验收，全部通过。

静态 GUID 检查补充识别 Unity 自带的 skybox/lighting/cookie 保留 GUID；普通项目 GUID 仍必须解析到资产。此前手写的初始 seed 资产保留用于回归比较，活动资产与 DemoScene 已由 Unity 原生导入/保存。

## 已执行

- .NET SDK 8.0.424，目标 net8.0，编译语言强制 C# 7.3，警告视为错误。
- 使用实际的 Core、Features 和 Editor/CsvImporter.cs 源码进行编译，不是 Python 翻译版或伪代码测试。
- 测试 JSON 依赖为 NuGet Newtonsoft.Json 13.0.3；Unity 编辑器依赖是官方 com.unity.nuget.newtonsoft-json 3.2.2。命令行检查本身不替代上面的 Unity 验收。
- 47 项检查全部通过；包括九模块/12资源、严格字段与数值类型、CSV/JSON一致性、ID与依赖、只读隔离、节点循环、概率边界、保底、幂等、原子失败、并发重试、扩展处理器引用逃逸/重入防护、时间与热更新。
- 穷举普通卡池全部 1000 个整数随机值：角色 100、装备 300、材料 600。
- 静态资产检查通过：九个 SO 内嵌配置与 JSON 一致，所有已提交的 Unity GUID 引用可解析，程序集定义引用可解析。

复现命令（安装 .NET 8 SDK 并加入 PATH 后）：

```powershell
.\Tools\verify.ps1
```

末行结果：

```text
RESULT 47 checks passed; real C# sources compiled with C# 7.3.
LIMIT: Unity asset import, scene execution and player build require a Unity Editor run.
```

## 验证边界

- 未验证 IL2CPP/AOT、Windows 之外的平台和旧版 Unity 2022.3。
- 未做故障注入覆盖每一个磁盘发布回滚分支；已验证正常原生发布及运行时坏引用热更保持旧快照。
- 玩家状态仅内存保存；没有存档恢复、服务器、CDN 或支付功能。
