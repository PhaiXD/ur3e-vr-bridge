import math
import socket
import sys
import threading
import time
import keyboard
import rtde_control
import rtde_receive

# ตั้งค่า stdout เป็น utf-8 เพื่อรองรับภาษาไทยและ emoji บน Windows Console
sys.stdout.reconfigure(encoding="utf-8")

ROBOT_IP = "192.168.1.20"

# ==================== UR3e Specifications ====================
# DH Parameters (มิลลิเมตร): d1=151.9, a2=243.6, a3=213.2, d4=131.0, d5=85.0, d6=92.1
# Max reach: 500 mm (0.5 m)
# Max joint speed: 180°/s (joints 1-3), 360°/s (joints 4-6)
# Max TCP speed: 1 m/s

# ==================== ความเร็วและการเคลื่อนที่แบบ Smooth (speedL) ====================
SMOOTH_SPEED = 0.12     # m/s   ความเร็วเชิงเส้นต่อเนื่อง (Smooth & ไหลลื่น ไม่กระตุกทีละเซน)
SMOOTH_ACCEL = 0.40     # m/s²  อัตราเร่งให้นุ่มนวล ไม่กระชาก
STOP_ACCEL = 1.50       # m/s²  อัตราการชะลอหยุดเมื่อปล่อยปุ่ม
J_SPEED = 1.05          # rad/s สำหรับ moveJ ตอนกลับ Safe Home
J_ACCEL = 1.40          # rad/s²

# ==================== การตั้งค่า Gripper (Robotiq) ====================
GRIPPER_PORT = 63352
GRIPPER_SPEED = 128     # 50% ของความเร็วสูงสุด (ช่วง 0-255)
GRIPPER_FORCE = 128     # 50% ของแรงหนีบสูงสุด (ช่วง 0-255) ป้องกันการบีบวัตถุแตก

# ==================== ท่า Home ของ UR3e (จาก Datasheet) ====================
# [Base, Shoulder, Elbow, Wrist1, Wrist2, Wrist3] ในหน่วย radian
# ท่า Default: [0°, -90°, 0°, -90°, 0°, 0°]
# !! แต่ท่านี้มี Elbow=0° และ Wrist2=0° ซึ่งอยู่ตรง Singularity !!
# ดังนั้นเราใช้ "Safe Home" ที่งอข้อศอกออก เพื่อหลบ Singularity
HOME_Q = [
    math.radians(0),      # Base:     0°      (หันหน้าตรง)
    math.radians(-90),    # Shoulder: -90°    (แขนบนอยู่แนวนอน)
    math.radians(-90),    # Elbow:    -90°    (งอข้อศอก 90° → หลบ Singularity!)
    math.radians(-90),    # Wrist 1:  -90°    (ข้อมือชี้ลง)
    math.radians(90),     # Wrist 2:  +90°    (หลบ Wrist Singularity!)
    math.radians(0),      # Wrist 3:  0°
]

# ==================== ขอบเขตความปลอดภัย (Safety Boundaries) ====================
# 1. ขอบเขตพื้นที่ทำงาน Cartesian
MIN_BASE_RADIUS = 0.10   # เมตร - รัศมีขั้นต่ำรอบเสาฐาน (UR3e ฐานเล็ก)
MAX_REACH_XY = 0.45      # เมตร - ระยะเอื้อมแนวราบ (UR3e reach 0.5 m, เว้นระยะ)
MIN_Z = 0.01             # เมตร - ความสูงต่ำสุด (ป้องกันชนโต๊ะ)
MAX_Z = 0.75             # เมตร - ความสูงสูงสุด

# 2. ขอบเขตมุมข้อต่อ (Joint Angle Safety Limits)
# กำหนดช่วงปลอดภัยของแต่ละข้อต่อ เพื่อป้องกันแขนพับมาชนตัวเอง
# !! นี่คือการป้องกันหลักที่สำคัญที่สุด !!
JOINT_LIMITS = [
    # (min_deg, max_deg) สำหรับแต่ละข้อต่อ
    (-170, 170),     # J1 Base:     จำกัดการหมุนฐาน ป้องกันสายพันตัว
    (-170, -10),     # J2 Shoulder: ป้องกันแขนบนพับขึ้นสุดหรือลงสุดจนชนฐาน
    (-160, -10),     # J3 Elbow:    บังคับให้ข้อศอกงออยู่เสมอ (ห้ามเหยียดตรง!)
    (-170, 0),       # J4 Wrist 1:  จำกัดเพื่อป้องกันข้อมือม้วนชนท่อนแขน
    (10, 170),       # J5 Wrist 2:  ห้ามผ่าน 0° เด็ดขาด (Wrist Singularity!)
    (-170, 170),     # J6 Wrist 3:  ค่อนข้างอิสระ
]
JOINT_LIMITS_RAD = [(math.radians(lo), math.radians(hi)) for lo, hi in JOINT_LIMITS]

# 3. เกณฑ์เตือนเข้าใกล้ขีดจำกัด (Warning Margin)
JOINT_WARNING_MARGIN = math.radians(15)  # เตือนเมื่อเหลืออีก 15° จะถึงขอบ

# 4. เกณฑ์ตรวจจับ Joint Jump (การหมุนตลบกะทันหัน)
MAX_JOINT_JUMP = math.radians(15)  # ถ้าข้อต่อใดหมุนเกิน 15° ในสเต็ปเดียว = อันตราย

# ==================== ชื่อข้อต่อ ====================
JOINT_NAMES = ["Base", "Shoulder", "Elbow", "Wrist 1", "Wrist 2", "Wrist 3"]


