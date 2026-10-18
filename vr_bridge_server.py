import socket
import threading
import time
import sys

# ตั้งค่า stdout utf-8
try:
    sys.stdout.reconfigure(encoding="utf-8")
except Exception:
    pass

DEFAULT_HOST = "0.0.0.0"
DEFAULT_PORT = 5555


class VRBridgeServer:
    """
    TCP Server รับคำสั่งจาก Unity Meta Quest Hand Tracking
    - Port 5555
    - รับคำสั่ง: 'GRIP <pos>\n' (0 = เปิดสุด, 255 = ปิดสุด)
    - รับคำสั่ง: 'GRIP <pos>\n' (0 = เปิดสุด, 255 = ปิดสุด)
    - รับคำสั่ง: 'MOVE <vx> <vy> <vz> [wx] [wy] [wz]\n' (ความเร็ว 6-DOF จากมือ VR, หน่วย m/s และ rad/s)
    - ส่งสถานะกลับ: 'STATUS <pos> <force_n>\n'
    """
    def __init__(self, host=DEFAULT_HOST, port=DEFAULT_PORT, on_grip_callback=None, on_move_callback=None):
        self.host = host
        self.port = port
        self.on_grip_callback = on_grip_callback
        self.on_move_callback = on_move_callback
        self.server_sock = None
        self.client_sock = None
        self.running = False
        self.thread = None
        self.latest_pos = 0
        self.latest_force = 0.0
        self.latest_vel = [0.0, 0.0, 0.0, 0.0, 0.0, 0.0]  # vx, vy, vz, wx, wy, wz จาก VR
        self.client_address = None

    def start(self):
        """ เริ่ม TCP Server ใน Background Thread """
        if self.running:
            return
        self.running = True
        self.thread = threading.Thread(target=self._server_loop, daemon=True, name="VRBridgeServerThread")
        self.thread.start()
        print(f"📡 [VR Bridge] เริ่มต้น TCP Server ที่ {self.host}:{self.port} (รอ Unity เชื่อมต่อ...)")

    def stop(self):
        """ ปิด Server และปล่อย socket """
        self.running = False
        if self.client_sock:
            try:
                self.client_sock.close()
            except Exception:
                pass
            self.client_sock = None
        if self.server_sock:
            try:
                self.server_sock.close()
            except Exception:
                pass
            self.server_sock = None
        print("🛑 [VR Bridge] ปิด TCP Server เรียบร้อย")

    def send_feedback(self, actual_pos, force_n):
        """ ส่งข้อมูลแรงบีบและตำแหน่งจริงกลับไปให้ Unity """
        if not self.client_sock:
            return
        try:
            msg = f"STATUS {int(actual_pos)} {float(force_n):.2f}\n"
            self.client_sock.sendall(msg.encode("ascii"))
        except Exception:
            pass

    def _server_loop(self):
        try:
            self.server_sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            self.server_sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
            self.server_sock.bind((self.host, self.port))
            self.server_sock.listen(1)
            self.server_sock.settimeout(1.0)
        except Exception as e:
            print(f"❌ [VR Bridge] ไม่สามารถเปิด Port {self.port}: {e}")
            self.running = False
            return

        while self.running:
            try:
                # รอ Client เชื่อมต่อ
                try:
                    sock, addr = self.server_sock.accept()
                except socket.timeout:
                    continue

                self.client_sock = sock
                self.client_address = addr
                self.client_sock.settimeout(1.0)
                print(f"\n🎉 [VR Bridge] Unity เชื่อมต่อสำเร็จจาก {addr[0]}:{addr[1]}!")

                buffer = ""
                while self.running:
                    try:
                        data = self.client_sock.recv(1024).decode("ascii")
                        if not data:
                            print(f"\n⚠️ [VR Bridge] Unity หลุดการเชื่อมต่อ ({addr[0]})")
                            break

                        buffer += data
                        while "\n" in buffer:
                            line, buffer = buffer.split("\n", 1)
                            line = line.strip()
                            if not line:
                                continue

                            # ประมวลผลคำสั่ง 'GRIP <pos> [force]'
                            if line.startswith("GRIP"):
                                parts = line.split()
                                if len(parts) >= 2:
                                    try:
                                        pos = int(parts[1])
                                        pos = max(0, min(255, pos))
                                        force = int(parts[2]) if len(parts) >= 3 else None
                                        self.latest_pos = pos

                                        if self.on_grip_callback:
                                            try:
                                                self.on_grip_callback(pos, force)
                                            except TypeError:
                                                self.on_grip_callback(pos)
                                    except ValueError:
                                        pass

                            # ประมวลผลคำสั่ง 'MOVE <vx> <vy> <vz> [wx] [wy] [wz]'
                            # ความเร็ว 6-DOF จากมือ VR ที่ Unity คำนวณแล้ว (linear: m/s, angular: rad/s)
                            elif line.startswith("MOVE"):
                                parts = line.split()
                                if len(parts) >= 4:
                                    try:
                                        vx = float(parts[1])
                                        vy = float(parts[2])
                                        vz = float(parts[3])
                                        wx = float(parts[4]) if len(parts) >= 7 else 0.0
                                        wy = float(parts[5]) if len(parts) >= 7 else 0.0
                                        wz = float(parts[6]) if len(parts) >= 7 else 0.0
                                        self.latest_vel = [vx, vy, vz, wx, wy, wz]

                                        if self.on_move_callback:
                                            try:
                                                self.on_move_callback(vx, vy, vz, wx, wy, wz)
                                            except TypeError:
                                                self.on_move_callback(vx, vy, vz)
                                    except ValueError:
                                        pass

                    except socket.timeout:
                        continue
                    except Exception as ex:
                        print(f"⚠️ [VR Bridge] Client read error: {ex}")
                        break

                if self.client_sock:
                    try:
                        self.client_sock.close()
                    except Exception:
                        pass
                    self.client_sock = None
                    self.client_address = None

            except Exception as e:
                if self.running:
                    print(f"⚠️ [VR Bridge] Server exception: {e}")
                    time.sleep(1.0)


# =========================================================================
# โหมด Standalone Test: รันเดี่ยวๆ เพื่อเทสการขยับนิ้วใน Unity VR ได้ทันที
# =========================================================================
if __name__ == "__main__":
    print("==================================================")
    print("🤖 UR3e / Robotiq VR Hand Bridge Test Server")
    print("==================================================")
    print("เซิร์ฟเวอร์นี้ใช้สำหรับทดสอบรับค่า Pinch จาก Unity Meta Quest")
    print("โดยไม่ต้องต่อหุ่นยนต์จริง!")
    print("==================================================")

    def on_grip(pos):
        pct = int((pos / 255.0) * 100)
        bar_len = 24
        filled = int((pct / 100.0) * bar_len)
        bar = "█" * filled + "░" * (bar_len - filled)
        sys.stdout.write(f"\r🖐️ [VR Hand Pinch] [{bar}] {pct:3d}% | Gripper POS: {pos:3d}/255   ")
        sys.stdout.flush()

    server = VRBridgeServer(host="0.0.0.0", port=5555, on_grip_callback=on_grip)
    server.start()

    print("\nกด Ctrl+C เพื่อหยุดเซิร์ฟเวอร์\n")
    try:
        while True:
            time.sleep(0.5)
    except KeyboardInterrupt:
        print("\n\nกำลังปิดเซิร์ฟเวอร์...")
        server.stop()
