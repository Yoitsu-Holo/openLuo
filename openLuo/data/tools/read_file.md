---
name: read_file
category: tool
description: 直接读取文件内容。
usage: @read_file --path <file_path>
riskLevel: low
needsConfirm: false
capabilities: fs-read
---
# 读取文件工具

## 用途
直接读取指定文件的文本内容。

## 输入
- `path`：目标文件路径

## 输出
- 文件内容（过长时会截断）
