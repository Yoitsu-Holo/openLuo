import sys
import json

PLUGIN_ID = "builtin_system_status"
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


def build_bar(value, max_val, width=20):
    filled = round(value / max_val * width) if max_val > 0 else 0
    filled = max(0, min(width, filled))
    return "[" + "#" * filled + "-" * (width - filled) + "]"


def to_int(value, default=0):
    try:
        return int(float(value))
    except (ValueError, TypeError):
        return default


def format_status_line(item):
    label = item.get("label") or item.get("id") or item.get("key") or "资源"
    value = item.get("value", "")
    max_value = item.get("max")
    item_type = item.get("type", "text")
    text = item.get("text") or ""

    if item_type == "bar" and max_value not in (None, ""):
        v = to_int(value, 0)
        max_v = max(1, to_int(max_value, 1))
        return f"{label}  {build_bar(v, max_v)}  {v}/{max_v}"

    if text and text != str(value):
        return f"{label}  {text}"

    return f"{label}  {value}"


def group_label(group):
    labels = {
        "status": "状态",
        "stat": "状态",
        "resources": "资源",
        "resource": "资源",
        "emotion": "情绪",
        "physical": "身体",
        "intimacy": "关系",
        "gallery": "画廊",
        "world": "世界",
        "system": "系统",
    }
    return labels.get(str(group or "").lower(), group or "状态")


def handle_status(args):
    try:
        state = call_host("game/session/get")
        if not state:
            return {"success": False, "error": "游戏未初始化"}

        status = call_host("game/resource/status", {})
        if not isinstance(status, dict) or not status.get("ok"):
            return {"success": False, "error": status.get("error", "status_query_failed")}

        char = status.get("character") or {}
        items = status.get("items") or []
        grouped = {}
        for item in items:
            group = item.get("group") or "status"
            grouped.setdefault(group, []).append(item)

        lines = [
            f"第 {state['currentDay']} 天  {state.get('timeStr', '??:??')}  |  {state['playerName']}",
            "",
        ]

        if char.get("name"):
            lines.append(f"  {char['name']}")
            lines.append("")

        for group, group_items in grouped.items():
            visible_items = [
                item for item in group_items
                if item.get("value") not in (None, "")
            ]
            if not visible_items:
                continue

            lines.append(f"  [{group_label(group)}]")
            for item in sorted(visible_items, key=lambda x: (x.get("order", 0), x.get("id", ""))):
                lines.append(f"  {format_status_line(item)}")
            lines.append("")

        additional_text = status.get("additionalText")
        if additional_text:
            lines.append(str(additional_text))

        output = "\n".join(lines).rstrip()
        log("debug", f"status output length: {len(output)}")
        return {"success": True, "output": output}
    except Exception as e:
        import traceback

        error_msg = f"status error: {str(e)}\n{traceback.format_exc()}"
        log("error", error_msg)
        return {"success": False, "error": error_msg}


TOOLS = {"status": handle_status}

TOOLS_LIST = [
    {
        "name": "status",
        "description": "显示当前状态面板（金币、体力、好感度、心情）",
        "category": "command",
        "aliases": [],
        "helpShort": "查看状态面板",
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
                "serverInfo": {"name": "builtin_system_status", "version": "1.0.0"},
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
