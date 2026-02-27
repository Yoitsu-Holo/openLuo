import sys
import json
from datetime import datetime, timedelta, timezone

try:
    from zoneinfo import ZoneInfo
except Exception:
    ZoneInfo = None

PLUGIN_ID = "builtin_event_date"
_req_id = 0
MAX_DATE_IDEA_CHARS = 120
MAX_PLAN_DAY_OFFSET = 365


def call_host(method, params=None):
    global _req_id
    _req_id += 1
    req = {
        "jsonrpc": "2.0",
        "id": f"dt{_req_id}",
        "method": method,
        "params": params or {},
    }
    print(json.dumps(req, ensure_ascii=False), flush=True)
    line = sys.stdin.readline()
    return json.loads(line).get("result", {})


def log(level, msg):
    call_host("game/log", {"pluginId": PLUGIN_ID, "level": level, "msg": msg})


def _build_date_context(char, state, char_id):
    """Build the dateContext text injected into the first beat's system prompt via playerMessage."""
    char_name = char.get("name", "角色")
    stage_map = {
        "Stranger": "陌生人",
        "Acquaintance": "熟人",
        "Friend": "朋友",
        "CloseFriend": "好友",
        "Lover": "恋人",
    }
    stage_res = call_host(
        "game/state/get",
        {
            "namespace": "char_status",
            "key": "relationship_stage",
            "ownerKind": "character",
            "ownerId": char_id,
        },
    )
    relationship_stage = (
        stage_res.get("value", "陌生人")
        if (stage_res and stage_res.get("ok"))
        else "陌生人"
    )
    stage = stage_map.get(relationship_stage, relationship_stage)
    affection_state = call_host(
        "game/state/get",
        {
            "namespace": "char_status",
            "key": "affection",
            "ownerKind": "character",
            "ownerId": char_id,
        },
    )
    affection = (
        int(float(affection_state.get("value", 0)))
        if (affection_state and affection_state.get("ok"))
        else 0
    )
    mood_state = call_host(
        "game/state/get",
        {
            "namespace": "char_status",
            "key": "mood",
            "ownerKind": "character",
            "ownerId": char_id,
        },
    )
    mood = (
        mood_state.get("value", "Neutral")
        if (mood_state and mood_state.get("ok"))
        else "Neutral"
    )
    player_name = state.get("playerName", "玩家")

    mood_label = {
        "Happy": "开心",
        "Neutral": "平静",
        "Sad": "难过",
        "Angry": "生气",
    }.get(mood, "平静")
    return char_name, player_name, stage, affection, mood_label


def _parse_day_expr(day_expr, current_day, mode):
    token = (day_expr or "").strip().lower()
    if not token:
        raise ValueError("缺少日期参数")

    if token.startswith("d"):
        if mode != "virtual":
            raise ValueError("realtime 模式不支持 dN 绝对游戏日，请改用 +N（相对天数）")
        if not token[1:].isdigit():
            raise ValueError("dN 格式错误，例：d5")
        target = int(token[1:])
        if target - current_day > MAX_PLAN_DAY_OFFSET:
            raise ValueError(f"计划天数过远，最多支持未来 {MAX_PLAN_DAY_OFFSET} 天")
        return target

    if token.startswith("+") and token[1:].isdigit():
        offset = int(token[1:])
        if offset > MAX_PLAN_DAY_OFFSET:
            raise ValueError(f"计划天数过远，最多支持未来 {MAX_PLAN_DAY_OFFSET} 天")
        return current_day + offset

    if token.isdigit():
        offset = int(token)
        if offset > MAX_PLAN_DAY_OFFSET:
            raise ValueError(f"计划天数过远，最多支持未来 {MAX_PLAN_DAY_OFFSET} 天")
        return current_day + offset

    raise ValueError("日期格式错误，支持 +N、N、dN")


def _parse_minute_of_day(time_expr):
    text = (time_expr or "").strip()
    if ":" not in text:
        raise ValueError("时间格式错误，需为 HH:MM")
    hh, mm = text.split(":", 1)
    if not (hh.isdigit() and mm.isdigit()):
        raise ValueError("时间格式错误，需为 HH:MM")
    hour = int(hh)
    minute = int(mm)
    if hour < 0 or hour > 23 or minute < 0 or minute > 59:
        raise ValueError("时间超出范围，小时 0-23，分钟 0-59")
    return hour * 60 + minute


def _resolve_timezone(timezone_name):
    name = (timezone_name or "").strip()
    if not name or name.lower() == "local":
        return datetime.now().astimezone().tzinfo or timezone.utc
    if name.lower() == "utc":
        return timezone.utc
    if ZoneInfo is not None:
        try:
            return ZoneInfo(name)
        except Exception:
            pass
    return datetime.now().astimezone().tzinfo or timezone.utc


def _read_kernel_timezone():
    tz_res = call_host(
        "game/state/get",
        {
            "namespace": "system_time",
            "key": "timezone",
            "ownerKind": "system",
            "ownerId": "kernel",
        },
    )
    if tz_res and tz_res.get("ok"):
        tz_value = (tz_res.get("value") or "").strip()
        if tz_value:
            return tz_value
    return "local"


