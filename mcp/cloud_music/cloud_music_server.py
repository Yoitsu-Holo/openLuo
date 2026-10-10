"""openLuo 网易云音乐 MCP server：双后端搜索（标准 MCP 协议）。

由宿主 config/mcp-servers.jsonc 以 stdio transport 启动。

后端一（匿名，无需凭据，默认）：网易云 /api/cloudsearch/pc。
  - cloud_music_search：返回歌曲的**数字 id**（1860163 形态）+ 网页 URL，
    供 QQ 音乐卡 / 分享链接使用（推荐点歌发卡走本工具）。

后端二（官方开放平台 openapi.music.163.com，需凭据）：
  - 配置环境变量（stdio 子进程继承父进程环境）：
      NCM_OPENAPI_APP_ID      开放平台应用 appId
      NCM_OPENAPI_PRIVATE_KEY 应用 RSA 私钥（原始 X509 base64，无头尾无换行）
      NCM_OPENAPI_ACCESS_TOKEN 可选：已有匿名 accessToken 时直接复用（否则自动
                               anonymous 登录获取并缓存 7 天，进程重启后重新获取）
  - cloud_music_official_search：返回官方歌曲信息文本。注意官方返回的是 32 位
    hex 资源 id（1CE4... 形态），与网易云网页/客户端的数字 id 不同，**不能**用于
    QQ 音乐卡；本工具定位是合规、稳定的信息检索（名称/艺人/专辑/时长/可播状态）。
  - 依赖 cryptography（pip install cryptography）。未配置凭据或缺少依赖时，
    工具返回引导文案，不影响匿名工具。

登录态能力（每日推荐/我的歌单）不在本 server：搜索与 anonymous 令牌均不需扫码登录。
"""

from __future__ import annotations

import asyncio
import json
import os
import time
import urllib.parse
import urllib.request

from mcp.server import MCPServer
from mcp.server.mcpserver.exceptions import ToolError

_SEARCH_URL = "https://music.163.com/api/cloudsearch/pc"
_OPENAPI_BASE = "https://openapi.music.163.com/openapi"
_REQUEST_TIMEOUT = 15.0
_MAX_LIMIT = 20
_USER_AGENT = (
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
    "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36"
)

# category -> cloudsearch stype（与 cloud-music-mcp 保持一致）
_CATEGORY_STYPE = {
    "song": 1,
    "album": 10,
    "artist": 100,
    "playlist": 1000,
}
_DEFAULT_CATEGORY = "song"

# 官方开放平台设备参数（个人接入无分配值时使用官方示例设备；
# 企业/厂商接入请替换为平台分配的 channel/os/brand/deviceType）
_OPENAPI_DEVICE = (
    '{"clientIp":"191.1.1.3","deviceType":"mobile","os":"mobilegame","appVer":"0.1",'
    '"channel":"yanyun","model":"xtc","deviceId":"123321","brand":"yanyun","osVer":"8.1.0"}'
)

_ENV_APP_ID = "NCM_OPENAPI_APP_ID"
_ENV_PRIVATE_KEY = "NCM_OPENAPI_PRIVATE_KEY"
_ENV_ACCESS_TOKEN = "NCM_OPENAPI_ACCESS_TOKEN"


# ─────────────────────────────────────────────────────────────────────
# 后端一：匿名 cloudsearch（数字 id，发卡用）
# ─────────────────────────────────────────────────────────────────────

def _fetch(query: dict[str, str | int]) -> dict:
    url = f"{_SEARCH_URL}?{urllib.parse.urlencode(query)}"
    req = urllib.request.Request(
        url,
        headers={
            "User-Agent": _USER_AGENT,
            "Referer": "https://music.163.com/",
        },
    )
    with urllib.request.urlopen(req, timeout=_REQUEST_TIMEOUT) as resp:
        return json.loads(resp.read().decode("utf-8"))


def _song_url(song_id: int | str) -> str:
    return f"https://music.163.com/#/song?id={song_id}"


def _search(keyword: str, category: str, limit: int) -> str:
    stype = _CATEGORY_STYPE.get(category, _CATEGORY_STYPE[_DEFAULT_CATEGORY])
    payload = _fetch(
        {
            "s": keyword,
            "type": stype,
            "offset": 0,
            "limit": max(1, min(int(limit), _MAX_LIMIT)),
            "total": "true",
        }
    )
    if payload.get("code") != 200:
        raise RuntimeError(payload.get("message") or f"search failed (code={payload.get('code')})")

    result = payload.get("result") or {}
    if category == "song":
        songs = result.get("songs") or []
        if not songs:
            return "未找到相关歌曲"
        lines = [
            "候选歌曲(数字 id 可发卡,请原样保留第一列 id 用于下一步 share_song):"
        ]
        for i, song in enumerate(songs, 1):
            artists = ",".join(a.get("name", "") for a in (song.get("ar") or []))
            sid = song.get("id", "")
            lines.append(f"[{i}] id={sid} | {song.get('name', '')} - {artists} | {_song_url(sid)}")
        return "\n".join(lines)
    if category == "album":
        albums = result.get("albums") or []
        if not albums:
            return "未找到相关专辑"
        return "\n".join(
            f"{i}. {a.get('name', '')} - {(a.get('artist') or {}).get('name', '')} (ID: {a.get('id', '')})"
            for i, a in enumerate(albums, 1)
        )
    if category == "artist":
        artists = result.get("artists") or []
        if not artists:
            return "未找到相关歌手"
        return "\n".join(
            f"{i}. {a.get('name', '')} (ID: {a.get('id', '')})"
            for i, a in enumerate(artists, 1)
        )
    playlists = result.get("playlists") or []
    if not playlists:
        return "未找到相关歌单"
    return "\n".join(
        f"{i}. {p.get('name', '')} (ID: {p.get('id', '')}, {p.get('trackCount', 0)}首)"
        for i, p in enumerate(playlists, 1)
    )


