"""openLuo Qwen-TTS MCP server：远程 Qwen-TTS 合成 + 预设音色库（标准 MCP 协议）。

由宿主 config/mcp-servers.jsonc 以 stdio transport 启动。
服务地址**不在仓库内落地**：经宿主进程环境变量注入（stdio 子进程继承父环境），
从环境变量 `QWEN_TTS_BASE_URL` 读取；未配置时工具返回引导文案，不影响其它工具。

远程服务暴露两类 API（见 Qwen-TTS 本地参考实现 client.py）：
  - design —— 声音设计：仅凭文本 + 自然语言风格指令凭空创造新音色朗读，无需参考音频。
      POST /v1/audio/voice-design      JSON: {text, instruct, language?}
  - clone  —— 声音克隆：用参考音频的音色朗读目标语句。
      POST /v1/audio/voice-clone       multipart: text, ref_audio(文件), ref_text?, language?
                                       JSON:      text, ref_audio(URL/base64), ref_text?, language?

声音控制（Voice Control）
--------------------------
控制能力**只存在于 design（音色创造）**；**clone / speak（角色音色）没有任何逐句风格控制**。
以下均为对当前部署的实测/schema 结论：

  - /v1/audio/voice-clone 的请求字段只有 text / ref_audio / ref_text / language，**没有 instruct**；
    额外传入会被静默忽略（不改音色、也不改表达）。
  - 在 text 里内联 `[标记]` **无效且有害**：服务端不解析标记，方括号被忽略而**括号里的字会被原样念出来**。
    实测 text="[略带撒娇]" 会合成出「略带撒娇」的语音；text="[略带撒娇]哥哥你回来啦" 时长≈两段之和。
    所以**绝不要**把情绪标记写进 clone / speak 的 text。

  - design + instruct → **唯一的控制入口，实测有效**：同一句文本把 instruct 从「正常语速」改为
    「用特别缓慢的语速，一个字一个字地说」，时长由 1.28s 变为 6.88s。instruct 可写整段自然语言，
    也可写逐行「维度: 描述」，中英均可，越具体越稳。控制维度：
      1) 声学属性控制：性别 / 音高 / 语速 / 音量 / 年龄 / 清晰度 / 流畅度 / 口音 / 音色质感 / 情绪 / 语调 / 性格
      2) 年龄控制：如「体现撒娇稚嫩的萝莉女声，音调偏高且起伏明显」
      3) 渐变控制：同一段内随时间演变，如「初始平稳，后段因激动逐渐加快，音量逐渐提高至喊叫」
      4) 拟人感：口语化、自然停顿/笑声/叹息，如「模仿别人嘘你时压低嗓音，就是平时聊天的感觉」
      5) 背景信息：角色姓名 / 音色信息 / 身份背景 / 外貌特征 / 性格特质 / 人生信条（给音色立人设）
      6) 音色复用：同一段描述在多轮、多角色长对话中反复使用，保持音色一致

想要「角色音色 + 逐句情绪」，需要服务端能力支持（clone 端点增加 instruct，或部署
CustomVoice 这类「音色 + 控制指令」模型）；当前部署不具备。

使用方案
--------
  1) 角色常驻音色        qwen_tts_speak(charId, text)   ← 无风格参数；text 只放要念的内容
  2) 凭空创造音色        qwen_tts_design(text, instruct="<声学属性 / 人设 / 背景信息>")
  3) 多角色长对话        每个角色写一条音色描述 → design 逐句合成；同一角色复用同一描述
  4) 临时参考音频克隆     qwen_tts_clone(text, ref_audio, ref_text)

控制指令参考文档：https://qwen.ai/blog?id=qwen3tts-0115

预设音色库
-----------
`data/tts/tts.json`（相对运行时 cwd = publish 根）描述一份预设音色清单，每条一个角色音色：
  { "voices": [ { "id": "custom-rin-bot", "name": "泠汐", "ref_audio": "raw_01_24k.wav",
                  "ref_text": "<转写>", "aliases": [...], "default": true } ] }

`qwen_tts_speak(charId, text)` 是预设音色的主入口：按 角色卡 id → aliases → default 依次匹配
音色，命中后用该音色的 ref_audio + ref_text 直接 clone，无需模型关心音色细节。
`qwen_tts_clone` 保留为"临时给一段参考音频"的通用入口；`qwen_tts_design` 保留为"凭空造音色"。

所有合成返回 16-bit 单声道 24kHz WAV。本 server 将合成结果以
`data:audio/wav;base64,...` 形式的 data URL 返回（供宿主 invoker 识别为音频输出项，
音频 base64 被抽出放进公共输出项、不会回填 LLM 上下文）。

配置环境变量
  QWEN_TTS_BASE_URL  必填：远程服务 base，如 http://host:port
  QWEN_TTS_TIMEOUT   可选：单次请求超时秒数，默认 300（长文本合成较慢）
  QWEN_TTS_PROXY     可选：JSON dict，如 {"http":"...","https":"..."}；默认绕过环境代理直连
  QWEN_TTS_VOICES    可选：音色库文件路径，默认 data/tts/tts.json
  QWEN_TTS_OUTPUT_DIR 可选：合成 WAV 落盘目录，默认 <server 脚本目录>/tmp（方便清理）。
                      每次合成结果存为 tts_<时间戳>_<kind>_<文本前20字>.wav，用于跟踪排查。

注意：本机若有 http_proxy/https_proxy 环境变量（如 v2ray），requests 默认会走代理导致
连接失败；因此显式 trust_env=False 直连，除非显式配置 QWEN_TTS_PROXY。
服务端无 ffmpeg/audioread，参考音频仅支持 WAV；mp3 等会解码失败。
"""

