import sys
import json
import re

PLUGIN_ID = "builtin_system_commands"
_req_id = 0
MAX_CHAT_MSG_CHARS = 400


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
    if not line:
        return {}
    try:
        return json.loads(line).get("result", {})
    except Exception:
        return {}


def log(level, msg):
    call_host("game/log", {"pluginId": PLUGIN_ID, "level": level, "msg": msg})


def load_json_file(filename):
    """Load JSON/JSONC file (tries .jsonc first, then .json, strips // comments)."""
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
    return {} if filename.startswith("config") else []


def load_config():
    return load_json_file("config")


def handle_help(args):
    cmds = call_host("game/commands/list")
    cmds = cmds or []
    if not cmds:
        return {"success": True, "output": "暂无可用指令。"}

    def infer_usage(cmd):
        default_usage = {
            "give": "/give <物品名>",
            "help": "/help [命令名]",
            "time_mode": "/time_mode [virtual|realtime|disabled] [local|utc|时区]",
            "buy": "/buy <分类.序号>",
            "shop": "/shop [分类]",
            "inventory": "/inventory",
            "status": "/status",
            "sleep": "/sleep",
            "rest": "/rest",
            "nap": "/nap",
            "work": "/work [part-time|overtime]",
            "plan_date": "/plan_date <+天数> <HH:mm> <计划内容>",
            "diary": "/diary [页码]",
            "galleryList": "/galleryList [筛选条件]",
        }
        prefix = cmd.get("prefix", "/")
        usage = cmd.get("usage")
        if usage:
            return usage
        help_short = cmd.get("helpShort", "")
        marker = "例："
        if marker in help_short:
            return help_short.split(marker, 1)[1].strip()
        if cmd.get("name") in default_usage:
            return default_usage[cmd.get("name")].replace("/", prefix, 1)
        return f"{prefix}{cmd.get('name', '')}"

    def display_name(cmd):
        return f"{cmd.get('prefix', '/')}{cmd.get('name', '')}"

    if args:
        raw_target = args[0].strip()
        if not raw_target:
            return {"success": False, "error": "请输入命令名，例：/help /status"}
        target_prefix = raw_target[0] if raw_target[0] in ["/", "$", "&", "@"] else None
        target_name = (
            raw_target[1:] if target_prefix else raw_target.lstrip("/$&@")
        ).strip()
        for c in cmds:
            name_match = c.get("name") == target_name or target_name in c.get("aliases", [])
            prefix_match = target_prefix is None or c.get("prefix", "/") == target_prefix
            if name_match and prefix_match:
                usage = infer_usage(c)
                alias_text = (
                    f"别名：{', '.join((c.get('prefix', '/')) + a for a in c.get('aliases', []))}\n"
                    if c.get("aliases")
                    else ""
                )
                return {
                    "success": True,
                    "output": f"{display_name(c)}\n{c.get('helpShort', '')}\n{alias_text}用法：{usage}",
                }
        return {"success": False, "error": f"未知条目：{raw_target}"}
    lines = ["可用指令："]
    for c in sorted(cmds, key=lambda x: (x.get("prefix", "/"), x.get("name", ""))):
        if c.get("name") == "debug":
            continue
        if not c.get("helpShort"):
            continue
        usage = infer_usage(c)
        name = display_name(c)
        lines.append(f"  {name:<12} {c.get('helpShort', '')}")
        lines.append(f"  {'':12} 用法：{usage}")
    return {"success": True, "output": "\n".join(lines)}


def handle_give(args):
    if not args:
        return {"success": False, "error": "请指定物品名称，例：/give 玫瑰花束"}
    item_ref = " ".join(args)

    result = call_host("game/gift/execute", {"itemRef": item_ref})
    if not result or not result.get("ok"):
        return {
            "success": False,
            "error": (
                result.get("error", "赠送失败")
                if isinstance(result, dict)
                else "赠送失败"
            ),
        }

    delta = int(result.get("affectionDelta", 0))
    sign = "+" if delta >= 0 else ""
    stage_map = {
        "Stranger": "陌生人",
        "Acquaintance": "熟人",
        "Friend": "朋友",
        "CloseFriend": "好友",
        "Lover": "恋人",
    }
    return {
        "success": True,
        "output": (
            f"赠送了「{result.get('itemName', item_ref)}」给 {result.get('characterName', '角色')}。\n"
            f"{result.get('characterName', '角色')}：{result.get('reply', '')}\n"
            f"好感度 {sign}{delta}（当前：{result.get('affection', 0)}）  关系：{stage_map.get(result.get('relationshipStage', ''), '')}"
        ),
    }


