---
name: bash
category: tool
description: 直接调用本地 shell 工具执行一条命令。
usage: @bash <command> [--timeout 20]
riskLevel: high
needsConfirm: true
capabilities: shell,local-process
---
# Bash 工具

## 用途
直接在本地运行环境执行一条 shell 命令。

## 输入
- `command`：命令字符串
- `timeout`：可选，超时秒数

## 输出
- 退出码
- 标准输出/错误输出摘要

## 安全说明
高风险操作，需要确认。
