import sys
import json

PLUGIN_ID = "builtin_inventory_shop"
_req_id = 0
PAGE_SIZE = 8


def call_host(method, params=None):
    global _req_id
    _req_id += 1
    req = {
        "jsonrpc": "2.0",
        "id": f"p{_req_id}",
        "method": method,
        "params": params or {},
    }
    print(json.dumps(req, ensure_ascii=False), flush=True)
    line = sys.stdin.readline()
    return json.loads(line).get("result", {})


def log(level, msg):
    call_host("game/log", {"pluginId": PLUGIN_ID, "level": level, "msg": msg})


def rarity_label(r):
    return {"Uncommon": "稀有", "Rare": "珍贵", "Special": "特殊"}.get(r, "")


def handle_shop(args):
    categories_result = call_host("game/shop/categories") or {}
    categories = (
        categories_result.get("categories", [])
        if isinstance(categories_result, dict)
        else []
    )
    if not categories:
        return {"success": False, "error": "无法获取物品列表"}

    if not args:
        lines = ["【商店】选择分类：\n"]
        for idx, category in enumerate(categories, 1):
            lines.append(
                f"  {idx}. {category.get('categoryName', category.get('name', '分类'))}（{category.get('count', 0)} 件）"
            )
        lines.append("\n输入 /shop <编号> 进入分类")
        return {"success": True, "output": "\n".join(lines)}

    try:
        cat_index = int(args[0])
    except ValueError:
        return {
            "success": False,
            "error": f"请输入有效的分类编号（1-{len(categories)}）",
        }

    if cat_index < 1 or cat_index > len(categories):
        return {
            "success": False,
            "error": f"请输入有效的分类编号（1-{len(categories)}）",
        }

    page = int(args[1]) if len(args) > 1 and args[1].isdigit() else 1
    listing = (
        call_host(
            "game/shop/list",
            {"categoryIndex": cat_index, "page": page, "pageSize": PAGE_SIZE},
        )
        or {}
    )
    if not listing or not listing.get("ok"):
        return {"success": False, "error": listing.get("error", "无法获取商品列表")}

    items = listing.get("items", [])
    total_pages = listing.get("totalPages", 1)
    current_page = listing.get("page", page)
    gold = listing.get("currentGold", 0)
    cat_name = listing.get("categoryName", "商店")

    lines = [
        f"【{cat_name}】第 {current_page}/{total_pages} 页  (当前金币：{gold} G)\n"
    ]
    for item in items:
        global_idx = item.get("index", 0)
        rl = rarity_label(item.get("rarity", "Common"))
        rarity_str = f" [{rl}]" if rl else ""
        lines.append(
            f"  {cat_index}.{global_idx:<4} {item.get('name', ''):<12} {item.get('price', 0):>5} G{rarity_str}"
        )
        lines.append(f"         {item.get('description', '')}")

    hint = (
        f"\n输入 /shop {cat_index} <页码> 翻页，/buy {cat_index}.<编号> 购买"
        if total_pages > 1
        else f"\n输入 /buy {cat_index}.<编号> 购买"
    )
    lines.append(hint)
    return {"success": True, "output": "\n".join(lines)}


def handle_buy(args):
    if not args:
        return {"success": False, "error": "请指定商品编号，例：/buy 1.3"}

    parts = args[0].split(".")
    if len(parts) != 2 or not parts[0].isdigit() or not parts[1].isdigit():
        return {
            "success": False,
            "error": "格式错误，请使用 /buy <分类>.<编号>，例：/buy 2.1",
        }

    cat_index, item_index = int(parts[0]), int(parts[1])
    result = call_host(
        "game/shop/buy", {"categoryIndex": cat_index, "itemIndex": item_index}
    )
    if not result or not result.get("ok"):
        return {"success": False, "error": result.get("error", "购买失败")}

    return {
        "success": True,
        "output": (
            f"购买成功：{result.get('itemName', '')}（-{result.get('price', 0)} G）\n"
            f"{result.get('description', '')}\n"
            f"剩余金币：{result.get('remainingGold', 0)} G"
        ),
    }


def handle_inventory(args):
    inv = call_host("game/inventory/get")
    all_items = call_host("game/items/list")
    if not inv:
        return {"success": True, "output": "背包是空的。去 /shop 逛逛吧！"}

    item_map = {i["id"]: i for i in all_items} if all_items else {}
    lines = ["【背包】\n"]
    for entry in inv:
        item_id = (
            entry[0]
            if isinstance(entry, list)
            else entry.get("itemId") or entry.get("Item1")
        )
        qty = (
            entry[1]
            if isinstance(entry, list)
            else entry.get("quantity") or entry.get("Item2", 1)
        )
        item = item_map.get(item_id)
        if item:
            lines.append(f"  {item['name']:<14} x{qty}   {item['description']}")
    lines.append("\n输入 /give <物品名> 赠送给角色")
    return {"success": True, "output": "\n".join(lines)}


TOOLS = {
    "shop": handle_shop,
    "buy": handle_buy,
    "inventory": handle_inventory,
}

TOOLS_LIST = [
    {
        "name": "shop",
        "description": "浏览商店，购买礼物道具",
        "category": "command",
        "aliases": [],
        "inputSchema": {"type": "object", "properties": {"args": {"type": "array"}}},
    },
    {
        "name": "buy",
        "description": "购买商品，例：/buy 1.3",
        "category": "command",
        "aliases": [],
        "inputSchema": {"type": "object", "properties": {"args": {"type": "array"}}},
    },
    {
        "name": "inventory",
        "description": "查看背包中的物品",
        "category": "command",
        "aliases": ["bag", "inv"],
        "inputSchema": {"type": "object", "properties": {"args": {"type": "array"}}},
    },
]


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
                "serverInfo": {"name": "builtin_inventory_shop", "version": "1.0.0"},
            },
        }

    if method == "tools/list":
        return {"jsonrpc": "2.0", "id": req_id, "result": {"tools": TOOLS_LIST}}

    if method == "tools/call":
        name = params.get("name")
        arguments = params.get("arguments", {})
        args = arguments.get("args", [])
        handler = TOOLS.get(name)
        if handler is None:
            return {
                "jsonrpc": "2.0",
                "id": req_id,
                "error": {"code": -32601, "message": f"Unknown tool: {name}"},
            }
        result = handler(args)
        return {
            "jsonrpc": "2.0",
            "id": req_id,
            "result": {
                "content": [
                    {"type": "text", "text": json.dumps(result, ensure_ascii=False)}
                ]
            },
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
        except Exception as e:
            print(
                json.dumps(
                    {
                        "jsonrpc": "2.0",
                        "id": None,
                        "error": {"code": -32603, "message": str(e)},
                    }
                ),
                flush=True,
            )


if __name__ == "__main__":
    main()
