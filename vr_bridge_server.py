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
    def __init__(self, host=DEFAULT_HOST, port=DEFAULT_PORT, on_grip_callback=None, on_move_callback=None, on_tare_callback=None, on_pose_callback=None, on_home_callback=None, on_shake_callback=None, on_speed_callback=None):
        self.host = host
        self.port = port
        self.on_grip_callback = on_grip_callback
        self.on_move_callback = on_move_callback
        self.on_tare_callback = on_tare_callback
        self.on_pose_callback = on_pose_callback
        self.on_home_callback = on_home_callback
        self.on_shake_callback = on_shake_callback
        self.on_speed_callback = on_speed_callback
        self.speed_pct = 100
        self.server_sock = None
        self.client_sock = None
        self.running = False
        self.thread = None
        self.latest_pos = 0
        self.latest_force = 0.0
        self.latest_vel = [0.0, 0.0, 0.0, 0.0, 0.0, 0.0]  # vx, vy, vz, wx, wy, wz จาก VR
        self.latest_target_pose = None                     # [x, y, z, rx, ry, rz] Target Pose จาก VR
        self.is_teleop_paused = False                      # Pause teleoperation freeze state
        self.on_pause_callback = None
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

    def send_feedback(self, actual_pos, force_n, speed_pct=100, actual_tcp_pose=None, safety_proximity=None):
        """
        ส่งข้อมูลแรงบีบ ตำแหน่งจริง ระดับความเร็ว (Speed %) พิกัดจริง (Actual TCP Pose)
        และอัตราส่วนความใกล้ขอบเขตความปลอดภัย (Safety Proximity Ratios) กลับไปให้ Unity
        Format: STATUS <pos> <force> <speed%> <x> <y> <z> <rx> <ry> <rz> [<outer_r> <inner_r> <cyl_r> <spd_r>]
        """
        if not self.client_sock:
            return
        try:
            if actual_tcp_pose is not None and len(actual_tcp_pose) >= 6:
                x, y, z, rx, ry, rz = actual_tcp_pose[:6]
                base_msg = f"STATUS {int(actual_pos)} {float(force_n):.2f} {int(speed_pct)} {x:.4f} {y:.4f} {z:.4f} {rx:.4f} {ry:.4f} {rz:.4f}"
                # Append safety proximity ratios if available (including Joint Rotation Limits)
                if safety_proximity is not None:
                    o_r = safety_proximity.get("outer_ratio", 0.0)
                    i_r = safety_proximity.get("inner_ratio", 0.0)
                    c_r = safety_proximity.get("cylinder_ratio", 0.0)
                    s_r = safety_proximity.get("speed_ratio", 0.0)
                    rot_r = safety_proximity.get("rot_limit_ratio", 0.0)
                    j_idx = safety_proximity.get("worst_joint_idx", 5)
                    deg_left = safety_proximity.get("worst_deg_left", 999.0)
                    msg = f"{base_msg} {o_r:.3f} {i_r:.3f} {c_r:.3f} {s_r:.3f} {rot_r:.3f} {int(j_idx)} {deg_left:.1f}\n"
                else:
                    msg = f"{base_msg}\n"
            else:
                msg = f"STATUS {int(actual_pos)} {float(force_n):.2f} {int(speed_pct)}\n"
            self.client_sock.sendall(msg.encode("ascii"))
        except Exception:
            pass

    def send_alert(self, level, message):
        """
        ส่งข้อความแจ้งเตือนหรือ Error (ALERT WARNING/ERROR <msg>) ไปยัง Unity แบบ UTF-8
        """
        if not self.client_sock:
            return
        try:
            clean_msg = str(message).replace("\n", " ").strip()
            now = time.time()
            if not hasattr(self, "_last_alert_msg"):
                self._last_alert_msg = ""
                self._last_alert_time = 0.0
            if clean_msg == self._last_alert_msg and (now - self._last_alert_time) < 1.5:
                return
            self._last_alert_msg = clean_msg
            self._last_alert_time = now
            msg = f"ALERT {level} {clean_msg}\n"
            self.client_sock.sendall(msg.encode("utf-8"))
        except Exception:
            pass

    def send_shake_status(self, is_shaking: bool):
        """ ส่งข้อความ SHAKE START / SHAKE DONE ไปยัง Unity """
        if not self.client_sock:
            return
        try:
            cmd = "SHAKE START\n" if is_shaking else "SHAKE DONE\n"
            self.client_sock.sendall(cmd.encode("ascii"))
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
                try:
                    self.client_sock.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
                except Exception:
                    pass
                print(f"\n🎉 [VR Bridge] Unity เชื่อมต่อสำเร็จจาก {addr[0]}:{addr[1]}!")

                buffer = ""
                while self.running:
                    try:
                        data = self.client_sock.recv(1024).decode("ascii")
                        if not data:
                            print(f"\n⚠️ [VR Bridge] Unity หลุดการเชื่อมต่อ ({addr[0]})")
                            break

                        buffer += data
                        latest_grip_cmd = None
                        latest_move_cmd = None
                        latest_pose_cmd = None

                        while "\n" in buffer:
                            line, buffer = buffer.split("\n", 1)
                            line = line.strip()
                            if not line:
                                continue

                            if line.startswith("GRIP"):
                                latest_grip_cmd = line
                            elif line.startswith("MOVE"):
                                latest_move_cmd = line
                            elif line.startswith("POSE"):
                                latest_pose_cmd = line
                            elif line.startswith("TARE"):
                                if self.on_tare_callback:
                                    try:
                                        self.on_tare_callback()
                                    except Exception as ex:
                                        print(f"⚠️ [VR Bridge] on_tare_callback error: {ex}")
                            elif line.startswith("HOME"):
                                if self.on_home_callback:
                                    try:
                                        self.on_home_callback()
                                    except Exception as ex:
                                        print(f"⚠️ [VR Bridge] on_home_callback error: {ex}")
                            elif line.startswith("SHAKE"):
                                if self.on_shake_callback:
                                    try:
                                        self.on_shake_callback()
                                    except Exception as ex:
                                        print(f"⚠️ [VR Bridge] on_shake_callback error: {ex}")
                            elif line.startswith("PAUSE"):
                                parts = line.split()
                                if len(parts) >= 2:
                                    self.is_teleop_paused = (parts[1].strip() == "1")
                                else:
                                    self.is_teleop_paused = not self.is_teleop_paused
                                print(f"⏸️ [VR Bridge] Teleoperation Pause state: {self.is_teleop_paused}")
                                if self.on_pause_callback:
                                    try:
                                        self.on_pause_callback(self.is_teleop_paused)
                                    except Exception as ex:
                                        print(f"⚠️ [VR Bridge] on_pause_callback error: {ex}")
                            elif line.startswith("SPEED"):
                                parts = line.split()
                                if len(parts) >= 2:
                                    try:
                                        sp = int(parts[1])
                                        self.speed_pct = max(0, min(100, sp))
                                        print(f"⚡ [VR Bridge] Speed set to: {self.speed_pct}%")
                                        if self.on_speed_callback:
                                            try:
                                                self.on_speed_callback(self.speed_pct)
                                            except Exception as ex:
                                                print(f"⚠️ [VR Bridge] on_speed_callback error: {ex}")
                                    except ValueError:
                                        pass

                        # 1. ประมวลผลคำสั่ง 'GRIP' ล่าสุดในรอบนี้ (ตัดคำสั่งเก่าตกค้าง ป้องกัน Delay)
                        if latest_grip_cmd:
                            parts = latest_grip_cmd.split()
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

                        # 2. ประมวลผลคำสั่ง 'MOVE' ล่าสุดในรอบนี้
                        if latest_move_cmd:
                            parts = latest_move_cmd.split()
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

                        # 3. ประมวลผลคำสั่ง 'POSE' ล่าสุดในรอบนี้ (ตัดคำสั่งซ้ำซ้อนตกค้างใน TCP Buffer ทันที)
                        if latest_pose_cmd:
                            parts = latest_pose_cmd.split()
                            if len(parts) >= 7:
                                try:
                                    px = float(parts[1])
                                    py = float(parts[2])
                                    pz = float(parts[3])
                                    prx = float(parts[4])
                                    pry = float(parts[5])
                                    prz = float(parts[6])
                                    self.latest_target_pose = [px, py, pz, prx, pry, prz]

                                    if self.on_pose_callback:
                                        try:
                                            self.on_pose_callback(px, py, pz, prx, pry, prz)
                                        except Exception as ex:
                                            print(f"⚠️ [VR Bridge] on_pose_callback error: {ex}")
                                except ValueError:
                                    pass

                    except socket.timeout:
                        continue
                    except Exception as ex:
                        if self.running:
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
