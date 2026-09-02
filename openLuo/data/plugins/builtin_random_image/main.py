import sys
import json
import base64
import urllib.request
import urllib.error
import ssl
import subprocess

PLUGIN_ID = "builtin_random_image"
_req_id = 0

DUCK_MO_URL = "https://api.mossia.top/duckMo"
PIXIV_CAT_MAX_ATTEMPTS = 3
_ssl_ctx = None


def _ssl_context():
    global _ssl_ctx
    if _ssl_ctx is None:
        _ssl_ctx = ssl.create_default_context()
    return _ssl_ctx


def call_host(method, params=None):
    global _req_id
    _req_id += 1
    req = {"jsonrpc": "2.0", "id": f"ri{_req_id}", "method": method, "params": params or {}}
    print(json.dumps(req, ensure_ascii=False), flush=True)
    line = sys.stdin.readline()
    return json.loads(line).get("result", {})


def download_image(url):
    """Download image via curl subprocess (urllib fails on pixiv.cat from Python on this distro)."""
    try:
        result = subprocess.run(
            ["curl", "-s", "--max-time", "25", "-L",
             "-A", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36",
             "-e", "https://www.pixiv.net/",
             "-o", "-", url],
            capture_output=True, timeout=30)
        if result.returncode != 0:
            return None, None, f"curl exit {result.returncode}"
        data = result.stdout
        if not data:
            return None, None, "empty response"
        mime = "image/png" if url.endswith(".png") else "image/jpeg"
        return data, mime, None
    except Exception as e:
        return None, None, str(e)


def download_with_retry(url, max_attempts):
    import time
    for attempt in range(1, max(1, max_attempts) + 1):
        try:
            data, mime, error = download_image(url)
            if data:
                return data, mime
        except Exception:
            pass
        if attempt < max_attempts:
            time.sleep(0.4 * attempt)
    return None, None

def extract_pixiv_id(source_url):
    import re
    from urllib.parse import urlparse
    if not source_url: return None
    try:
        parsed = urlparse(source_url)
        filename = parsed.path.rsplit("/", 1)[-1] if "/" in parsed.path else parsed.path
        m = re.match(r"^(\d+)", filename)
        return m.group(1) if m else None
    except Exception:
        return None


def handle_fetch_random_image(args):
    bridge = (args or {}).get("bridgeContext", {})
    game_id = bridge.get("GameId") or (args or {}).get("gameId", "")
    if not game_id:
        gs = call_host("core/session/get", {"gameId": bridge.get("GameId") or ""}) or {}
        game_id = gs.get("gameId") or gs.get("id", "")

    # duckMo
    source_url = None
    try:
        req = urllib.request.Request(DUCK_MO_URL, headers={
            "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36"})
        with urllib.request.urlopen(req, timeout=15, context=_ssl_context()) as resp:
            payload = json.loads(resp.read())
        items = (payload or {}).get("data", [])
        if items:
            urls = items[0].get("urlsList", [])
            if urls:
                source_url = urls[0].get("url")
    except Exception as e:
        return {"content": [{"type": "text", "text": f"duckMo error: {e}"}]}

    if not source_url:
        return {"content": [{"type": "text", "text": "no image url"}]}

    # pixiv.cat download
    artwork_id = extract_pixiv_id(source_url)
    pixiv_artwork_url = f"https://www.pixiv.net/artworks/{artwork_id}" if artwork_id else None
    pixiv_cat_url = f"https://pixiv.cat/{artwork_id}.png" if artwork_id else None

    if pixiv_cat_url:
        img, mime = download_with_retry(pixiv_cat_url, PIXIV_CAT_MAX_ATTEMPTS)
        if img:
            b64 = base64.b64encode(img).decode("ascii")
            return {"content": [{"type": "image", "data": b64, "mimeType": mime}]}
        else:
            return {"content": [{"type": "text", "text": f"download failed: {pixiv_cat_url}"}]}
    if pixiv_artwork_url:
        return {"content": [{"type": "text", "text": pixiv_artwork_url}]}
    return {"content": [{"type": "text", "text": "download failed"}]}


TOOLS = {"fetch_random_image": handle_fetch_random_image}


def dispatch(request):
    method = request.get("method")
    req_id = request.get("id")

    if method == "initialize":
        return {"jsonrpc": "2.0", "id": req_id,
                "result": {"protocolVersion": "2024-11-05", "capabilities": {},
                           "serverInfo": {"name": PLUGIN_ID, "version": "1.0.0"}}}

    if method == "tools/call":
        params = request.get("params", {})
        tool_name = params.get("name", "")
        arguments = params.get("arguments", {})
        handler = TOOLS.get(tool_name)
        if handler is None:
            return {"jsonrpc": "2.0", "id": req_id,
                    "result": {"content": [{"type": "text",
                               "text": json.dumps({"error": f"unknown: {tool_name}"},
                                                  ensure_ascii=False)}]}}
        result = handler(arguments)
        if isinstance(result, dict) and "content" in result:
            return {"jsonrpc": "2.0", "id": req_id, "result": result}
        return {"jsonrpc": "2.0", "id": req_id,
                "result": {"content": [{"type": "text",
                           "text": json.dumps(result, ensure_ascii=False)}]}}

    return {"jsonrpc": "2.0", "id": req_id, "error": {"code": -32601, "message": f"unknown: {method}"}}


def main():
    for line in sys.stdin:
        line = line.strip()
        if not line: continue
        try:
            request = json.loads(line)
            response = dispatch(request)
            print(json.dumps(response, ensure_ascii=False), flush=True)
        except Exception as e:
            print(json.dumps({"jsonrpc": "2.0", "id": None,
                              "error": {"code": -32603, "message": str(e)}}), flush=True)


if __name__ == "__main__":
    main()
