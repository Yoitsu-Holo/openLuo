import sys
import json
import base64

# ============================================================
# 背景图资产生成插件
# ============================================================
# 监听 onNarrativeAfter hook，检测叙事结果中的场景切换标记，
# 将背景资产通过 Asset API 完整入库：
#   register → create → blob_put → meta_put → link → unlock
# ============================================================

_req_id = 0


def call_host(method, params=None):
    global _req_id
    _req_id += 1
    req = {
        "jsonrpc": "2.0",
        "id": f"bg{_req_id}",
        "method": method,
        "params": params or {},
    }
    print(json.dumps(req, ensure_ascii=False), flush=True)
    line = sys.stdin.readline()
    return json.loads(line).get("result", {})


def onStartup(args):
    """注册背景图资产类型"""
    call_host(
        "game/asset/register",
        {
            "def": {
                "assetType": "background",
                "namespace": "gallery",
                "pluginId": "builtin_asset_bg_generator",
                "metadata": {
                    "displayName": "背景图",
                    "description": "游戏场景的背景图资产",
                },
            }
        },
    )
    return {"success": True}


def onNarrativeAfter(args):
    """
    叙事结束后检测场景切换，若场景变化则入库背景资产。

    期望 args 字段：
      - narrative_result.scene_id   (str | None)：场景标识符
      - narrative_result.location   (str | None)：地点名称
      - context.day                 (int)：当前游戏天数
      - context.stateSnapshot       (dict)：当前状态快照
    """
    narrative = args.get("narrative_result", {})
    scene_id = narrative.get("scene_id")
    if not scene_id:
        return {}

    context = args.get("context", {})
    day = context.get("day", 1)
    location = narrative.get("location", scene_id)
    time_of_day = narrative.get("time_of_day", "day")

    label = f"{location}_{time_of_day}"

    # 1. 创建资产记录
    create_result = call_host(
        "game/asset/create",
        {
            "assetType": "background",
            "namespace": "gallery",
            "ownerKind": "game",
            "ownerId": "global",
            "label": label,
            "sourceType": "ai",
        },
    )
    asset_id = create_result.get("assetId")
    if not asset_id:
        return {}

    # 2. 存储占位 Blob（实际实现中替换为真实图片数据）
    placeholder = f"[BG:{scene_id}:{time_of_day}]".encode("utf-8")
    call_host(
        "game/asset/blob_put",
        {
            "assetId": asset_id,
            "mimeType": "image/png",
            "blobRole": "primary",
            "blobBase64": base64.b64encode(placeholder).decode("ascii"),
            "isPrimary": True,
        },
    )

    # 3. 存储场景元数据
    call_host(
        "game/asset/meta_put",
        {
            "assetId": asset_id,
            "metaType": "bg_scene_meta",
            "payload": {
                "sceneId": scene_id,
                "location": location,
                "timeOfDay": time_of_day,
                "firstSeenOnDay": day,
            },
        },
    )

    # 4. 解锁到背景画廊
    call_host(
        "game/asset/unlock",
        {
            "ownerKind": "game",
            "ownerId": "gallery_bg",
            "entityType": "asset",
            "entityId": asset_id,
            "unlockType": "bg_gallery_unlock",
            "metadata": {"unlockedOnDay": day, "sceneId": scene_id},
        },
    )

    return {"bgAssetId": asset_id}


TOOLS = {
    "onStartup": onStartup,
    "onNarrativeAfter": onNarrativeAfter,
}

TOOLS_LIST = [
    {
        "name": "onStartup",
        "description": "Register background asset type",
        "category": "hook",
    },
    {
        "name": "onNarrativeAfter",
        "description": "Detect scene changes and ingest background assets",
        "category": "hook",
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
                "serverInfo": {
                    "name": "builtin_asset_bg_generator",
                    "version": "1.0.0",
                },
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
