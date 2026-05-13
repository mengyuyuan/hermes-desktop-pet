#!/usr/bin/env python3
"""
鸿钧桥接服务 - 让桌面宠物实时反映 Hermes Agent 的状态
监听 :9101，提供宠物所需的实时状态 API

检测机制（四层，每1.5秒轮询一次）:
1. 网关日志监控: "inbound message" → thinking, "response ready" → responding
2. Hermes API: is_streaming=true → thinking, is_streaming=false → responding
3. CLI 会话监控: 监控会话文件变化 → 自动同步思考/回复状态
4. 网关心跳: systemctl is-active → online/offline

新增: CLI 会话检测层
- 自动发现最新会话文件
- 用户新消息 → thinking 状态
- AI 新回复 → responding + 自动推气泡
"""
import http.server
import json
import os
import glob
import time
import subprocess
import threading

PORT = 9101
HERMES_LOG = os.path.expanduser("~/.hermes/logs/gateway.log")
HERMES_HOME = os.path.expanduser("~/.hermes")
SESSIONS_DIR = os.path.join(HERMES_HOME, "sessions")
POLL_INTERVAL = 1.5  # 秒


# ====== 状态管理 ======
class HermesMonitor:
    def __init__(self):
        self.status = "idle"       # idle | thinking | responding | offline
        self.mood = "happy"        # happy | normal | surprised | sleepy | thinking
        self.bubble = ""           # 当前气泡文本
        self.last_reply = ""       # 最近一次回复内容
        self.last_reply_time = ""
        self.bubble_expire = 0
        self.last_log_size = 0
        self.online = False
        self._lock = threading.Lock()
        self._running = True
        self._last_think_time = 0
        self._latest_reply = ""  # 只保留最新一条，不堆积
        self._next_display_time = 0
        self._surprise_end_time = 0  # 惊讶表情结束时间
        self._last_api_check = ""

        # CLI 会话追踪
        self._session_file = None
        self._session_msg_count = 0
        self._last_session_check = 0

    def poll(self):
        """持续监控 Hermes 状态"""
        while self._running:
            try:
                self._flush_pending_reply()
                self._check_surprise_timer()  # 惊讶→思考过渡
                self._check_gateway()
                self._check_log_activity()
                self._check_api()
                # _check_cli_session() DISABLED - causes session replay flood on restart
            except Exception:
                pass
            time.sleep(POLL_INTERVAL)

    def _check_surprise_timer(self):
        """惊讶 0.5 秒后自动切到思考"""
        if self._surprise_end_time and time.time() >= self._surprise_end_time:
            with self._lock:
                if self.mood == "surprised":
                    self.mood = "thinking"
                self._surprise_end_time = 0

    def _flush_pending_reply(self):
        """显示最新一条回复"""
        if not self._latest_reply:
            return
        now = time.time()
        if now - self._last_think_time < 2.5:
            return
        text = self._latest_reply
        self._latest_reply = ""
        if text.strip() == "[SILENT]":
            return
        with self._lock:
            self.last_reply = text
            self.last_reply_time = time.strftime("%H:%M:%S")
            self.status = "responding"
            self.mood = "happy"
            self.bubble = text
            self.bubble_expire = time.time() + 10

    def _check_gateway(self):
        """检查网关是否在线"""
        try:
            result = subprocess.run(
                ["systemctl", "--user", "is-active", "hermes-gateway"],
                capture_output=True, text=True, timeout=3
            )
            with self._lock:
                was_offline = not self.online
                self.online = (result.stdout.strip() == "active")
                if was_offline and self.online:
                    self.mood = "happy"
                    self.bubble = ""
                    self.bubble_expire = 0
                elif not self.online:
                    self.mood = "sleepy"
                    self.status = "offline"
        except:
            with self._lock:
                self.online = False
                self.status = "offline"

    def _check_log_activity(self):
        """检测日志变化来判断是否在思考/回复"""
        if not os.path.exists(HERMES_LOG):
            return

        try:
            current_size = os.path.getsize(HERMES_LOG)
        except:
            return

        if current_size == self.last_log_size:
            if self.status == "thinking" and time.time() - self._last_think_time > 8:
                with self._lock:
                    self.status = "idle"
                    self.mood = "normal"
            return

        try:
            with open(HERMES_LOG, 'r', errors='replace') as f:
                f.seek(max(0, current_size - 5000))
                tail = f.read()
        except:
            return

        self.last_log_size = current_size

        # 检测"新消息"（不覆盖已有回复状态）
        if ("inbound message" in tail.lower() or "inbound from" in tail.lower()) \
                and self.status != "responding":
            with self._lock:
                self.status = "thinking"
                self.mood = "thinking"
                self.bubble = ""  # 只显示状态，不设占位文字
                self.bubble_expire = 0
                self._last_think_time = time.time()

        # 检测"回复"
        elif "response ready" in tail.lower() or "sending response" in tail.lower():
            with self._lock:
                self.status = "responding"
                self.mood = "happy"
                self.bubble = ""  # 实际回复由 hook 推送
                self.bubble_expire = 0

    def _check_api(self):
        """通过 Hermes API 检测是否正在思考/生成"""
        import urllib.request
        try:
            req = urllib.request.Request("http://localhost:8787/api/sessions",
                                         headers={"Accept": "application/json"})
            resp = urllib.request.urlopen(req, timeout=2)
            data = resp.read().decode("utf-8")

            if '"is_streaming": true' in data:
                with self._lock:
                    self.status = "thinking"
                    self.mood = "thinking"
                    
                    self._last_think_time = time.time()
                    self._last_api_check = "streaming"
            elif self._last_api_check == "streaming":
                self._last_api_check = ""
                with self._lock:
                    self.status = "responding"
                    self.mood = "happy"
                    self.bubble = ""  # 实际回复由 hook 推送
                    self.bubble_expire = 0
        except:
            pass

    def _find_latest_session(self):
        """找到最新的非cron会话文件"""
        try:
            all_files = glob.glob(os.path.join(SESSIONS_DIR, "session_*.json"))
            # 排除 cron 会话
            regular = [f for f in all_files if "cron_" not in os.path.basename(f)]
            if not regular:
                return None
            # 返回最近修改的文件
            return max(regular, key=os.path.getmtime)
        except:
            return None

    def _check_cli_session(self):
        """监控 CLI 会话文件变化，自动同步思考/回复状态"""
        # 每2秒检查一次
        now = time.time()
        if now - self._last_session_check < 2:
            return
        self._last_session_check = now

        session_file = self._find_latest_session()
        if not session_file:
            return

        try:
            with open(session_file, 'r', encoding='utf-8', errors='replace') as f:
                data = json.load(f)
        except:
            return

        msgs = data.get("messages", [])
        if not isinstance(msgs, list):
            return

        current_count = len(msgs)

        # 第一次检测，只记录不触发
        if self._session_file != session_file:
            self._session_file = session_file
            self._session_msg_count = current_count
            return

        # 没有新消息
        if current_count <= self._session_msg_count:
            return

        # 有新消息！看看是什么
        new_msgs = msgs[self._session_msg_count:]
        self._session_msg_count = current_count

        for msg in new_msgs:
            role = msg.get("role", "")
            content = str(msg.get("content", ""))

            # 检测到任何非system新消息 → 可能有用户输入，触发思考
            if role != "system" and role != "tool":
                if self.status != "thinking":
                    with self._lock:
                        self.status = "thinking"
                        self.mood = "surprised"
                        self.bubble = ""
                        self.bubble_expire = 0
                        self._last_think_time = time.time()
                        self._surprise_end_time = time.time() + 1.0
                # 如果是助手消息且有内容，推送回复
                if role == "assistant":
                    text = content.strip()
                    if text and not msg.get("tool_calls"):
                        with self._lock:
                            if time.time() - self._last_think_time < 2.5:
                                self._pending_reply = text
                            else:
                                self.last_reply = text
                                self.last_reply_time = time.strftime("%H:%M:%S")
                                self.status = "responding"
                                self.mood = "happy"
                                self.bubble = text
                                self.bubble_expire = time.time() + 10  # 10秒后消失
                                self._pending_reply = ""

    def set_bubble(self, text, duration=4):
        """手动设置气泡"""
        with self._lock:
            self.bubble = text
            self.bubble_expire = time.time() + duration

    def get_state(self):
        """获取当前状态（线程安全）"""
        with self._lock:
            now = time.time()
            # 气泡过期清空，同时重置状态为 idle（如果当前是 responding）
            if self.bubble_expire < now and self.status != "thinking":
                self.bubble = ""
                if self.status == "responding":
                    self.status = "idle"
                    self.mood = "normal"

            return {
                "online": self.online,
                "status": self.status,
                "mood": self.mood,
                "bubble": self.bubble,
                "last_reply": self.last_reply,
                "last_reply_time": self.last_reply_time,
                "queue_size": 0,
                "timestamp": time.strftime("%H:%M:%S")
            }


