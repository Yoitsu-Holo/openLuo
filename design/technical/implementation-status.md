# 技术实现现状

## 已稳定落地

- CLI / TUI 双入口
- 配置加载与依赖注入
- SQLite 初始化与迁移
- `sqlite-vec` 向量记忆基线
- Python 插件运行时
- `game/*` 反向 API
- 状态系统、时间系统、时间线系统
- 资产系统
- Agent tool-use loop
- 统一 Agent capability registry
- inter-agent ask 与 session chat

## 当前活跃重构面

- Agent runtime 与消息流细化
- `InterAgent` 能力继续扩展
- 多会话线程隔离

## 当前仍偏宽或偏重的区域

- `GameBridge`
- 背景定义文件职责
- Agent 调度链观测能力

## 测试基线

- `dotnet test openLuo.sln` 通过
- 当前基线：`270 passed / 0 failed / 0 skipped`
