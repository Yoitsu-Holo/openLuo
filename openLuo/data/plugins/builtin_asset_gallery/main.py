import sys
import json

# ============================================================
# 资产画廊插件
# ============================================================
# - onStatusQuery：在状态面板中显示已解锁的资产数量
# - galleryList 工具：查询并返回画廊资产列表
# ============================================================

_req_id = 0


def call_host(method, params=None):
    global _req_id
    _req_id += 1
    req = {
        "jsonrpc": "2.0",
        "id": f"gl{_req_id}",
        "method": method,
        "params": params or {},
    }
    print(json.dumps(req, ensure_ascii=False), flush=True)
    line = sys.stdin.readline()
    return json.loads(line).get("result", {})


def onStatusQuery(args):
    """在状态面板中显示已解锁的 CG / 背景 / 梦境碎片数量"""
    status_items = []

    for asset_type, gallery_id, label in [
        ("cg_scene", "gallery_cg", "CG"),
        ("background", "gallery_bg", "背景"),
        ("dreamFragment", "gallery_main", "梦境"),
    ]:
        result = call_host(
            "game/asset/query",
            {"assetType": asset_type, "namespace": "gallery", "limit": 1000},
        )
        count = len(result.get("items", []))
        if count > 0:
            status_items.append(
                {
                    "id": f"gallery_{asset_type}_count",
                    "label": f"{label}画廊",
                    "type": "text",
                    "value": str(count),
                    "group": "gallery",
                    "order": 900,
                    "text": f"已解锁 {count} 张",
                }
            )

    return {"statusItems": status_items}


def galleryList(args):
    """
    查询画廊资产列表。

    参数：
      - assetType  (str, optional)：资产类型过滤，如 "cg_scene"/"background"/"dreamFragment"
      - ownerId    (str, optional)：按角色 ID 过滤
      - limit      (int, optional)：返回数量上限，默认 20
      - offset     (int, optional)：分页偏移，默认 0
    """
    asset_type = args.get("assetType")
    owner_id = args.get("ownerId")
    limit = args.get("limit", 20)
    offset = args.get("offset", 0)

    query_params = {"namespace": "gallery", "limit": limit, "offset": offset}
    if asset_type:
        query_params["assetType"] = asset_type
    if owner_id:
        query_params["ownerKind"] = "character"
        query_params["ownerId"] = owner_id

    result = call_host("game/asset/query", query_params)
    items = result.get("items", [])

    return {
        "ok": True,
        "total": len(items),
        "items": [
            {
                "assetId": item.get("assetId"),
                "assetType": item.get("assetType"),
                "label": item.get("label"),
                "ownerId": item.get("ownerId"),
                "createdAt": item.get("createdAt"),
            }
            for item in items
        ],
    }


TOOLS = {
    "onStatusQuery": onStatusQuery,
    "galleryList": galleryList,
}

TOOLS_LIST = [
    {
        "name": "onStatusQuery",
        "description": "Show unlocked asset counts in status panel",
        "category": "hook",
    },
    {
        "name": "galleryList",
        "description": "Query and list gallery assets",
        "category": "command",
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
                "serverInfo": {"name": "builtin_asset_gallery", "version": "1.0.0"},
            },
        }
    elif method == "tools/list":
        return {"jsonrpc": "2.0", "id": req_id, "result": {"tools": TOOLS_LIST}}
    elif method == "tools/call":
        tool_name = params.get("name")
        tool_args = params.get("arguments", {})
        if tool_name in TOOLS:
            result = TOOLS[tool_name](tool_args)
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
            "error": {"code": -32601, "message": f"Tool {tool_name} not found"},
        }
    elif method == "hooks/call":
        hook_name = params.get("hookName")
        hook_args = params.get("args", {})
        if hook_name in TOOLS:
            result = TOOLS[hook_name](hook_args)
            return {"jsonrpc": "2.0", "id": req_id, "result": result}
        return {"jsonrpc": "2.0", "id": req_id, "result": {}}
    return {
        "jsonrpc": "2.0",
        "id": req_id,
        "error": {"code": -32601, "message": "Method not found"},
    }


if __name__ == "__main__":
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
                    },
                    ensure_ascii=False,
                ),
                flush=True,
            )