STATE_ALIASES = {
    "好感度": ("char_status", "affection"),
    "好感": ("char_status", "affection"),
    "affection": ("char_status", "affection"),
    "关系": ("char_status", "relationship_stage"),
    "关系阶段": ("char_status", "relationship_stage"),
    "relationship_stage": ("char_status", "relationship_stage"),
    "信任度": ("char_status", "trust"),
    "信任": ("char_status", "trust"),
    "trust": ("char_status", "trust"),
    "心情": ("char_status", "mood"),
    "mood": ("char_status", "mood"),
    "压力": ("char_status", "stress"),
    "stress": ("char_status", "stress"),
    "角色体力": ("char_status", "energy"),
    "精力": ("char_status", "energy"),
    "energy": ("char_status", "energy"),
    "健康": ("char_status", "health"),
    "health": ("char_status", "health"),
    "疲劳度": ("char_status", "fatigue"),
    "疲劳": ("char_status", "fatigue"),
    "fatigue": ("char_status", "fatigue"),
    "羞耻度": ("char_status", "shame"),
    "羞耻": ("char_status", "shame"),
    "shame": ("char_status", "shame"),
    "依赖度": ("char_status", "dependency"),
    "依赖": ("char_status", "dependency"),
    "dependency": ("char_status", "dependency"),
    "占有欲": ("char_status", "possessiveness"),
    "possessiveness": ("char_status", "possessiveness"),
    "欲望": ("char_status", "lust"),
    "lust": ("char_status", "lust"),
    "性意愿": ("char_status", "sexual_intent"),
    "意愿": ("char_status", "sexual_intent"),
    "sexual_intent": ("char_status", "sexual_intent"),
    "体力": ("game_resource", "stamina"),
    "stamina": ("game_resource", "stamina"),
    "金币": ("game_resource", "gold"),
    "金钱": ("game_resource", "gold"),
    "钱": ("game_resource", "gold"),
    "gold": ("game_resource", "gold"),
    "天气": ("world_state", "world_weather"),
    "weather": ("world_state", "world_weather"),
    "world_weather": ("world_state", "world_weather"),
    "季节": ("world_state", "world_season"),
    "season": ("world_state", "world_season"),
    "world_season": ("world_state", "world_season"),
    "时间段": ("world_state", "world_time_of_day"),
    "world_time_of_day": ("world_state", "world_time_of_day"),
    "时间模式": ("system_time", "mode"),
    "time_mode": ("system_time", "mode"),
    "mode": ("system_time", "mode"),
    "时区": ("system_time", "timezone"),
    "timezone": ("system_time", "timezone"),
}


def _debug_state_scopes(char_id):
    return [
        ("game_resource", "game", "global"),
        ("world_state", "game", "global"),
        ("char_status", "character", char_id),
        ("system_time", "system", "kernel"),
    ]


def _available_debug_states(char_id):
    available = {}
    state_lines = []
    for namespace, owner_kind, owner_id in _debug_state_scopes(char_id):
        query = call_host(
            "game/state/query",
            {
                "namespace": namespace,
                "ownerKind": owner_kind,
                "ownerId": owner_id,
                "includeDefaults": True,
            },
        )
        items = query.get("items", []) if isinstance(query, dict) else []
        for item in items:
            key = item.get("key")
            value = item.get("value")
            if not key:
                continue
            state_ref = (namespace, key)
            available[state_ref] = {
                "namespace": namespace,
                "key": key,
                "ownerKind": item.get("ownerKind", owner_kind),
                "ownerId": item.get("ownerId", owner_id),
                "value": value,
            }
            state_lines.append(
                f"- {namespace}.{key} ({owner_kind}:{owner_id}) = {value}"
            )
    return available, state_lines


def _parse_debug_value(text, target_text):
    match = re.search(r"(?:为|到|成|=|：|:)\s*([^\s，,。；;]+)", text)
    if match:
        return match.group(1).strip()
    if target_text:
        after_target = text.split(target_text, 1)[1]
        match = re.search(
            r"[-+]?\d+(?:\.\d+)?|[A-Za-z_]+|[\u4e00-\u9fff]+",
            after_target,
        )
        if match:
            return match.group(0).strip()
    match = re.search(r"[-+]?\d+(?:\.\d+)?", text)
    if match:
        return match.group(0)
    return ""


