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

# ==================== UR3e Specifications ====================
# DH Parameters (มิลลิเมตร): d1=151.9, a2=243.6, a3=213.2, d4=131.0, d5=85.0, d6=92.1
# Max reach: 500 mm (0.5 m)
# Max joint speed: 180°/s (joints 1-3), 360°/s (joints 4-6)
# Max TCP speed: 1 m/s

# ==================== ความเร็วแบบ Smooth (Real-time Jogging) ====================
SMOOTH_SPEED = 0.15     # m/s   ความเร็วเชิงเส้นต่อเนื่อง (Smooth & แม่นยำ)
SMOOTH_ACCEL = 0.5      # m/s²  อัตราเร่งให้นุ่มนวล ไม่กระตุก
STOP_ACCEL = 1.5        # m/s²  อัตราการชะลอหยุดเมื่อปล่อยปุ่ม
J_SPEED = 1.57          # rad/s (50% ของ max joint speed ~3.14 rad/s สำหรับ moveJ)
J_ACCEL = 1.57          # rad/s²

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
    ควบคุม Robotiq Gripper (เช่น 2F-85 / 2F-140 / Hand-E) ผ่านพอร์ต 63352 ของ UR
    รองรับการควบคุมแบบ Smooth: กดค้างเพื่อเคลื่อนที่ต่อเนื่อง ปล่อยปุ่มเพื่อหยุดทันที
    """
    def __init__(self, ip=ROBOT_IP, port=GRIPPER_PORT):
        self.ip = ip
        self.port = port
        self.sock = None
        self.connected = False
        self.contact_detected = False
        self.is_moving = False
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

            actual_pos, _, _, _, _ = self.get_status()
            print(f"✅ เชื่อมต่อ Robotiq Gripper สำเร็จ (Port {self.port}, ตำแหน่งเริ่มต้น: {actual_pos})")
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

        # การคำนวณแรงบีบ (Grip Force ในหน่วย N):
        # - เมื่อ OBJ 2: นิ้วชนวัตถุขณะหนีบ -> มีแรงบีบจริงกระทำต่อวัตถุ (เช่น ขวดน้ำ/ขวดแก้ว)
        # - เมื่อ OBJ 1: นิ้วชนวัตถุขณะคลายออก
        # - เมื่อ OBJ 3: นิ้ววิ่งในอากาศถึงเป้าหมาย -> ไม่มีแรงต้าน = 0.0 N
        if "OBJ 2" in obj_str:
            force_n = 20.0 + (GRIPPER_FORCE / 255.0) * (235.0 - 20.0)
            status_desc = "🗜️ หนีบโดนวัตถุแน่น (เช่น ขวดน้ำ/แก้วน้ำ)"
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

    def start_close(self):
        """ สั่ง Gripper เริ่มหนีบเข้าอย่างต่อเนื่องและนุ่มนวล """
        if not self.connected:
            return
        actual_pos, obj_str, _, _, _ = self.get_status()
        if self.contact_detected and "OBJ 2" in obj_str:
            return  # วัตถุต้านแน่นอยู่แล้ว ไม่ฝืนบีบต่อ
        if actual_pos >= 225:
            return  # ปิดสุดแล้ว

        self._send(f"SET SPE {GRIPPER_SPEED}")
        self._send(f"SET FOR {GRIPPER_FORCE}")
        self._send("SET POS 255")
        self.is_moving = True

    def start_open(self):
        """ สั่ง Gripper เริ่มคลายออกอย่างต่อเนื่องและนุ่มนวล """
        if not self.connected:
            return
        actual_pos, _, _, _, _ = self.get_status()
        if actual_pos <= 5:
            return  # เปิดสุดแล้ว

        self.contact_detected = False
        self._send(f"SET SPE {GRIPPER_SPEED}")
        self._send("SET POS 0")
        self.is_moving = True

    def stop(self):
        """ สั่งหยุด Gripper ให้อยู่กับที่ทันทีเมื่อปล่อยปุ่ม """
        if not self.connected:
            return
        pos_res = self._send("GET POS")
        try:
            actual_pos = int(pos_res.split()[1])
            self._send(f"SET POS {actual_pos}")
        except Exception:
            pass
        self.is_moving = False

    def _print_gauge(self, pos, force_n, cou_val, status_desc):
        """ แสดงผลตำแหน่ง หลอดแสดงสถานะ และแรงบีบที่วัดได้ """
        pct = min(100, max(0, int(((pos - 3) / (228.0 - 3.0)) * 100)))
        bar_len = 16
        filled = int((pct / 100.0) * bar_len)
        bar = "█" * filled + "░" * (bar_len - filled)
        current_ma = cou_val * 10

        if force_n > 0:
            print(f"   [{bar}] {pct:3d}% | 🗜️ แรงบีบ: {force_n:5.1f} N ({current_ma} mA) | {status_desc}")
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

        print("\n✅ เชื่อมต่อระบบสำเร็จ!")
        print("==================================================")
        print("🚀 โหมดควบคุมแบบ Smooth (Real-time Continuous Jogging):")
        print(f"   • ความเร็วเคลื่อนที่แขน: {SMOOTH_SPEED} m/s (speedL ต่อเนื่อง)")
        print(f"   • อัตราเร่ง/เบรก: เร่ง {SMOOTH_ACCEL} m/s², ชะลอหยุด {STOP_ACCEL} m/s² (ไร้การกระตุก)")
        print(f"   • Cartesian Bounds: r_xy=[{MIN_BASE_RADIUS}m, {MAX_REACH_XY}m], Z=[{MIN_Z}m, {MAX_Z}m]")
        print(f"   • Joint Protection: ระบบหยุดอัตโนมัติเมื่อใกล้ขีดจำกัดข้อต่อ")
        print(f"   • Gripper: กดค้างเพื่อหนีบ/คลายต่อเนื่อง ปล่อยปุ่มเพื่อหยุดทันที")
        print("==================================================")
        print("🎮 การควบคุม (กดค้าง = ไหลต่อเนื่อง, ปล่อย = ชะลอหยุดนุ่มนวล):")
        print("   [W] / [S]        : เดินหน้า / ถอยหลัง (แกน X)")
        print("   [A] / [D]        : ซ้าย / ขวา (แกน Y)")
        print("   [Space] / [Ctrl] : ขึ้น / ลง (แกน Z)")
        print("   [=]              : กดค้างเพื่อหนีบต่อเนื่อง (ปล่อย = หยุดทันที)")
        print("   [-]              : กดค้างเพื่อคลายต่อเนื่อง (ปล่อย = หยุดทันที)")
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

        # ตัวแปรจับเวลาสำหรับ print force ตลอดเวลา (5 Hz)
        last_force_print = 0.0
        FORCE_PRINT_INTERVAL = 0.2

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

            # 5. คำนวณความเร็วเชิงเส้น (Linear Velocity) จากการกดปุ่ม
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

            # 6. ระบบป้องกันขอบเขตแบบ Real-time (Cartesian Boundary Clamping)
            # ตรวจสอบทิศทางไปข้างหน้าล่วงหน้า 0.15 วินาที
            LOOKAHEAD = 0.15
            pred_x = current_pose[0] + vx * LOOKAHEAD
            pred_y = current_pose[1] + vy * LOOKAHEAD
            pred_z = current_pose[2] + vz * LOOKAHEAD

            # ป้องกันแกน Z (ห้ามชนโต๊ะ หรือสูงเกินไป)
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
            joint_safe, joint_violations, _ = check_joint_safety(current_q)
            if not joint_safe:
                vx = 0.0
                vy = 0.0
                vz = 0.0

            # 7. สั่งเคลื่อนที่แขนกลแบบ speedL ต่อเนื่อง (Smooth Velocity Control)
            is_moving_now = (vx != 0.0 or vy != 0.0 or vz != 0.0)

            if is_moving_now:
                # ส่งความเร็ว xd ไปยัง controller อย่างต่อเนื่อง ไม่มีการเบรกหยุดระหว่างทาง
                rtde_c.speedL([vx, vy, vz, 0.0, 0.0, 0.0], SMOOTH_ACCEL, 0.0)
                was_moving_arm = True
            elif was_moving_arm:
                # เมื่อปล่อยปุ่มทั้งหมด สั่งให้ชะลอหยุดอย่างนุ่มนวล
                rtde_c.speedStop(STOP_ACCEL)
                was_moving_arm = False

            # 8. ควบคุม Gripper แบบ Smooth (กดค้าง = เคลื่อนที่ต่อเนื่อง, ปล่อย = หยุดทันที)
            eq_pressed = keyboard.is_pressed('=') or keyboard.is_pressed('+')
            minus_pressed = keyboard.is_pressed('-') or keyboard.is_pressed('_')

            if eq_pressed:
                if not gripper_moving_close:
                    gripper.start_close()
                    gripper_moving_close = True
                    gripper_moving_open = False
            elif minus_pressed:
                if not gripper_moving_open:
                    gripper.start_open()
                    gripper_moving_open = True
                    gripper_moving_close = False
            else:
                # ปล่อยปุ่มทั้งสอง -> สั่งหยุดกริปเปอร์อยู่กับที่
                if gripper_moving_close or gripper_moving_open:
                    gripper.stop()
                    gripper_moving_close = False
                    gripper_moving_open = False
                    # แสดงผลตำแหน่งและแรงบีบที่จุดหยุด
                    pos, _, cou, f_n, desc = gripper.get_status()
                    gripper._print_gauge(pos, f_n, cou, desc)

            # 9. พิมพ์ค่า Force ตลอดเวลา (Continuous Force Monitoring @ 5 Hz)
            current_time = time.time()
            if current_time - last_force_print >= FORCE_PRINT_INTERVAL:
                force = rtde_r.getActualTCPForce()
                f_norm = math.sqrt(force[0]**2 + force[1]**2 + force[2]**2)
                pos, _, cou, grip_force, grip_desc = gripper.get_status()

                # พิมพ์ค่าแรงทั้ง TCP Force และ Gripper พร้อมพิกัดปัจจุบัน
                print(f"⚡ [force] TCP={[round(x, 2) for x in force[:3]]} (|F|={f_norm:.2f} N) | Gripper={grip_force:5.1f} N ({grip_desc}) | Pos=[{current_pose[0]:.3f}, {current_pose[1]:.3f}, {current_pose[2]:.3f}]")
                last_force_print = current_time

            # รอบลูป 50 Hz สำหรับการตอบสนองที่ลื่นไหลระดับ 20 ms
            time.sleep(0.02)

    except Exception as e:
        print(f"\n❌ เกิดข้อผิดพลาด: {e}")

    finally:
        if 'gripper' in locals():
            gripper.disconnect()
        if 'rtde_c' in locals():
            try:
                rtde_c.speedStop(STOP_ACCEL)
            except Exception:
                pass
            rtde_c.stopScript()
            print("ปิดการเชื่อมต่อแล้ว")