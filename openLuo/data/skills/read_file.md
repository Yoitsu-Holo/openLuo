---
name: read_file
category: skill
description: 提供文件读取能力的用途、参数格式与安全约束说明。
usage: $read_file --path <file_path>
riskLevel: low
needsConfirm: false
capabilities: fs-read
---
# 读取文件技能

## 用途
说明如何安全读取文件内容，用于查看、调试或作为上下文输入。

## 输入
- `path`：目标文件路径

## 输出
- 推荐参数格式
- 适用场景与限制

## 安全说明
这是只读操作，不会修改文件。
