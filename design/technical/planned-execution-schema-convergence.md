# Planned Execution Schema Convergence

本文档定义 `planned_execution` 的下一阶段收敛方案。

目标不是修某一条 prompt，而是把当前过度模板化的 step schema 收敛成更稳定、更可扩展的执行语义。

---

## 1. 问题定义

当前 `planned_execution` step schema 是：

- `pre_action_response`
- `tool_use`
- `final_response`

这套设计在最初阶段有价值，因为它能快速把：

- 工具执行前确认
- 工具执行
- 工具执行后汇报

三段行为清楚跑通。

但继续保留这套 schema，会引出几个问题：

### 1.1 `response` 被拆成了两个 step kind

实际上：

- `pre_action_response`
- `final_response`

都属于同一种能力：

> 让角色产生一段对玩家可见的自然语言回复

它们的区别不是能力不同，而是：

- 发生时机不同
- 约束不同
- 语义目标不同

把这些差异编码成 `kind`，会让 planner 过度依赖固定模板。

### 1.2 planner 容易被诱导成“先拆三段再说”

当前 prompt 明确告诉模型：

- 需要工具时，通常先 `pre_action_response`
- 再 `tool_use`
- 最后 `final_response`

这会让 planner 在很多本不需要工具、或本可直接回答的问题上，也倾向于向“先回一句、再调一步、再回一句”靠拢。

### 1.3 多步 response 场景不自然

后续很可能出现这些场景：

1. 先短答一句
2. 再执行动作
3. 再补一句说明
4. 再追问一句

这些都还是 `response`。

如果继续用：

- `pre_action_response`
- `final_response`

来划分，step kind 会越来越不自然。

### 1.4 当前 schema 把“阶段”误当成“能力”

真正稳定的抽象应该是：

- `response`
- `tool_use`

而不是：

- `pre_action_response`
- `tool_use`
- `final_response`

---

## 2. 收敛目标

将 `planned_execution` 的 step kind 收敛为：

- `response`
- `tool_use`

其中：

- `response` 负责所有对玩家可见的角色回复
- `tool_use` 负责受控工具执行与局部循环

`response` 的差异化语义不再由 kind 承载，而改由 step 元数据承载。

---

## 3. 新 schema

### 3.1 Step Kind

新的最小 kind 集合：

```text
- response
- tool_use
```

未来如果确实需要，再新增：

- `abort`
- `handoff`
- `wait`

但在当前阶段不建议继续扩充。

### 3.2 Response Step 元数据

`response` step 继续保留：

- `stepId`
- `goal`
- `maxIterations`
- `responseMode`
- `completionHint`

其中最关键的是：

## `responseMode`

它承载“这是哪种回复”。

建议当前至少支持：

- `ack_before_action`
- `report_after_action`
- `final_direct_answer`
- `clarify_question`

后续可扩展：

- `partial_progress`
- `error_report`
- `handoff_notice`

### 3.3 Tool Step 元数据

`tool_use` step 保留：

- `allowedToolNames`
- `maxIterations`
- `completionHint`

不需要再额外引入“pre tool”或“post tool”类 kind。

---

## 4. 新旧 schema 对照

### 旧 schema

```json
{
  "steps": [
    {
      "stepId": "step_1",
      "kind": "pre_action_response",
      "goal": "先告诉玩家接下来会去确认",
      "responseMode": "ack_before_action",
      "completionHint": "玩家知道角色将立即执行动作"
    },
    {
      "stepId": "step_2",
      "kind": "tool_use",
      "goal": "执行工具",
      "allowedToolNames": ["ask_character"],
      "maxIterations": 1,
      "completionHint": "拿到工具结果"
    },
    {
      "stepId": "step_3",
      "kind": "final_response",
      "goal": "向玩家汇报结果",
      "responseMode": "report_after_action",
      "completionHint": "玩家收到结果"
    }
  ]
}
```

### 新 schema

```json
{
  "steps": [
    {
      "stepId": "step_1",
      "kind": "response",
      "goal": "先告诉玩家接下来会去确认",
      "responseMode": "ack_before_action",
      "maxIterations": 1,
      "completionHint": "玩家知道角色将立即执行动作"
    },
    {
      "stepId": "step_2",
      "kind": "tool_use",
      "goal": "执行工具",
      "allowedToolNames": ["ask_character"],
      "maxIterations": 1,
      "completionHint": "拿到工具结果"
    },
    {
      "stepId": "step_3",
      "kind": "response",
      "goal": "向玩家汇报结果",
      "responseMode": "report_after_action",
      "maxIterations": 1,
      "completionHint": "玩家收到结果"
    }
  ]
}
```

---

## 5. Planner Prompt 调整

