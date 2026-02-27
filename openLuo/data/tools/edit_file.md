---
name: edit_file
category: tool
description: 直接按搜索/替换模式修改文件。
usage: @edit_file --path <file_path> --search <text> --replace <text> [--all yes]
riskLevel: high
needsConfirm: true
capabilities: fs-write,fs-edit
---
# 编辑文件工具

## 用途
直接按确定性字符串替换方式修改文件。

## 输入
- `path`：目标文件
- `search`：旧文本
- `replace`：新文本
- `all`：若为 `yes`，替换全部匹配

## 输出
- 匹配次数与更新状态
