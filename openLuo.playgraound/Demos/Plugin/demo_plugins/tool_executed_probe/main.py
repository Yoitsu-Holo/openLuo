import json
import sys


def on_tool_executed(args):
    tool_name = args.get("toolName", "")
    success = args.get("success", False)
    asset_ids = args.get("assetIds", []) or []
    game_id = args.get("gameId", "")
    bridge_context = args.get("bridgeContext", {}) or {}

    return {
        "additionalText": f"[demo_tool_executed_probe] tool={tool_name} success={str(success).lower()} gameId={game_id}",
        "notices": [
            f"assetCount={len(asset_ids)}",
            f"actorId={bridge_context.get('actorId', '') or '<none>'}",
        ],
    }


TOOLS = {
    "onToolExecuted": on_tool_executed,
}

TOOLS_LIST = [
    {
        "name": "onToolExecuted",
        "category": "hook",
        "description": "Observe tool execution result",
        "inputSchema": {"type": "object", "properties": {"args": {"type": "object"}}},
    }
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
                "serverInfo": {"name": "demo_tool_executed_probe", "version": "1.0.0"},
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
            "result": {"content": [{"type": "text", "text": json.dumps(result, ensure_ascii=False)}]},
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
        request = json.loads(line)
        response = dispatch(request)
        sys.stdout.write(json.dumps(response, ensure_ascii=False) + "\n")
        sys.stdout.flush()


if __name__ == "__main__":
    main()
