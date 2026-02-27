---
name: write_file
category: skill
description: 提供文件写入能力的用途、风险与调用说明。
usage: $write_file --path <file_path> --content <text>
riskLevel: low
needsConfirm: false
capabilities: fs-write
---
# 写入文件技能

## 用途
说明如何创建或覆盖文本文件，以及何时应要求确认。

## 输入
- `path`：目标文件路径
- `content`：要写入的文本内容

## 输出
- 推荐参数格式
- 风险说明与确认要求

## 安全说明
此操作会修改本地文件内容。