from __future__ import annotations

import asyncio
import base64
import json
import os
import sys
import time
from pathlib import Path

from mcp.server import MCPServer
from mcp.server.mcpserver.exceptions import ToolError

_ENV_BASE_URL = "QWEN_TTS_BASE_URL"
_ENV_TIMEOUT = "QWEN_TTS_TIMEOUT"
_ENV_PROXY = "QWEN_TTS_PROXY"
_ENV_VOICES = "QWEN_TTS_VOICES"
_ENV_OUTPUT_DIR = "QWEN_TTS_OUTPUT_DIR"

_DEFAULT_TIMEOUT = 300  # 长文本合成可能较慢
_DEFAULT_VOICES_PATH = Path("data/tts/tts.json")
# 合成 WAV 落盘目录:默认相对本脚本目录的 tmp/ (MCP 本地,方便清理),可用 QWEN_TTS_OUTPUT_DIR 覆盖
_DEFAULT_OUTPUT_DIR = (Path(__file__).resolve().parent / "tmp")

DESIGN_PATH = "/v1/audio/voice-design"
CLONE_PATH = "/v1/audio/voice-clone"


def _base_url() -> str | None:
    val = os.environ.get(_ENV_BASE_URL, "").strip()
    return val.rstrip("/") if val else None


def _timeout() -> int:
    try:
        return max(1, int(os.environ.get(_ENV_TIMEOUT, "").strip() or _DEFAULT_TIMEOUT))
    except ValueError:
        return _DEFAULT_TIMEOUT


def _load_client():
    """惰性加载 requests 并构造客户端；未安装/未配置时抛带引导的 RuntimeError。"""
    try:
        import requests
    except ImportError as ex:  # pragma: no cover - 依赖缺失路径
        raise RuntimeError(
            "Qwen-TTS 依赖 requests（pip install requests）。"
        ) from ex

    base = _base_url()
    if not base:
        raise RuntimeError(
            f"Qwen-TTS 服务未配置：请设置环境变量 {_ENV_BASE_URL}（远程服务地址）。"
            "未配置时请改用其它 TTS 能力。"
        )

    proxy_text = os.environ.get(_ENV_PROXY, "").strip()
    proxies = json.loads(proxy_text) if proxy_text else None

    import requests as _requests
    session = _requests.Session()
    session.trust_env = False  # 忽略 http_proxy 等环境变量，直连服务端
    if proxies:
        session.proxies.update(proxies)
    return session, base, _timeout()


# ─────────────────────────────────────────────────────────────────────
# 预设音色库
# ─────────────────────────────────────────────────────────────────────

def _voices_path() -> Path:
    val = os.environ.get(_ENV_VOICES, "").strip()
    return Path(val) if val else _DEFAULT_VOICES_PATH


