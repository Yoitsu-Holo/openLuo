# 测试策略

## 1. 当前测试结构

测试项目位于 `openLuo.Tests/`，当前已覆盖：

- Application
- Agent
- AgentCapabilities
- Executor
- Infrastructure
- Integration
- InterAgent
- Security

## 2. 当前测试目标

- 保证命令主链可用
- 保证 Agent 调度与 executor 驱动的 turn pipeline 稳定
- 保证 inter-agent 基线能力可用
- 保证插件桥接与关键 `game/*` API 可用
- 保证安全、限流和输入校验不回退

## 3. 当前基线

- 命令：`dotnet test openLuo.sln`
- 结果：`270 passed / 0 failed`

## 4. 推荐继续补的测试

- 多线程会话隔离测试
- 长链 inter-agent 协作测试
- 插件错误恢复测试
- 资源 schema 校验测试
- 向量维度迁移和异常恢复测试

## 5. 环境注意事项

- 当前环境可能出现 `NU1900` 警告，原因是 NuGet 漏洞数据源不可访问
- 该警告不影响当前本地构建与测试结果