def _parse_debug_modifications(instruction, available):
    text = instruction.strip()
    lowered = text.lower()
    target = None
    target_text = ""

    explicit = re.search(r"\b([a-zA-Z_][\w]*)\.([a-zA-Z_][\w]*)\b", text)
    if explicit:
        target = (explicit.group(1), explicit.group(2))
        target_text = explicit.group(0)

    if target is None:
        for alias, state_ref in sorted(
            STATE_ALIASES.items(), key=lambda x: len(x[0]), reverse=True
        ):
            if alias.lower() in lowered:
                target = state_ref
                target_text = alias
                break

    if target is None:
        for state_ref in available:
            ns, key = state_ref
            if key.lower() in lowered or f"{ns}.{key}".lower() in lowered:
                target = state_ref
                target_text = key
                break

    if target is None:
        return (
            [],
            "未识别到要修改的状态。请使用状态名或 namespace.key，例如：/debug char_status.affection=70",
        )

    state_info = available.get(target)
    if state_info is None:
        return [], f"当前存档未注册状态：{target[0]}.{target[1]}"

    value = _parse_debug_value(text, target_text)
    if value == "":
        return (
            [],
            f"未识别到 {target[0]}.{target[1]} 的目标值。例：/debug {target[0]}.{target[1]}=70",
        )

    delta_words = ("增加", "加", "提升", "减少", "减", "降低")
    op = "delta" if any(word in text for word in delta_words) else "set"
    if any(word in text for word in ("减少", "减", "降低")) and not value.startswith("-"):
        value = f"-{value}"

    return [
        {
            "namespace": state_info["namespace"],
            "key": state_info["key"],
            "ownerKind": state_info["ownerKind"],
            "ownerId": state_info["ownerId"],
            "op": op,
            "value": value,
        }
    ], ""


def handle_debug(args):
    if not args:
        return {
            "success": False,
            "error": "请输入调试指令，例：/debug 将好感度设置为 50",
        }

    instruction = " ".join(args)
    state = call_host("game/session/get")
    char = call_host("game/character/get")
    if not state or not char:
        return {"success": False, "error": "游戏未初始化"}

    char_id = char.get("id", "")
    available, state_lines = _available_debug_states(char_id)
    modifications, parse_error = _parse_debug_modifications(instruction, available)
    if not modifications:
        return {
            "success": False,
            "error": parse_error
            + "\n可修改状态：\n"
            + ("\n".join(state_lines) if state_lines else "（暂无状态）"),
        }

    results = []
    for mod in modifications:
        ns = mod.get("namespace")
        key = mod.get("key")
        owner_kind = mod.get("ownerKind", "game")
        owner_id = mod.get("ownerId", "global")
        op = mod.get("op", "set")
        value = mod.get("value")
        if not ns or not key:
            results.append(f"✗ 跳过非法修改：{mod}")
            continue

        apply_res = call_host(
            "game/state/apply",
            {
                "mutations": [
                    {
                        "namespace": ns,
                        "key": key,
                        "ownerKind": owner_kind,
                        "ownerId": owner_id,
                        "op": op,
                        "value": str(value),
                        "sourceType": "plugin",
                        "sourceId": PLUGIN_ID,
                    }
                ]
            },
        )

        ok = bool(isinstance(apply_res, dict) and apply_res.get("ok"))
        detail = ""
        if ok and apply_res.get("results"):
            first = apply_res["results"][0]
            if not first.get("ok"):
                ok = False
                detail = first.get("error", "")
            else:
                detail = f"{first.get('oldValue')} -> {first.get('newValue')}"

        if ok:
            results.append(
                f"✓ {ns}.{key} = {value}" + (f" ({detail})" if detail else "")
            )
        else:
            err = detail or (
                apply_res.get("error") if isinstance(apply_res, dict) else "unknown"
            )
            results.append(f"✗ {ns}.{key} 失败: {err}")

    summary = f"调试修改完成：{instruction}"
    output = f"{summary}\n\n" + "\n".join(results)
    return {"success": True, "output": output}


def _clear_timeline_events_for_status(status, limit=200, max_rounds=1000):
    cleared = 0
    failed = 0
    rounds = 0

    # Always pull the first page so deletions do not cause offset skipping.
    while rounds < max_rounds:
        rounds += 1
        query_res = call_host(
            "game/timeline/query", {"status": status, "limit": limit, "offset": 0}
        )
        items = (query_res or {}).get("items", [])
        if not items:
            break

        progressed = False
        for item in items:
            event_id = item.get("id")
            if not event_id:
                failed += 1
                continue
            cancel_res = call_host("game/timeline/cancel", {"eventId": event_id})
            if cancel_res and cancel_res.get("ok"):
                cleared += 1
                progressed = True
            else:
                failed += 1

        if not progressed:
            # Avoid infinite loops when everything in the current page fails to cancel.
            break

    exhausted = rounds >= max_rounds
    return {"cleared": cleared, "failed": failed, "exhausted": exhausted}


