using openLuo.Modules.Agent.Core.Interfaces;
using openLuo.Modules.Commanding.Core.Models;

namespace openLuo.Modules.Agent.Application;

public sealed class MultiCharacterCommandCatalog : IMultiCharacterCommandCatalog
{
    private static readonly IReadOnlyList<CommandDescriptor> Commands =
    [
        new()
        {
            Name = "task",
            Aliases = ["party"],
            Category = "command",
            Prefix = "/",
            HelpShort = "发起多角色协作任务",
            Usage = "/task <任务描述> [--team 角色1,角色2]",
            ProviderId = "core_multi_character",
            RiskLevel = "medium",
            NeedsConfirm = false,
            Capabilities = ["dispatch", "multi-agent"]
        },
        new()
        {
            Name = "task_status",
            Aliases = ["taskstatus"],
            Category = "command",
            Prefix = "/",
            HelpShort = "查看协作任务状态与步骤结果",
            Usage = "/task_status [任务ID]",
            ProviderId = "core_multi_character",
            RiskLevel = "low",
            NeedsConfirm = false,
            Capabilities = ["task-query"]
        },
        new()
        {
            Name = "characters",
            Category = "command",
            Prefix = "/",
            HelpShort = "列出当前存档可用角色",
            Usage = "/characters",
            ProviderId = "core_multi_character",
            RiskLevel = "low",
            NeedsConfirm = false,
            Capabilities = ["character-query"]
        },
        new()
        {
            Name = "switch",
            Category = "command",
            Prefix = "/",
            HelpShort = "切换当前角色",
            Usage = "/switch <角色名|角色ID>",
            ProviderId = "core_multi_character",
            RiskLevel = "low",
            NeedsConfirm = false,
            Capabilities = ["character-switch"]
        }
    ];

    public IReadOnlyList<CommandDescriptor> GetCommands() => Commands;
}
