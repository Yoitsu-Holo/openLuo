using openLuo.playgraound.Demos.Agent;
using openLuo.playgraound.Demos.Content;
using openLuo.playgraound.Demos.Executor;
using openLuo.playgraound.Demos.Llm;
using openLuo.playgraound.Demos.Plugin;

var demo = args.FirstOrDefault()?.Trim().ToLowerInvariant() ?? "pipeline";

if (demo is "character-agent" or "agent")
    return await CharacterAgentDemo.RunAsync();

if (demo == "llm")
    return await MicrosoftAiLlmClientDemo.RunAsync();

if (demo is "memory-recall" or "recall")
    return await MemoryRecallExecutorDemo.RunAsync();

if (demo is "memory-fallback" or "keyword-fallback")
    return await MemoryKeywordFallbackDemo.RunAsync();

if (demo is "memory-vector" or "memory-live" or "vector")
    return await MemoryVectorDemo.RunAsync();

if (demo is "pipeline" or "character-turn")
    return await CharacterTurnPipelineDemo.RunAsync();

if (demo is "flow-routing" or "routing")
    return await FlowRoutingExecutorDemo.RunAsync();

if (demo is "subgraph" or "subgraph-flow")
    return await SubgraphFlowDemo.RunAsync();

if (demo is "turn-message" or "turn-message-emitter" or "msgflow")
    return await TurnMessageEmitterDemo.RunAsync();

if (demo is "content-bootstrap" or "bootstrap")
    return await ContentBootstrapDemo.RunAsync();

if (demo is "tool-hook" or "plugin-hook" or "tool-executed-hook")
    return await ToolExecutedHookDemo.RunAsync();

Console.Error.WriteLine("Unknown demo.");
Console.Error.WriteLine("Available demos:");
Console.Error.WriteLine("  character-agent");
Console.Error.WriteLine("  memory-recall");
Console.Error.WriteLine("  memory-fallback");
Console.Error.WriteLine("  memory-vector");
Console.Error.WriteLine("  pipeline");
Console.Error.WriteLine("  flow-routing");
Console.Error.WriteLine("  subgraph");
Console.Error.WriteLine("  turn-message");
Console.Error.WriteLine("  content-bootstrap");
Console.Error.WriteLine("  tool-hook");
Console.Error.WriteLine("  llm");
return 1;
