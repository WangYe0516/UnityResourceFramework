# Unity 多系统数据驱动框架

本工程实现 Unity 框架测试题要求的资源配置、角色构建、材料合成与抽卡骨架。

目标编辑器：Unity 2022.3.62f1。开发过程中将按配置核心、业务服务、Unity 接入和验收文档分阶段提交。正式运行与验证说明会随功能完成更新。

配置生产链路：Excel 导出 UTF-8 CSV → Editor 生成 JSON → ScriptableObject → Runtime。

原题允许 CSV。本工程不直接解析 `.xlsx`。不实现战斗、UI、动画、特效或任意脚本代码热更新。
