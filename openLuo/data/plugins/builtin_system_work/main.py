import sys
import json

PLUGIN_ID = "builtin_system_work"
_req_id = 0


def call_host(method, params=None):
    global _req_id
    _req_id += 1
    req = {
        "jsonrpc": "2.0",
        "id": f"p{_req_id}",
        "method": method,
        "params": params or {},
    }
    print(json.dumps(req, ensure_ascii=False), flush=True)
    line = sys.stdin.readline()
    return json.loads(line).get("result", {})


def log(level, msg):
    call_host("game/log", {"pluginId": PLUGIN_ID, "level": level, "msg": msg})


WORK_TYPES = {
    "part-time": {"gold": 75, "stamina": 20, "minutes": 240, "desc": "兼职打工"},
    "overtime": {"gold": 150, "stamina": 35, "minutes": 480, "desc": "加班工作"},
}

REST_MINUTES = 30
REST_STAMINA = 10

NAP_MINUTES = 90
NAP_STAMINA = 25


def handle_work(args):
    work_type = args[0] if args else "part-time"
    work = WORK_TYPES.get(work_type)
    if not work:
        return {
            "success": False,
            "error": f"未知工作类型：{work_type}。可选：part-time、overtime",
        }

    state = call_host("game/session/get")
    if not state:
        return {"success": False, "error": "游戏未初始化"}

    stamina_res = call_host(
        "game/state/get",
        {
            "namespace": "game_resource",
            "key": "stamina",
            "ownerKind": "game",
            "ownerId": "global",
        },
    )
    stamina = (
        int(float((stamina_res or {}).get("value", 0)))
        if (stamina_res and stamina_res.get("ok"))
        else 0
    )
    max_stamina = int(float((stamina_res or {}).get("max", 100) or 100))

    if stamina < work["stamina"]:
        return {
            "success": False,
            "error": f"体力不足（需要 {work['stamina']}，当前 {stamina}）。先去睡一觉吧。",
        }

    call_host(
        "game/state/apply",
        {
            "mutations": [
                {
                    "namespace": "game_resource",
                    "key": "gold",
                    "ownerKind": "game",
                    "ownerId": "global",
                    "op": "delta",
                    "value": str(work["gold"]),
                    "sourceType": "plugin",
                    "sourceId": PLUGIN_ID,
                },
                {
                    "namespace": "game_resource",
                    "key": "stamina",
                    "ownerKind": "game",
                    "ownerId": "global",
                    "op": "delta",
                    "value": str(-work["stamina"]),
                    "sourceType": "plugin",
                    "sourceId": PLUGIN_ID,
                },
            ]
        },
    )

    time_result = call_host("game/time/advance", {"minutes": work["minutes"]})
    new_time = time_result.get("timeStr", "??:??") if time_result else "??:??"
    new_day = (
        time_result.get("day", state["currentDay"])
        if time_result
        else state["currentDay"]
    )

    gold_res = call_host(
        "game/state/get",
        {
            "namespace": "game_resource",
            "key": "gold",
            "ownerKind": "game",
            "ownerId": "global",
        },
    )
    new_gold = (
        int(float((gold_res or {}).get("value", 0)))
        if (gold_res and gold_res.get("ok"))
        else 0
    )
    new_stamina = stamina - work["stamina"]

    hours = work["minutes"] // 60
    mins = work["minutes"] % 60
    time_desc = f"{hours}小时" + (f"{mins}分钟" if mins else "")
    log(
        "info",
        f"work type={work_type} gold=+{work['gold']} stamina=-{work['stamina']} time=+{work['minutes']}min",
    )
    return {
        "success": True,
        "output": (
            f"完成了{work['desc']}（耗时 {time_desc}）。\n"
            f"获得 {work['gold']} G，消耗 {work['stamina']} 体力。\n"
            f"当前：{new_gold} G，体力 {new_stamina}/{max_stamina}\n"
            f"现在是第 {new_day} 天 {new_time}"
        ),
    }