class RobotiqGripper:
    """
    ควบคุม Robotiq Gripper (2F-85 / 2F-140 / Hand-E) ผ่านพอร์ต 63352 ของ UR
    ระบบ Ultra-Sensitive Force Detection & Adaptive Squeeze:
    - ตรวจจับการสัมผัสวัตถุได้ทันทีจาก position lag (ไม่ต้องรอให้มอเตอร์อัดจนสุด)
    - เริ่มวัดค่าและสั่งแรงบีบทันทีตั้งแต่เริ่มสัมผัส (แตะเบาๆ บีบนิดเดียว จนถึงบีบแน่นเต็มที่)
    - รองรับทั้ง VR Hand Tracking และ Keyboard Jogging
    """
    def __init__(self, ip=ROBOT_IP, port=GRIPPER_PORT):
        self.ip = ip
        self.port = port
        self.sock = None
        self.connected = False
        self.actual_pos = 0
        self.prev_actual_pos = 0
        self.pos_stall_time = 0.0
        self._last_status_time = time.time()
        self.contact_detected = False
        self.contact_pos = None
        self.current_for = 25        # แรงเริ่มต้นนุ่มนวล (~35 N)
        self.measured_force_n = 0.0
        self.status_desc = "ไม่ได้เชื่อมต่อ"
        self.is_moving = False
        self.lock = threading.Lock()
        self._last_target_pos = -1
        self._last_target_for = -1
        self._last_sent_pos = -1
        self.connect()

    def connect(self):
        try:
            self.sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            self.sock.settimeout(2.0)
            self.sock.connect((self.ip, self.port))
            self.connected = True

            # ตรวจสอบว่า Activate หรือยัง ถ้ายัง ให้ Activate ก่อนเริ่มทำงาน
            self.ensure_activated()

            actual_pos, _, _, force_n, _ = self.get_status()
            self.actual_pos = actual_pos
            print(f"✅ เชื่อมต่อ Robotiq Gripper สำเร็จ (Port {self.port}, ตำแหน่งเริ่มต้น: {actual_pos}, แรง: {force_n:.1f} N)")
        except Exception as e:
            print(f"⚠️ ไม่สามารถเชื่อมต่อ Gripper: {e}")
            self.sock = None
            self.connected = False

    def is_activated(self):
        """ ตรวจสอบว่า Gripper Activate แล้วหรือไม่ (ACT == 1 และ STA == 3) """
        if not self.connected:
            return False
        act_res = self._send("GET ACT")
        sta_res = self._send("GET STA")
        return ("ACT 1" in act_res) and ("STA 3" in sta_res)

    def ensure_activated(self, timeout=10.0):
        """
        ตรวจสอบสถานะว่า Gripper Activate หรือยัง
        - หาก Activate เรียบร้อยแล้ว: แจ้งพร้อมใช้งานทันที
        - หากติด Fault: สั่ง Reset Fault อัตโนมัติ
        - หากยังไม่ได้ Activate: สั่ง SET ACT 1 และรอจน Homing เสร็จสิ้น
        """
        if not self.connected or not self.sock:
            return False

        act_res = self._send("GET ACT")
        sta_res = self._send("GET STA")
        flt_res = self._send("GET FLT")

        flt_code = "0"
        try:
            flt_code = flt_res.split()[1]
        except Exception:
            pass

        is_fault = False
        try:
            is_fault = (int(flt_code, 16) != 0)
        except Exception:
            is_fault = False

        if is_fault:
            print(f"⚠️ Gripper ติด Fault (FLT={flt_code}) -> กำลัง Reset Fault อัตโนมัติ...")
            self._send("SET ACT 0")
            time.sleep(0.3)
            self._send("SET ACT 1")
            act_res = "ACT 1"
            sta_res = "STA 1"

        if "ACT 1" in act_res and "STA 3" in sta_res:
            print("   ✅ Gripper ได้รับการ Activate แล้ว (สถานะ: พร้อมใช้งานทันที)")
            self._send("SET GTO 1")
            self._send(f"SET SPE {GRIPPER_SPEED}")
            self._send(f"SET FOR {self.current_for}")
            return True

        print("   ⚡ [Gripper] ยังไม่ได้ Activate -> กำลังสั่ง Activate (Auto-homing)...")
        self._send("SET ACT 1")
        self._send("SET GTO 1")
        self._send(f"SET SPE {GRIPPER_SPEED}")
        self._send(f"SET FOR {self.current_for}")

        start_time = time.time()
        while time.time() - start_time < timeout:
            sta_res = self._send("GET STA")
            if "STA 3" in sta_res:
                print("   ✅ [Gripper] ทำการ Activate สำเร็จเรียบร้อย พร้อมใช้งาน!")
                return True
            time.sleep(0.3)

        print(f"   ❌ [Gripper] หมดเวลาการ Activate ({timeout}s)! กรุณาตรวจสอบสถานะ Gripper")
        return False

    def _send(self, cmd):
        if not self.connected or not self.sock:
            return ""
        with self.lock:
            try:
                self.sock.sendall((cmd + "\n").encode())
                res = self.sock.recv(1024).decode().strip()
                return res
            except Exception:
                return ""

    def get_status(self):
        """
        อ่านค่า POS, OBJ, COU และคำนวณแรงบีบจริง (Grip Force) แบบละเอียดต่อเนื่อง ทันทีที่สัมผัสวัตถุ
        คืนค่า: (actual_pos, obj_str, cou_val, force_n, status_desc)
        """
        if not self.connected:
            return 0, "OBJ ?", 0, 0.0, "ไม่ได้เชื่อมต่อ"

        pos_res = self._send("GET POS")
        obj_res = self._send("GET OBJ")
        cou_res = self._send("GET COU")

        actual_pos = 0
        try:
            actual_pos = int(pos_res.split()[1])
        except Exception:
            pass

        obj_str = obj_res.strip()
        cou_val = 0
        try:
            cou_val = int(cou_res.split()[1])
        except Exception:
            pass

        now = time.time()
        dt = max(0.001, now - self._last_status_time)
        self._last_status_time = now

        # ตรวจสอบการเคลื่อนไหวของกริปเปอร์จริง
        pos_delta = abs(actual_pos - self.prev_actual_pos)
        self.prev_actual_pos = actual_pos

        if pos_delta <= 1:
            self.pos_stall_time += dt
        else:
            self.pos_stall_time = 0.0

        self.actual_pos = actual_pos

        # -------------------------------------------------------------
        # การตรวจจับการสัมผัสวัตถุแบบ Ultra-Sensitive:
        # 1. Hardware Detection: OBJ 2 (หยุดชนขณะปิด) หรือ OBJ 1 (หยุดชนขณะเปิด)
        # 2. Impedance / Stall: กริปเปอร์หยุดนิ่งเกิน 60ms ขณะที่ target สั่งให้ปิดลึกกว่า actual_pos + 4
        #    (และ actual_pos < 225 เพื่อไม่นับการปิดชนตัวเองในอากาศ)
        # -------------------------------------------------------------
        is_obj_contact = ("OBJ 2" in obj_str) or ("OBJ 1" in obj_str)
        is_stall_contact = (self.pos_stall_time >= 0.06) and (self._last_target_pos > actual_pos + 4) and (actual_pos < 225)

        if is_obj_contact or is_stall_contact:
            if not self.contact_detected:
                self.contact_detected = True
                self.contact_pos = actual_pos
        elif "OBJ 3" in obj_str and abs(actual_pos - self._last_target_pos) <= 3:
            # อยู่ในอากาศและถึงเป้าหมายจริงโดยไม่มีอะไรขวาง
            self.contact_detected = False
            self.contact_pos = None

        if self.contact_detected and self.contact_pos is not None:
            # ตรวจสอบว่าผู้ใช้คลายนิ้วออกหรือยัง (กางนิ้วกว้างกว่าผิววัตถุ)
            if self._last_target_pos <= self.contact_pos - 2:
                self.contact_detected = False
                self.contact_pos = None
                force_n = 0.0
                status_desc = "คลายการสัมผัส"
            else:
                # ---------------------------------------------------------
                # คำนวณแรงบีบตามสัดส่วนความลึก (Proportional Squeeze Effort):
                # เริ่มต้นทันทีที่สัมผัส (depth >= 1) -> แรงไต่ระดับตั้งแต่ ~18 N ไปจนถึง 235 N
                # ---------------------------------------------------------
                depth = max(1, self._last_target_pos - self.contact_pos)
                max_depth = 35.0  # บีบลึกเข้าไป 35 units จากผิวสัมผัส = บีบแรง 100%
                squeeze_ratio = min(1.0, depth / max_depth)

                base_force = 18.0 + squeeze_ratio * (235.0 - 18.0)

                # ผสานกับกระแสไฟฟ้ามอเตอร์จริง (cou_val * 10 mA) เพื่อความสมจริงยิ่งขึ้น
                if cou_val > 0:
                    cur_ratio = min(1.0, cou_val / 80.0)
                    force_n = 0.6 * base_force + 0.4 * (18.0 + cur_ratio * (235.0 - 18.0))
                else:
                    force_n = base_force

                if force_n < 45.0:
                    status_desc = "🪶 แตะโดนวัตถุ (บีบนิดเดียว)"
                elif force_n < 95.0:
                    status_desc = "🤏 บีบปานกลาง (กำลังดี)"
                elif force_n < 165.0:
                    status_desc = "🗜️ บีบแน่น (จับมั่นคง)"
                else:
                    status_desc = "💪 บีบแรงเต็มที่"

        else:
            force_n = 0.0
            if "OBJ 0" in obj_str:
                status_desc = "กำลังเคลื่อนที่ในอากาศ"
            else:
                status_desc = "ไม่มีแรงต้าน (ในอากาศ)"

        self.measured_force_n = round(force_n, 1)
        self.status_desc = status_desc
        return actual_pos, obj_str, cou_val, self.measured_force_n, status_desc

    def start_close(self):
        """ สั่ง Gripper เริ่มหนีบเข้าแบบนุ่มนวล หรือค่อยๆ เร่งแรงบีบขึ้นถ้าสัมผัสวัตถุแล้ว """
        if not self.connected:
            return
        if not self.is_activated():
            if not self.ensure_activated():
                return

        actual_pos, obj_str, _, _, _ = self.get_status()
        self.actual_pos = actual_pos

        # หากกำลังสัมผัสวัตถุอยู่แล้ว หรือชนโดนวัตถุ: ค่อยๆ เร่งแรงบีบขึ้นเรื่อยๆ
        if self.contact_detected or ("OBJ 2" in obj_str) or (self.current_for > 30 and actual_pos < 225):
            self.contact_detected = True
            if self.contact_pos is None:
                self.contact_pos = actual_pos
            if self.current_for < 255:
                self.current_for = min(255, self.current_for + 5)
                self._send(f"SET FOR {self.current_for}")
                self._send("SET POS 255")
            self._last_target_pos = 255
            self.is_moving = True
            return

        if actual_pos >= 225:
            return

        # เคลื่อนที่ในอากาศ: เริ่มต้นด้วยแรงนุ่มนวลปลอดภัย (FOR 20, ~30 N)
        self.contact_detected = False
        self.contact_pos = None
        self.current_for = 20
        self._last_target_pos = 255
        self._send("SET GTO 1")
        self._send(f"SET SPE {GRIPPER_SPEED}")
        self._send(f"SET FOR {self.current_for}")
        self._send("SET POS 255")
        self.is_moving = True

    def start_open(self):
        """ สั่ง Gripper เริ่มคลายออกอย่างต่อเนื่องและนุ่มนวล """
        if not self.connected:
            return
        if not self.is_activated():
            if not self.ensure_activated():
                return

        actual_pos, _, _, _, _ = self.get_status()
        self.actual_pos = actual_pos
        if actual_pos <= 5:
            return

        self.contact_detected = False
        self.contact_pos = None
        self.current_for = 25
        self._last_target_pos = 0
        self._send("SET GTO 1")
        self._send(f"SET SPE {GRIPPER_SPEED}")
        self._send(f"SET FOR {self.current_for}")
        self._send("SET POS 0")
        self.is_moving = True

    def stop(self):
        """ สั่งหยุดการเคลื่อนที่ ณ ตำแหน่งปัจจุบันเมื่อปล่อยปุ่ม (คงแรงบีบไว้หากจับวัตถุอยู่) """
        if not self.connected:
            return

        obj_res = self._send("GET OBJ")
        if "OBJ 2" in obj_res or self.contact_detected:
            self.contact_detected = True
            self.is_moving = False
            return

        self._send("SET GTO 0")
        self.is_moving = False

    def set_position(self, pos, force=None):
        """
        ตั้งค่าตำแหน่งและแรงบีบของ Gripper แบบ Sensitive & Adaptive (สำหรับ VR Hand Tracking)
        - pos: 0 (เปิดสุด) ถึง 255 (ปิดสุด)
        - force: กำหนดแรงเจาะจง 0-255 หรือ None เพื่อคำนวณอัตโนมัติตามระยะบีบนิ้ว
        
        กลไก:
        1. ในอากาศ: เคลื่อนที่ตามตำแหน่งมือจริงด้วยแรงต่ำนุ่มนวล (FOR 25, ~35 N)
        2. ทันทีที่สัมผัสโดนวัตถุ: ตรวจจับได้ทันทีจาก position lag (ไม่ต้องรอให้มอเตอร์อัดจนสุด)
        3. การบีบวัตถุ: สเกลแรงบีบแบบ Proportional จาก 18 N ไปจนถึง 235 N ตามระยะที่นิ้วใน VR บีบลึกเข้าไป
        4. การคลาย: กางนิ้วกว้างกว่าผิววัตถุ -> กริปเปอร์คลายออกและปล่อยวัตถุทันที
        """
        if not self.connected or not self.sock:
            return
        if not self.is_activated():
            if not self.ensure_activated():
                return

        pos = max(0, min(255, int(pos)))
        self._last_target_pos = pos

        actual_pos = self.actual_pos
        if actual_pos == 0:
            actual_pos, _, _, _, _ = self.get_status()

        # -------------------------------------------------------------
        # 1. ตรวจจับการเริ่มสัมผัสวัตถุ หรือการคลายออก
        # -------------------------------------------------------------
        if not self.contact_detected:
            # ตรวจสอบการชนวัตถุเมื่อตำแหน่งหยุดนิ่งทั้งที่สั่งปิดลึกกว่า actual_pos + 4
            if (self.pos_stall_time >= 0.06) and (pos > actual_pos + 4) and (actual_pos < 225):
                self.contact_detected = True
                self.contact_pos = actual_pos
        else:
            # ผู้ใช้กางนิ้วออกกว้างกว่าตัววัตถุ -> ปลดปล่อยการสัมผัส
            ref_pos = self.contact_pos if self.contact_pos is not None else actual_pos
            if pos <= ref_pos - 2:
                self.contact_detected = False
                self.contact_pos = None

        # -------------------------------------------------------------
        # 2. ควบคุมแรงบีบและตำแหน่ง
        # -------------------------------------------------------------
        if self.contact_detected and self.contact_pos is not None:
            # กำลังบีบวัตถุ: คำนวณแรงบีบตามสัดส่วนความลึก (Squeeze Depth)
            depth = max(1, pos - self.contact_pos)
            max_depth = 35.0  # ระยะ 35 units จากผิว = บีบแรง 100%
            ratio = min(1.0, depth / max_depth)

            if force is not None:
                target_for = max(10, min(255, int(force)))
            else:
                target_for = int(18 + ratio * (255 - 18))

            for_changed = abs(self.current_for - target_for) >= 2
            if for_changed:
                self._send(f"SET FOR {target_for}")
                self.current_for = target_for
                self._last_target_for = target_for

            # สั่งกริปเปอร์บีบวัตถุด้วยแรง target_for
            if self._last_sent_pos != 255:
                self._send("SET POS 255")
                self._last_sent_pos = 255

            # อัปเดตค่าแรงบีบที่วัดได้แบบเรียลไทม์
            self.measured_force_n = round(18.0 + ratio * (235.0 - 18.0), 1)
            if self.measured_force_n < 45.0:
                self.status_desc = "🪶 แตะโดนวัตถุ (บีบนิดเดียว)"
            elif self.measured_force_n < 95.0:
                self.status_desc = "🤏 บีบปานกลาง (กำลังดี)"
            elif self.measured_force_n < 165.0:
                self.status_desc = "🗜️ บีบแน่น (จับมั่นคง)"
            else:
                self.status_desc = "💪 บีบแรงเต็มที่"

        else:
            # เคลื่อนที่ในอากาศ: ส่งตำแหน่งจริงตามมือ และใช้แรงต่ำปลอดภัย
            if self.current_for != 25:
                self._send("SET FOR 25")
                self.current_for = 25
                self._last_target_for = 25

            pos_changed = abs(self._last_sent_pos - pos) >= 2
            if pos_changed:
                self._send(f"SET POS {pos}")
                self._last_sent_pos = pos

            self.measured_force_n = 0.0
            self.status_desc = "ไม่มีแรงต้าน (ในอากาศ)"

        self.is_moving = True

    def _print_gauge(self, pos, force_n, cou_val, status_desc):
        """ แสดงผลตำแหน่ง หลอดแสดงสถานะ และแรงบีบที่วัดได้แบบละเอียด """
        pct = min(100, max(0, int(((pos - 3) / (228.0 - 3.0)) * 100)))
        bar_len = 16
        filled = int((pct / 100.0) * bar_len)
        bar = "█" * filled + "░" * (bar_len - filled)
        current_ma = cou_val * 10

        if force_n > 0:
            print(f"   [{bar}] {pct:3d}% | 🗜️ แรงบีบ: {force_n:5.1f} N ({current_ma:4d} mA) | {status_desc}")
        else:
            print(f"   [{bar}] {pct:3d}% | 👐 แรงบีบ:   0.0 N (ไม่มีแรงต้าน) | {status_desc}")

    def disconnect(self):
        if self.sock:
            try:
                self.sock.close()
            except Exception:
                pass
            self.connected = False