# ─────────────────────────────────────────────────────────────────────
# 后端二：网易云开放平台（RSA_SHA256 签名，anonymous 令牌）
# ─────────────────────────────────────────────────────────────────────

def _openapi_config() -> dict | None:
    """读取官方后端配置；未完整配置返回 None。"""
    app_id = os.environ.get(_ENV_APP_ID, "").strip()
    private_key = os.environ.get(_ENV_PRIVATE_KEY, "").strip()
    if not app_id or not private_key:
        return None
    return {"appId": app_id, "privateKey": private_key}


def _load_signer(private_key_b64: str):
    """惰性加载 cryptography 并返回私钥签名器；未安装抛 RuntimeError。"""
    try:
        from cryptography.hazmat.primitives import hashes, serialization
        from cryptography.hazmat.primitives.asymmetric import padding, utils
    except ImportError as ex:  # pragma: no cover - 依赖缺失路径
        raise RuntimeError(
            "openapi 后端需要 cryptography 库：pip install cryptography（且须在运行 openLuo 的 python3 环境）"
        ) from ex

    raw = "".join(private_key_b64.split())
    pem_body = "\n".join(raw[i : i + 64] for i in range(0, len(raw), 64))
    pem = f"-----BEGIN PRIVATE KEY-----\n{pem_body}\n-----END PRIVATE KEY-----"
    key = serialization.load_pem_private_key(pem.encode("utf-8"), password=None)
    import hashlib
    import base64

    def sign(content: str) -> str:
        digest = hashlib.sha256(content.encode("utf-8")).digest()
        sig = key.sign(digest, padding.PKCS1v15(), utils.Prehashed(hashes.SHA256()))
        return base64.b64encode(sig).decode("ascii")

    return sign


def _format_params(params: dict) -> str:
    """官方签名组串：剔除 sign/空值，按键 ASCII 排序，k=v&k=v。"""
    filtered = {
        k: v for k, v in params.items()
        if k != "sign" and v != "" and not isinstance(v, bytes)
    }
    return "&".join(f"{k}={v}" for k, v in sorted(filtered.items()))


class _OpenApiSession:
    """轻量状态：anonymous token 缓存（进程内，7 天过期按平台 expireTime 刷新）。"""

    _token: str | None = os.environ.get(_ENV_ACCESS_TOKEN, "").strip() or None
    _expires_at: float = 0.0

    def __init__(self, config: dict):
        self._config = config
        self._signer = None  # 惰性:首次调用才 import cryptography

    def _do(self, api: str, biz: dict, token: str | None = None) -> dict:
        if self._signer is None:
            self._signer = _load_signer(self._config["privateKey"])
        params = {
            "appId": self._config["appId"],
            "timestamp": int(time.time() * 1000),
            "device": _OPENAPI_DEVICE,
            "signType": "RSA_SHA256",
            "bizContent": json.dumps(biz, separators=(",", ":")),
        }
        if token:
            params["accessToken"] = token
        content = _format_params(params)
        sign = self._signer(content)
        params["sign"] = sign  # 原始 base64；组 URL 时统一编码（签名基于未编码原值）
        query = "&".join(f"{k}={urllib.parse.quote(str(v), safe='')}" for k, v in params.items())
        url = f"{_OPENAPI_BASE}/{api}?{query}"
        req = urllib.request.Request(url, headers={"User-Agent": _USER_AGENT})
        with urllib.request.urlopen(req, timeout=_REQUEST_TIMEOUT) as resp:
            return json.loads(resp.read().decode("utf-8", "replace"))

    def token(self) -> str:
        now = time.time()
        if self._token and now < self._expires_at:
            return self._token
        resp = self._do("music/basic/oauth2/login/anonymous", {"clientId": self._config["appId"]})
        if resp.get("code") != 200:
            raise RuntimeError(resp.get("message") or f"anonymous login failed (code={resp.get('code')})")
        data = resp.get("data") or {}
        token = data.get("accessToken") or ""
        if not token:
            raise RuntimeError("anonymous login returned no accessToken")
        self._token = token
        # expireTime 单位秒；留 60s 余量提前刷新
        self._expires_at = now + int(data.get("expireTime") or 604800) - 60
        return token

    def call(self, api: str, biz: dict) -> dict:
        resp = self._do(api, biz, self.token())
        if resp.get("code") not in (200, None):
            raise RuntimeError(resp.get("message") or f"api error (code={resp.get('code')})")
        return resp


