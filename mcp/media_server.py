"""openLuo 媒体 MCP server：随机图片获取（标准 MCP 协议）。

由宿主 config/mcp-servers.jsonc 以 stdio transport 启动。
工具返回 data URL（base64），宿主直接作为图片消息发送。
"""

from __future__ import annotations

import asyncio
import base64
import json
import re
import time
import urllib.request

from mcp.server import MCPServer

DUCK_MO_URL = "https://api.mossia.top/duckMo"
PIXIV_CAT_MAX_ATTEMPTS = 3
_USER_AGENT = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36"

mcp = MCPServer(
    name="openluo-media",
    version="1.0.0",
    instructions="媒体工具：随机图片获取。图片以 data URL 返回，宿主直接发送，勿用 markdown 引用。",
)


def _get(url: str, timeout: float = 15.0) -> bytes:
    req = urllib.request.Request(url, headers={"User-Agent": _USER_AGENT})
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        return resp.read()


def _extract_pixiv_id(source_url: str) -> str | None:
    """从 pixiv 图片 URL 提取作品 id（文件名数字前缀）。"""
    filename = source_url.rstrip("/").rsplit("/", 1)[-1]
    m = re.match(r"^(\d+)", filename)
    return m.group(1) if m else None


def _download_with_retry(url: str, max_attempts: int = PIXIV_CAT_MAX_ATTEMPTS) -> bytes | None:
    for attempt in range(1, max(1, max_attempts) + 1):
        try:
            data = _get(url)
            if data:
                return data
        except Exception:
            pass
        if attempt < max_attempts:
            time.sleep(0.4 * attempt)
    return None


def _fetch_random_image() -> str:
    """duckMo JSON → pid → pixiv.cat 下载 → 缩放 → data URL。"""
    payload = json.loads(_get(DUCK_MO_URL).decode("utf-8"))
    items = (payload or {}).get("data", [])
    source_url = None
    if items:
        urls = items[0].get("urlsList", [])
        if urls:
            source_url = urls[0].get("url")
    if not source_url:
        raise RuntimeError("no image url from source")

    artwork_id = _extract_pixiv_id(source_url)
    pixiv_cat_url = f"https://pixiv.cat/{artwork_id}.png" if artwork_id else source_url
    img = _download_with_retry(pixiv_cat_url)
    if not img:
        raise RuntimeError("image download failed")

    return _to_data_url(img, pixiv_cat_url.endswith(".png"))


def _to_data_url(img: bytes, is_png: bool) -> str:
    """缩放（最长边 1280px）并编码为 JPEG data URL，控制消息体积。"""
    try:
        from io import BytesIO
        from PIL import Image, ImageOps

        with Image.open(BytesIO(img)) as im:
            im = ImageOps.exif_transpose(im)
            im.thumbnail((1280, 1280))
            out = BytesIO()
            im.convert("RGB").save(out, format="JPEG", quality=82, optimize=True)
            payload = out.getvalue()
            return f"data:image/jpeg;base64,{base64.b64encode(payload).decode('ascii')}"
    except Exception:
        # 缩放失败则原样返回（保留原始 mime）
        mime = "image/png" if is_png else "image/jpeg"
        return f"data:{mime};base64,{base64.b64encode(img).decode('ascii')}"


@mcp.tool()
def fetch_random_image() -> str:
    """获取一张随机图片并返回 data URL（base64 编码）。

    流程：从图源 API（duckMo）取随机插画 → 解析作品 pid → 经 pixiv.cat
    代理下载图片。返回格式 data:<mime>;base64,<payload>，宿主直接作为
    图片消息发送，无需 markdown 引用。

    调用方上下文：宿主在 arguments 注入 _openluo_game_id / _openluo_session_id /
    _openluo_turn_id（可配置关闭）。无参工具自动忽略这些键；需按游戏空间隔离
    的工具请用 lowlevel Server 的 call_tool handler 读取 arguments 字典。
    """
    return _fetch_random_image()


if __name__ == "__main__":
    asyncio.run(mcp.run_stdio_async())