def check_cartesian_safety(pose):
    """
    ตรวจสอบว่าพิกัดเป้าหมาย (X, Y, Z) อยู่ในพื้นที่ทำงานที่ปลอดภัยหรือไม่
    คืนค่า (is_safe, reason)
    """
    x, y, z = pose[0], pose[1], pose[2]

    # 1. ระยะห่างจากเสาฐาน
    r_xy = math.hypot(x, y)
    if r_xy < MIN_BASE_RADIUS:
        return False, f"ใกล้เสาฐานเกินไป! (r={r_xy:.3f}m < {MIN_BASE_RADIUS}m)"

    # 2. ระยะเอื้อมแนวราบ
    if r_xy > MAX_REACH_XY:
        return False, f"เอื้อมไกลเกินไป! (r={r_xy:.3f}m > {MAX_REACH_XY}m)"

    # 3. ขอบเขตแกน Z
    if z < MIN_Z:
        return False, f"ต่ำเกินไป! (Z={z:.3f}m < {MIN_Z}m) เสี่ยงชนโต๊ะ"
    if z > MAX_Z:
        return False, f"สูงเกินไป! (Z={z:.3f}m > {MAX_Z}m)"

    return True, "OK"


def check_joint_safety(q_current):
    """
    ตรวจสอบว่าองศาข้อต่อปัจจุบันอยู่ในช่วงปลอดภัยหรือไม่
    คืนค่า (is_safe, violations_list, warnings_list)
    """
    violations = []
    warnings = []

    for i in range(6):
        deg = math.degrees(q_current[i])
        lo, hi = JOINT_LIMITS[i]
        lo_rad, hi_rad = JOINT_LIMITS_RAD[i]

        # ตรวจสอบการละเมิดขอบเขต
        if q_current[i] < lo_rad or q_current[i] > hi_rad:
            violations.append(f"{JOINT_NAMES[i]}: {deg:.1f}° (ขอบเขต: {lo}° ถึง {hi}°)")

        # ตรวจสอบเข้าใกล้ขอบ
        elif (q_current[i] - lo_rad) < JOINT_WARNING_MARGIN:
            warnings.append(f"{JOINT_NAMES[i]}: {deg:.1f}° ใกล้ขอบล่าง ({lo}°)")
        elif (hi_rad - q_current[i]) < JOINT_WARNING_MARGIN:
            warnings.append(f"{JOINT_NAMES[i]}: {deg:.1f}° ใกล้ขอบบน ({hi}°)")

    return len(violations) == 0, violations, warnings


