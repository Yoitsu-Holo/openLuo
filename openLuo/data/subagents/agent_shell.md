---
name: agent_shell
category: subagent
description: 以独立子代理执行一条委托 shell 任务。
usage: &agent_shell <command> [--timeout 20]
riskLevel: high
needsConfirm: true
capabilities: subagent,shell
---
# Agent Shell 子代理

## 用途
执行委托给子代理的 shell 任务，适合多步骤工作流中的隔离执行。

## 输入
- `command`：委托执行的命令字符串
- `timeout`：可选，超时秒数

## 输出
- 子代理执行摘要

## 安全说明
高影响操作，始终需要确认。
