# 插件规范

新架构下需要区分两类扩展：

- **Content Pack**
- **Runtime Plugin**

本文档描述后者，即真正带代码执行能力的插件。

## 1. 当前代码事实

当前插件形态仍然是：

- `openLuo/data/plugins/<plugin-id>/`
- `plugin.jsonc`
- `main.py`

由：

- `McpPluginHost.LoadAllAsync`

扫描并启动 Python 进程。

当前 runtime plugin 仍然是有效的，不会被新 schema 设计直接替换掉。

## 2. 新定位

Runtime Plugin 负责：

- 暴露命令/工具/skill/subagent
- 提供 hook
- 提供运行时代码扩展
- 反向调用 `game/*` API
- 可注册 `AgentFlowRegistration`

Runtime Plugin 不应继续承担：

- 角色卡的完整定义
- 所有资源 schema 的事实标准
- 与角色背景强耦合的系统特化数据中心

同时也不应被误解为唯一扩展抽象。  
内容扩展优先走 pack + canonical schema，runtime plugin 负责代码与协议扩展。

这些应迁回内容 pack 或插件配置文件。

## 3. Manifest 建议

当前 manifest 字段至少包含：

- `id`
- `name`
- `version`
- `entry`
- `hooks`

新架构建议增加 pack 级思维，但 runtime plugin 仍可保留当前最小结构。  
后续建议向统一 `PackManifest` 靠拢，并至少补充：

- `kind = runtime-plugin`
- `schemaVersion`
- `dependencies`
- `permissions`

可落地字段模板见：

- [content-schema.md](/home/yoitsuholo/Code/openLuo-cli/design/technical/content-schema.md) 中的 `PackManifest`

## 4. 运行时能力

当前 runtime plugin 可以：

- `tools/list`
- `tools/call`
- hook call
- `game/*` bridge
- 注册 flow

这意味着它适合承载：

- 自定义执行器桥接
- 外部工具适配
- 规则引擎
- 资源生成器
- 特殊系统服务

新增扩展逻辑时，默认优先放在 runtime plugin，而不是先要求扩一轮宿主 C# 编译期类型。

## 5. 插件配置分层

如果某个系统需要角色级差异化配置，例如：

- mood
- relationship
- daily behavior

建议 runtime plugin 目录使用：

```text
defaults.jsonc
characters/<character-id>.jsonc
```

语义为：

- 先加载插件默认配置
- 再叠加角色覆盖
- 不存在覆盖时走默认回退

这比把所有特化字段都塞进角色卡更健康。

推荐默认配置模板：

```jsonc
{
  "pluginId": "builtin_mood",
  "defaults": {
    "volatility": 0.3,
    "recoveryRate": 0.5,
    "expressionBias": "stable"
  }
}
```

推荐角色覆盖模板：

```jsonc
{
  "characterId": "rin",
  "overrides": {
    "volatility": 0.7,
    "expressionBias": "expressive"
  }
}
```

## 6. 与 Content Pack 的边界

### Content Pack 负责

- 定义角色、物品、资源、工具、技能等内容

### Runtime Plugin 负责

- 执行逻辑
- 对外桥接
- 协议与 hook
- 动态能力注册

不要再把这两种职责混成一个抽象。

## 7. 结论

插件在新架构里仍然重要，但它应被收缩回：

- 代码扩展
- 协议扩展
- 动态能力扩展

而不是继续承担“整个内容系统的默认容器”或“唯一扩展单元”。
