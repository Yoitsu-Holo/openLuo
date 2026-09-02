import json
import sys

PLUGIN_ID = "demo_capability_tool"

# 1x1 红色 PNG
_IMAGE_B64 = (
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8"
    "z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="
)


def handle_demo_generate_image(args):
    return {
        "content": [
            {"type": "text", "text": "demo image generated"},
            {"type": "image", "data": _IMAGE_B64, "mimeType": "image/png"},
        ]
    }


TOOLS = {
    "demo_generate_image": handle_demo_generate_image,
}

TOOLS_LIST = [
    {
        "name": "demo_generate_image",
        "category": "tool",
        "description": "Generate a demo image",
        "inputSchema": {"type": "object", "properties": {"args": {"type": "array", "items": {"type": "string"}}}},
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
        handler = TOOLS.get(name)
        if handler is None:
            return {
                "jsonrpc": "2.0",
                "id": req_id,
                "error": {"code": -32601, "message": f"Unknown tool: {name}"},
            }
        result = handler(arguments)
        if isinstance(result, dict) and "content" in result:
            return {"jsonrpc": "2.0", "id": req_id, "result": result}
        return {
            "jsonrpc": "2.0",
            "id": req_id,
            "result": {"content": [{"type": "text", "text": json.dumps(result, ensure_ascii=False)}]},
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
        request = json.loads(line)
        response = dispatch(request)
        sys.stdout.write(json.dumps(response, ensure_ascii=False) + "\n")
        sys.stdout.flush()


if __name__ == "__main__":
    main()
