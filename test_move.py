import math
import socket
import sys
import time
import keyboard
import rtde_control
import rtde_receive

# ตั้งค่า stdout เป็น utf-8 เพื่อรองรับภาษาไทยและ emoji บน Windows Console
sys.stdout.reconfigure(encoding="utf-8")

ROBOT_IP = "192.168.1.20"

# กำหนดระยะทางที่จะขยับต่อการกด 1 ครั้ง (0.01 เมตร = 1 เซนติเมตร)
STEP = 0.01

# ==================== UR3e Specifications ====================
# DH Parameters (มิลลิเมตร): d1=151.9, a2=243.6, a3=213.2, d4=131.0, d5=85.0, d6=92.1
# Max reach: 500 mm (0.5 m)
# Max joint speed: 180°/s (joints 1-3), 360°/s (joints 4-6)
# Max TCP speed: 1 m/s

# ==================== ความเร็ว (50% ของ max) ====================
SPEED = 0.5       # m/s  (50% ของ max TCP speed 1 m/s)
ACCEL = 0.5       # m/s² (50% ของ max)
J_SPEED = 1.57    # rad/s (50% ของ max joint speed ~3.14 rad/s)
J_ACCEL = 1.57    # rad/s²

# ==================== การตั้งค่า Gripper (Robotiq) ====================
GRIPPER_PORT = 63352
GRIPPER_SPEED = 128     # 50% ของความเร็วสูงสุด (ช่วง 0-255)
GRIPPER_FORCE = 128     # 50% ของแรงหนีบสูงสุด (ช่วง 0-255) ป้องกันการบีบวัตถุแตก
GRIPPER_STEP = 15       # ระยะปรับทีละนิดต่อการกด 1 ครั้ง (ช่วง 0-255 หรือ ~6.5% ต่อคลิก)

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
    ควบคุม Robotiq Gripper (เช่น 2F-85 / 2F-140 / Hand-E) ผ่านพอร์ต 63352 ของ UR
    รองรับการปรับทีละสเต็ป ตรวจสอบแรงต้าน และคำนวณแรงบีบแบบ Real-time
    """
    def __init__(self, ip=ROBOT_IP, port=GRIPPER_PORT):
        self.ip = ip
        self.port = port
        self.sock = None
        self.connected = False
        self.current_target = 0
        self.contact_detected = False
        self.connect()

    def connect(self):
        try:
            self.sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            self.sock.settimeout(2.0)
            self.sock.connect((self.ip, self.port))
            # เปิดการทำงาน Gripper (Activate)
            self._send("SET ACT 1")
            self._send("SET GTO 1")
            self._send(f"SET SPE {GRIPPER_SPEED}")
            self._send(f"SET FOR {GRIPPER_FORCE}")
            self.connected = True

            # อ่านตำแหน่งเริ่มต้นจริงจากอุปกรณ์
            actual_pos, _, _, _, _ = self.get_status()
            if actual_pos is not None:
                self.current_target = actual_pos
            print(f"✅ เชื่อมต่อ Robotiq Gripper สำเร็จ (Port {self.port}, ตำแหน่งเริ่มต้น: {self.current_target})")
        except Exception as e:
            print(f"⚠️ ไม่สามารถเชื่อมต่อ Gripper: {e}")
            self.sock = None
            self.connected = False

    def _send(self, cmd):
        if not self.connected or not self.sock:
            return ""
        try:
            self.sock.sendall((cmd + "\n").encode())
            res = self.sock.recv(1024).decode().strip()
            return res
        except Exception:
            return ""

    def get_status(self):
        """
        อ่านค่า POS, OBJ, COU และคำนวณแรงบีบ (Grip Force)
        คืนค่า: (actual_pos, obj_str, cou_val, force_n, status_desc)
        """
        if not self.connected:
            return None, None, None, 0.0, "ไม่ได้เชื่อมต่อ"

        pos_res = self._send("GET POS")
        obj_res = self._send("GET OBJ")
        cou_res = self._send("GET COU")

        actual_pos = self.current_target
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

        # การคำนวณแรงบีบ (Grip Force ในหน่วย N):
        # Robotiq 2F-85 ช่วงแรง 20N - 235N ตามค่า FOR (0-255)
        # - เมื่อ OBJ 2: นิ้วชนวัตถุขณะหนีบ -> มีแรงบีบจริงกระทำต่อวัตถุ (เช่น ขวดน้ำ/ขวดแก้ว)
        # - เมื่อ OBJ 1: นิ้วชนวัตถุขณะคลายออก
        # - เมื่อ OBJ 3: นิ้ววิ่งในอากาศถึงเป้าหมาย -> ไม่มีแรงต้าน = 0.0 N
        if "OBJ 2" in obj_str:
            force_n = 20.0 + (GRIPPER_FORCE / 255.0) * (235.0 - 20.0)
            status_desc = "🗜️ ตรวจพบแรงต้าน! (หนีบโดนวัตถุแน่น เช่น ขวดน้ำ/แก้วน้ำ)"
            self.contact_detected = True
        elif "OBJ 1" in obj_str:
            force_n = 20.0 + (GRIPPER_FORCE / 255.0) * (235.0 - 20.0)
            status_desc = "⚠️ ตรวจพบแรงต้านขณะคลายออก"
            self.contact_detected = True
        else:
            force_n = 0.0
            status_desc = "ไม่มีแรงต้าน (เคลื่อนที่ในอากาศ)"
            self.contact_detected = False

        return actual_pos, obj_str, cou_val, force_n, status_desc

    def _wait_step(self, target, max_wait=0.35):
        """ รอให้การเคลื่อนที่สเต็ปนี้เริ่มและหยุดลง """
        t0 = time.time()
        time.sleep(0.04)  # ให้เวลา controller เริ่มเคลื่อนที่
        while time.time() - t0 < max_wait:
            obj_res = self._send("GET OBJ")
            pos_res = self._send("GET POS")
            try:
                p = int(pos_res.split()[1])
            except Exception:
                p = target

            # ถ้าตรวจพบแรงต้านวัตถุ (OBJ 1 หรือ OBJ 2) ให้หยุดรอทันที
            if "OBJ 1" in obj_res or "OBJ 2" in obj_res:
                break
            # ถ้าถึงตำแหน่งเป้าหมายแล้ว
            if "OBJ 3" in obj_res and abs(p - target) <= 3:
                break
            time.sleep(0.02)

    def _print_gauge(self, pos, force_n, cou_val, status_desc):
        """ แสดงผลตำแหน่ง หลอดแสดงสถานะ และแรงบีบที่วัดได้ """
        # ปรับสเกล 3 (เปิดสุด) ถึง 228 (ปิดสนิท) เป็น 0% - 100%
        pct = min(100, max(0, int(((pos - 3) / (228.0 - 3.0)) * 100)))
        bar_len = 16
        filled = int((pct / 100.0) * bar_len)
        bar = "█" * filled + "░" * (bar_len - filled)
        current_ma = cou_val * 10

        if force_n > 0:
            print(f"   [{bar}] {pct:3d}% | 🗜️ แรงบีบ: {force_n:5.1f} N ({current_ma} mA) | {status_desc}")
        else:
            print(f"   [{bar}] {pct:3d}% | 👐 แรงบีบ:   0.0 N (ไม่มีแรงต้าน) | {status_desc}")

    def step_close(self):
        """
        ค่อยๆ หนีบเข้าทีละนิด (Step close)
        มีระบบป้องกัน: หากหนีบสุดหรือหนีบวัตถุแน่นแล้วจะไม่ฝืนบีบต่อ
        """
        if not self.connected:
            print("⚠️ Gripper ไม่ได้เชื่อมต่อ")
            return

        actual_pos, obj_str, cou_val, force_n, status_desc = self.get_status()

        # 1. ป้องกันกรณีหนีบวัตถุแน่นเต็มแรงแล้ว (เช่น ขวดน้ำ/ขวดแก้ว) ไม่ให้ฝืนบีบต่อ
        if self.contact_detected and "OBJ 2" in obj_str:
            print(f"🛑 [กันสุด/วัตถุต้าน] หนีบวัตถุแน่นเต็มแรงแล้ว! (ไม่สามารถบีบเข้าได้มากกว่านี้ เพื่อความปลอดภัยของวัตถุ)")
            self._print_gauge(actual_pos, force_n, cou_val, "หนีบขวดน้ำ/ขวดแก้วแน่นแล้ว")
            return

        # 2. ป้องกันกรณีปิดสุดแล้ว (Physical limit)
        if actual_pos >= 225 or self.current_target >= 255:
            print(f"🛑 [กันสุด] Gripper หนีบเข้าสุดแล้ว! (ตำแหน่งปิดสนิท ไม่พบวัตถุ)")
            self._print_gauge(actual_pos, 0.0, cou_val, "ปิดสุดแล้ว (ไม่พบวัตถุ)")
            return

        # ขยับเป้าหมายทีละ STEP
        new_target = min(255, self.current_target + GRIPPER_STEP)
        self.current_target = new_target

        # สั่งขยับ
        self._send(f"SET SPE {GRIPPER_SPEED}")
        self._send(f"SET FOR {GRIPPER_FORCE}")
        self._send(f"SET POS {new_target}")

        # รอให้เสร็จสิ้นสเต็ปสั้นๆ
        self._wait_step(new_target)

        # อ่านค่าและรายงานแรงบีบ
        actual_pos, obj_str, cou_val, force_n, status_desc = self.get_status()
        self.current_target = actual_pos  # ซิงค์กับตำแหน่งจริง
        self._print_gauge(actual_pos, force_n, cou_val, status_desc)

    def step_open(self):
        """
        ค่อยๆ คลายออกทีละนิด (Step open)
        มีระบบป้องกัน: หากเปิดกว้างสุดแล้วจะไม่ขยับต่อ
        """
        if not self.connected:
            print("⚠️ Gripper ไม่ได้เชื่อมต่อ")
            return

        actual_pos, obj_str, cou_val, force_n, status_desc = self.get_status()

        # ป้องกันกรณีเปิดกว้างสุดแล้ว
        if actual_pos <= 5 or self.current_target <= 0:
            print(f"🛑 [กันสุด] Gripper คลายออกสุดแล้ว! (ตำแหน่งเปิดกว้างสุด)")
            self._print_gauge(actual_pos, 0.0, cou_val, "เปิดสุดแล้ว")
            return

        # คลายเป้าหมายทีละ STEP
        new_target = max(0, self.current_target - GRIPPER_STEP)
        self.current_target = new_target
        self.contact_detected = False

        # สั่งขยับ
        self._send(f"SET SPE {GRIPPER_SPEED}")
        self._send(f"SET POS {new_target}")

        # รอให้เสร็จสิ้นสเต็ปสั้นๆ
        self._wait_step(new_target)

        # อ่านค่าและรายงานแรงบีบ
        actual_pos, obj_str, cou_val, force_n, status_desc = self.get_status()
        self.current_target = actual_pos
        self._print_gauge(actual_pos, force_n, cou_val, status_desc)

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


def check_joint_jump(q_before, q_after):
    """
    ตรวจจับการหมุนตลบกะทันหัน (Joint Jump / Configuration Flip)
    ถ้ามุมข้อต่อใดเปลี่ยนไปเกิน MAX_JOINT_JUMP ในสเต็ปเดียว = อันตราย
    คืนค่า (is_safe, culprit_msg)
    """
    for i in range(6):
        delta = abs(q_after[i] - q_before[i])
        delta = min(delta, 2 * math.pi - delta)  # กรณีข้ามรอบ

        if delta > MAX_JOINT_JUMP:
            deg = math.degrees(delta)
            return False, f"{JOINT_NAMES[i]} หมุนตลบ {deg:.1f}° (เกินเกณฑ์ {math.degrees(MAX_JOINT_JUMP):.0f}°)"

    return True, "OK"


def check_protective_stop(rtde_r):
    """
    ตรวจสอบว่าหุ่นยนต์เข้าสู่สถานะ Protective Stop หรือไม่
    """
    return rtde_r.isProtectiveStopped()


def recover_from_protective_stop(rtde_c, rtde_r):
    """
    กู้คืนจาก Protective Stop อัตโนมัติ
    """
    print("🔧 กำลังกู้คืนจาก Protective Stop...")
    time.sleep(1.0)

    # ใน ur_rtde ไม่มี unlockProtectiveStop โดยตรง
    # ต้อง reconnect หรือใช้ dashboard command
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
    print("🏠 กำลังกลับท่า Safe Home...")
    print(f"   เป้าหมาย (deg): [{', '.join([f'{math.degrees(q):.0f}' for q in HOME_Q])}]")

    try:
        # moveJ ปลอดภัยจาก Singularity เพราะเคลื่อนที่ใน Joint Space
        rtde_c.moveJ(HOME_Q, J_SPEED, J_ACCEL)
        time.sleep(0.3)

        # ยืนยันว่าถึงท่า Home
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

        print("\n✅ เชื่อมต่อระบบสำเร็จ!")
        print("==================================================")
        print("🛡️ ระบบป้องกัน Self-Collision สำหรับ UR3e:")
        print(f"   • Cartesian Bounds: r_xy=[{MIN_BASE_RADIUS}m, {MAX_REACH_XY}m], Z=[{MIN_Z}m, {MAX_Z}m]")
        print(f"   • Joint Limits: ข้อศอกห้ามเหยียดตรง, ข้อมือห้ามผ่าน 0°")
        print(f"   • Joint Jump Detection: บล็อกการหมุนตลบ > {math.degrees(MAX_JOINT_JUMP):.0f}°/step")
        print(f"   • Protective Stop Recovery: ตรวจจับอัตโนมัติ")
        print(f"   • ความเร็วแขน: 50% (TCP={SPEED} m/s, Joint={math.degrees(J_SPEED):.0f}°/s)")
        print(f"   • การปรับ Gripper: ค่อยๆ ปรับทีละ {GRIPPER_STEP} หน่วย (~6.5%) พร้อมแสดงแรงบีบ")
        print("==================================================")
        print("🎮 การควบคุม:")
        print("   [W] / [S]        : เดินหน้า / ถอยหลัง (แกน X)")
        print("   [A] / [D]        : ซ้าย / ขวา (แกน Y)")
        print("   [Space] / [Ctrl] : ขึ้น / ลง (แกน Z)")
        print("   [=]              : ค่อยๆ หนีบทีละนิด (พร้อมแสดงค่าแรงบีบ)")
        print("   [-]              : ค่อยๆ คลายทีละนิด (พร้อมแสดงค่าแรงบีบ)")
        print("   [H]              : กลับท่า Safe Home")
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

        # เก็บค่า Q ก่อนหน้า สำหรับตรวจจับ Joint Jump
        prev_q = rtde_r.getActualQ()

        # ตัวแปรบันทึกสถานะปุ่ม (Edge Trigger)
        prev_eq_pressed = False
        prev_minus_pressed = False
        prev_h_pressed = False

        while True:
            # ตรวจสอบ Protective Stop ก่อน
            if check_protective_stop(rtde_r):
                print("\n🛑 ตรวจพบ PROTECTIVE STOP!")
                recover_from_protective_stop(rtde_c, rtde_r)
                print("   กด [H] เพื่อกลับท่า Safe Home\n")
                time.sleep(0.5)
                prev_q = rtde_r.getActualQ()
                continue

            # อ่านตำแหน่งและองศาข้อต่อปัจจุบัน
            current_pose = rtde_r.getActualTCPPose()
            current_q = rtde_r.getActualQ()
            target_pose = current_pose.copy()
            moved = False

            # === ตรวจสอบปุ่ม Gripper (Edge Trigger: กด 1 ครั้ง = ปรับ 1 สเต็ป) ===
            eq_pressed = keyboard.is_pressed('=') or keyboard.is_pressed('+')
            minus_pressed = keyboard.is_pressed('-') or keyboard.is_pressed('_')

            if eq_pressed and not prev_eq_pressed:
                gripper.step_close()
            prev_eq_pressed = eq_pressed

            if minus_pressed and not prev_minus_pressed:
                gripper.step_open()
            prev_minus_pressed = minus_pressed

            # === ตรวจสอบปุ่มพิเศษ ===
            # กลับ Home (Edge Trigger)
            h_pressed = keyboard.is_pressed('h')
            if h_pressed and not prev_h_pressed:
                move_to_safe_home(rtde_c, rtde_r)
                prev_q = rtde_r.getActualQ()
                time.sleep(0.1)
            prev_h_pressed = h_pressed

            # ออกจากโปรแกรม
            if keyboard.is_pressed('q'):
                print("\nกำลังออกจากโปรแกรม...")
                break

            # === ตรวจสอบปุ่มทิศทาง ===
            if keyboard.is_pressed('w'):
                target_pose[0] += STEP
                moved = True
            elif keyboard.is_pressed('s'):
                target_pose[0] -= STEP
                moved = True

            if keyboard.is_pressed('a'):
                target_pose[1] += STEP
                moved = True
            elif keyboard.is_pressed('d'):
                target_pose[1] -= STEP
                moved = True

            if keyboard.is_pressed('space'):
                target_pose[2] += STEP
                moved = True
            elif keyboard.is_pressed('ctrl'):
                target_pose[2] -= STEP
                moved = True

            if moved:
                # ============================================================
                # ด่านที่ 1: ตรวจสอบขอบเขต Cartesian
                # ============================================================
                cart_safe, cart_reason = check_cartesian_safety(target_pose)
                if not cart_safe:
                    print(f"🛑 [Cartesian] {cart_reason}")
                    time.sleep(0.05)
                    continue

                # ============================================================
                # ด่านที่ 2: ตรวจสอบข้อต่อปัจจุบันก่อนขยับ
                # ============================================================
                joint_safe, joint_violations, joint_warnings = check_joint_safety(current_q)
                if not joint_safe:
                    print(f"🛑 [Joint Safety] ข้อต่ออยู่นอกขอบเขตปลอดภัย!")
                    for v in joint_violations:
                        print(f"   🚫 {v}")
                    print("   กด [H] เพื่อกลับท่า Safe Home")
                    time.sleep(0.2)
                    continue

                # ============================================================
                # ด่านที่ 3: สั่งขยับ แล้วตรวจสอบผลลัพธ์ทันที
                # ============================================================
                try:
                    rtde_c.moveL(target_pose, SPEED, ACCEL)
                except Exception as e:
                    print(f"❌ moveL ล้มเหลว: {e}")
                    print("   กด [H] เพื่อกลับท่า Safe Home")
                    time.sleep(0.2)
                    continue

                # อ่านสถานะหลังขยับ
                actual_q = rtde_r.getActualQ()
                actual_pose = rtde_r.getActualTCPPose()

                # ============================================================
                # ด่านที่ 4: ตรวจจับ Joint Jump (การหมุนตลบหลังขยับ)
                # ============================================================
                jump_safe, jump_msg = check_joint_jump(prev_q, actual_q)
                if not jump_safe:
                    print(f"⚠️ [Joint Jump] ตรวจพบ: {jump_msg}")
                    print("   → หยุดเคลื่อนที่ชั่วคราว กด [H] เพื่อกลับท่าปลอดภัย")
                    # หยุดหุ่นทันที
                    rtde_c.stopL(2.0)
                    time.sleep(0.5)
                    prev_q = rtde_r.getActualQ()
                    continue

                # ============================================================
                # ด่านที่ 5: ตรวจสอบข้อต่อหลังขยับ
                # ============================================================
                post_safe, post_violations, post_warnings = check_joint_safety(actual_q)

                # แสดงพิกัด
                print(f"📍 X={actual_pose[0]:.3f}, Y={actual_pose[1]:.3f}, Z={actual_pose[2]:.3f}", end="")

                if post_warnings:
                    print(f"  ⚡{', '.join(post_warnings)}", end="")

                if not post_safe:
                    print(f"\n   🛑 ข้อต่อออกนอกขอบเขตหลังขยับ! → หยุดแล้ว กด [H] กลับ Home")
                    for v in post_violations:
                        print(f"   🚫 {v}")
                    rtde_c.stopL(2.0)
                    time.sleep(0.2)
                else:
                    print()  # ขึ้นบรรทัดใหม่

                # อัพเดต Q ก่อนหน้า
                prev_q = actual_q

            time.sleep(0.05)

    except Exception as e:
        print(f"\n❌ เกิดข้อผิดพลาด: {e}")

    finally:
        if 'gripper' in locals():
            gripper.disconnect()
        if 'rtde_c' in locals():
            rtde_c.stopScript()
            print("ปิดการเชื่อมต่อแล้ว")