def _load_voices() -> tuple[list[dict], dict | None]:
    """读取音色库文件，返回 (voices, default_voice)。文件缺失/损坏时返回空列表与 None。"""
    path = _voices_path()
    try:
        with path.open("r", encoding="utf-8") as f:
            data = json.load(f)
    except FileNotFoundError:
        return [], None
    except (json.JSONDecodeError, OSError) as ex:
        # 配置错误直接暴露给调用方（模型可感知），不静默吞掉
        raise RuntimeError(f"音色库 {path} 解析失败: {ex}") from ex

    voices = data.get("voices") or []
    defaults = [v for v in voices if v.get("default")]
    return list(voices), (defaults[0] if defaults else None)


def _match_voice(voices: list[dict], default: dict | None, char_id: str) -> tuple[dict | None, str]:
    """按 角色卡 id → aliases → default 依次匹配音色，返回音色对应的说明文本（供工具回填）。

    返回 (voice, matched_mode)。matched_mode: 'id' | 'alias' | 'default' | 'none'。
    """
    for v in voices:
        if v.get("id") == char_id:
            return v, "id"
    for v in voices:
        aliases = v.get("aliases") or []
        if char_id in aliases:
            return v, "alias"
    if default is not None:
        return default, "default"
    return None, "none"


def _voice_to_text(voice: dict | None, mode: str, char_id: str) -> str:
    """给模型的匹配说明（合成前的确认文本，不会作为最终输出内容）。"""
    vid = (voice or {}).get("id", "")
    if mode == "id":
        return ""
    if mode == "alias":
        return f"[音色匹配] charId={char_id} 经别名匹配到 {vid}。"
    if mode == "default":
        return f"[音色匹配] charId={char_id} 未命中任何音色，回退默认 {vid}。"
    return f"[音色匹配] 未找到 charId={char_id}，且无默认音色可用。"


# ─────────────────────────────────────────────────────────────────────

def _wav_to_data_url(wav: bytes) -> str:
    b64 = base64.b64encode(wav).decode("ascii")
    return f"data:audio/wav;base64,{b64}"


# ─────────────────────────────────────────────────────────────────────

def _sanitize(label: str, max_len: int = 20) -> str:
    """把任意文本清洗成可安全放入文件名的短串(仅保留字母数字/中文/中划线/下划线)。"""
    keep = []
    for ch in label:
        if ch.isalnum() or ch in "-_":
            keep.append(ch)
        elif ch.isspace():
            keep.append("_")
    cleaned = "".join(keep).strip("_-")
    return cleaned[:max_len] if cleaned else "speech"


def _persist_audio(wav: bytes, kind: str, text: str) -> Path:
    """把合成 WAV 落盘到输出目录,返回保存路径。失败不抛(仅 stderr 记录),不影响返回。"""
    try:
        out_dir = Path(os.environ.get(_ENV_OUTPUT_DIR, "").strip() or _DEFAULT_OUTPUT_DIR)
        out_dir.mkdir(parents=True, exist_ok=True)
        stamp = time.strftime("%Y%m%d_%H%M%S")
        name = f"tts_{stamp}_{_sanitize(kind)}_{_sanitize(text)}.wav"
        dest = out_dir / name
        dest.write_bytes(wav)
        return dest
    except Exception as ex:  # noqa: BLE001 - 落盘失败不影响合成结果交付
        print(f"[tts] 落盘失败: {ex}", file=sys.stderr)
        return Path("")


# ─────────────────────────────────────────────────────────────────────

def _run(session, base: str, timeout: int, method: str, path: str, **kw) -> bytes:
    """POST 到远程合成端点，校验返回为 WAV，返回原始字节。"""
    resp = session.post(f"{base}{path}", timeout=timeout, **kw)
    if resp.status_code != 200:
        detail = ""
        try:
            detail = (resp.json().get("detail") or "").strip()
        except Exception:
            pass
        raise RuntimeError(
            f"合成失败 HTTP {resp.status_code} ({method}): {detail or resp.text[:300]}"
        )
    ctype = resp.headers.get("content-type", "")
    if "audio/wav" not in ctype:
        raise RuntimeError(
            f"意外响应类型 {ctype!r}，期望 audio/wav。服务端可能异常: {resp.text[:200]}"
        )
    return resp.content


