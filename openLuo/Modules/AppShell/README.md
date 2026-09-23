# AppShell Module

负责宿主应用装配与基础配置入口。

包含：
- `Application/ServiceCollectionExtensions.cs`
- `Application/AppConfig.cs`

边界：
- 提供 DI 装配与启动前准备
- 不直接承载内容定义与静态内容加载
- 不直接承载具体玩法逻辑