monitor = HermesMonitor()


# ====== HTTP 服务 ======
class BridgeHandler(http.server.BaseHTTPRequestHandler):
    def do_GET(self):
        if self.path == "/status":
            self._json(monitor.get_state())
        elif self.path == "/health":
            self._json({"status": "ok", "time": time.strftime("%H:%M:%S")})
        elif self.path == "/ping":
            self._json({"pong": True})
        else:
            self.send_error(404)

    def do_POST(self):
        try:
            length = int(self.headers.get('Content-Length', 0))
            body = json.loads(self.rfile.read(length))
        except:
            self.send_error(400)
            return

        if self.path == "/bubble":
            text = body.get("text", "")
            duration = body.get("duration", 4)
            mood = body.get("mood", "")
            with monitor._lock:
                monitor.bubble = text
                monitor.bubble_expire = time.time() + duration
                if mood:
                    monitor.mood = mood
                if text:
                    monitor.status = "responding"
            self._json({"ok": True})
        elif self.path == "/reply":
            text = body.get("text", "")
            if text:
                if text.strip() != "[SILENT]":
                    with monitor._lock:
                        monitor._latest_reply = text
                        monitor._last_think_time = time.time()
            self._json({"ok": True})
        elif self.path == "/think":
            with monitor._lock:
                monitor.status = "thinking"
                monitor.mood = "thinking"
                monitor.bubble = ""  # 不设占位气泡，表情本身表达状态
                monitor.bubble_expire = 0
                monitor._last_think_time = time.time()
            self._json({"ok": True})
        elif self.path == "/clear":
            with monitor._lock:
                monitor._latest_reply = ""
                monitor.bubble = ""
                monitor.bubble_expire = 0
                monitor.last_reply = ""
                monitor.last_reply_time = ""
                monitor.status = "idle"
                monitor.mood = "normal"
                monitor._pending_reply = ""
                monitor._next_display_time = 0
            self._json({"ok": True, "message": "Queue cleared, state reset to idle"})
        else:
            self.send_error(404)

    def _json(self, data):
        self.send_response(200)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Access-Control-Allow-Origin", "*")
        self.end_headers()
        self.wfile.write(json.dumps(data, ensure_ascii=False).encode("utf-8"))

    def log_message(self, *a):
        pass


def serve():
    server = http.server.HTTPServer(("0.0.0.0", PORT), BridgeHandler)
    print(f"鸿钧桥接服务 :{PORT}")
    server.serve_forever()


if __name__ == "__main__":
    t = threading.Thread(target=monitor.poll, daemon=True)
    t.start()
    serve()