def _clone_wav(session, base: str, timeout: int,
               text: str, ref_audio: str, ref_text: str | None, language: str | None) -> bytes:
    """克隆核心：ref_audio 分流（URL/本地文件/base64），返回合成 WAV 字节。

    与 qwen_tts_clone 及 qwen_tts_speak 共用，避免重复分支。
    语气/情绪控制靠 text 内联的 `[标记]`，不额外传参数。
    """
    def _styled(payload: dict) -> dict:
        if ref_text:
            payload["ref_text"] = ref_text
        if language:
            payload["language"] = language
        return payload

    s = str(ref_audio)
    if s.startswith(("http://", "https://")):
        # 远程 URL：JSON，服务端拉取
        return _run(session, base, timeout, "clone", CLONE_PATH,
                    json=_styled({"text": text, "ref_audio": s}))

    if Path(s).is_file():
        # 本地文件：multipart 上传
        head = Path(s).read_bytes()[:12]
        if not (head.startswith(b"RIFF") and b"WAVE" in head):
            raise ValueError(
                f"参考音频不是 WAV 文件: {s}。服务端无 ffmpeg，仅支持 WAV；mp3 请先转换。"
            )
        with Path(s).open("rb") as f:
            return _run(
                session, base, timeout, "clone", CLONE_PATH,
                data=_styled({"text": text}),
                files={"ref_audio": (Path(s).name, f, "audio/wav")},
            )

    # 既非 URL 也非现有文件 → 当作 base64 音频串
    try:
        base64.b64decode(s, validate=True)
    except Exception:
        raise ValueError(
            "ref_audio 既不是本地文件也不是 http(s) URL，且不是合法的 base64 音频串。"
        ) from None
    return _run(session, base, timeout, "clone", CLONE_PATH,
                json=_styled({"text": text, "ref_audio": f"data:audio/wav;base64,{s}"}))


# ─────────────────────────────────────────────────────────────────────

mcp = MCPServer(
    name="openluo-qwen-tts",
    version="1.5.0",
    instructions=(
        "Qwen-TTS 语音合成工具。首选 qwen_tts_speak(charId, text)：按角色卡 id 自动选择预设音色，"
        "charId 用 openLuo 角色卡(openLuo/data/archetypes/*.jsonc)的 id 字段；未匹配时回退默认音色。"
        "qwen_tts_list_voices 列出可用音色。qwen_tts_design 凭空造音色、qwen_tts_clone 临时给参考音频。\n"
        "【text 里不要写控制符号】qwen_tts_speak / qwen_tts_clone **没有任何风格参数**，服务端也不解析控制标记——"
        "`[委屈]`、`（小声）`、`*叹气*` 这类符号会被**原样念出来**。text 只放真正要念给用户听的内容。\n"
        "【语气与情绪控制】只有 qwen_tts_design 能做到，靠必填参数 instruct（实测有效：改语速可让同一句话"
        "时长 1.28s→6.88s）。instruct 可写整段自然语言，也可写逐行「维度: 描述」，中英均可。控制维度："
        "性别 / 音高 / 语速 / 音量 / 年龄 / 清晰度 / 流畅度 / 口音 / 音色质感 / 情绪 / 语调 / 性格；"
        "另支持年龄控制（萝莉/中老年等）、渐变控制（同段内随时间演变，如「初始平稳，后段逐渐加快」）、"
        "拟人感（口语化、自然停顿/笑声，如「模仿别人嘘你时压低嗓音」）、"
        "背景信息（角色姓名/身份背景/外貌/性格特质/人生信条，用于立人设）、"
        "音色复用（多角色长对话中每个角色固定用同一条描述，保持音色一致）。\n"
        "用户要求某个角色用特定语气说话时：不要编造风格参数，也不要往 text 里塞标记；可改用 qwen_tts_design "
        "按该语气描述一个音色来朗读，或如实说明当前角色音色不支持逐句语气控制。\n"
        "【语音合成规则】qwen_tts_speak 用于**任何需要把一段文本以语音形式发送给用户的场景**"
        "（讲故事、朗读、念消息、播报等都适用——讲故事只是其中之一）：\n"
        "1. 把要**语音化的一段完整内容**整体作为 text 传入 qwen_tts_speak，一次调用读完全部；服务端支持中等长度文本（250字）。\n"
        "2. 只有当内容较长、确需拆成多次时，才分段调用；且分段必须是**同一内容的连续承接**——后一段 text "
        "从上一段结尾接续，不得另起无关内容、不得重复开头、不得各说各的。\n"
        "3. tool 结果会回填**本次朗读的原始文本**与“已完成”状态，据此你应确认：\n"
        "   - 这是否是同一用户请求内的**下一次** qwen_tts_speak？若是，请确保 text 是上一段的**直接延续**。\n"
        "4. **停止时机**：本次请求要发送的语音已完整(已返回一次成功合成且无需续段)，或已到自然收尾，"
        "**立即停止调用 qwen_tts_speak**，用适当的文本回复用户。\n"
        "   同一用户回合内不要为“更完整/更长”而连续追加多个各自独立的内容。"
    ),
)


