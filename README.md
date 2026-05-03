# 星澜桌面宠物 ✨

一个开源的 Windows 桌面宠物，支持 AI 实时交互、表情切换、语音气泡。

**让你的 AI 助手在桌面上活过来！**

## 功能特性

- 🖥️ **桌面悬浮** — 小窗口置顶显示，鼠标穿透，不挡操作
- 🎭 **表情切换** — 6 种动作姿态（开心/思考/惊讶/困倦/生气/日常）
- 💬 **语音气泡** — 随文字自动变宽，显示 AI 回复内容
- 🤖 **AI 联动** — 通过桥接服务实时同步 AI 助手状态
- 🎨 **自定义皮肤** — 替换 PNG 即可换装，支持透明背景
- ⚡ **空闲小动作** — 不说话时会自己做表情，像活的一样
- 🌀 **20fps 动画** — 呼吸浮动、眨眼、触角抖动

## 系统要求

| 组件 | 要求 |
|------|------|
| **操作系统** | Windows 10/11（宠物本体）+ WSL2（桥接服务） |
| **.NET** | .NET Framework 4.8（Windows 自带） |
| **Python** | Python 3.8+（仅 WSL 侧需要） |
| **编译器** | C# 5.0+（`csc.exe`，Windows 自带） |
| **AI 模型** | 任意 OpenAI 兼容 API（可选，用于自动生成皮肤） |

> 💡 **纯本地使用**：如果不接入 AI，宠物仍然可以独立运行，显示自定义表情和气泡文字。

## 快速开始

### 1. 下载项目

```bash
git clone https://github.com/你的用户名/xinglan-pet.git
# 或者直接下载 ZIP
```

### 2. 编译宠物程序

```batch
C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe /target:winexe /out:"D:\星澜宠物\HongjunPet.exe" /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Net.Http.dll "src\HongjunPet.cs"
```

或者双击 `scripts\compile.bat` 一键编译。

### 3. 放皮肤图片

在宠物程序同级目录创建 `skins\` 文件夹，放入以下 PNG 文件（推荐尺寸 100×130px，透明背景）：

| 文件名 | 表情 |
|--------|------|
| `normal.png` | 日常/空闲 |
| `happy.png` | 开心/回复 |
| `thinking.png` | 思考中 |
| `surprised.png` | 惊讶 |
| `sleepy.png` | 困倦/离线 |
| `angry.png` | 生气 |

没有皮肤图片？宠物会自动用代码绘制一个蓝色小精灵作为备选。

### 4. 启动宠物

双击 `HongjunPet.exe` 即可！宠物会出现在桌面右下角。

## 接入 AI 助手（可选）

如果想让宠物实时显示 AI 助手的回复和状态，需要部署桥接服务：

### 4a. 部署桥接服务（WSL）

```bash
# 复制桥接脚本
cp src/hongjun_bridge.py ~/

# 安装 systemd 服务
mkdir -p ~/.config/systemd/user/
cp config/hongjun-bridge.service ~/.config/systemd/user/
systemctl --user daemon-reload
systemctl --user enable hongjun-bridge
systemctl --user start hongjun-bridge

# 查看状态
systemctl --user status hongjun-bridge
```

### 4b. 配置 Hermes Agent 钩子（可选）

如果使用 [Hermes Agent](https://hermes-agent.nousresearch.com)，可以添加网关钩子让每次 AI 回复自动推送：

创建 `~/.hermes/hooks/bridge-notify/handler.py`：

```python
import json, urllib.request

async def handle(event_type: str, context: dict) -> None:
    response = (context or {}).get("response", "")
    if not response:
        return
    text = response.strip()[:200]
    data = json.dumps({"text": text}).encode("utf-8")
    try:
        req = urllib.request.Request(
            "http://localhost:9101/reply",
            data=data, headers={"Content-Type": "application/json"}, method="POST",
        )
        import asyncio
        loop = asyncio.get_running_loop()
        await loop.run_in_executor(None, lambda: urllib.request.urlopen(req, timeout=2))
    except Exception:
        pass