def handle_time_mode(args):
    """View or switch runtime time mode."""
    snapshot = call_host("game/time/get") or {}
    if not args:
        mode = snapshot.get("mode", "virtual")
        day = snapshot.get("day", 1)
        time_str = snapshot.get("timeStr", "00:00")
        return {
            "success": True,
            "output": f"当前时间模式：{mode}\n当前时间：第 {day} 天 {time_str}",
        }

    mode = (args[0] or "").strip().lower()
    if mode not in {"virtual", "realtime", "disabled"}:
        return {"success": False, "error": "模式仅支持：virtual / realtime / disabled"}

    timezone = (args[1] if len(args) > 1 else "local").strip()
    if len(timezone) > 64:
        return {"success": False, "error": "timezone 参数过长（最多 64 字符）"}
    if timezone and not re.match(r"^[A-Za-z0-9_./+\-]+$", timezone):
        return {"success": False, "error": "timezone 参数格式非法"}
    policy = (args[2] if len(args) > 2 else "no_op").strip().lower()
    if mode != "realtime":
        policy = "no_op"

    mutations = [
        {
            "namespace": "system_time",
            "key": "mode",
            "ownerKind": "system",
            "ownerId": "kernel",
            "op": "set",
            "value": mode,
            "sourceType": "plugin",
            "sourceId": PLUGIN_ID,
        },
        {
            "namespace": "system_time",
            "key": "timezone",
            "ownerKind": "system",
            "ownerId": "kernel",
            "op": "set",
            "value": timezone,
            "sourceType": "plugin",
            "sourceId": PLUGIN_ID,
        },
        {
            "namespace": "system_time",
            "key": "realtime_advance_policy",
            "ownerKind": "system",
            "ownerId": "kernel",
            "op": "set",
            "value": policy,
            "sourceType": "plugin",
            "sourceId": PLUGIN_ID,
        },
    ]
    apply_res = call_host("game/state/apply", {"mutations": mutations})
    if not apply_res or not apply_res.get("ok"):
        return {
            "success": False,
            "error": (apply_res or {}).get("error", "时间模式切换失败"),
        }
    failed_mutation = next(
        (x for x in (apply_res.get("results") or []) if not x.get("ok")), None
    )
    if failed_mutation:
        return {
            "success": False,
            "error": f"时间模式切换失败：{failed_mutation.get('namespace')}.{failed_mutation.get('key')} {failed_mutation.get('error', '')}".strip(),
        }

    cleared_events = 0
    failed_clears = 0
    cleanup_exhausted = False
    if mode == "disabled":
        for status in ("pending", "active"):
            cleanup = _clear_timeline_events_for_status(status)
            cleared_events += cleanup.get("cleared", 0)
            failed_clears += cleanup.get("failed", 0)
            cleanup_exhausted = cleanup_exhausted or bool(cleanup.get("exhausted"))

    after = call_host("game/time/get") or {}
    cleanup_line = ""
    if mode == "disabled" and (failed_clears > 0 or cleanup_exhausted):
        cleanup_line = f"\n- cleanupWarnings: failed={failed_clears}, exhausted={str(cleanup_exhausted).lower()}"
    return {
        "success": True,
        "output": (
            f"已切换时间模式：{mode}\n"
            f"- timezone: {timezone}\n"
            f"- realtimePolicy: {policy}\n"
            f"- clearedTimelineEvents: {cleared_events}\n"
            f"{cleanup_line}"
            f"- 当前时间：第 {after.get('day', 1)} 天 {after.get('timeStr', '00:00')}"
        ),
    }


TOOLS = {
    "help": handle_help,
    "give": handle_give,
    "debug": handle_debug,
    "time_mode": handle_time_mode,
}

TOOLS_LIST = [
    {
        "name": "help",
        "description": "显示帮助信息",
        "category": "command",
        "aliases": ["?"],
        "helpShort": "显示所有可用指令",
        "inputSchema": {"type": "object", "properties": {"args": {"type": "array"}}},
    },
    {
        "name": "give",
        "description": "赠送背包中的物品给角色",
        "category": "command",
        "aliases": [],
        "helpShort": "赠送物品，例：/give 玫瑰花束",
        "timeoutSeconds": 60,
        "inputSchema": {"type": "object", "properties": {"args": {"type": "array"}}},
    },
    {
        "name": "debug",
        "description": "调试命令（隐藏）",
        "category": "command",
        "aliases": [],
        "helpShort": "",
        "timeoutSeconds": 60,
        "inputSchema": {"type": "object", "properties": {"args": {"type": "array"}}},
    },
    {
        "name": "time_mode",
        "description": "查看或切换时间模式（virtual/realtime/disabled）",
        "category": "command",
        "aliases": ["timemode", "time"],
        "helpShort": "切换时间模式，例：/time_mode realtime utc",
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
