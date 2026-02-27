using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace openLuo.Integration.Tests;

public class BuiltinTimePluginBehaviorTests
{
    [Fact]
    public async Task BuiltinSystemCommands_DisabledMode_ClearsTimelineWithPagination()
    {
        if (!PythonAvailable()) return;

        var pluginPath = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "openLuo", "data", "plugins", "builtin_system_commands", "main.py");

        var script = """
            import importlib.util, json

            plugin_path = __PLUGIN_PATH__
            spec = importlib.util.spec_from_file_location("builtin_system_commands_main", plugin_path)
            mod = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(mod)

            pending = [{"id": f"p{i}"} for i in range(205)]
            active = [{"id": f"a{i}"} for i in range(3)]
            cancelled = []

            def fake_call_host(method, params=None):
                params = params or {}
                if method == "game/time/get":
                    return {"mode": "virtual", "day": 1, "timeStr": "08:00"}
                if method == "game/state/apply":
                    muts = (params or {}).get("mutations", [])
                    return {"ok": True, "results": [{"ok": True} for _ in muts]}
                if method == "game/timeline/query":
                    status = params.get("status")
                    limit = int(params.get("limit", 200))
                    source = pending if status == "pending" else active
                    return {"ok": True, "items": [dict(x) for x in source[:limit]]}
                if method == "game/timeline/cancel":
                    event_id = params.get("eventId")
                    for source in (pending, active):
                        for idx, item in enumerate(source):
                            if item.get("id") == event_id:
                                source.pop(idx)
                                cancelled.append(event_id)
                                return {"ok": True}
                    return {"ok": False}
                return {}

            mod.call_host = fake_call_host
            result = mod.handle_time_mode(["disabled"])
            print(json.dumps({
                "result": result,
                "remainingPending": len(pending),
                "remainingActive": len(active),
                "cancelled": len(cancelled)
            }, ensure_ascii=False))
            """
            .Replace("__PLUGIN_PATH__", JsonSerializer.Serialize(Path.GetFullPath(pluginPath)));

