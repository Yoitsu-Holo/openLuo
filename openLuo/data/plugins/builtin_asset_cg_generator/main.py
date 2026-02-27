import sys
import json
import base64

# ============================================================
# CG 场景图资产生成插件
# ============================================================
# 监听 onNarrativeAfter hook，检测叙事结果中的 CG 标记，
# 将 CG 资产通过 Asset API 完整入库：
#   register → create → blob_put → meta_put → link → unlock
# ============================================================

_req_id = 0


def call_host(method, params=None):
    global _req_id
    _req_id += 1
    req = {
        "jsonrpc": "2.0",
        "id": f"cg{_req_id}",
        "method": method,
        "params": params or {},
    }
    print(json.dumps(req, ensure_ascii=False), flush=True)
    line = sys.stdin.readline()
    return json.loads(line).get("result", {})


def onStartup(args):
    """注册 CG 场景图资产类型"""
    call_host(
        "game/asset/register",
        {
            "def": {
                "assetType": "cg_scene",
                "namespace": "gallery",
                "pluginId": "builtin_asset_cg_generator",
                "metadata": {
                    "displayName": "CG 场景",
                    "description": "游戏中解锁的 CG 场景图",
                },
            }
        },
    )
    return {"success": True}


def onNarrativeAfter(args):
    """
    叙事结束后检测 CG 标记，若存在则入库 CG 资产。

    期望 args 字段：
      - narrative_result.cg_id    (str | None)：CG 标识符
      - narrative_result.reply    (str)：叙事文本
      - context.characterId       (str)：角色 ID
      - context.stateSnapshot     (dict)：当前状态快照
    """
    narrative = args.get("narrative_result", {})
    cg_id = narrative.get("cg_id")
    if not cg_id:
        return {}

    context = args.get("context", {})
    char_id = context.get("characterId", "unknown")
    day = context.get("day", 1)
    reply_text = narrative.get("reply", "")

    # 1. 创建资产记录
    create_result = call_host(
        "game/asset/create",
        {
            "assetType": "cg_scene",
            "namespace": "gallery",
            "ownerKind": "character",
            "ownerId": char_id,
            "label": cg_id,
            "sourceType": "ai",
        },
    )
    asset_id = create_result.get("assetId")
    if not asset_id:
        return {}

    # 2. 存储占位 Blob（实际实现中替换为真实图片数据）
    placeholder = f"[CG:{cg_id}] {reply_text[:50]}".encode("utf-8")
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
            "metaType": "cg_scene_meta",
            "payload": {
                "cgId": cg_id,
                "characterId": char_id,
                "unlockedOnDay": day,
                "emotion": narrative.get("emotion", "Neutral"),
            },
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
            "linkType": "cg_subject",
        },
    )

    # 5. 解锁到 CG 画廊
    call_host(
        "game/asset/unlock",
        {
            "ownerKind": "game",
            "ownerId": "gallery_cg",
            "entityType": "asset",
            "entityId": asset_id,
            "unlockType": "cg_gallery_unlock",
            "metadata": {"unlockedOnDay": day, "cgId": cg_id},
        },
    )

    return {"cgAssetId": asset_id}


TOOLS = {
    "onStartup": onStartup,
    "onNarrativeAfter": onNarrativeAfter,
}

TOOLS_LIST = [
    {
        "name": "onStartup",
        "description": "Register cg_scene asset type",
        "category": "hook",
    },
    {
        "name": "onNarrativeAfter",
        "description": "Detect CG markers in narrative and ingest CG assets",
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
                    "name": "builtin_asset_cg_generator",
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
