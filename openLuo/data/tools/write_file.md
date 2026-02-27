---
name: write_file
category: tool
description: 直接写入文件内容。
usage: @write_file --path <file_path> --content <text>
riskLevel: high
needsConfirm: true
capabilities: fs-write
---
# 写入文件工具

## 用途
直接创建或覆盖文本文件。

## 输入
- `path`：目标文件路径
- `content`：写入内容

## 输出
- 写入字节数与执行状态
