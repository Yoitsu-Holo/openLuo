# Gameplay Module

负责游戏主循环、商店、礼物、状态聚合、命令门控和状态评估等玩法编排层。

包含：
- `Core/Interfaces`：玩法专属编排接口（如 `IStateEvaluationCoordinator`）
- `Application/Services`：`GameEngine`、`CommandGate`、`ShopService`、`GiftService`、`StatusAggregator`、`StateEvaluationCoordinator`

这是当前宿主应用与各模块的主要业务汇合点。

## 业务一致性约束

### 宿主负责副作用

- 礼物、库存扣减、状态变更、记忆写入等副作用必须由宿主显式执行。
- 不允许把这些副作用隐式绑定在模型是否“碰巧走到某条链路”上。
- 模型可以识别意图、生成建议或决定是否接受，但真正写数据库的一定是宿主服务。

### Chat 后处理

- 状态评估必须作为统一 chat pipeline 的后置阶段执行。
- 不能只在某条具体渲染链命中时才进行状态评估。
- 当前推荐位置是 `OnChatTurnAfter`：以本轮最终玩家输入和角色最终可见输出为依据，单独调用状态结算 LLM，再通过 `StateMutationService` 落库。

### 礼物处理

- 自动赠礼必须拆成三个阶段：
- 礼物意图识别：判断玩家是否真的在赠礼。
- 礼物解析与校验：检查背包中是否存在候选物品。
- 能力执行：通过明确能力或服务真实扣减库存并结算结果。
- 不要把“识别赠礼”和“直接执行扣库存”混成一个黑盒 LLM 调用。

### 文本与状态一致性

- 叙事文本不是状态事实本身。
- 当对白中出现“已收到礼物”“心情变化”“关系变化”时，必须有对应宿主 mutation 支撑。
- 如果 mutation 没有成功，输出应降级为未完成态或确认态，而不是继续伪造已发生的业务事实。

### 变化必须可控

- 状态结算只允许修改白名单状态。
- 所有修改都必须经过 mutable / derived / clamp / maxDeltaPerTurn 校验。
- 任何新加的对话后置状态逻辑，都必须复用现有状态定义和 mutation 约束，而不是直接写库。
