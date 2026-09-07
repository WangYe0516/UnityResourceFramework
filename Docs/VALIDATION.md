# 验证记录

日期：2026-09-07。此记录明确区分已执行的检查与尚未执行的 Unity 验收。

## 已执行

- .NET SDK 8.0.424，目标 net8.0，编译语言强制 C# 7.3，警告视为错误。
- 使用实际的 Core、Features 和 Editor/CsvImporter.cs 源码进行编译，不是 Python 翻译版或伪代码测试。
- 测试 JSON 依赖为 NuGet Newtonsoft.Json 13.0.3；Unity 编辑器依赖是官方 com.unity.nuget.newtonsoft-json 3.2.1。命令行检查不验证 Unity 自身包加载。
- 47 项检查全部通过；包括九模块/12资源、严格字段与数值类型、CSV/JSON一致性、ID与依赖、只读隔离、节点循环、概率边界、保底、幂等、原子失败、并发重试、扩展处理器引用逃逸/重入防护、时间与热更新。
- 穷举普通卡池全部 1000 个整数随机值：角色 100、装备 300、材料 600。
- 静态资产检查通过：九个 SO 内嵌配置与 JSON 一致，所有已提交的 Unity GUID 引用可解析，程序集定义引用可解析。

执行命令：

```powershell
.\Tools\verify.ps1 -Dotnet 'C:/Users/10657/Documents/Codex/2026-09-07/jie/work/dotnet/dotnet.exe'
```

末行结果：

```text
RESULT 47 checks passed; real C# sources compiled with C# 7.3.
LIMIT: Unity asset import, scene execution and player build require a Unity Editor run.
```

## 尚未执行

当前环境未找到 Unity Editor。因此以下项目没有测试通过声明：

1. Unity Package Manager 解析、Unity 程序集编译。
2. AssetDatabase 实际导入/生成 SO 与发布回滚。
3. DemoScene 的 Play Mode 运行。
4. JSON → 临时 SO 的 Unity 原生对象生命周期。
5. Windows Player 构建与运行。
6. IL2CPP/AOT、其他平台以及保存/恢复玩家状态。

README 提供 `BatchValidateAndRunDemo`、`BatchBuildDemoWindows` 的命令。拿到可用且授权的 Unity 编辑器后应执行它们，并将真实日志与结果加入后续提交。不得用本次命令行测试替代这些结论。