def handle_rest(args):
    return _do_rest(REST_MINUTES, REST_STAMINA, "休息了一小会儿")


def handle_nap(args):
    return _do_rest(NAP_MINUTES, NAP_STAMINA, "小睡了一觉")


def _do_rest(minutes, recover, verb):
    stamina_res = call_host(
        "game/state/get",
        {
            "namespace": "game_resource",
            "key": "stamina",
            "ownerKind": "game",
            "ownerId": "global",
        },
    )
    stamina = (
        int(float((stamina_res or {}).get("value", 0)))
        if (stamina_res and stamina_res.get("ok"))
        else 0
    )
    max_stamina = int(float((stamina_res or {}).get("max", 100) or 100))

    time_result = call_host("game/time/advance", {"minutes": minutes})
    new_time = time_result.get("timeStr", "??:??") if time_result else "??:??"
    new_day = time_result.get("day", 1) if time_result else 1

    restored = min(recover, max_stamina - stamina)
    if restored > 0:
        call_host(
            "game/state/apply",
            {
                "mutations": [
                    {
                        "namespace": "game_resource",
                        "key": "stamina",
                        "ownerKind": "game",
                        "ownerId": "global",
                        "op": "delta",
                        "value": str(restored),
                        "sourceType": "plugin",
                        "sourceId": PLUGIN_ID,
                    },
                ]
            },
        )

    new_stamina = stamina + restored
    hours = minutes // 60
    mins = minutes % 60
    time_desc = (f"{hours}小时" if hours else "") + (f"{mins}分钟" if mins else "")
    log(
        "info",
        f"rest verb={verb} restored={restored} stamina={new_stamina}/{max_stamina} time=+{minutes}min",
    )
    return {
        "success": True,
        "output": (
            f"{verb}（{time_desc}）。\n"
            f"恢复了 {restored} 体力（{new_stamina}/{max_stamina}）。\n"
            f"现在是第 {new_day} 天 {new_time}"
        ),
    }


TOOLS = {
    "work": handle_work,
    "rest": handle_rest,
    "nap": handle_nap,
}

TOOLS_LIST = [
    {
        "name": "work",
        "description": "打工赚取金币（part-time / overtime）",
        "category": "command",
        "aliases": [],
        "helpShort": "打工赚钱，例：/work part-time",
        "inputSchema": {"type": "object", "properties": {"args": {"type": "array"}}},
    },
    {
        "name": "rest",
        "description": "小休息：消耗 30 分钟，恢复 10 点体力（随时可用）",
        "category": "command",
        "aliases": [],
        "helpShort": "稍作休息，小幅恢复体力",
        "inputSchema": {"type": "object", "properties": {"args": {"type": "array"}}},
    },
    {
        "name": "nap",
        "description": "小睡：消耗 90 分钟，恢复 25 点体力（随时可用）",
        "category": "command",
        "aliases": [],
        "helpShort": "小睡一觉，较多恢复体力",
        "inputSchema": {"type": "object", "properties": {"args": {"type": "array"}}},
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
                "serverInfo": {"name": "builtin_system_work", "version": "1.0.0"},
            },
        }

    if method == "tools/list":
        return {"jsonrpc": "2.0", "id": req_id, "result": {"tools": TOOLS_LIST}}

    if method == "tools/call":
        name = params.get("name")
        arguments = params.get("arguments", {})
        args = arguments.get("args", [])
        handler = TOOLS.get(name)
        if handler is None:
            return {
                "jsonrpc": "2.0",
                "id": req_id,
                "error": {"code": -32601, "message": f"Unknown tool: {name}"},
            }
        result = handler(args)
        return {
            "jsonrpc": "2.0",
            "id": req_id,
            "result": {
                "content": [
                    {"type": "text", "text": json.dumps(result, ensure_ascii=False)}
                ]
            },
        }

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