def isPoseReachable(rtde_c, pose, qnear=None):
    """
    ตรวจสอบว่าพิกัดเป้าหมาย (X, Y, Z, Rx, Ry, Rz) สามารถไปถึงได้จริงและปลอดภัยหรือไม่ (4 ระดับ)
    1. ตรวจสอบขอบเขต Cartesian ทั่วไป (Workspace Safety Boundaries)
    2. ตรวจสอบผ่าน Controller Safety Limits (isPoseWithinSafetyLimits)
    3. ตรวจสอบว่ามีคำตอบ Inverse Kinematics ใกล้กับท่าปัจจุบันหรือไม่ (getInverseKinematicsHasSolution)
    4. คำนวณ Joint Target ล่วงหน้า แล้วตรวจว่าไม่ละเมิด Joint Limits / Singularity
    คืนค่า: (is_reachable, reason)
    """
    # ระดับ 1: ขอบเขตระยะ Cartesian
    cart_safe, cart_reason = check_cartesian_safety(pose)
    if not cart_safe:
        return False, f"🛑 [Cartesian] {cart_reason}"

    # ระดับ 2: Safety Limits และ Planes ของ UR Controller
    try:
        if not rtde_c.isPoseWithinSafetyLimits(pose):
            return False, "🛑 [Safety Limits] พิกัดอยู่นอก Safety Limits หรือข้อจำกัดของ Controller"
    except Exception as e:
        return False, f"⚠️ ไม่สามารถตรวจสอบ Safety Limits: {e}"

    # ระดับ 3: ตรวจสอบว่ามีคำตอบ IK ใกล้เคียงท่าปัจจุบัน (ป้องกันแขนหมุนตลบ)
    try:
        if qnear is not None:
            has_sol = rtde_c.getInverseKinematicsHasSolution(pose, qnear)
        else:
            has_sol = rtde_c.getInverseKinematicsHasSolution(pose)

        if not has_sol:
            return False, "🛑 [IK Solver] ไม่มีคำตอบ Inverse Kinematics ที่ปลอดภัยใกล้เคียงท่าปัจจุบัน (เสี่ยงข้อต่อขัดกัน)"
    except Exception as e:
        return False, f"⚠️ ไม่สามารถตรวจสอบ IK Solution: {e}"

    # ระดับ 4: ตรวจสอบองศาข้อต่อที่คำนวณได้ล่วงหน้า
    try:
        target_q = rtde_c.getInverseKinematics(pose, qnear if qnear else [])
        if target_q and len(target_q) == 6:
            joint_safe, violations, _ = check_joint_safety(target_q)
            if not joint_safe:
                return False, f"🛑 [Joint Safety] องศาข้อต่อที่คำนวณได้เสี่ยงชนตัวเอง: {', '.join(violations)}"
    except Exception as e:
        return False, f"⚠️ ไม่สามารถคำนวณ Target Joint: {e}"

    return True, "OK"


