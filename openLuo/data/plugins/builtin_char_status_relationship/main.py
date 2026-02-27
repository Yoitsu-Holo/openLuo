import sys
import json
import re
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent.parent / "shared"))

_req_id = 0


def call_host(method, params=None):
    global _req_id
    _req_id += 1
    req = {
        "jsonrpc": "2.0",
        "id": f"r{_req_id}",
        "method": method,
        "params": params or {},
    }
    print(json.dumps(req, ensure_ascii=False), flush=True)
    line = sys.stdin.readline()
    return json.loads(line).get("result", {})


def load_json_file(filename):
    base_path = Path(__file__).parent
    for ext in [".jsonc", ".json", ""]:
        path = base_path / (
            filename if ext == "" else filename.replace(".json", "").replace(".jsonc", "") + ext
        )
        if path.exists():
            try:
                with open(path, "r", encoding="utf-8") as f:
                    content = f.read()
                content = re.sub(r"//.*", "", content)
                return json.loads(content)
            except Exception:
                continue
    return []


def load_character_config(character_id):
    """优先加载角色配置，如果不存在则返回 None"""
    config_path = Path(__file__).parent / "config" / f"{character_id}.jsonc"
    if config_path.exists():
        try:
            with open(config_path, "r", encoding="utf-8") as f:
                content = f.read()
            content = re.sub(r"//.*", "", content)
            return json.loads(content)
        except Exception:
            pass
    return None


def apply_character_overrides(state_defs, character_id):
    """应用角色个性化配置到状态定义"""
    char_config = load_character_config(character_id)
    if not char_config or "states" not in char_config:
        return state_defs

    overrides = char_config["states"]
    for state_def in state_defs:
        key = state_def.get("key")
        if key in overrides:
            override = overrides[key]
            if "defaultValue" in override:
                state_def["defaultValue"] = override["defaultValue"]

    return state_defs


def get_stage_info(affection):
    stages = [
        {"min": 0, "max": 199, "label": "陌生人", "desc": "仍保持距离感"},
        {"min": 200, "max": 399, "label": "熟人", "desc": "开始有基本信任"},
        {"min": 400, "max": 599, "label": "朋友", "desc": "愿意吐露真心话"},
        {"min": 600, "max": 799, "label": "好友", "desc": "深度信任和依赖"},
        {"min": 800, "max": 1000, "label": "恋人", "desc": "亲密关系"},
    ]
    for s in stages:
        if s["min"] <= affection <= s["max"]:
            return s
    return stages[0]


def onStartup(args):
    # 获取角色 ID（从 args 或环境变量）
    character_id = args.get("characterId") or args.get("character_id")

    state_defs = load_json_file("state_defs")

    # 如果有角色 ID，应用角色配置覆盖
    if character_id:
        state_defs = apply_character_overrides(state_defs, character_id)

    for res in state_defs:
        call_host("game/state/register", {"def": res})
    return {"success": True}


def get_prompt_for_value(char_config, state_key, value):
    """根据角色配置和当前值获取对应的 prompt"""
    if not char_config or "states" not in char_config:
        return None

    state_config = char_config["states"].get(state_key)
    if not state_config:
        return None

    # 处理 enum 类型（promptByValue）
    if "promptByValue" in state_config:
        return state_config["promptByValue"].get(value)

    # 处理 number 类型（promptRanges）
    if "promptRanges" in state_config:
        try:
            num_value = int(float(value))
            for range_config in state_config["promptRanges"]:
                if range_config["min"] <= num_value <= range_config["max"]:
                    return range_config["prompt"]
        except (ValueError, TypeError):
            pass

    return None