@mcp.tool(
    description=(
        "用预设音色把一段文本合成为语音发送给用户(推荐主入口)。charId 必填：openLuo 角色卡 id(如 custom-rin-bot)；"
        "text 必填：要语音化的完整文本(把**这一段要说的全部内容**传进来)，用于讲故事、朗读、念消息、播报等任何需要发送语音的场合。"
        "自动按 charId→别名→默认音色 选择预设音色库中的音色并克隆。"
        "text 只放要念的内容：是纯文本，**不要写 `[委屈]`、`（小声）` 之类的控制符号**——服务端不解析标记，"
        "这些字会被原样念出来。需要某种语气/音色时改用 qwen_tts_design 按描述造音色。"
        "返回带本次朗读**原始文本**与**已完成状态**。要语音化的内容较长可一次传入；确需分段时后一段 text 必须承接前一段结尾(同一内容)，"
        "不要各说各的无关片段；发送完成后立即停止调用，用文本回复用户。"
    )
)
def qwen_tts_speak(charId: str, text: str) -> str:
    """按角色卡 id 选预设音色，把一段文本合成为语音发送（前置本次朗读原文+完成状态）。

    本工具无风格参数：服务端 clone 端点不接受 instruct，也不解析 `[标记]`（标记会被原样念出来）。
    返回格式：`本次合成完成(共{N}字)：\n<完整原文>\n[状态] 本段语音已生成并交付；若这就是完整的发送内容请停止续段。\ndata:audio/wav;base64,...`
    返回原始完整文本 + 明确的"已完成"状态，供模型判断"已发送了什么、该不该停"；
    data URL 供宿主 invoker 抽取为音频输出项。
    """
    try:
        voices, default = _load_voices()
        voice, mode = _match_voice(voices, default, charId)
        if voice is None:
            # 无音色可匹配：返回明确错误，不静默乱选
            raise ValueError(_voice_to_text(None, "none", charId))
        if mode != "id":
            print(_voice_to_text(voice, mode, charId), file=sys.stderr)
        # ref_audio 相对音色库文件所在目录解析（tts.json 同目录）
        voices_file = _voices_path()
        ref_audio = voice["ref_audio"]
        if not Path(ref_audio).is_absolute():
            ref_audio = str((voices_file.parent / ref_audio))
        session, base, timeout = _load_client()
        wav = _clone_wav(session, base, timeout, text, ref_audio,
                         voice.get("ref_text"), voice.get("language"))
        _persist_audio(wav, charId, text)
        raw = text.strip()
        head = f"本次合成完成(共{len(raw)}字)：\n{raw}"
        note = "[状态] 本段语音已生成并交付。若这就是本次要发送的完整语音内容，请立即停止调用 qwen_tts_speak，用文本回复用户；若确有后续内容需续下一段，text 必须承接上一段结尾，且不要另起无关内容。"
        return f"{head}\n{note}\n{_wav_to_data_url(wav)}"
    except Exception as ex:  # noqa: BLE001 - 失败必须转成 isError(status=failed)；原文仍随 isError 内容回填给模型
        raise ToolError(f"语音合成失败: {ex}") from ex


@mcp.tool(description="列出预设音色库中的全部音色(角色 id / 名称 / 是否默认)；用于查看可用音色。")
def qwen_tts_list_voices() -> str:
    """列出预设音色库中的音色清单。"""
    try:
        voices, default = _load_voices()
        if not voices:
            return "未找到音色库(空或文件缺失): 请检查 data/tts/tts.json。"
        lines = []
        for v in voices:
            flag = "（默认）" if v.get("default") else ""
            lines.append(f"- id={v.get('id')} name={v.get('name', '')}{flag}")
        return "\n".join(lines)
    except Exception as ex:  # noqa: BLE001 - 错误文本直接回填给模型
        raise ToolError(f"音色库读取失败: {ex}") from ex


