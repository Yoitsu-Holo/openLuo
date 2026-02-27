import sys
import json
PLUGIN_ID = "builtin_system_lifecycle"
_req_id = 0


def call_host(method, params=None):
    global _req_id
    _req_id += 1
    req = {
        "jsonrpc": "2.0",
        "id": f"c{_req_id}",
        "method": method,
        "params": params or {},
    }
    print(json.dumps(req, ensure_ascii=False), flush=True)
    line = sys.stdin.readline()
    return json.loads(line).get("result", {})


def log(level, msg):
    call_host("game/log", {"pluginId": PLUGIN_ID, "level": level, "msg": msg})


def handle_sleep(args):
    result = call_host("game/lifecycle/sleep")
    if not result:
        return {"success": False, "error": "睡眠指令执行失败"}
    if not result.get("ok"):
        return {"success": False, "error": result.get("error", "未知错误")}
    if result.get("refused"):
        return {
            "success": True,
            "output": result.get("message", "你并不觉得累，现在还不想睡。"),
        }
    return {"success": True, "output": result.get("message", "")}


def on_day_start(args):
    new_day = args.get("newDay", 0)
    last_day = args.get("lastInteractionDay", new_day)
    days_since = new_day - last_day

    if days_since < 2:
        return {"success": True}

    char_info = call_host("game/character/get", {})
    char_id = (char_info or {}).get("id")
    # Try new State API first
    state_res = call_host(
        "game/state/get",
        {
            "namespace": "char_status",
            "key": "affection",
            "ownerKind": "character",
            "ownerId": char_id,
        },
    )
    affection = (
        int(float(state_res.get("value", 0)))
        if (state_res and state_res.get("ok"))
        else 0
    )
    if affection < 300:
        return {"success": True}

    log("info", f"onDayStart proactive narrative skipped days_since={days_since}")
    return {"success": True}


TOOLS = {
    "sleep": handle_sleep,
    "onDayStart": on_day_start,
}

TOOLS_LIST = [
    {
        "name": "sleep",
        "description": "休息恢复体力，午夜前入睡睡眠更充足",
        "category": "command",
        "aliases": [],
        "helpShort": "休息恢复体力，推进到次日",
        "inputSchema": {"type": "object", "properties": {"args": {"type": "array"}}},
    },
    {
        "name": "onDayStart",
        "description": "Day start hook - character proactively reaches out if player has been away",
        "category": "hook",
        "inputSchema": {
            "type": "object",
            "properties": {
                "characterId": {"type": "string"},
                "newDay": {"type": "integer"},
                "lastInteractionDay": {"type": "integer"},
            },
        },
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
                "serverInfo": {"name": PLUGIN_ID, "version": "1.0.0"},
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
