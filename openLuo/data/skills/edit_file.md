---
name: edit_file
category: skill
description: 提供文件编辑能力的替换模式、适用场景与安全说明。
usage: $edit_file --path <file_path> --search <text> --replace <text> [--all yes]
riskLevel: low
needsConfirm: false
capabilities: fs-write,fs-edit
---
# 编辑文件技能

## 用途
说明如何通过确定性的字符串替换方式修改文件内容。

## 输入
- `path`：目标文件路径
- `search`：要查找的旧文本
- `replace`：要替换成的新文本
- `all`：若为 `yes`，则替换全部匹配项

## 输出
- 推荐参数格式
- 搜索/替换的使用边界

## 安全说明
执行前请确认搜索内容准确，避免误改。
