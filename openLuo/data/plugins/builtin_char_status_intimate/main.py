import sys
import json
import re
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent.parent / "shared"))

_req_id = 0


def call_host(method, params=None):
    global _req_id
    _req_id += 1
    req = {"jsonrpc": "2.0", "id": f"i{_req_id}", "method": method, "params": params or {}}
    print(json.dumps(req, ensure_ascii=False), flush=True)
    line = sys.stdin.readline()
    return json.loads(line).get("result", {})


def load_json_file(filename):
    base_path = Path(__file__).parent
    for ext in [".jsonc", ".json", ""]:
        path = base_path / (filename if ext == "" else filename.replace(".json", "").replace(".jsonc", "") + ext)
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
    char_config = load_character_config(character_id)
    if not char_config or "states" not in char_config:
        return state_defs
    overrides = char_config["states"]
    for state_def in state_defs:
        key = state_def.get("key")
        if key in overrides and "defaultValue" in overrides[key]:
            state_def["defaultValue"] = overrides[key]["defaultValue"]
    return state_defs


def onStartup(args):
    character_id = args.get("characterId") or args.get("character_id")
    state_defs = load_json_file("state_defs")
    if character_id:
        state_defs = apply_character_overrides(state_defs, character_id)
    for res in state_defs:
        call_host("game/state/register", {"def": res})
    return {"success": True}


def get_prompt_for_value(char_config, state_key, value):
    if not char_config or "states" not in char_config:
        return None
    state_config = char_config["states"].get(state_key)
    if not state_config:
        return None
    if "promptByValue" in state_config:
        return state_config["promptByValue"].get(value)
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
    shame = int(float(snapshot.get("shame", 50)))
    dependency = int(float(snapshot.get("dependency", 30)))
    possessiveness = int(float(snapshot.get("possessiveness", 20)))
    lust = int(float(snapshot.get("lust", 20)))
    sexual_intent = int(float(snapshot.get("sexual_intent", 10)))

    char_config = load_character_config(character_id) if character_id else None
    fragments = []

    # 只在高值时提示
    if shame >= 70:
        prompt = get_prompt_for_value(char_config, "shame", shame)
        if prompt:
            fragments.append({"phase": args.get("phase", "chat-evaluate"), "priority": 65, "text": prompt})

    if dependency >= 70:
        prompt = get_prompt_for_value(char_config, "dependency", dependency)
        if prompt:
            fragments.append({"phase": args.get("phase", "chat-evaluate"), "priority": 60, "text": prompt})

    if possessiveness >= 70:
        prompt = get_prompt_for_value(char_config, "possessiveness", possessiveness)
        if prompt:
            fragments.append({"phase": args.get("phase", "chat-evaluate"), "priority": 55, "text": prompt})

    if lust >= 70:
        prompt = get_prompt_for_value(char_config, "lust", lust)
        if prompt:
            fragments.append({"phase": args.get("phase", "chat-evaluate"), "priority": 50, "text": prompt})

    if sexual_intent >= 70:
        prompt = get_prompt_for_value(char_config, "sexual_intent", sexual_intent)
        if prompt:
            fragments.append({"phase": args.get("phase", "chat-evaluate"), "priority": 45, "text": prompt})

    return {"promptFragments": fragments}


def onStatusQuery(args):
    snapshot = args.get("stateSnapshot", {}).get("charStatus", {})

    items = [
        {
            "id": "shame",
            "label": "羞耻",
            "type": "bar",
            "value": snapshot.get("shame", "50"),
            "max": "100",
            "group": "intimacy",
            "order": 120,
        },
        {
            "id": "dependency",
            "label": "依赖",
            "type": "bar",
            "value": snapshot.get("dependency", "30"),
            "max": "100",
            "group": "intimacy",
            "order": 130,
        },
        {
            "id": "possessiveness",
            "label": "占有欲",
            "type": "bar",
            "value": snapshot.get("possessiveness", "20"),
            "max": "100",
            "group": "intimacy",
            "order": 140,
        },
        {
            "id": "lust",
            "label": "欲望",
            "type": "bar",
            "value": snapshot.get("lust", "20"),
            "max": "100",
            "group": "intimacy",
            "order": 170,
        },
        {
            "id": "sexual_intent",
            "label": "性意图",
            "type": "bar",
            "value": snapshot.get("sexual_intent", "10"),
            "max": "100",
            "group": "intimacy",
            "order": 180,
        }
    ]
    return {"statusItems": items}


TOOLS = {
    "onStartup": onStartup,
    "onPromptContext": onPromptContext,
    "onStatusQuery": onStatusQuery,
}

TOOLS_LIST = [
    {"name": "onStartup", "description": "Register intimate states", "category": "hook"},
    {"name": "onPromptContext", "description": "Provide intimate status context", "category": "hook"},
    {"name": "onStatusQuery", "description": "Provide intimate status display", "category": "hook"},
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
                "serverInfo": {"name": "builtin_char_status_intimate", "version": "1.0.0"},
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