def _official_search(cfg: dict, keyword: str, limit: int, offset: int) -> str:
    session = _OpenApiSession(cfg)
    resp = session.call(
        "music/basic/search/song/get/v2",
        {"keyword": keyword, "limit": str(max(1, min(int(limit), 500))), "offset": str(max(0, int(offset)))},
    )
    data = resp.get("data") or {}
    records = data.get("records") or []
    if not records:
        return "未找到相关歌曲"
    lines = []
    for i, song in enumerate(records, 1):
        name = song.get("name", "")
        artists = ",".join(a.get("name", "") for a in (song.get("artists") or []) or (song.get("fullArtists") or []))
        album = (song.get("album") or {}).get("name", "")
        ms = song.get("duration") or 0
        mm, ss = divmod(ms // 1000, 60)
        flags = []
        if not song.get("playFlag", True):
            flags.append("不可播放")
        elif song.get("vipFlag") or song.get("vipPlayFlag"):
            flags.append("VIP")
        elif song.get("payPlayFlag"):
            flags.append("付费")
        flag_text = ("，" + "，".join(flags)) if flags else ""
        line = f"{i}. {name} - {artists}"
        if album:
            line += f"（专辑：{album}）"
        line += f" [{mm}:{ss:02d}]{flag_text}（官方资源ID: {song.get('id', '')}）"
        lines.append(line)
    return "\n".join(lines)


# ─────────────────────────────────────────────────────────────────────

mcp = MCPServer(
    name="openluo-cloud-music",
    version="1.1.0",
    instructions=(
        "网易云音乐工具。点歌/发卡流程：第一步调 cloud_music_search 得到候选列表（每行首列 id 为数字资源 id），"
        "**把选中的 id 原样保留**；第二步用该 id 调 share_song 才真正发出音乐卡。"
        "cloud_music_official_search 返回官方开放平台信息（hex 资源 id，不能发 QQ 卡，仅作信息检索）。"
        "不要编造歌曲 id，一律使用工具返回的 id。"
    ),
)


@mcp.tool(
    description=(
        "按关键词搜索网易云音乐（匿名接口）。category: song(默认)/album/artist/playlist。"
        "返回行格式：`[序号] id=<数字资源id> | 歌名 - 歌手 | 网页URL`。"
        "点歌/分享卡片必须使用该行第一列的 id；请把选中的 id 原样保留到发卡步骤，不要凭歌名改写或编造 id。"
    )
)
def cloud_music_search(keyword: str, category: str = "song", limit: int = 5) -> str:
    """搜索歌曲/专辑/歌手/歌单（匿名，无需登录，返回数字 id 可发卡）。"""
    try:
        return _search(keyword, category, limit)
    except Exception as ex:  # noqa: BLE001 - 错误文本直接回填给模型
        raise ToolError(f"搜索失败: {ex}") from ex


@mcp.tool(
    description=(
        "官方开放平台搜索歌曲（openapi.music.163.com，需配置 NCM_OPENAPI_APP_ID 与 "
        "NCM_OPENAPI_PRIVATE_KEY 环境变量）。返回名称/艺人/专辑/时长/可播状态与官方 hex 资源 id；"
        "hex id 不能用于 QQ 音乐卡。keyword 必填，limit 默认 10 最大 500，offset 默认 0。"
    )
)
def cloud_music_official_search(keyword: str, limit: int = 10, offset: int = 0) -> str:
    """官方开放平台歌曲搜索（合规稳定信息检索，非发卡通道）。"""
    cfg = _openapi_config()
    if cfg is None:
        return (
            "官方后端未配置：请设置环境变量 "
            f"{_ENV_APP_ID} 与 {_ENV_PRIVATE_KEY}（开放平台应用的 appId 与 RSA 私钥），"
            "并确认已 pip install cryptography。未配置时请改用 cloud_music_search。"
        )
    try:
        return _official_search(cfg, keyword, limit, offset)
    except Exception as ex:  # noqa: BLE001
        raise ToolError(f"官方搜索失败: {ex}") from ex


@mcp.tool(description="检查官方开放平台后端配置与令牌状态（appId 尾号脱敏显示）。")
def cloud_music_official_status() -> str:
    cfg = _openapi_config()
    if cfg is None:
        return (
            "官方后端未配置。设置环境变量 "
            f"{_ENV_APP_ID} / {_ENV_PRIVATE_KEY}（/ {_ENV_ACCESS_TOKEN} 可选复用令牌）后重试。"
        )
    try:
        session = _OpenApiSession(cfg)
        session.token()
        return f"官方后端已配置：appId={cfg['appId'][:6]}...{cfg['appId'][-6:]}，anonymous token 已获取（进程内缓存 7 天）。"
    except Exception as ex:  # noqa: BLE001
        raise ToolError(f"官方后端配置检查失败: {ex}") from ex


if __name__ == "__main__":
    asyncio.run(mcp.run_stdio_async())
