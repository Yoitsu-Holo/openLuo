import sys
import json
import random

PLUGIN_ID = "builtin_world_state_core"
_req_id = 0

# ─── Weather Markov chain ─────────────────────────────────────────────────────
# Transition probabilities: current -> {next: weight}
WEATHER_TRANSITIONS = {
    "Sunny": {"Sunny": 60, "Cloudy": 30, "Rainy": 10},
    "Cloudy": {"Sunny": 25, "Cloudy": 40, "Rainy": 30, "Snowy": 5},
    "Rainy": {"Sunny": 10, "Cloudy": 35, "Rainy": 45, "Snowy": 10},
    "Snowy": {"Cloudy": 30, "Rainy": 20, "Snowy": 50},
}
WEATHER_LABELS = {"Sunny": "晴天", "Cloudy": "多云", "Rainy": "下雨", "Snowy": "下雪"}
SEASON_WEATHER_BIAS = {
    "Spring": {"Sunny": 1.2, "Rainy": 1.1},
    "Summer": {"Sunny": 1.4, "Rainy": 0.8},
    "Autumn": {"Cloudy": 1.2, "Rainy": 1.1},
    "Winter": {"Snowy": 2.0, "Cloudy": 1.2, "Sunny": 0.6},
}
SEASONS = ["Spring", "Summer", "Autumn", "Winter"]
SEASON_LABELS = {"Spring": "春天", "Summer": "夏天", "Autumn": "秋天", "Winter": "冬天"}

TIME_OF_DAY_LABELS = {
    "Morning": "早晨",
    "Afternoon": "下午",
    "Evening": "傍晚",
    "Night": "夜晚",
}


def call_host(method, params=None):
    global _req_id
    _req_id += 1
    req = {
        "jsonrpc": "2.0",
        "id": f"w{_req_id}",
        "method": method,
        "params": params or {},
    }
    print(json.dumps(req, ensure_ascii=False), flush=True)
    line = sys.stdin.readline()
    return json.loads(line).get("result", {})


def log(level, msg):
    call_host("game/log", {"pluginId": PLUGIN_ID, "level": level, "msg": msg})


def _weighted_choice(weights: dict) -> str:
    keys = list(weights.keys())
    vals = [weights[k] for k in keys]
    return random.choices(keys, weights=vals, k=1)[0]


def _next_weather(current: str, season: str) -> str:
    base = dict(WEATHER_TRANSITIONS.get(current, WEATHER_TRANSITIONS["Sunny"]))
    bias = SEASON_WEATHER_BIAS.get(season, {})
    for k in base:
        base[k] = base[k] * bias.get(k, 1.0)
    return _weighted_choice(base)