def _build_due_epoch(mode, now_info, target_day, minute_of_day, timezone_name):
    if mode == "virtual":
        return ((target_day - 1) * 1440 + minute_of_day) * 60_000

    tzinfo = _resolve_timezone(timezone_name)
    now_epoch_ms = int(now_info.get("epochMs", 0) or 0)
    if now_epoch_ms <= 0:
        now_epoch_ms = int(datetime.now(timezone.utc).timestamp() * 1000)
    now_dt = datetime.fromtimestamp(now_epoch_ms / 1000.0, tz=timezone.utc).astimezone(
        tzinfo
    )
    now_day = int(now_info.get("day", 1) or 1)
    day_offset = max(0, target_day - now_day)
    target_dt = datetime.combine(
        (now_dt + timedelta(days=day_offset)).date(), datetime.min.time(), tzinfo=tzinfo
    )
    target_dt = target_dt + timedelta(minutes=minute_of_day)
    if target_dt <= now_dt:
        target_dt = target_dt + timedelta(days=1)
    return int(target_dt.timestamp() * 1000)


# ─── Command handlers ─────────────────────────────────────────────────────────


def handle_plan_date(args):
    """Create a timeline date invite task instead of immediate execution."""
    if len(args) < 3:
        return {
            "success": False,
            "error": "用法：/plan_date +N HH:MM 约会内容（例：/plan_date +2 10:00 去看电影）",
        }

    day_expr = args[0]
    time_expr = args[1]
    date_idea = " ".join(args[2:]).strip()
    if not date_idea:
        return {"success": False, "error": "请提供约会内容"}
    if len(date_idea) > MAX_DATE_IDEA_CHARS:
        return {
            "success": False,
            "error": f"约会内容过长，最多 {MAX_DATE_IDEA_CHARS} 个字符",
        }

    now_info = call_host("game/time/get") or {}
    mode = (now_info.get("mode", "virtual") or "virtual").lower()
    if mode == "disabled":
        return {
            "success": False,
            "error": "disabled 模式下不可创建日程，请先切换到 /time_mode virtual 或 realtime",
        }
    current_day = int(now_info.get("day", 1) or 1)
    try:
        target_day = _parse_day_expr(day_expr, current_day, mode)
        minute_of_day = _parse_minute_of_day(time_expr)
    except ValueError as ex:
        return {"success": False, "error": str(ex)}

    if mode == "virtual" and target_day < current_day:
        return {"success": False, "error": "目标天数早于当前天数"}

    timezone_name = _read_kernel_timezone()
    due_epoch_ms = _build_due_epoch(
        mode, now_info, target_day, minute_of_day, timezone_name
    )
    title = (
        f"{date_idea}（第{target_day}天 {time_expr}）"
        if mode == "virtual"
        else f"{date_idea}（{time_expr}）"
    )
    create_res = call_host(
        "game/timeline/create",
        {
            "eventType": "date_invite",
            "title": title,
            "dueAtEpochMs": due_epoch_ms,
            "action": {
                "message": f"⏰ 你与角色约好的「{date_idea}」时间到了。即时约会流程正在迁移中，请先通过普通聊天继续互动。",
            },
            "context": {
                "source": f"plugin:{PLUGIN_ID}",
                "idea": date_idea,
                "mode": mode,
                "timezone": timezone_name,
            },
        },
    )
    if not create_res or not create_res.get("ok"):
        return {
            "success": False,
            "error": (create_res or {}).get("error", "创建日程失败"),
        }

    event_id = ((create_res.get("item") or {}).get("id")) or ""
    return {
        "success": True,
        "output": (
            f"已创建约会计划：{title}\n"
            f"- 模式：{mode}\n"
            f"- 时区：{timezone_name}\n"
            f"- 事件ID：{event_id}\n"
            f"- 到期后会触发提醒；即时 /date 流程将在 executor-backed 版本中恢复"
        ),
    }


def on_schedule_due(arguments):
    event_type = arguments.get("eventType", "")
    if event_type != "date_invite":
        return {}

    title = arguments.get("title", "约会")
    action_raw = arguments.get("actionJson")
    message = None

    if isinstance(action_raw, str) and action_raw.strip():
        try:
            message = (json.loads(action_raw) or {}).get("message")
        except Exception:
            message = None
    elif isinstance(action_raw, dict):
        message = action_raw.get("message")

    if not message:
        message = f"⏰ 约会计划到期：{title}。即时约会流程正在迁移中，请先通过普通聊天继续互动。"
    return {"additionalText": message}


# ─── Tools registration ───────────────────────────────────────────────────────

TOOLS = {
    "plan_date": handle_plan_date,
    "onScheduleDue": on_schedule_due,
}

TOOLS_LIST = [
    {
        "name": "plan_date",
        "description": "创建未来约会计划（timeline），到时自动提醒",
        "category": "command",
        "aliases": ["date_plan"],
        "helpShort": "创建约会日程，例：/plan_date +2 10:00 去看电影",
        "inputSchema": {"type": "object", "properties": {"args": {"type": "array"}}},
    },
    {
        "name": "onScheduleDue",
        "description": "响应 timeline 到期事件并输出约会提醒",
        "category": "hook",
        "inputSchema": {"type": "object", "properties": {"args": {"type": "object"}}},
    },
]


# ─── MCP Protocol dispatcher ──────────────────────────────────────────────────


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
                "serverInfo": {"name": "builtin_event_date", "version": "2.0.0"},
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
        if name in {"plan_date"}:
            handler_input = arguments.get("args", [])
        else:
            handler_input = arguments
        result = handler(handler_input)
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
