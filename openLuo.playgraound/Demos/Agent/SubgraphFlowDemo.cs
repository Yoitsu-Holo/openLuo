using openLuo.Modules.Agent.Application;
using openLuo.Modules.Agent.Core.Interfaces.Flow;
using openLuo.Modules.Agent.Core.Models.Flow;
using openLuo.Modules.Executor.Application.CharacterResponse;
using openLuo.Modules.Executor.Application.FlowRouting;
using openLuo.Modules.Executor.Core.Interfaces;
using openLuo.Modules.Executor.Core.Models;
using openLuo.Modules.Executor.Infrastructure;
using openLuo.playgraound.Infrastructure;

namespace openLuo.playgraound.Demos.Agent;

internal static class SubgraphFlowDemo
{
    public static async Task<int> RunAsync()
    {
        var client = LlmDemoBootstrap.TryCreateClient(out var error);
        if (client is null)
        {
            Console.Error.WriteLine(error);
            return 1;
        }

        var registry = new DefaultAgentFlowRegistry();
        registry.Register(BuildChildFlow());
        registry.Register(BuildParentFlow());

        var responseExecutor = new CharacterResponseExecutor(
            client,
            new CharacterResponsePromptBuilder());
        var proxy = new FlowRunnerProxy();
        var runner = new DefaultAgentFlowRunner(
            registry,
            new DefaultAgentFlowGuardEvaluator(),
            [
                new DemoCharacterResponseFlowNodeExecutor(responseExecutor),
                new SubgraphFlowNodeExecutor(proxy)
            ],
            new NoOpRoutingExecutor());
        proxy.Inner = runner;

        Console.WriteLine("=== Subgraph Flow Demo ===");
        Console.WriteLine("flow: demo.parent -> flow.subgraph -> demo.child(response)");
        Console.WriteLine();

        AgentFlowRunResult result;
        try
        {
            result = await runner.RunAsync(new AgentFlowRunRequest
            {
                FlowId = "demo.parent",
                AgentId = "demo-agent",
                GameId = "demo-game",
                Inputs = new Dictionary<string, object?>
                {
                    ["playerInput"] = "hello"
                }
            });
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine("subgraph demo failed during LLM request.");
            Console.Error.WriteLine($"reason: {ex.Message}");
            Console.Error.WriteLine("hint: check openLuo.playgraound/config/llm.demo.ini and any local proxy settings.");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("subgraph demo failed.");
            Console.Error.WriteLine($"{ex.GetType().Name}: {ex.Message}");
            return 1;
        }

        Console.WriteLine($"success: {result.Success}");
        Console.WriteLine($"terminalNodeId: {result.TerminalNodeId}");
        if (!result.Success)
        {
            Console.WriteLine($"error: {result.Error}");
            return 1;
        }

        if (result.Outputs.TryGetValue("finalReply", out var finalReply))
            Console.WriteLine($"finalReply: {finalReply}");

        Console.WriteLine("steps:");
        foreach (var step in result.Steps)
            Console.WriteLine($"- {step.NodeId} -> {step.NextNodeId}");

        return 0;
    }

    private static AgentFlowDefinition BuildChildFlow() => new()
    {
        Id = "demo.child",
        StartNodeId = "respond",
        Nodes =
        [
            new AgentFlowNode
            {
                Id = "respond",
                Kind = AgentFlowNodeKind.Executor,
                CallName = "demo.character_response",
                OutputKey = "finalReply",
                InputMap = new Dictionary<string, string>
                {
                    ["inputKey"] = "playerInput"
                }
            },
            new AgentFlowNode
            {
                Id = "done",
                Kind = AgentFlowNodeKind.Terminal,
                CallName = "terminal.done"
            }
        ],
        Edges =
        [
            new AgentFlowEdge
            {
                Id = "respond-to-done",
                FromNodeId = "respond",
                ToNodeId = "done",
                When = "完成"
            }
        ]
    };

    private static AgentFlowDefinition BuildParentFlow() => new()
    {
        Id = "demo.parent",
        StartNodeId = "subgraph",
        Nodes =
        [
            new AgentFlowNode
            {
                Id = "subgraph",
                Kind = AgentFlowNodeKind.Executor,
                CallName = "flow.subgraph",
                OutputKey = "childResult",
                InputMap = new Dictionary<string, string>
                {
                    ["flowId"] = "demo.child",
                    ["inheritKeys"] = "playerInput",
                    ["exportOutputKey"] = "finalReply"
                }
            },
            new AgentFlowNode
            {
                Id = "done",
                Kind = AgentFlowNodeKind.Terminal,
                CallName = "terminal.done"
            }
        ],
        Edges =
        [
            new AgentFlowEdge
            {
                Id = "subgraph-to-done",
                FromNodeId = "subgraph",
                ToNodeId = "done",
                When = "子图执行完成"
            }
        ]
    };

    private sealed class DemoCharacterResponseFlowNodeExecutor : IAgentFlowNodeExecutor
    {
        private readonly IExecutor<CharacterResponseInput, string> _responseExecutor;

        public DemoCharacterResponseFlowNodeExecutor(
            IExecutor<CharacterResponseInput, string> responseExecutor)
        {
            _responseExecutor = responseExecutor;
        }

        public string CallName => "demo.character_response";

        public async Task<AgentFlowNodeExecutionResult> ExecuteAsync(
            AgentFlowNode node,
            AgentFlowRunRequest request,
            IReadOnlyDictionary<string, object?> state,
            CancellationToken ct = default)
        {
            node.InputMap.TryGetValue("inputKey", out var inputKey);
            var resolvedInputKey = string.IsNullOrWhiteSpace(inputKey) ? "playerInput" : inputKey;
            var playerInput = state.TryGetValue(resolvedInputKey, out var inputObj)
                ? Convert.ToString(inputObj)?.Trim()
                : null;
            if (string.IsNullOrWhiteSpace(playerInput))
                playerInput = "hello";

            var result = await _responseExecutor.ExecuteAsync(new CharacterResponseInput
            {
                CharacterProfile =
                    """
                    名字：子图助手
                    身份：一个用于验证 flow.subgraph 的轻量角色
                    性格：简洁、友好、稳定
                    说话风格：自然、短句
                    """,
                GoalContext = "这是一个 subgraph 推理验证，请自然回应输入内容，不要展开成长对话。",
                PlayerInput = playerInput
            }, ct);

            if (!result.Success || result.Output is null)
                return AgentFlowNodeExecutionResult.Fail(result.Error ?? "subgraph response failed");

            return AgentFlowNodeExecutionResult.Ok(result.Output.Trim());
        }
    }

    private sealed class NoOpRoutingExecutor : IExecutor<FlowRoutingInput, FlowRoutingOutput>
    {
        public string Name => "noop_flow_routing";

        public Task<ExecutorResult<FlowRoutingOutput>> ExecuteAsync(FlowRoutingInput input, CancellationToken ct = default) =>
            Task.FromResult(ExecutorResult<FlowRoutingOutput>.Fail("routing not needed"));
    }

    private sealed class FlowRunnerProxy : IAgentFlowRunner
    {
        public IAgentFlowRunner? Inner { get; set; }

        public Task<AgentFlowRunResult> RunAsync(AgentFlowRunRequest request, CancellationToken ct = default)
        {
            if (Inner is null)
                throw new InvalidOperationException("FlowRunnerProxy.Inner is not assigned.");
            return Inner.RunAsync(request, ct);
        }
    }
}