```

## API 参考（桥接服务）

桥接服务运行在 `localhost:9101`，提供以下端点：

| 端点 | 方法 | 说明 |
|------|------|------|
| `/status` | GET | 获取当前状态（online/status/mood/bubble） |
| `/health` | GET | 健康检查 |
| `/ping` | GET | Pong |
| `/bubble` | POST | `{"text":"...", "duration":4}` — 设置气泡 |
| `/reply` | POST | `{"text":"..."}` — 设置回复气泡 + 开心表情 |
| `/think` | POST | 设置思考状态（无占位气泡） |

示例：
```bash
curl -X POST http://localhost:9101/reply \
  -H "Content-Type: application/json" \
  -d '{"text":"你好呀～"}'
```

## 自定义皮肤

### 方法 1：手动制作

用 Photoshop、Procreate 等工具制作 100×130px 的透明 PNG，放到 `skins/` 文件夹即可。文件名对应表情名称。

### 方法 2：AI 生成

本项目包含 `skin_generator/skin_gen.cs`，可以配合 OpenAI 兼容的图生图 API 批量生成皮肤。

```bash
# 设置 API Key
export API_KEY="your-api-key"
export API_BASE="https://your-api-endpoint/v1"

# 编译并运行皮肤生成器
C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe /target:exe /out:skin_gen.exe /reference:System.Drawing.dll skin_generator\skin_gen.cs
skin_gen.exe
```

### 方法 3：代码绘制

`HongjunPet.cs` 中包含 GDI+ 绘制的备选角色（蓝色圆形小精灵），可作为参考修改代码绘制自己的角色。

## 项目结构

```
xinglan-pet/
├── README.md                  # 本教程
├── src/
│   ├── HongjunPet.cs          # C# 桌面宠物主程序（~600行）
│   └── hongjun_bridge.py      # Python 桥接服务（~300行）
├── skin_generator/
│   ├── skin_gen.cs            # C# 皮肤生成器
│   └── resize_skins.cs        # 图片缩放工具
├── config/
│   └── hongjun-bridge.service # systemd 服务文件
├── scripts/
│   ├── compile.bat            # Windows 一键编译脚本
│   └── start_bridge.sh        # WSL 桥接启动脚本
└── skins/                     # 皮肤目录（放你的 PNG）
    ├── happy.png
    ├── thinking.png
    ├── sleepy.png
    ├── surprised.png
    ├── normal.png
    └── angry.png
```

## 技术架构

```
┌──────────────┐     HTTP/500ms轮询     ┌──────────────┐
│  星澜宠物.exe  │ ◄──────────────────► │ 桥接服务:9101  │
│  (C# WinForms) │     GET /status       │ (Python/WSL)  │
│  140×200px     │                       │               │
│  20fps GDI+    │                       │ 监控网关日志  │
│  PNG 皮肤系统  │                       │ 检测会话文件  │
└──────────────┘                       └──────┬───────┘
                                              │
                                     ┌────────▼───────┐
                                     │  Hermes Agent   │
                                     │  网关/CLI 会话  │
                                     └────────────────┘
```

**工作原理：**
1. 桥接服务每 1.5 秒轮询 Hermes 网关日志和会话文件
2. 检测到用户消息 → 设置 thinking 状态
3. 检测到 AI 回复 → 设置 happy 状态 + 推送气泡文字
4. 宠物每 0.5 秒轮询桥接 `/status` 接口
5. 根据返回的 `status` 和 `mood` 切换表情皮肤和气泡

## 开源协议

MIT License

## 致谢

- 灵感来自 [Shimeji-ee](https://github.com/kilkakon/Shimeji-ee) 桌宠
- 桥接架构参考 Hermes Agent 网关钩子系统
- AI 画图使用 OpenAI 兼容的图生图 API

---

**星澜** — 让 AI 在桌面上陪伴你 🌟
