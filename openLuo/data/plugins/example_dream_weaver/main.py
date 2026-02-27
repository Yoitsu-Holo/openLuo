import sys
import json
import base64

# ============================================================
# State API / Asset API 示例插件
# ============================================================
# 本插件演示新架构下的最佳实践：
# - 使用 game/state/* 与 stateSnapshot
# - 展示 Asset API 的完整入库闭环
#   register → create → blob_put → meta_put → link → unlock
# ============================================================

DREAMS = [
    "你梦见了一片樱花林，微风吹过，花瓣纷纷飘落……",
    "你梦见了一个宁静的湖边，水面倒映着星空……",
    "你做了一个模糊的梦，醒来只记得有人在微笑……",
]

_req_id = 0


def call_host(method, params=None):
    global _req_id
    _req_id += 1
    req = {
        "jsonrpc": "2.0",
        "id": f"dw{_req_id}",
        "method": method,
        "params": params or {},
    }
    print(json.dumps(req, ensure_ascii=False), flush=True)
    line = sys.stdin.readline()
    return json.loads(line).get("result", {})


def onStartup(args):
    """注册梦境碎片资产类型"""
    call_host(
        "game/asset/register",
        {
            "def": {
                "assetType": "dreamFragment",
                "namespace": "gallery",
                "pluginId": "example_dream_weaver",
                "metadata": {
                    "displayName": "梦境碎片",
                    "description": "入睡时触发的梦境记录",
                },
            }
        },
    )
    return {"success": True}


def onSleep(args):
    """
    入睡时生成梦境文本，并通过 Asset API 完整入库：
    create → blob_put → meta_put → link → unlock
    """
    import random

    # 从新的 stateSnapshot 结构读取状态
    state = args.get("context", {}).get("stateSnapshot", {})
    char_status = state.get("charStatus", {})
    affection = int(float(char_status.get("affection", 0)))
    char_id = args.get("context", {}).get("characterId", "unknown")
    day = args.get("context", {}).get("day", 1)

    if affection < 50:
        return {}

    dream_text = random.choice(DREAMS)

    # 1. 创建资产记录
    create_result = call_host(
        "game/asset/create",
        {
            "assetType": "dreamFragment",
            "namespace": "gallery",
            "ownerKind": "character",
            "ownerId": char_id,
            "label": f"Day{day}的梦境",
            "sourceType": "ai",
        },
    )
    asset_id = create_result.get("assetId")
    if not asset_id:
        return {"dreamText": dream_text}

    # 2. 存储梦境文本 Blob
    blob_data = base64.b64encode(dream_text.encode("utf-8")).decode("ascii")
    call_host(
        "game/asset/blob_put",
        {
            "assetId": asset_id,
            "mimeType": "text/plain",
            "blobRole": "content",
            "blobBase64": blob_data,
            "isPrimary": True,
        },
    )

    # 3. 存储元数据
    call_host(
        "game/asset/meta_put",
        {
            "assetId": asset_id,
            "metaType": "dream_context",
            "payload": {"affection": affection, "day": day, "characterId": char_id},
        },
    )

    # 4. 关联到角色
    call_host(
        "game/asset/link",
        {
            "fromEntityType": "asset",
            "fromEntityId": asset_id,
            "toEntityType": "character",
            "toEntityId": char_id,
            "linkType": "belongs_to",
        },
    )

    # 5. 解锁到梦境画廊
    call_host(
        "game/asset/unlock",
        {
            "ownerKind": "game",
            "ownerId": "gallery_main",
            "entityType": "asset",
            "entityId": asset_id,
            "unlockType": "dream_gallery",
            "metadata": {"unlockedOnDay": day},
        },
    )

    return {"dreamText": dream_text}


TOOLS = {
    "onStartup": onStartup,
    "onSleep": onSleep,
}

TOOLS_LIST = [
    {
        "name": "onStartup",
        "description": "Register dreamFragment asset type",
        "category": "hook",
    },
    {
        "name": "onSleep",
        "description": "Generate dream text and ingest into Asset API on sleep",
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
                "serverInfo": {"name": "example_dream_weaver", "version": "2.1.0"},
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
