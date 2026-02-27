import sys
import json
import re
import os

PLUGIN_ID = "builtin_game_resource_core"
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


def load_json_file(filename):
    base_path = os.path.dirname(os.path.abspath(__file__))
    for ext in [".jsonc", ".json", ""]:
        path = os.path.join(
            base_path,
            (
                filename
                if ext == ""
                else filename.replace(".json", "").replace(".jsonc", "") + ext
            ),
        )
        if os.path.exists(path):
            try:
                with open(path, "r", encoding="utf-8") as f:
                    content = f.read()
                content = re.sub(r"//.*", "", content)
                return json.loads(content)
            except Exception:
                continue
    return []


RESOURCE_DEFS = load_json_file("state_defs")


def on_startup(args):
    for rd in RESOURCE_DEFS:
        call_host("game/state/register", {"def": rd})
    log("info", f"{len(RESOURCE_DEFS)} game resources registered")
    return {"success": True}


TOOLS = {"onStartup": on_startup}

TOOLS_LIST = [
    {
        "name": "onStartup",
        "description": "Register global game resources (stamina, gold)",
        "category": "hook",
        "inputSchema": {"type": "object", "properties": {"args": {"type": "array"}}},
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
