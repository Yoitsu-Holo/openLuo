import json
import re
import sys
from pathlib import Path

PLUGIN_ID = "builtin_skill_core"


def _root_dir():
    return Path(__file__).resolve().parents[3]


def _skills_dir():
    return _root_dir() / "data" / "skills"


def _parse_bool(value, default=False):
    if value is None:
        return default
    return str(value).strip().lower() in {"1", "true", "yes", "y", "on"}


def _load_doc(path):
    content = path.read_text(encoding="utf-8")
    match = re.match(r"\s*---\n(.*?)\n---\n(.*)\Z", content, re.S)
    if not match:
        return None

    frontmatter = {}
    for raw in match.group(1).splitlines():
        line = raw.strip()
        if not line or ":" not in line:
            continue
        key, value = line.split(":", 1)
        frontmatter[key.strip()] = value.strip()

    name = frontmatter.get("name", "").strip()
    if not name:
        return None

    capabilities = [
        item.strip()
        for item in frontmatter.get("capabilities", "").split(",")
        if item.strip()
    ]

    return {
        "name": name,
        "description": frontmatter.get("description", ""),
        "usage": frontmatter.get("usage", ""),
        "category": "skill",
        "riskLevel": frontmatter.get("riskLevel", "low"),
        "needsConfirm": _parse_bool(frontmatter.get("needsConfirm"), False),
        "capabilities": capabilities,
        "body": match.group(2).strip(),
    }


def _load_docs():
    docs = []
    root = _skills_dir()
    if not root.exists():
        return docs

    for path in sorted(root.glob("*.md")):
        doc = _load_doc(path)
        if doc:
            docs.append(doc)
    return docs


def _tool_from_doc(doc):
    return {
        "name": doc["name"],
        "description": doc["description"],
        "category": "skill",
        "aliases": [],
        "helpShort": doc["description"],
        "usage": doc["usage"],
        "riskLevel": doc["riskLevel"],
        "needsConfirm": doc["needsConfirm"],
        "capabilities": doc["capabilities"],
        "timeoutSeconds": 15,
        "inputSchema": {
            "type": "object",
            "properties": {
                "args": {"type": "array"},
                "options": {"type": "object"},
            },
        },
    }


def _render_doc(doc):
    lines = [
        f"技能：${doc['name']}",
        f"说明：{doc['description']}",
    ]
    if doc.get("usage"):
        lines.append(f"用法：{doc['usage']}")
    if doc.get("capabilities"):
        lines.append(f"能力：{', '.join(doc['capabilities'])}")
    lines.append("")
    lines.append(doc["body"])
    return "\n".join(lines).strip()


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
        tools = [_tool_from_doc(doc) for doc in _load_docs()]
        return {"jsonrpc": "2.0", "id": req_id, "result": {"tools": tools}}

    if method == "tools/call":
        name = params.get("name")
        doc = next((item for item in _load_docs() if item["name"] == name), None)
        if doc is None:
            return {
                "jsonrpc": "2.0",
                "id": req_id,
                "error": {"code": -32601, "message": f"Unknown skill: {name}"},
            }

        result = {
            "success": True,
            "output": _render_doc(doc),
        }
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
