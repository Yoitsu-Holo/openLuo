using System.Reflection;
using openLuo.Protocol;
using Xunit;

namespace openLuo.E2E.Tests;

/// <summary>
/// HTTP 控制面的**结构性守卫**：路径常量必须落在 Protocol 的常量区（禁止散落字面量），
/// 且满足 REST 形状——`/v1/&lt;复数名词&gt;[/{id}][/子资源]`、**路径里不出现动词**、模板与具体常量分工明确。
/// 另有错误码 → HTTP 状态码映射的稳定性断言（客户端与文档依赖同一张表）。
/// </summary>
public sealed class HttpSurfaceTests
{
    private static readonly string[] Verbs =
        ["get", "post", "put", "patch", "delete", "create", "update", "remove", "cancel", "list", "query", "set", "fetch", "do"];

    private static string[] StringConsts(Type type) => type
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.IsLiteral && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToArray();

    private static (string Name, string Value)[] NamedStringConsts(Type type) => type
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.IsLiteral && f.FieldType == typeof(string))
        .Select(f => (f.Name, (string)f.GetRawConstantValue()!))
        .ToArray();

    [Fact]
    public void 路径常量_统一以v1前缀开头_且不含动词()
    {
        var problems = new List<string>();

        foreach (var (name, value) in NamedStringConsts(typeof(HttpPaths)))
        {
            if (!value.StartsWith(ProtocolInfo.HttpBasePath + "/", StringComparison.Ordinal))
                problems.Add($"HttpPaths.{name} = \"{value}\" 未以 {ProtocolInfo.HttpBasePath}/ 开头");

            // 去掉 {占位符} 后，路径段不得是 HTTP 动词（REST：动作由方法表达）
            var path = System.Text.RegularExpressions.Regex.Replace(value, @"\{[^}]+\}", "x");
            foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries).Skip(1))
            {
                if (Verbs.Contains(segment.ToLowerInvariant()))
                    problems.Add($"HttpPaths.{name} = \"{value}\" 的路径段 \"{segment}\" 是动词（REST 违规）");
            }
        }

        Assert.Empty(problems);
    }

    [Fact]
    public void 路径常量_模板与具体路径分工明确且唯一()
    {
        var problems = new List<string>();
        var concrete = new List<string>();

        foreach (var (name, value) in NamedStringConsts(typeof(HttpPaths)))
        {
            var isTemplate = name.EndsWith("Template", StringComparison.Ordinal) || value.Contains('{');
            if (isTemplate && !value.Contains('{'))
                problems.Add($"HttpPaths.{name} 名字像模板但值里没有占位符：\"{value}\"");
            if (!isTemplate)
                concrete.Add(value);
        }

        var duplicates = concrete.GroupBy(v => v, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key);
        foreach (var dup in duplicates)
            problems.Add($"具体路径重复定义：\"{dup}\"");

        Assert.Empty(problems);
    }

    [Fact]
    public void URL构造方法_与模板一致()
    {
        Assert.Equal($"{ProtocolInfo.HttpBasePath}/sessions/sess_1", HttpPaths.Session("sess_1"));
        Assert.Equal($"{ProtocolInfo.HttpBasePath}/sessions/sess_1/context", HttpPaths.SessionContext("sess_1"));
        Assert.Equal($"{ProtocolInfo.HttpBasePath}/sessions/sess_1/state", HttpPaths.SessionState("sess_1"));
        Assert.Equal($"{ProtocolInfo.HttpBasePath}/sessions/sess_1/assets", HttpPaths.SessionAssets("sess_1"));
        Assert.Equal($"{ProtocolInfo.HttpBasePath}/assets/ast_1", HttpPaths.Asset("ast_1"));
        Assert.Equal($"{ProtocolInfo.HttpBasePath}/config/log", HttpPaths.ConfigNamespace("log"));
        Assert.Equal($"{ProtocolInfo.HttpBasePath}/jobs/job_1", HttpPaths.Job("job_1"));
        Assert.Equal($"{ProtocolInfo.HttpBasePath}/schedules/sch_1", HttpPaths.Schedule("sch_1"));
        Assert.Equal($"{ProtocolInfo.HttpBasePath}/traces/turn_1", HttpPaths.Trace("turn_1"));

        // 路径段必须转义（挡注入）：id 部分不得再含未转义的 '/'
        Assert.DoesNotContain("/", HttpPaths.Asset("a/b")[(HttpPaths.Assets.Length + 1)..]);
    }

    [Fact]
    public void 查询键与响应类型_都是非空常量集()
    {
        Assert.All(StringConsts(typeof(HttpQueryKeys)), v => Assert.False(string.IsNullOrWhiteSpace(v)));
        Assert.All(StringConsts(typeof(HttpResponseTypes)), v => Assert.False(string.IsNullOrWhiteSpace(v)));

        // 响应类型里出现过的关键资源（客户端按 type 分发时依赖）
        var types = StringConsts(typeof(HttpResponseTypes));
        foreach (var required in new[] { "token", "asset", "job", "schedule", "schedules", "logs", "config", "state" })
            Assert.Contains(required, types);
    }

    [Fact]
    public void 错误码到HTTP状态码_映射稳定()
    {
        Assert.Equal(401, ErrorCodes.HttpStatusOf(ErrorCodes.AuthUnauthorized));
        Assert.Equal(403, ErrorCodes.HttpStatusOf(ErrorCodes.AuthForbidden));
        Assert.Equal(403, ErrorCodes.HttpStatusOf(ErrorCodes.ConfigReadOnly));

        foreach (var notFound in new[]
                 {
                     ErrorCodes.SessionNotFound, ErrorCodes.AssetNotFound, ErrorCodes.JobNotFound,
                     ErrorCodes.ScheduleNotFound, ErrorCodes.ConfigNamespaceNotFound,
                     ErrorCodes.DeviceNotFound, ErrorCodes.TraceNotFound,
                 })
            Assert.Equal(404, ErrorCodes.HttpStatusOf(notFound));

        Assert.Equal(413, ErrorCodes.HttpStatusOf(ErrorCodes.AssetTooLarge));
        Assert.Equal(429, ErrorCodes.HttpStatusOf(ErrorCodes.RateLimited));
        Assert.Equal(429, ErrorCodes.HttpStatusOf(ErrorCodes.SessionLimitExceeded));
        Assert.Equal(409, ErrorCodes.HttpStatusOf(ErrorCodes.TurnBusy));
        Assert.Equal(409, ErrorCodes.HttpStatusOf(ErrorCodes.TurnCancelled));
        Assert.Equal(409, ErrorCodes.HttpStatusOf(ErrorCodes.CapabilityConfirmationRequired));
        Assert.Equal(503, ErrorCodes.HttpStatusOf(ErrorCodes.PresenceUnavailable));
        Assert.Equal(500, ErrorCodes.HttpStatusOf(ErrorCodes.JobFailed));
        Assert.Equal(500, ErrorCodes.HttpStatusOf(ErrorCodes.ConfigPersistFailed));
        Assert.Equal(500, ErrorCodes.HttpStatusOf(ErrorCodes.ServerInternal));

        // 未分类 → 400（含入口治理新码）
        Assert.Equal(400, ErrorCodes.HttpStatusOf(ErrorCodes.SessionInvalid));
        Assert.Equal(400, ErrorCodes.HttpStatusOf(ErrorCodes.TurnInvalid));
        Assert.Equal(400, ErrorCodes.HttpStatusOf(ErrorCodes.Unknown));
    }
}
