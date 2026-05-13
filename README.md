# ⚕ Hermes 桌面宠物 (鸿钧浮窗)

> **让 AI 在桌面上活过来。**
> 一个开源的 Windows 桌面精灵，实时显示 Hermes Agent 的表情、文字和状态。

---

## ✨ 特性

- 🎨 **双层窗口架构** — 宠物窗口（140×200）+ 气泡窗口（自适应宽度）完全分离
- 🖼️ **PNG 皮肤系统** — 14 张 100×130 皮肤，支持表情切换
- 🌉 **HTTP Bridge 桥接** — Python 桥接服务运行在 WSL，C# 客户端通过 HTTP 轮询状态
- 💬 **FIFO 消息队列** — 支持多段落顺序弹出，1.5s 间隔
- 😲 **三阶段表情** — 惊讶 → 思考 → 回复
- 🎮 **丰富交互** — 拖拽/双击/滚轮缩放/闲置动画（伸懒腰、打哈欠、散步、随机微表情）
- ⚡ **内存优化** — 从 70MB 降到 ~39MB（↓47%），缓存 GDI+ 对象，懒加载皮肤

---

## 🚀 快速开始

### 环境要求

- Windows 10/11 + WSL2
- .NET Framework 4.0+（Windows 自带）
- Python 3（WSL 侧桥接服务）

### 1. 启动桥接服务 (WSL)

```bash
cp config/bridge_url.txt.example config/bridge_url.txt
# 编辑 bridge_url.txt，填入 WSL IP
python3 src/hongjun_bridge.py --port 9101
```

### 2. 编译并启动宠物 (Windows)

```batch
# 使用 scripts/ 下的脚本，或手动编译：
C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe ^
  /target:winexe ^
  /out:"HongjunPet.exe" ^
  /reference:System.Drawing.dll ^
  /reference:System.Windows.Forms.dll ^
  /reference:System.dll ^
  HongjunPet.cs
```

### 3. 配置 bridge_url.txt

在 exe 同目录下创建 `bridge_url.txt`，内容为 WSL 的 IP：

```
http://172.23.227.107:9101
```

---

## 🏗️ 架构

```
┌──────────────────────────────────────────┐
│  Hermes Agent (run_agent.py)             │
│  _notify_bridge() → POST /reply          │
└──────────────────┬───────────────────────┘
                   │
                   ▼
    ┌──────────────────────────────────┐
    │  hongjun_bridge.py (WSL :9101)   │
    │  /reply → FIFO queue → pop       │
    │  /status → state JSON            │
    └──────────────────────────────────┘
                   │ 1s poll
         ┌────────┴────────┐
         ▼                  ▼
┌──────────────┐  ┌──────────────────┐
│ PetForm      │  │ BubbleForm       │
│ (140×200)    │  │ (dynamic width)  │
│ skin动画     │  │ 8s 自动消失      │
└──────────────┘  └──────────────────┘
```

### Bridge API

| 端点 | 方法 | 用途 |
|------|------|------|
| `/status` | GET | 获取状态 JSON（status/mood/bubble/expression） |
| `/reply` | POST | 入队消息到 FIFO 队列 |
| `/clear` | POST | 清空队列和气泡 |
| `/think` | POST | 强制思考状态 |
| `/bubble` | POST | 直接设置气泡文字 |
| `/health` | GET | 存活检测 |

---

## 📂 文件结构

```
hermes-desktop-pet/
├── src/
│   ├── HongjunPet.cs       # C# WinForms 主程序 (987行)
│   └── hongjun_bridge.py   # Python 桥接服务
├── skin_generator/
│   └── skin_gen.cs         # 皮肤生成器（GDI 绘制回退方案）
├── scripts/
│   ├── 编译宠物.bat         # 编译脚本
│   └── 启动鸿钧宠物.bat     # 启动脚本
├── config/
│   └── bridge_url.txt.example  # 桥接地址配置模板
└── skins/                  # 皮肤 PNG（运行时目录，不在此 repo）
```

---

## 🔧 编译

```batch
taskkill /F /IM HongjunPet.exe 2>nul
C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe ^
  /target:winexe ^
  /out:"HongjunPet.exe" ^
  /reference:System.Drawing.dll ^
  /reference:System.Windows.Forms.dll ^
  /reference:System.dll ^
  HongjunPet.cs
```

⚠️ **C# 5 限制**: 无字符串插值 `$""`、无 `using var`、无 `?.`、无 `??`。

---

## 🐛 常见问题

| 症状 | 原因 | 解决 |
|------|------|------|
| 宠物透明/无气泡 | Portproxy 缺失，C# 无法访问 WSL bridge | `netsh portproxy add` 或用 WSL IP 直连 |
| 多消息只显示最后一条 | Bridge 用直接赋值而非队列 | 检查 `/reply` 是否用 `_bubble_queue.append` |
| 编译报 CS0016 | 旧 exe 正在运行 | `taskkill /F /IM HongjunPet.exe` 后再编译 |
| 宠物不停讲话 | 气泡无自动消失 | 检查 `_bubbleFrames > 80` 8秒自动清除 |
| bridge_url.txt 缺失 | 宠物无数据连接 | 创建文件写入 WSL IP |

完整排障见 skill: `hermes-desktop-pet`

---

## 📝 更新日志

### 2026-05-13 — v2.1 交互 + 内存优化
- 新增：双击反应、伸懒腰(10s)、打哈欠(30s)、散步(60s)、拖拽特效、滚轮缩放(0.7x–1.3x)、边缘弹跳、随机微表情
- 内存优化：70.4MB → 38.5MB（缓存 GDI+ 对象、懒加载皮肤、降低 FPS 到 10fps、桥接轮询 1s）
- 气泡 8 秒自动消失
- 移除冗余 Cursor log 检测（−15MB）

### 2026-05-03 — v2.0 架构
- 星澜 + 气泡双窗口分离
- FIFO 消息队列 + 段落分割
- _notify_bridge 源推送
- 三阶段表情系统

---

## 📄 License

MIT