def check_joint_jump(q_before, q_after):
    """
    ตรวจจับการหมุนตลบกะทันหัน (Joint Jump / Configuration Flip)
    ถ้ามุมข้อต่อใดเปลี่ยนไปเกิน MAX_JOINT_JUMP ในสเต็ปเดียว = อันตราย
    คืนค่า (is_safe, culprit_msg)
    """
    for i in range(6):
        delta = abs(q_after[i] - q_before[i])
        delta = min(delta, 2 * math.pi - delta)

        if delta > MAX_JOINT_JUMP:
            deg = math.degrees(delta)
            return False, f"{JOINT_NAMES[i]} หมุนตลบ {deg:.1f}° (เกินเกณฑ์ {math.degrees(MAX_JOINT_JUMP):.0f}°)"

    return True, "OK"


def check_protective_stop(rtde_r):
    """ ตรวจสอบว่าหุ่นยนต์เข้าสู่สถานะ Protective Stop หรือไม่ """
    return rtde_r.isProtectiveStopped()


def recover_from_protective_stop(rtde_c, rtde_r):
    """ กู้คืนจาก Protective Stop อัตโนมัติ """
    print("🔧 กำลังกู้คืนจาก Protective Stop...")
    time.sleep(1.0)
    try:
        rtde_c.reconnect()
        time.sleep(0.5)
        if not rtde_r.isProtectiveStopped():
            print("✅ กู้คืนสำเร็จ!")
            return True
    except Exception as e:
        print(f"⚠️ กู้คืนล้มเหลว: {e}")

    print("❌ กรุณากด 'Resume' บนหน้าจอ Teach Pendant แล้วกด Enter ในโปรแกรมนี้")
    return False


