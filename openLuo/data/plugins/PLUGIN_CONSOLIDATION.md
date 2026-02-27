# 角色状态插件整合完成

## 整合结果

**原有插件（12 个）：**
- builtin_char_status_affection
- builtin_char_status_trust
- builtin_char_status_mood
- builtin_char_status_stress
- builtin_char_status_fatigue
- builtin_char_status_health
- builtin_char_status_energy
- builtin_char_status_lust
- builtin_char_status_shame
- builtin_char_status_dependency
- builtin_char_status_possessiveness
- builtin_char_status_sexual_intent

**整合后插件（3 个）：**

### 1. builtin_char_status_relationship（核心关系）
- affection（好感度）
- relationship_stage（关系阶段，派生）
- trust（信任度）

### 2. builtin_char_status_daily（日常状态）
- mood（心情）
- stress（压力）
- energy（体力）
- health（健康）
- fatigue（疲劳）

### 3. builtin_char_status_intimate（亲密关系，18+）
- shame（羞耻）
- dependency（依赖）
- possessiveness（占有欲）
- lust（欲望，隐藏）
- sexual_intent（性意愿，隐藏）

## 代码减少

- 原有代码：~1200 行（12 个插件 × 100 行）
- 整合后代码：~450 行（3 个插件 × 150 行）
- **减少 62.5%**

## 迁移方式

完全迁移自现有插件，保持：
- state_defs.jsonc 格式一致
- main.py 逻辑一致
- Hook 行为一致
- 默认值一致

## 下一步

1. **测试新插件**：启动游戏验证 3 个新插件正常工作
2. **禁用旧插件**：在旧插件的 plugin.jsonc 中添加 `"disabled": true`
3. **验证兼容性**：确保存档数据正常迁移
4. **删除旧插件**：确认无问题后删除 12 个旧插件目录

## 角色个性化配置（待实现）

由于未找到 4 个内置角色的具体定义，角色个性化配置暂未实现。

建议格式：
```
builtin_char_status_relationship/
├── state_defs.jsonc          # 默认配置
├── character_overrides.jsonc  # 角色个性化（可选）
└── main.py
```

character_overrides.jsonc 示例：
```json
{
  "characters": {
    "character_id_1": {
      "affection": {"defaultValue": "100"},
      "trust": {"defaultValue": "70"}
    }
  }
}
```
