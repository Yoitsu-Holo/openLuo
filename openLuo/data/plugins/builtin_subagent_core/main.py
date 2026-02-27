import json
import re
import subprocess
import sys
from pathlib import Path

PLUGIN_ID = "builtin_subagent_core"
MAX_READ_CHARS = 20000
MAX_OUTPUT_CHARS = 12000


def _root_dir():
    return Path(__file__).resolve().parents[3]


def _subagents_dir():
    return _root_dir() / "data" / "subagents"


def _tools_dir():
    return _root_dir() / "data" / "tools"


def _parse_bool(value, default=False):
    if value is None:
        return default
    return str(value).strip().lower() in {"1", "true", "yes", "y", "on"}


def _truncate(text, n=MAX_OUTPUT_CHARS):
    if text is None:
        return ""
    text = str(text)
    return text if len(text) <= n else text[:n] + "..."


def _merge_cli_style_options(args, options):
    merged = dict(options or {})
    positional = []
    idx = 0
    while idx < len(args):
        current = args[idx]
        if isinstance(current, str) and current.startswith("--"):
            key = current[2:]
            if key:
                next_idx = idx + 1
                if next_idx < len(args) and not str(args[next_idx]).startswith("--"):
                    merged.setdefault(key, str(args[next_idx]))
                    idx += 2
                    continue
                merged.setdefault(key, "true")
                idx += 1
                continue
        positional.append(str(current))
        idx += 1
    return positional, merged


def _load_doc(path):
    content = path.read_text(encoding="utf-8")
    match = re.match(r"\s*---\n(.*?)\n---\n(.*)\Z", content, re.S)
    if not match:
        return None

    meta = {}
    for raw in match.group(1).splitlines():
        line = raw.strip()
        if not line or ":" not in line:
            continue
        key, value = line.split(":", 1)
        meta[key.strip()] = value.strip()

    name = meta.get("name", "").strip()
    if not name:
        return None

    capabilities = [
        item.strip()
        for item in meta.get("capabilities", "").split(",")
        if item.strip()
    ]

    return {
        "name": name,
        "description": meta.get("description", ""),
        "usage": meta.get("usage", ""),
        "category": (meta.get("category", "subagent") or "subagent").strip().lower(),
        "riskLevel": meta.get("riskLevel", "high"),
        "needsConfirm": _parse_bool(meta.get("needsConfirm"), True),
        "capabilities": capabilities,
    }


def _load_execution_docs():
    docs = []
    for root in (_tools_dir(), _subagents_dir()):
        if not root.exists():
            continue
        for path in sorted(root.glob("*.md")):
            doc = _load_doc(path)
            if doc:
                docs.append(doc)
    return docs


def _tool_from_doc(doc):
    return {
        "name": doc["name"],
        "description": doc["description"],
        "category": doc["category"],
        "aliases": [],
        "helpShort": doc["description"],
        "usage": doc["usage"],
        "riskLevel": doc["riskLevel"],
        "needsConfirm": doc["needsConfirm"],
        "capabilities": doc["capabilities"],
        "timeoutSeconds": 60,
        "inputSchema": {
            "type": "object",
            "properties": {
                "args": {"type": "array"},
                "options": {"type": "object"},
            },
        },
    }


def _resolve_path(path_str):
    path = Path(path_str)
    if not path.is_absolute():
        path = _root_dir() / path
    return path.resolve()


def _format_success(actor_label, detail, extra=None):
    lines = [actor_label, "状态：成功"]
    if extra:
        lines.extend(extra)
    lines.append(detail)
    return "\n".join(lines)


def _format_failure(actor_label, error):
    return f"{actor_label}\n状态：失败\n错误：{error}"


def _format_actor(category, name):
    if category == "tool":
        return f"工具：{name}"
    return f"子代理：{name}"


def _execute_bash(category, name, args, options):
    args, options = _merge_cli_style_options(args, options)
    command = " ".join(args).strip() if args else (options.get("cmd") or "").strip()
    if not command:
        return {"success": False, "error": "缺少命令。用法：&bash <command>"}

    timeout = int(options.get("timeout", 20) or 20)
    try:
        proc = subprocess.run(
            command,
            shell=True,
            capture_output=True,
            text=True,
            timeout=max(1, timeout),
            cwd=str(_root_dir()),
        )
        stdout = _truncate(proc.stdout)
        stderr = _truncate(proc.stderr)
        extra = [f"退出码：{proc.returncode}", f"工作目录：{_root_dir()}"]
        detail_lines = []
        if stdout:
            detail_lines.append(f"标准输出：\n{stdout}")
        if stderr:
            detail_lines.append(f"错误输出：\n{stderr}")
        detail = "\n".join(detail_lines) if detail_lines else "无输出"
        if proc.returncode == 0:
            return {"success": True, "output": _format_success(_format_actor(category, name), detail, extra)}
        return {"success": False, "error": _format_failure(_format_actor(category, name), stderr or f"命令退出码为 {proc.returncode}") }
    except subprocess.TimeoutExpired:
        return {"success": False, "error": _format_failure(_format_actor(category, name), f"执行超时（{timeout}s）")}
    except Exception as exc:
        return {"success": False, "error": _format_failure(_format_actor(category, name), str(exc))}