        var output = await RunPythonJsonAsync(script);
        Assert.True(output["result"]!["success"]!.GetValue<bool>());
        Assert.Equal(0, output["remainingPending"]!.GetValue<int>());
        Assert.Equal(0, output["remainingActive"]!.GetValue<int>());
        Assert.Equal(208, output["cancelled"]!.GetValue<int>());
    }

    [Fact]
    public async Task BuiltinEventDate_PlanDate_RealtimeUsesKernelTimezone()
    {
        if (!PythonAvailable()) return;

        var pluginPath = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "openLuo", "data", "plugins", "builtin_event_date", "main.py");

        var now = new DateTimeOffset(2026, 3, 1, 15, 0, 0, TimeSpan.Zero);
        var nowEpochMs = now.ToUnixTimeMilliseconds();
        var expectedDueEpochMs = new DateTimeOffset(2026, 3, 2, 10, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

        var script = """
            import importlib.util, json

            plugin_path = __PLUGIN_PATH__
            spec = importlib.util.spec_from_file_location("builtin_event_date_main", plugin_path)
            mod = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(mod)

            captured = {}
            epoch_ms = __NOW_EPOCH_MS__

            def fake_call_host(method, params=None):
                params = params or {}
                if method == "game/time/get":
                    return {"mode": "realtime", "day": 5, "epochMs": epoch_ms}
                if method == "game/state/get":
                    if params.get("namespace") == "system_time" and params.get("key") == "timezone":
                        return {"ok": True, "value": "utc"}
                    return {"ok": False}
                if method == "game/timeline/create":
                    captured["dueAtEpochMs"] = params.get("dueAtEpochMs")
                    captured["contextTimezone"] = ((params.get("context") or {}).get("timezone"))
                    return {"ok": True, "item": {"id": "e1"}}
                return {}

            mod.call_host = fake_call_host
            result = mod.handle_plan_date(["+1", "10:00", "去看电影"])
            print(json.dumps({
                "result": result,
                "dueAtEpochMs": captured.get("dueAtEpochMs"),
                "contextTimezone": captured.get("contextTimezone")
            }, ensure_ascii=False))
            """
            .Replace("__PLUGIN_PATH__", JsonSerializer.Serialize(Path.GetFullPath(pluginPath)))
            .Replace("__NOW_EPOCH_MS__", nowEpochMs.ToString());

        var output = await RunPythonJsonAsync(script);
        Assert.True(output["result"]!["success"]!.GetValue<bool>());
        Assert.Equal(expectedDueEpochMs, output["dueAtEpochMs"]!.GetValue<long>());
        Assert.Equal("utc", output["contextTimezone"]!.GetValue<string>());
    }

    [Fact(Skip="handle_chat removed from plugin")]
    public async Task BuiltinSystemCommands_ChatRejectsTooLongInput()
    {
        if (!PythonAvailable()) return;

        var pluginPath = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "openLuo", "data", "plugins", "builtin_system_commands", "main.py");

        var script = """
            import importlib.util, json

            plugin_path = __PLUGIN_PATH__
            spec = importlib.util.spec_from_file_location("builtin_system_commands_main", plugin_path)
            mod = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(mod)

            mod.call_host = lambda method, params=None: {}
            result = mod.handle_chat(["x" * 401])
            print(json.dumps({"result": result}, ensure_ascii=False))
            """
            .Replace("__PLUGIN_PATH__", JsonSerializer.Serialize(Path.GetFullPath(pluginPath)));

        var output = await RunPythonJsonAsync(script);
        Assert.False(output["result"]!["success"]!.GetValue<bool>());
        Assert.Contains("过长", output["result"]!["error"]!.GetValue<string>());
    }

    [Fact]
    public async Task BuiltinSystemCommands_DebugSetsAffectionWithoutLegacyLlmEndpoint()
    {
        if (!PythonAvailable()) return;

        var pluginPath = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "openLuo", "data", "plugins", "builtin_system_commands", "main.py");

        var script = """
            import importlib.util, json

            plugin_path = __PLUGIN_PATH__
            spec = importlib.util.spec_from_file_location("builtin_system_commands_main", plugin_path)
            mod = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(mod)

            calls = []

            def fake_call_host(method, params=None):
                params = params or {}
                calls.append({"method": method, "params": params})
                if method == "game/session/get":
                    return {"currentDay": 1}
                if method == "game/character/get":
                    return {"id": "char_builtin-nekomimi", "name": "洛"}
                if method == "game/state/query":
                    if params.get("namespace") == "char_status":
                        return {
                            "ok": True,
                            "items": [
                                {
                                    "key": "affection",
                                    "value": "0",
                                    "ownerKind": "character",
                                    "ownerId": "char_builtin-nekomimi"
                                }
                            ]
                        }
                    return {"ok": True, "items": []}
                if method == "game/state/apply":
                    mutation = params["mutations"][0]
                    return {
                        "ok": True,
                        "results": [
                            {
                                "ok": True,
                                "oldValue": "0",
                                "newValue": mutation["value"]
                            }
                        ]
                    }
                if method == "game/llm/complete":
                    raise AssertionError("debug must not call legacy LLM endpoint")
                return {}

            mod.call_host = fake_call_host
            result = mod.handle_debug(["设置好感度为", "70"])
            apply_calls = [x for x in calls if x["method"] == "game/state/apply"]
            print(json.dumps({
                "result": result,
                "applyCalls": apply_calls
            }, ensure_ascii=False))
            """
            .Replace("__PLUGIN_PATH__", JsonSerializer.Serialize(Path.GetFullPath(pluginPath)));

        var output = await RunPythonJsonAsync(script);
        Assert.True(output["result"]!["success"]!.GetValue<bool>());
        var mutation = output["applyCalls"]![0]!["params"]!["mutations"]![0]!;
        Assert.Equal("char_status", mutation["namespace"]!.GetValue<string>());
        Assert.Equal("affection", mutation["key"]!.GetValue<string>());
        Assert.Equal("character", mutation["ownerKind"]!.GetValue<string>());
        Assert.Equal("char_builtin-nekomimi", mutation["ownerId"]!.GetValue<string>());
        Assert.Equal("set", mutation["op"]!.GetValue<string>());
        Assert.Equal("70", mutation["value"]!.GetValue<string>());
    }

    [Fact]
    public async Task BuiltinEventDate_PlanDateRejectsFarFutureOffset()
    {
        if (!PythonAvailable()) return;

        var pluginPath = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "openLuo", "data", "plugins", "builtin_event_date", "main.py");

        var script = """
            import importlib.util, json

            plugin_path = __PLUGIN_PATH__
            spec = importlib.util.spec_from_file_location("builtin_event_date_main", plugin_path)
            mod = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(mod)

            def fake_call_host(method, params=None):
                params = params or {}
                if method == "game/time/get":
                    return {"mode": "realtime", "day": 1, "epochMs": 1700000000000}
                if method == "game/state/get":
                    if params.get("namespace") == "system_time" and params.get("key") == "timezone":
                        return {"ok": True, "value": "utc"}
                return {}

            mod.call_host = fake_call_host
            result = mod.handle_plan_date(["+999", "10:00", "远期计划"])
            print(json.dumps({"result": result}, ensure_ascii=False))
            """
            .Replace("__PLUGIN_PATH__", JsonSerializer.Serialize(Path.GetFullPath(pluginPath)));

        var output = await RunPythonJsonAsync(script);
        Assert.False(output["result"]!["success"]!.GetValue<bool>());
        Assert.Contains("最多支持未来", output["result"]!["error"]!.GetValue<string>());
    }

    [Fact]
    public async Task BuiltinEventDate_PlanDateRejectsInDisabledMode()
    {
        if (!PythonAvailable()) return;

        var pluginPath = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "openLuo", "data", "plugins", "builtin_event_date", "main.py");

        var script = """
            import importlib.util, json

            plugin_path = __PLUGIN_PATH__
            spec = importlib.util.spec_from_file_location("builtin_event_date_main", plugin_path)
            mod = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(mod)

            called_create = {"value": False}

            def fake_call_host(method, params=None):
                if method == "game/time/get":
                    return {"mode": "disabled", "day": 1, "epochMs": 1700000000000}
                if method == "game/timeline/create":
                    called_create["value"] = True
                    return {"ok": True, "item": {"id": "e1"}}
                return {}

            mod.call_host = fake_call_host
            result = mod.handle_plan_date(["+1", "10:00", "去看电影"])
            print(json.dumps({"result": result, "calledCreate": called_create["value"]}, ensure_ascii=False))
            """
            .Replace("__PLUGIN_PATH__", JsonSerializer.Serialize(Path.GetFullPath(pluginPath)));

        var output = await RunPythonJsonAsync(script);
        Assert.False(output["result"]!["success"]!.GetValue<bool>());
        Assert.Contains("disabled 模式", output["result"]!["error"]!.GetValue<string>());
        Assert.False(output["calledCreate"]!.GetValue<bool>());
    }

    private static bool PythonAvailable()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "python",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            });
            p?.WaitForExit(3000);
            return p?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<JsonNode> RunPythonJsonAsync(string script)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"gimai_py_{Guid.NewGuid():N}.py");
        await File.WriteAllTextAsync(tempFile, script);
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "python",
                Arguments = tempFile,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }) ?? throw new InvalidOperationException("无法启动 python 进程");

            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
                throw new Xunit.Sdk.XunitException($"python 脚本执行失败 (code={process.ExitCode})\nSTDERR:\n{stderr}\nSTDOUT:\n{stdout}");

            var lastLine = stdout
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .LastOrDefault();
            if (string.IsNullOrWhiteSpace(lastLine))
                throw new Xunit.Sdk.XunitException($"python 未输出 JSON。\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");

            return JsonNode.Parse(lastLine)
                   ?? throw new Xunit.Sdk.XunitException($"无法解析 JSON 输出：{lastLine}");
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }
}