`PlannedExecutionPlanPromptBuilder` 的系统 prompt 应做如下收敛：

### 当前

- 允许的 kind：
  - `pre_action_response`
  - `tool_use`
  - `final_response`
- 强约束：
  - 需要工具时通常先 `pre_action_response`
  - 最后一步必须是 `final_response`

### 目标

- 允许的 kind：
  - `response`
  - `tool_use`
- 强约束改为：
  - 如果当前计划需要先向玩家确认动作，可以先放一个 `response`
  - 如果当前计划需要工具，通常会有一个或多个 `tool_use`
  - 整个 steps 最后通常以一个 `response` 收束
  - 但最终语义依赖 `responseMode`，不是依赖 `kind`

### 关键变化

不要再在 prompt 中把：

- `pre_action_response`
- `final_response`

写成两种能力。

而应明确告诉模型：

> 你只有一种 `response` step。  
> 区别请通过 `responseMode` 表达。

---

## 6. 运行时映射调整

### 当前实现

当前运行时内部枚举仍是：

- `PreActionResponse`
- `ToolUse`
- `FinalResponse`

对应文件：

- [CharacterPlannedExecutionPlanModels.cs](/home/yoitsuholo/Code/openLuo-cli/openLuo/Modules/Agent/Application/Flow/Nodes/CharacterPlannedExecutionPlanModels.cs)

### 收敛方案

运行时内部也应同步改成：

- `Response`
- `ToolUse`

然后根据 `ResponseMode` 决定：

- 是否作为前置确认回复
- 是否作为后置汇报回复
- 是否作为直接最终回答

也就是说：

```text
kind = response
mode = ack_before_action | report_after_action | final_direct_answer | ...
```

### CharacterPlannedExecutionNode

`CharacterPlannedExecutionNode` 当前对于：

- `PreActionResponse`
- `FinalResponse`

应统一归并成：

- `Response`

实际分支逻辑改为按 `ResponseMode` 走，而不是按 kind 走。

---

## 7. 默认降级构造器调整

`DefaultCharacterExecutionPlanBuilder` 当前也在直接构造：

- `PreActionResponse`
- `ToolUse`
- `FinalResponse`

这部分也应同步改成：

- `Response(ack_before_action)`
- `ToolUse`
- `Response(report_after_action|final_direct_answer)`

这样：

- planner 主路径
- fallback 构造器路径

才能保持一致。

---

## 8. 迁移策略

建议分两步：

### 第一步：兼容读取

先让 mapper 同时支持旧 kind 和新 kind：

- `pre_action_response` -> `response + ack_before_action`
- `final_response` -> `response + final_direct_answer` 或保留原 mode
- `tool_use` -> `tool_use`

这样不会一下打断现有 prompt 输出。

### 第二步：切 planner prompt

等 mapper 和 node 执行逻辑兼容后，再把：

- `PlannedExecutionPlanPromptBuilder`
- `DefaultCharacterExecutionPlanBuilder`

一起切到新 schema。

### 第三步：删旧 kind

等测试和日志都稳定后，再删：

- `PreActionResponse`
- `FinalResponse`

这两个运行时枚举分支。

---

## 9. 不在本轮解决的问题

这份方案只处理：

- step schema 过度模板化
- response kind 过度拆分

它**不直接解决**这些问题：

### 9.1 工具目录污染

`TOOL_CATALOG` 是否混入：

- `chat`
- `debug`
- `bash`

这属于能力注入边界问题，应单独处理。

### 9.2 JSON 输出稳定性

结构化输出是否被截断、是否需要 fenced json、是否需要重试，属于 executor 输出稳定性问题，不属于 step schema 本身。

### 9.3 状态结算质量

资源评估器输出非法资源、过度增减等，属于状态评估模型与校验问题。

---

## 10. 推荐实施顺序

建议顺序：

1. 先修能力注入边界
   - 只把 C# 内部工具放进角色 `TOOL_CATALOG`
2. 再收敛 `planned_execution` schema
   - `response`
   - `tool_use`
3. 再观察 planner 是否仍有过度编排
4. 最后才继续调整 prompt 细节

这样做的原因是：

- 如果能力面仍然脏，先改 schema 只会把变量搅在一起
- 先把工具边界收干净，再做 step 收敛，问题定位更稳定

---

## 11. 结论

结论很明确：

- 当前 `pre_action_response / final_response` 设计能跑，但偏模板化
- 长期更好的 schema 是：
  - `response`
  - `tool_use`
- `response` 的前后置语义应交给 `responseMode` 承载

这会让：

- planner 更少被模板诱导
- schema 更小
- 多步 response 更自然
- 后续扩展 `clarify / partial progress / error report` 更容易

这是值得推进的下一层收敛。