def move_to_safe_home(rtde_c, rtde_r):
    """
    เคลื่อนที่ไปท่า Safe Home ด้วย moveJ (Joint Space)
    ใช้ moveJ เพราะปลอดภัยกว่า moveL โดยเฉพาะเมื่ออยู่ใกล้ Singularity
    """
    print("🏠 กำลังกลับท่า Safe Home (moveJ)...")
    print(f"   เป้าหมาย (deg): [{', '.join([f'{math.degrees(q):.0f}' for q in HOME_Q])}]")

    try:
        rtde_c.moveJ(HOME_Q, J_SPEED, J_ACCEL)
        time.sleep(0.3)

        actual_q = rtde_r.getActualQ()
        actual_pose = rtde_r.getActualTCPPose()
        print(f"✅ ถึงท่า Safe Home แล้ว!")
        print(f"   📍 TCP: X={actual_pose[0]:.3f}, Y={actual_pose[1]:.3f}, Z={actual_pose[2]:.3f}")
        print(f"   🔧 Q (deg): [{', '.join([f'{math.degrees(q):.1f}' for q in actual_q])}]")
        return True
    except Exception as e:
        print(f"❌ ไม่สามารถกลับ Home ได้: {e}")
        return False


# ==================== โปรแกรมหลัก ====================
if __name__ == "__main__":
    print(f"กำลังเชื่อมต่อไปที่หุ่นยนต์ UR3e IP: {ROBOT_IP} ...")

    try:
        rtde_c = rtde_control.RTDEControlInterface(ROBOT_IP)
        rtde_r = rtde_receive.RTDEReceiveInterface(ROBOT_IP)
        gripper = RobotiqGripper(ROBOT_IP, GRIPPER_PORT)

        # เริ่มต้น VR Bridge Server (TCP Port 5555 สำหรับเชื่อม Unity Meta Quest Hand Tracking)
        try:
            from vr_bridge_server import VRBridgeServer
            vr_server = VRBridgeServer(host="0.0.0.0", port=5555, on_grip_callback=lambda p, f=None: gripper.set_position(p, f))
            vr_server.start()
        except Exception as e:
            print(f"⚠️ ไม่สามารถเริ่ม VR Bridge Server: {e}")
            vr_server = None

        print("\n✅ เชื่อมต่อระบบสำเร็จ!")
        print("==================================================")
        print("🚀 โหมดควบคุมแบบ Smooth (Continuous Jogging):")
        print(f"   • แขนกล: กดค้างเพื่อไหลต่อเนื่อง ({SMOOTH_SPEED} m/s) ปล่อยปุ่ม = ชะลอหยุดนุ่มนวล")
        print(f"   • Gripper: กดค้างเพื่อหนีบ/คลายต่อเนื่อง ปล่อยปุ่ม = หยุดทันที")
        print(f"   • Cartesian Bounds: r_xy=[{MIN_BASE_RADIUS}m, {MAX_REACH_XY}m], Z=[{MIN_Z}m, {MAX_Z}m]")
        print("   • Joint Limits: ข้อศอกห้ามเหยียดตรง, ข้อมือห้ามผ่าน 0°")
        print("   • Lookahead Safety: ตรวจสอบความปลอดภัยล่วงหน้า 0.15s ป้องกันชนโต๊ะและหลุดระยะ")
        print("==================================================")
        print("🎮 การควบคุม (กดค้าง = ไหลต่อเนื่อง, ปล่อย = ชะลอหยุดนุ่มนวล):")
        print("   [W] / [S]        : เดินหน้า / ถอยหลัง (แกน X)")
        print("   [A] / [D]        : ซ้าย / ขวา (แกน Y)")
        print("   [Space] / [Ctrl] : ขึ้น / ลง (แกน Z)")
        print("   [=]              : Gripper หนีบเข้า (กดค้าง / ปล่อยหยุด)")
        print("   [-]              : Gripper คลายออก (กดค้าง / ปล่อยหยุด)")
        print("   [H]              : กลับท่า Safe Home (moveJ)")
        print("   [Q]              : ออกจากโปรแกรม")
        print("==================================================")

        # แสดงสถานะเริ่มต้น
        init_q = rtde_r.getActualQ()
        init_pose = rtde_r.getActualTCPPose()
        print(f"\n📍 พิกัดเริ่มต้น: X={init_pose[0]:.3f}, Y={init_pose[1]:.3f}, Z={init_pose[2]:.3f}")
        print(f"🔧 Q (deg): [{', '.join([f'{math.degrees(q):.1f}' for q in init_q])}]")

        # ตรวจสอบท่าเริ่มต้น
        is_safe, violations, warnings = check_joint_safety(init_q)
        if not is_safe:
            print("\n⚠️ ท่าเริ่มต้นอยู่นอกขอบเขตปลอดภัย!")
            for v in violations:
                print(f"   🚫 {v}")
            print("   → กำลังกลับท่า Safe Home อัตโนมัติ...")
            move_to_safe_home(rtde_c, rtde_r)
        elif warnings:
            print("\n⚠️ ข้อควรระวัง:")
            for w in warnings:
                print(f"   ⚡ {w}")

        print("\n✅ พร้อมรับคำสั่ง! คลิกที่หน้าต่าง CMD แล้วกดปุ่มควบคุมได้เลย\n")

        # ตัวแปรสถานะการเคลื่อนที่แบบ Smooth
        was_moving_arm = False
        gripper_moving_close = False
        gripper_moving_open = False
        prev_h_pressed = False

        # ตัวแปรจับเวลาสำหรับส่งสถานะให้ VR (20 Hz) และ print force ใน CMD (5 Hz)
        last_status_update = 0.0
        STATUS_UPDATE_INTERVAL = 0.05
        last_force_print = 0.0
        FORCE_PRINT_INTERVAL = 0.20

        while True:
            # 1. ตรวจสอบ Protective Stop ก่อน
            if check_protective_stop(rtde_r):
                print("\n🛑 ตรวจพบ PROTECTIVE STOP!")
                if was_moving_arm:
                    try:
                        rtde_c.speedStop(STOP_ACCEL)
                    except Exception:
                        pass
                    was_moving_arm = False
                recover_from_protective_stop(rtde_c, rtde_r)
                print("   กด [H] เพื่อกลับท่า Safe Home\n")
                time.sleep(0.5)
                continue

            # 2. อ่านตำแหน่งและองศาปัจจุบัน
            current_pose = rtde_r.getActualTCPPose()
            current_q = rtde_r.getActualQ()

            # 3. ตรวจสอบปุ่มออกจากโปรแกรม
            if keyboard.is_pressed('q'):
                print("\nกำลังออกจากโปรแกรม...")
                if was_moving_arm:
                    rtde_c.speedStop(STOP_ACCEL)
                break

            # 4. ตรวจสอบปุ่มกลับ Home (Edge Trigger)
            h_pressed = keyboard.is_pressed('h')
            if h_pressed and not prev_h_pressed:
                if was_moving_arm:
                    rtde_c.speedStop(STOP_ACCEL)
                    was_moving_arm = False
                move_to_safe_home(rtde_c, rtde_r)
                time.sleep(0.1)
            prev_h_pressed = h_pressed

            # 5. ควบคุม Gripper (กดค้าง = เคลื่อนที่ต่อเนื่อง, ปล่อย = หยุดทันที)
            eq_pressed = keyboard.is_pressed('=') or keyboard.is_pressed('+')
            minus_pressed = keyboard.is_pressed('-') or keyboard.is_pressed('_')

            if eq_pressed:
                gripper.start_close()
                gripper_moving_close = True
                gripper_moving_open = False
            elif minus_pressed:
                if not gripper_moving_open:
                    gripper.start_open()
                    gripper_moving_open = True
                    gripper_moving_close = False
            else:
                if gripper_moving_close or gripper_moving_open:
                    gripper.stop()
                    gripper_moving_close = False
                    gripper_moving_open = False
                    pos, _, cou, f_n, desc = gripper.get_status()
                    gripper._print_gauge(pos, f_n, cou, desc)

            # 6. คำนวณความเร็วเชิงเส้น (Linear Velocity) จากการกดปุ่มแบบ Smooth ไหลลื่น
            vx = 0.0
            vy = 0.0
            vz = 0.0

            if keyboard.is_pressed('w'):
                vx += SMOOTH_SPEED
            if keyboard.is_pressed('s'):
                vx -= SMOOTH_SPEED

            if keyboard.is_pressed('a'):
                vy += SMOOTH_SPEED
            if keyboard.is_pressed('d'):
                vy -= SMOOTH_SPEED

            if keyboard.is_pressed('space'):
                vz += SMOOTH_SPEED
            if keyboard.is_pressed('ctrl'):
                vz -= SMOOTH_SPEED

            # ------------------------------------------------------------
            # ตรวจสอบขอบเขตความปลอดภัยล่วงหน้า 0.15 วินาที (Lookahead Safety Clamping)
            # ------------------------------------------------------------
            LOOKAHEAD = 0.15
            pred_x = current_pose[0] + vx * LOOKAHEAD
            pred_y = current_pose[1] + vy * LOOKAHEAD
            pred_z = current_pose[2] + vz * LOOKAHEAD

            # ป้องกันแกน Z ต่ำเกิน (ชนโต๊ะ) หรือสูงเกิน
            if pred_z < MIN_Z and vz < 0:
                vz = 0.0
            elif pred_z > MAX_Z and vz > 0:
                vz = 0.0

            # ป้องกันระยะเอื้อมแนวนอน (ห้ามเอื้อมเกิน reach และห้ามชนฐาน)
            pred_r = math.hypot(pred_x, pred_y)
            cur_r = math.hypot(current_pose[0], current_pose[1])
            if pred_r > MAX_REACH_XY and pred_r > cur_r:
                vx = 0.0
                vy = 0.0
            elif pred_r < MIN_BASE_RADIUS and pred_r < cur_r:
                vx = 0.0
                vy = 0.0

            # ป้องกันขีดจำกัดมุมข้อต่อ (Joint Limit Protection)
            joint_safe, _, _ = check_joint_safety(current_q)
            if not joint_safe:
                vx = 0.0
                vy = 0.0
                vz = 0.0

            # ------------------------------------------------------------
            # สั่งเคลื่อนที่แขนกลแบบ speedL ต่อเนื่อง (Smooth Real-time Velocity Streaming)
            # ไม่มีการหยุดทีละเซนติเมตร ไหลลื่นต่อเนื่องเหมือนจอยสติ๊ก
            # ------------------------------------------------------------
            is_moving_now = (vx != 0.0 or vy != 0.0 or vz != 0.0)

            if is_moving_now:
                rtde_c.speedL([vx, vy, vz, 0.0, 0.0, 0.0], SMOOTH_ACCEL, 0.0)
                was_moving_arm = True
            elif was_moving_arm:
                rtde_c.speedStop(STOP_ACCEL)
                was_moving_arm = False

            # 7. อ่านสถานะและส่ง Force Feedback กลับไปให้ Unity VR Headset (20 Hz)
            current_time = time.time()
            if current_time - last_status_update >= STATUS_UPDATE_INTERVAL:
                pos, obj_str, cou, grip_force, grip_desc = gripper.get_status()
                last_status_update = current_time

                # ส่ง Force Feedback กลับไปให้ Unity VR Headset ทันที (20 Hz, latency < 50ms)
                if 'vr_server' in locals() and vr_server:
                    vr_server.send_feedback(pos, grip_force)

                # พิมพ์ค่าแรงใน CMD ทุก 0.2s (5 Hz) เพื่อให้อ่านง่ายไม่กระตุก
                if current_time - last_force_print >= FORCE_PRINT_INTERVAL:
                    force = rtde_r.getActualTCPForce()
                    f_norm = math.sqrt(force[0]**2 + force[1]**2 + force[2]**2)
                    print(f"⚡ [force] TCP={[round(x, 2) for x in force[:3]]} (|F|={f_norm:.2f} N) | Gripper={grip_force:5.1f} N ({grip_desc}) | Pos=[{current_pose[0]:.3f}, {current_pose[1]:.3f}, {current_pose[2]:.3f}]")
                    last_force_print = current_time

            # รอบลูป 50 Hz สำหรับการตอบสนองที่ลื่นไหลระดับ 20 ms
            time.sleep(0.02)

    except Exception as e:
        print(f"\n❌ เกิดข้อผิดพลาด: {e}")

    finally:
        if 'vr_server' in locals() and vr_server:
            vr_server.stop()
        if 'gripper' in locals():
            gripper.disconnect()
        if 'rtde_c' in locals():
            try:
                rtde_c.speedStop(STOP_ACCEL)
            except Exception:
                pass
            rtde_c.stopScript()
            print("ปิดการเชื่อมต่อแล้ว")