def _get_season(day: int) -> str:
    return SEASONS[((day - 1) // 30) % 4]


def _minute_to_time_of_day(minute: int) -> str:
    if minute < 360:
        return "Night"
    if minute < 720:
        return "Morning"
    if minute < 1020:
        return "Afternoon"
    if minute < 1260:
        return "Evening"
    return "Night"


# ─── Hook handlers ────────────────────────────────────────────────────────────


def on_startup(args):
    for res_id, name, values, default in [
        ("world_weather", "天气", ["Sunny", "Cloudy", "Rainy", "Snowy"], "Sunny"),
        ("world_season", "季节", SEASONS, "Spring"),
        (
            "world_time_of_day",
            "时间段",
            ["Morning", "Afternoon", "Evening", "Night"],
            "Morning",
        ),
    ]:
        call_host(
            "game/state/register",
            {
                "def": {
                    "namespace": "world_state",
                    "key": res_id,
                    "ownerKind": "game",
                    "valueType": "enum",
                    "enumValues": values,
                    "defaultValue": default,
                    "pluginId": PLUGIN_ID,
                    "metadata": {"name": name, "category": "world", "displayIn": []},
                }
            },
        )
    log("info", "world resources registered")
    return {"success": True}


def on_sleep_after(args):
    """Advance season and generate next-day weather via Markov chain."""
    new_day = args.get("newDay", 1)
    season = _get_season(new_day)

    # Get current weather
    weather_res = call_host(
        "game/state/get",
        {
            "namespace": "world_state",
            "key": "world_weather",
            "ownerKind": "game",
            "ownerId": "global",
        },
    )
    current_weather = (
        (weather_res or {}).get("value", "Sunny")
        if (weather_res and weather_res.get("ok"))
        else "Sunny"
    )

    next_weather = _next_weather(current_weather, season)

    call_host(
        "game/state/apply",
        {
            "mutations": [
                {
                    "namespace": "world_state",
                    "key": "world_weather",
                    "ownerKind": "game",
                    "ownerId": "global",
                    "op": "set",
                    "value": next_weather,
                    "sourceType": "plugin",
                    "sourceId": PLUGIN_ID,
                },
                {
                    "namespace": "world_state",
                    "key": "world_season",
                    "ownerKind": "game",
                    "ownerId": "global",
                    "op": "set",
                    "value": season,
                    "sourceType": "plugin",
                    "sourceId": PLUGIN_ID,
                },
            ]
        },
    )

    log(
        "info",
        f"world updated day={new_day} season={season} weather={current_weather}->{next_weather}",
    )
    return {"success": True}


def on_chat_before(args):
    """Inject world state into beat system context."""
    context = args.get("context", args)
    weather_res = call_host(
        "game/state/get",
        {
            "namespace": "world_state",
            "key": "world_weather",
            "ownerKind": "game",
            "ownerId": "global",
        },
    )
    season_res = call_host(
        "game/state/get",
        {
            "namespace": "world_state",
            "key": "world_season",
            "ownerKind": "game",
            "ownerId": "global",
        },
    )
    time_res = call_host("game/time/get")

    weather = (
        (weather_res or {}).get("value", "Sunny")
        if (weather_res and weather_res.get("ok"))
        else "Sunny"
    )
    season = (
        (season_res or {}).get("value", "Spring")
        if (season_res and season_res.get("ok"))
        else "Spring"
    )
    mode = (time_res or {}).get("mode", "virtual")
    if mode == "disabled":
        return {"systemPrompt": context.get("systemPrompt", "")}

    minute = (time_res or {}).get("minute", 480)
    tod = _minute_to_time_of_day(minute)

    w_label = WEATHER_LABELS.get(weather, weather)
    s_label = SEASON_LABELS.get(season, season)
    tod_label = TIME_OF_DAY_LABELS.get(tod, tod)

    ctx = f"【当前环境】{s_label} · {tod_label} · {w_label}"
    return {"systemPrompt": context.get("systemPrompt", "") + f"\n{ctx}"}


def on_prompt_context(args):
    """Inject time semantics for chat-evaluate phase."""
    phase = args.get("phase", "chat-evaluate")
    if phase != "chat-evaluate":
        return {"promptFragments": []}

    time_res = call_host("game/time/get") or {}
    mode = (time_res.get("mode", "virtual") or "virtual").lower()
    if mode == "disabled":
        return {"promptFragments": []}

    day = time_res.get("day", 1)
    time_str = time_res.get("timeStr", "00:00")
    text = (
        f"时间语义：当前第{day}天 {time_str}（mode={mode}）。请让行为与时段保持一致。"
    )
    return {"promptFragments": [{"phase": phase, "priority": 35, "text": text}]}


# ─── MCP Protocol ─────────────────────────────────────────────────────────────

TOOLS = {
    "onStartup": on_startup,
    "onSleepAfter": on_sleep_after,
    "onChatBefore": on_chat_before,
    "onPromptContext": on_prompt_context,
}

TOOLS_LIST = [
    {
        "name": "onStartup",
        "category": "hook",
        "description": "Register world resources",
        "inputSchema": {"type": "object", "properties": {"args": {"type": "object"}}},
    },
    {
        "name": "onSleepAfter",
        "category": "hook",
        "description": "Advance weather/season after sleep",
        "inputSchema": {"type": "object", "properties": {"args": {"type": "object"}}},
    },
    {
        "name": "onChatBefore",
        "category": "hook",
        "description": "Inject world context into beat prompt",
        "inputSchema": {"type": "object", "properties": {"args": {"type": "object"}}},
    },
    {
        "name": "onPromptContext",
        "category": "hook",
        "description": "Inject time semantics for evaluate phase",
        "inputSchema": {"type": "object", "properties": {"args": {"type": "object"}}},
    },
]


def dispatch(request):
    method = request.get("method")
    req_id = request.get("id")
    params = request.get("params", {})

    if method == "initialize":
        return {
            "jsonrpc": "2.0",
            "id": req_id,
            "result": {
                "protocolVersion": "2024-11-05",
                "capabilities": {"tools": {}},
                "serverInfo": {"name": "builtin_world_state_core", "version": "1.0.0"},
            },
        }
    if method == "tools/list":
        return {"jsonrpc": "2.0", "id": req_id, "result": {"tools": TOOLS_LIST}}
    if method == "tools/call":
        name = params.get("name")
        arguments = params.get("arguments", {})
        handler = TOOLS.get(name)
        if handler is None:
            return {
                "jsonrpc": "2.0",
                "id": req_id,
                "error": {"code": -32601, "message": f"Unknown tool: {name}"},
            }
        result = handler(arguments)
        return {
            "jsonrpc": "2.0",
            "id": req_id,
            "result": {
                "content": [
                    {"type": "text", "text": json.dumps(result, ensure_ascii=False)}
                ]
            },
        }
    if method == "hooks/call":
        hook_name = params.get("hookName")
        args = params.get("args", {})
        handler = TOOLS.get(hook_name)
        if handler is None:
            return {"jsonrpc": "2.0", "id": req_id, "result": {}}
        return {"jsonrpc": "2.0", "id": req_id, "result": handler(args)}
    return {
        "jsonrpc": "2.0",
        "id": req_id,
        "error": {"code": -32601, "message": f"Unknown method: {method}"},
    }


def main():
    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        try:
            request = json.loads(line)
            response = dispatch(request)
            print(json.dumps(response, ensure_ascii=False), flush=True)
        except Exception as e:
            print(
                json.dumps(
                    {
                        "jsonrpc": "2.0",
                        "id": None,
                        "error": {"code": -32603, "message": str(e)},
                    }
                ),
                flush=True,
            )


if __name__ == "__main__":
    main()
