# ⚕ Hermes 桌面宠物

> **让 AI 在桌面上活过来。**
> 一个开源的 Windows 桌面精灵，实时显示 AI 助手的表情、文字和状态。

---

## ✨ v2.0 架构重构

**星澜窗口 + 气泡窗口完全分离**，两个独立 Form：

| 窗口 | 尺寸 | 功能 |
|------|------|------|
| 星澜窗口 | 140×200 固定 | 皮肤动画、呼吸、眨眼 |
| 气泡窗口 | 自适应文字 | 智能定位：优先右边→碰壁左边 |

**三阶段表情系统：**
😲 惊讶 (1s) → 🤔 思考 (2.5s) → 😊 开心回复

---

## 🚀 快速开始

```batch
# 编译
C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe /target:winexe ^
  /out:HongjunPet.exe /reference:System.Drawing.dll ^
  /reference:System.Windows.Forms.dll /reference:System.Net.Http.dll ^
  src\HongjunPet.cs

# 放皮肤到 skins\ 目录后双击启动
```

---

## 📁 项目结构

```
├── src/HongjunPet.cs          # 宠物主程序 (~450行)
├── src/hongjun_bridge.py      # Python 桥接服务
├── skin_generator/            # 皮肤生成工具
├── skins/                     # 放你的 PNG
└── README.md
```

## 🎨 自定义皮肤

100×130px 透明 PNG，放到 `skins/`：

| 文件 | 表情 | 文件 | 表情 |
|------|------|------|------|
| `happy.png` | 开心 | `thinking.png` | 思考 |
| `surprised.png` | 惊讶 | `sleepy.png` | 困 |
| `normal.png` | 日常 | `angry.png` | 生气 |

> 无皮肤时自动用 GDI+ 画蓝精灵备选。

## 📜 MIT License
