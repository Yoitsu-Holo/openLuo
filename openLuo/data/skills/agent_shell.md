---
name: agent_shell
category: skill
description: 提供子代理 shell 任务的使用说明、风险与调用方式。
usage: $agent_shell <command> [--timeout 20]
riskLevel: low
needsConfirm: false
capabilities: subagent,shell
---
# Agent Shell 技能

## 用途
说明如何以子代理方式委托 shell 操作，并返回摘要结果。

## 输入
- `command`：委托执行的命令字符串
- `timeout`：可选，超时秒数

## 输出
- 委托方式说明
- 风险与确认要求

## 安全说明
这是高影响操作，始终需要确认后才能执行。
