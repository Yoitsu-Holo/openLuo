# openLuo.Foundation

openLuo 的共享底座工程：不包含任何业务逻辑，只承载被**多个子工程共同使用**的基础设施与领域原语。所有子工程和宿主 `openLuo` 都引用它，但它不依赖任何子工程（依赖方向最底层）。

## 用途

在拆分为独立子工程（`openLuo.Llm` / `openLuo.Embedding` / `openLuo.Memory`）之前，这些类型分散在主工程各处，子工程无法引用它们而不产生反向依赖。Foundation 把它们收拢为最底层依赖，保证：

- 子工程之间、子工程与宿主之间**单向依赖、无环**
- 能力库（LLM / Embedding / Memory）可脱离宿主独立复用
- namespace 保持不变（`openLuo.Core.*` / `openLuo.Infrastructure.*`），既有调用方零改动

## 内容

| 位置                                         | 内容                                                                                  | 说明                                                          |
| -------------------------------------------- | ------------------------------------------------------------------------------------- | ------------------------------------------------------------- |
| `Core/Models/Block.cs`                       | `Block` / `TextBlock` / `ImageBlock` / `AssetBlock` 等                                | 全平台通用内容单元，`ChatMessage` 多模态载荷                  |
| `Core/Interfaces/IGameLogger.cs`             | `IGameLogger`                                                                         | 结构化日志接口                                                |
| `Infrastructure/Logging/Logger.cs`           | 静态 `Logger` 门面                                                                    | 启动时 `Initialize(IGameLogger)` 绑定实现，子工程直接静态调用 |
| `Infrastructure/Security/PromptSanitizer.cs` | 提示词脱敏工具                                                                        | LLM / Embedding 客户端共用                                    |
| `Infrastructure/Database/`                   | `IDatabaseConnectionFactory` / `SqliteConnectionFactory` / `SqliteVecExtensionLoader` | SQLite 连接工厂（含 sqlite-vec 扩展加载），`Memory` 仓储依赖  |

## 依赖

- `Microsoft.Data.Sqlite`（连接工厂）
- `SQLitePCLRaw.lib.e_sqlite3`（显式覆盖传递依赖的漏洞版本，GHSA-2m69-gcr7-jv3q）

## 注意事项

- `SqliteVecExtensionLoader` 为 `internal`，通过 `InternalsVisibleTo("openLuo")` 暴露给宿主（`DatabaseInitializer` 初始化 sqlite-vec 用），不构成公共 API
- 静态 `Logger` 的实现绑定由宿主在启动时完成；未初始化时调用会抛异常（与拆分前行为一致）
