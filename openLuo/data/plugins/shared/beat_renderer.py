"""
Beat renderer for openLuo.
Formats narrative beats for CLI terminal output.
"""


def render_beat(beat, character_name="角色"):
    """Render a single beat to a formatted string for terminal display."""
    btype = beat.get("type", "dialogue")

    if btype == "dialogue":
        return f"{character_name}：{beat.get('text', '')}"
    elif btype == "scene":
        location = beat.get("location", "")
        desc = beat.get("desc", "")
        return f"\n▶ 【{location}】\n  {desc}\n"
    elif btype == "action":
        return f"  *{beat.get('text', '')}*"
    elif btype == "skip":
        return f"\n  ── {beat.get('text', '')} ──\n"
    return ""


def render_beats(beats, character_name="角色"):
    """Render all beats into a formatted multi-line string."""
    lines = []
    for beat in beats:
        rendered = render_beat(beat, character_name)
        if rendered:
            lines.append(rendered)
    return "\n".join(lines)


def render_resource_changes(resource_changes, current_values=None):
    """
    Render resource changes summary line (e.g., [好感度 +8 → 50]).

    Args:
        resource_changes: list of {resourceId, op, value, characterId}
        current_values: optional dict of {resourceId: newValue}
    """
    name_map = {
        "affection": "好感度",
        "trust": "信任度",
        "stamina": "体力",
        "gold": "金币",
        "mood": "心情",
    }
    parts = []
    for change in resource_changes:
        res_id = change.get("resourceId", "")
        op = change.get("op", "delta")
        value = change.get("value", 0)
        name = name_map.get(res_id, res_id)

        if op == "delta":
            try:
                v = int(float(str(value)))
                sign = "+" if v >= 0 else ""
                new_val = (current_values or {}).get(res_id)
                if new_val is not None:
                    parts.append(f"[{name} {sign}{v} → {new_val}]")
                else:
                    parts.append(f"[{name} {sign}{v}]")
            except (ValueError, TypeError):
                parts.append(f"[{name} → {value}]")
        elif op == "set":
            parts.append(f"[{name} → {value}]")

    return "\n".join(parts)


def render_cost_summary(total_costs):
    """Render a summary line of total resource costs from the chain."""
    if not total_costs:
        return ""
    name_map = {"stamina": "体力", "gold": "金币"}
    parts = []
    for res_id, delta in total_costs.items():
        name = name_map.get(res_id, res_id)
        sign = "+" if delta >= 0 else ""
        parts.append(f"{name} {sign}{int(delta)}")
    return f"消耗：{', '.join(parts)}"
