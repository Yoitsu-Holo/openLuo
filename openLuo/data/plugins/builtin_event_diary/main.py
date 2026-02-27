import sys
import json

PLUGIN_ID = "builtin_event_diary"
_req_id = 0


def call_host(method, params=None):
    global _req_id
    _req_id += 1
    req = {
        "jsonrpc": "2.0",
        "id": f"d{_req_id}",
        "method": method,
        "params": params or {},
    }
    print(json.dumps(req, ensure_ascii=False), flush=True)
    line = sys.stdin.readline()
    return json.loads(line).get("result", {})


def log(level, msg):
    call_host("game/log", {"pluginId": PLUGIN_ID, "level": level, "msg": msg})


# ─── Hook handlers ────────────────────────────────────────────────────────────


def on_sleep_after(args):
    """Sleep diary generation was removed with the legacy narrative/memory API."""
    old_day = args.get("oldDay", 1) if isinstance(args, dict) else 1
    log("info", f"on_sleep_after: skipped legacy diary generation day={old_day}")
    return {}


# ─── Command handlers ─────────────────────────────────────────────────────────


def handle_diary(args):
    """Display diary entries, optionally filtered by page."""
    page = 1
    if args:
        try:
            page = int(args[0])
        except ValueError:
            pass

    limit = 5
    offset = (page - 1) * limit
    result = call_host("game/diary/list", {"limit": limit, "offset": offset})
    if not result:
        return {"success": True, "output": "还没有任何日记。"}

    if not isinstance(result, list) or len(result) == 0:
        return {
            "success": True,
            "output": "还没有任何日记。" if page == 1 else "没有更多日记了。",
        }

    lines = [f"【日记】第 {page} 页"]
    for entry in result:
        day = entry.get("day", "?")
        content = entry.get("content", "")
        lines.append(f"\n— 第 {day} 天 —\n{content}")

    if len(result) == limit:
        lines.append(f"\n（输入 /diary {page + 1} 查看下一页）")

    return {"success": True, "output": "\n".join(lines)}


# ─── Tools registration ───────────────────────────────────────────────────────

TOOLS = {
    "onSleepAfter": on_sleep_after,
    "diary": handle_diary,
}

TOOLS_LIST = [
    {
        "name": "onSleepAfter",
        "description": "onSleepAfter hook - generates character diary entry after sleep",
        "category": "hook",
        "inputSchema": {"type": "object", "properties": {"args": {"type": "array"}}},
    },
    {
        "name": "diary",
        "description": "查看角色日记",
        "category": "command",
        "aliases": [],
        "helpShort": "查看角色日记，例：/diary 或 /diary 2",
        "inputSchema": {"type": "object", "properties": {"args": {"type": "array"}}},
    },
]


# ─── MCP Protocol dispatcher ──────────────────────────────────────────────────


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
                "serverInfo": {"name": "builtin_event_diary", "version": "1.0.0"},
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
