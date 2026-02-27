import json
import os
from pathlib import Path


def load_character_config(plugin_dir, character_id, fallback_to_default=True):
    """
    通用配置加载器

    Args:
        plugin_dir: 插件目录路径（__file__ 所在目录）
        character_id: 角色ID（如 "builtin-ojousama"）
        fallback_to_default: 找不到角色配置时是否降级到默认配置

    Returns:
        配置字典

    查找顺序：
    1. {plugin_dir}/config/{character_id}.jsonc
    2. {plugin_dir}/config/default.jsonc（如果 fallback_to_default=True）
    """
    config_dir = Path(plugin_dir) / "config"

    # Try character-specific config
    for ext in [".jsonc", ".json"]:
        config_path = config_dir / f"{character_id}{ext}"
        if config_path.exists():
            return _load_jsonc(config_path)

    # Fallback to default
    if fallback_to_default:
        for ext in [".jsonc", ".json"]:
            default_path = config_dir / f"default{ext}"
            if default_path.exists():
                return _load_jsonc(default_path)

    return {}


def _load_jsonc(path):
    """Load JSON with // comments stripped"""
    with open(path, "r", encoding="utf-8") as f:
        lines = [line.split("//")[0] for line in f]
        return json.loads("".join(lines))
