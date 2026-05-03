# ⚕ Hermes 桌面宠物

> **让 AI 在桌面上活过来。**  
> 一个开源的 Windows 桌面精灵，实时显示 AI 助手的表情、文字和状态。

![演示](https://img.shields.io/badge/平台-Windows%20%7C%20WSL2-blue)
![语言](https://img.shields.io/badge/语言-C%23%20%7C%20Python-blueviolet)
![协议](https://img.shields.io/badge/协议-MIT-green)

---

## ✨ 她能做什么？

```
你聊天时      →   她在桌面上歪头思考 🤔
你收到回复时  →   她开心地挥手，气泡显示回复内容 😊
你不说话时    →   她自己做小动作：突然惊讶、自己笑、发呆 😲😊😌
你戳她一下    →   她会回应你 "嘿嘿～" "戳我干嘛～"
你拖着她      →   丝滑跟随鼠标，松手即停
```

**就像一个活生生的小精灵，住在你的桌面上。**

---

## 🎭 6 种表情姿态

| 表情 | 动作 |
|------|------|
| 😊 **开心** | 挥手微笑，粉色腮红 |
| 🤔 **思考** | 手指托下巴，若有所思 |
| 😴 **困倦** | 打哈欠，半眯泪眼 |
| 😲 **惊讶** | 双手捂脸，瞪大圆眼 |
| 😌 **日常** | 乖乖端茶，温柔微笑 |
| 😤 **生气** | 叉腰扭头，气鼓鼓的 |

> 每个表情都有对应的 **全身动作**，不止是换脸。

---

## 🧠 核心特性

| 特性 | 说明 |
|------|------|
| **桌面悬浮** | 140×200px 小窗口，置顶显示，鼠标穿透，不挡操作 |
| **实时联动** | 通过桥接服务同步 AI 助手状态，思考/回复自动切换 |
| **语音气泡** | 随文字自动变宽，只显示 AI 的最终回复，不显示工具调用 |
| **空闲小动作** | 不说话时每 3-8 秒随机做表情，像活着一样 |
| **丝滑拖拽** | 按住左键拖到任何位置，超过 10px 才触发，防误触 |
| **自定义皮肤** | 替换 PNG 即可换装，支持透明背景，100×130px |
| **20fps 动画** | 呼吸浮动、眨眼、触角抖动、身体微晃 |
| **完全开源** | MIT 协议，随便玩 |

---

## 🚀 快速开始

### Windows 端

```batch
# 1. 编译宠物程序
C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe /target:winexe ^
  /out:"HongjunPet.exe" /reference:System.Drawing.dll ^
  /reference:System.Windows.Forms.dll /reference:System.Net.Http.dll ^
  src\HongjunPet.cs

# 2. 放入皮肤 PNG 到 skins\ 目录
# 3. 双击 HongjunPet.exe 启动！
```

### WSL 桥接服务（可选，用于 AI 联动）

```bash
# 部署桥接
cp src/hongjun_bridge.py ~/
systemctl --user enable hongjun-bridge
systemctl --user start hongjun-bridge

# API 调用示例
curl -X POST http://localhost:9101/reply \
  -H "Content-Type: application/json" \
  -d '{"text":"你好呀～"}'
```

---

## 🏗️ 架构

```
┌──────────────┐     HTTP/500ms 轮询     ┌──────────────┐
│  星澜宠物.exe  │ ◄──────────────────► │ 桥接服务:9101  │
│ (C# WinForms) │     GET /status       │ (Python/WSL) │
│  140×200px    │                       │              │
│  20fps GDI+   │                       │ 监控 AI 状态  │
│  PNG 皮肤系统  │                       │ FIFO 气泡队列 │
└──────────────┘                       └──────┬───────┘
                                              │
                                     ┌────────▼───────┐
                                     │  你的 AI 助手   │
                                     │  微信/CLI/网页  │
                                     └────────────────┘
```

### 网络配置（重要）

桥接服务跑在 WSL 里，宠物跑在 Windows 上。默认 `BRIDGE_URL = "http://localhost:9101"` 在 Windows 上无法直接访问 WSL，需要配置：

**方案一：改代码用 WSL IP（简单）**

修改 `src/HongjunPet.cs` 第 58 行：
```csharp
private const string BRIDGE_URL = "http://你的WSL_IP:9101";
```
在 WSL 中执行 `hostname -I` 查看 IP。

**方案二：端口转发（一劳永逸）**

管理员 PowerShell：
```powershell
netsh interface portproxy add v4tov4 listenport=9101 listenaddress=0.0.0.0 connectport=9101 connectaddress=你的WSL_IP
```
之后 `localhost:9101` 就能用，WSL IP 变了才需更新。

---

## 🎨 自定义皮肤

制作 100×130px 透明 PNG，放到 `skins/` 目录：

| 文件名 | 表情 | 文件名 | 表情 |
|--------|------|--------|------|
| `happy.png` | 开心 | `normal.png` | 日常 |
| `thinking.png` | 思考 | `sleepy.png` | 困倦 |
| `surprised.png` | 惊讶 | `angry.png` | 生气 |

> 没有皮肤？宠物会自动用 GDI+ 画一个蓝色小精灵备选。

---

## 📁 项目结构

```
hermes-desktop-pet/
├── README.md                     # 本文件
├── src/
│   ├── HongjunPet.cs             # 宠物主程序 (~700行)
│   └── hongjun_bridge.py         # 桥接服务 (~300行)
├── skin_generator/
│   ├── skin_gen.cs               # C# 皮肤生成器
│   └── resize_skins.cs           # 图片缩放工具
├── config/
│   └── hongjun-bridge.service    # systemd 服务文件
├── scripts/
│   └── compile.bat               # 一键编译脚本
└── skins/                        # 皮肤 PNG 目录
```

---

## 📜 协议

[MIT License](LICENSE) — 随意使用、修改、分发。

---

**⚕ Hermes 桌面宠物** — 让 AI 有温度 🌟