def onPromptContext(args):
    character_id = args.get("characterId") or args.get("character_id")
    snapshot = args.get("stateSnapshot", {}).get("charStatus", {})
    affection = int(float(snapshot.get("affection", 0)))
    trust = int(float(snapshot.get("trust", 50)))
    stage = get_stage_info(affection)

    fragments = []

    # 尝试加载角色配置
    char_config = load_character_config(character_id) if character_id else None

    # 好感度 prompt
    affection_prompt = get_prompt_for_value(char_config, "affection", affection)
    if affection_prompt:
        fragments.append({
            "phase": args.get("phase", "chat-evaluate"),
            "priority": 100,
            "text": f"好感度 {affection}/1000（{stage['label']}）。{affection_prompt}"
        })
    else:
        # 使用默认 prompt
        fragments.append({
            "phase": args.get("phase", "chat-evaluate"),
            "priority": 100,
            "text": f"当前好感度 {affection}/1000（{stage['label']}）。普通寒暄不改变好感度，只有明确让角色感到满意、被理解、心动或受伤、失望时才调整。"
        })

    # 信任度 prompt
    trust_prompt = get_prompt_for_value(char_config, "trust", trust)
    if trust_prompt:
        fragments.append({
            "phase": args.get("phase", "chat-evaluate"),
            "priority": 90,
            "text": f"信任度 {trust}/100。{trust_prompt}"
        })
    else:
        fragments.append({
            "phase": args.get("phase", "chat-evaluate"),
            "priority": 90,
            "text": f"信任度 {trust}/100。信任度影响角色是否愿意分享秘密、接受建议。"
        })

    return {"promptFragments": fragments}


def onStatusQuery(args):
    snapshot = args.get("stateSnapshot", {}).get("charStatus", {})
    affection = int(float(snapshot.get("affection", 0)))
    trust = int(float(snapshot.get("trust", 50)))
    stage = get_stage_info(affection)

    return {
        "statusItems": [
            {
                "id": "affection",
                "label": "好感度",
                "type": "bar",
                "value": str(affection),
                "max": "1000",
                "group": "intimacy",
                "order": 100,
                "text": f"{stage['label']}：{stage['desc']}",
            },
            {
                "id": "trust",
                "label": "信任度",
                "type": "bar",
                "value": str(trust),
                "max": "100",
                "group": "intimacy",
                "order": 110,
            }
        ]
    }


TOOLS = {
    "onStartup": onStartup,
    "onPromptContext": onPromptContext,
    "onStatusQuery": onStatusQuery,
}

TOOLS_LIST = [
    {"name": "onStartup", "description": "Register relationship states", "category": "hook"},
    {"name": "onPromptContext", "description": "Provide relationship prompt context", "category": "hook"},
    {"name": "onStatusQuery", "description": "Provide relationship status display", "category": "hook"},
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
                "serverInfo": {"name": "builtin_char_status_relationship", "version": "1.0.0"},
            },
        }
    elif method == "tools/list":
        return {"jsonrpc": "2.0", "id": req_id, "result": {"tools": TOOLS_LIST}}
    elif method == "tools/call":
        tool_name = params.get("name")
        args = params.get("arguments", {})
        if tool_name in TOOLS:
            result = TOOLS[tool_name](args)
            return {
                "jsonrpc": "2.0",
                "id": req_id,
                "result": {"content": [{"type": "text", "text": json.dumps(result, ensure_ascii=False)}]},
            }
        return {"jsonrpc": "2.0", "id": req_id, "error": {"code": -32601, "message": f"Tool {tool_name} not found"}}
    elif method == "hooks/call":
        hook_name = params.get("hookName")
        args = params.get("args", {})
        if hook_name in TOOLS:
            result = TOOLS[hook_name](args)
            return {"jsonrpc": "2.0", "id": req_id, "result": result}
        return {"jsonrpc": "2.0", "id": req_id, "result": {}}
    return {"jsonrpc": "2.0", "id": req_id, "error": {"code": -32601, "message": "Method not found"}}


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
                    {"jsonrpc": "2.0", "id": None, "error": {"code": -32603, "message": str(e)}},
                    ensure_ascii=False,
                ),
                flush=True,
            )