def _execute_read_file(args, options):
    args, options = _merge_cli_style_options(args, options)
    path_str = (options.get("path") or (args[0] if args else "")).strip()
    if not path_str:
        return {"success": False, "error": "缺少路径。用法：&read_file --path <file_path>"}

    try:
        path = _resolve_path(path_str)
        if not path.exists():
            return {"success": False, "error": _format_failure("read_file", f"文件不存在：{path}")}
        content = path.read_text(encoding="utf-8", errors="replace")
        detail = _truncate(content, MAX_READ_CHARS)
        return {"success": True, "output": _format_success("工具：read_file", detail, [f"文件：{path}"])}
    except Exception as exc:
        return {"success": False, "error": _format_failure("工具：read_file", str(exc))}


def _execute_write_file(args, options):
    args, options = _merge_cli_style_options(args, options)
    path_str = (options.get("path") or (args[0] if args else "")).strip()
    if not path_str:
        return {"success": False, "error": "缺少路径。用法：&write_file --path <file_path> --content <text>"}

    content = options.get("content")
    if content is None:
        content = " ".join(args[1:]) if len(args) >= 2 else ""

    try:
        path = _resolve_path(path_str)
        path.parent.mkdir(parents=True, exist_ok=True)
        text = str(content)
        path.write_text(text, encoding="utf-8")
        return {
            "success": True,
            "output": _format_success("工具：write_file", f"已写入 {len(text)} 个字符", [f"文件：{path}"]),
        }
    except Exception as exc:
        return {"success": False, "error": _format_failure("工具：write_file", str(exc))}


def _execute_edit_file(args, options):
    args, options = _merge_cli_style_options(args, options)
    path_str = (options.get("path") or (args[0] if args else "")).strip()
    search = options.get("search")
    replace = options.get("replace")
    if search is None and len(args) >= 2:
        search = args[1]
    if replace is None and len(args) >= 3:
        replace = args[2]
    if not path_str or search is None or replace is None:
        return {
            "success": False,
            "error": "缺少参数。用法：&edit_file --path <file_path> --search <text> --replace <text> [--all yes]",
        }

    replace_all = _parse_bool(options.get("all"), False)
    try:
        path = _resolve_path(path_str)
        if not path.exists():
            return {"success": False, "error": _format_failure("工具：edit_file", f"文件不存在：{path}")}
        old = path.read_text(encoding="utf-8", errors="replace")
        if replace_all:
            new = old.replace(search, replace)
            count = old.count(search)
        else:
            new = old.replace(search, replace, 1)
            count = 1 if search in old else 0
        if count == 0:
            return {"success": False, "error": _format_failure("工具：edit_file", "未找到搜索文本")}
        path.write_text(new, encoding="utf-8")
        return {
            "success": True,
            "output": _format_success("工具：edit_file", f"已修改 {count} 处匹配", [f"文件：{path}"]),
        }
    except Exception as exc:
        return {"success": False, "error": _format_failure("工具：edit_file", str(exc))}


EXECUTORS = {
    "bash": lambda args, options: _execute_bash("tool", "bash", args, options),
    "agent_shell": lambda args, options: _execute_bash("subagent", "agent_shell", args, options),
    "read_file": _execute_read_file,
    "write_file": _execute_write_file,
    "edit_file": _execute_edit_file,
}


def dispatch(request):
    method = request.get("method")
    req_id = request.get("id")
    params = request.get("params", {})

    if method == "initialize":
        return {
            "jsonrpc": "2.0",
            "id": req_id,
            "result": {
                "protocolVersion": "2024-11-05",
                "capabilities": {"tools": {}},
                "serverInfo": {"name": PLUGIN_ID, "version": "1.0.0"},
            },
        }

    if method == "tools/list":
        tools = [_tool_from_doc(doc) for doc in _load_execution_docs()]
        return {"jsonrpc": "2.0", "id": req_id, "result": {"tools": tools}}

    if method == "tools/call":
        name = params.get("name")
        arguments = params.get("arguments", {})
        args = arguments.get("args", []) or []
        options = arguments.get("options", {}) or {}
        executor = EXECUTORS.get(name)
        if executor is None:
            return {
                "jsonrpc": "2.0",
                "id": req_id,
                "error": {"code": -32601, "message": f"Unknown subagent: {name}"},
            }

        result = executor(args, options)
        return {
            "jsonrpc": "2.0",
            "id": req_id,
            "result": {"content": [{"type": "text", "text": json.dumps(result, ensure_ascii=False)}]},
        }

    return {
        "jsonrpc": "2.0",
        "id": req_id,
        "error": {"code": -32601, "message": f"Unknown method: {method}"},
    }


def main():
    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        try:
            request = json.loads(line)
            response = dispatch(request)
            print(json.dumps(response, ensure_ascii=False), flush=True)
        except Exception as exc:
            print(
                json.dumps(
                    {
                        "jsonrpc": "2.0",
                        "id": None,
                        "error": {"code": -32603, "message": str(exc)},
                    },
                    ensure_ascii=False,
                ),
                flush=True,
            )


if __name__ == "__main__":
    main()