@mcp.tool(
    description=(
        "声音设计/音色创造：凭文本 + 自然语言控制指令凭空创造出新音色来朗读，无需参考音频。"
        "text 必填(要合成的文本)；instruct 必填(音色/风格描述)；language 可选(Chinese/English/Japanese…，缺省自动检测)。"
        "instruct 可写整段自然语言，也可写逐行「维度: 描述」，中英均可，越具体越稳。"
        "可控维度：性别/音高/语速/音量/年龄/清晰度/流畅度/口音/音色质感/情绪/语调/性格；"
        "还可给背景人设(角色姓名/身份背景/外貌特征/性格特质/人生信条)、要求渐变(同段内语气随时间演变)、"
        "拟人感(口语化、自然停顿/笑声)。示例：「体现撒娇稚嫩的萝莉女声，音调偏高且起伏明显」、"
        "「gender: Male. pitch: Low male pitch. speed: Fast-paced delivery with deliberate pauses.」、"
        "「性别: 男性 / 音高: 男性低沉音区，偶有拔高 / 语速: 初始平稳，后段因激动逐渐加快」。"
        "多角色长对话中每个角色固定用同一条描述，即可保持音色一致。"
    )
)
def qwen_tts_design(text: str, instruct: str, language: str | None = None) -> str:
    """根据控制指令合成新音色朗读文本，返回音频 data URL。"""
    try:
        session, base, timeout = _load_client()
        payload: dict = {"text": text, "instruct": instruct}
        if language:
            payload["language"] = language
        wav = _run(session, base, timeout, "design", DESIGN_PATH, json=payload)
        _persist_audio(wav, "design", text)
        return _wav_to_data_url(wav)
    except Exception as ex:  # noqa: BLE001 - 错误文本直接回填给模型
        raise ToolError(f"声音设计失败: {ex}") from ex


@mcp.tool(
    description=(
        "声音克隆：用一段参考音频的音色朗读文本(通用入口，临时参考音频用)。text 必填(要合成的目标语句)；"
        "ref_audio 必填(参考音频，本地 WAV 文件路径、http(s) URL、或 base64 音频串均自动分流)；"
        "ref_text 可选(参考音频的准确文字转写，建议提供以提升对齐质量)；"
        "language 可选(Chinese/English/Japanese…，缺省自动检测)。"
        "text 只放要念的内容：**不要写 `[委屈]`、`（小声）` 之类的控制符号**——服务端 clone 端点不接受 instruct，"
        "也不解析标记，这些字会被原样念出来。"
    )
)
def qwen_tts_clone(text: str, ref_audio: str, ref_text: str | None = None,
                   language: str | None = None) -> str:
    """克隆参考音频的音色朗读文本，返回音频 data URL。

    本工具无风格参数：服务端 clone 端点不接受 instruct，也不解析 `[标记]`（标记会被原样念出来）。
    """
    try:
        session, base, timeout = _load_client()
        wav = _clone_wav(session, base, timeout, text, ref_audio, ref_text, language)
        _persist_audio(wav, "clone", text)
        return _wav_to_data_url(wav)
    except Exception as ex:  # noqa: BLE001 - 错误文本直接回填给模型
        raise ToolError(f"声音克隆失败: {ex}") from ex


@mcp.tool(description="检查 Qwen-TTS 服务连接与配置状态（服务地址可能为空 → 提示未配置）。")
def qwen_tts_health() -> str:
    """检查远程 Qwen-TTS 服务健康状态与已加载模型。"""
    try:
        session, base, _ = _load_client()
        resp = session.get(f"{base}/health", timeout=15)
        resp.raise_for_status()
        return f"Qwen-TTS 服务正常: {resp.json()}"
    except Exception as ex:  # noqa: BLE001 - 错误文本直接回填给模型
        raise ToolError(f"Qwen-TTS 服务检查失败: {ex}") from ex


if __name__ == "__main__":
    asyncio.run(mcp.run_stdio_async())
