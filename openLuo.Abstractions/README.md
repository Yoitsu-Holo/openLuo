# openLuo.Abstractions

openLuo 的**公开扩展契约层**——mod / 扩展开发者的**唯一引用点**。

写一个扩展，只需 `ProjectReference` 本工程，即可通过传递引用获得全部契约类型：

- 插件入口契约：`IAgentExtension`、`ExtensionBuilder`、`ExtensionManifest`
- 能力契约：`ICapabilityInvoker`、`CapabilityDescriptor`、`CapabilityResult`、`CapabilityCall`、`CapabilityExecutionContext`
- 上下文契约：`IContextContributor`、`ContextContribution`、`ContextRegion`、`ContextBuildRequest`
- 状态变更契约：`IStateMutationHandler`、`MutationIntent`
- 运行时门面：`IAgentRuntime`

## 最小扩展

```csharp
using openLuo.Abstractions;
using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;

public sealed class MyExtension : IAgentExtension
{
    public void Configure(ExtensionBuilder builder)
    {
        builder.AddCapability(new CapabilityDescriptor
        {
            CanonicalId = "echo",
            DisplayName = "Echo",
            Summary = "回显输入。",
            Kind = CapabilityKind.Builtin,
            ProviderId = "my",
        }, new EchoInvoker());
    }
}
```

配合 `extension.jsonc`：

```jsonc
{
  "id": "my",
  "version": "1.0.0",
  "assembly": "openLuo.Extension.My.dll",
  "entryType": "MyExtension"
}
```

部署到 `extensions/<id>/`（manifest + DLL），宿主启动时 `ExtensionHost` 扫描加载。

## 契约语义（mod 开发者须知）

| 契约 | 时机 | 语义 |
|---|---|---|
| `IAgentExtension.Configure` | 加载时一次 | 声明能力 + 注册 invoker / contributor / state handler |
| `ICapabilityInvoker.InvokeAsync` | 模型 tool_calls 触发 | 执行能力；返回 `CapabilityResult`（文本/输出/mutation） |
| `IContextContributor.ContributeAsync` | 每回合构建上下文 | 注入 region 化上下文块 |
| `IStateMutationHandler` | 状态变更提交时 | 校验/变换 mutation |

> 注意：`ExtensionHost` / `ExtensionManifestLoader` 是加载器**实现**（过渡态暂居此工程），mod 开发者不使用；第二步将移出到宿主。
