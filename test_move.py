import math
import socket
import sys
import threading
import time

# Enable UTF-8 encoding on Windows console for Thai and emoji
if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')
if hasattr(sys.stderr, 'reconfigure'):
    sys.stderr.reconfigure(encoding='utf-8')

import keyboard
try:
    import rtde_control
    import rtde_receive
except ModuleNotFoundError:
    import os, subprocess, shutil
    cand_py312 = r"C:\Users\dhamm\AppData\Local\Programs\Python\Python312\python.exe"
    if os.path.exists(cand_py312) and cand_py312.lower() != sys.executable.lower():
        print(f"[Auto-Redirect] Switching to Python 3.12 ({cand_py312})...")
        sys.exit(subprocess.call([cand_py312] + sys.argv))
    elif shutil.which("py") and "3.12" not in sys.version:
        print("[Auto-Redirect] Switching to 'py -3.12'...")
        sys.exit(subprocess.call([shutil.which("py"), "-3.12"] + sys.argv))
    else:
        raise
from ukf_filter import UnscentedKalmanFilter1D
from robot_safety import (
    SHOULDER_Z, SPHERE_MAX, SPHERE_MIN, CYLINDER_RADIUS, CARTESIAN_TCP_SPEED_CAP,
    MIN_Z, MAX_Z,
    JOINT_LIMITS_DEG, JOINT_LIMITS_RAD, JOINT_NAMES,
    MAX_SAFE_JOINT_SPEED_DEG, JOINT_MAX_SPEEDS, filter_cartesian_velocity,
    filter_joint_velocity, compute_ur3e_jacobian, solve_dls_system,
    compute_tool_rotation_vectors, compute_pose_error,
    compute_safety_proximity
)

import os
ROBOT_IP = sys.argv[1] if (len(sys.argv) > 1 and not sys.argv[1].startswith("-")) else os.environ.get("ROBOT_IP", "192.168.1.20")

# ==================== UR3e Specifications ====================
# DH Parameters (mm): d1=151.9, a2=243.6, a3=213.2, d4=131.0, d5=85.0, d6=92.1
# Max reach: 500 mm (0.5 m)
# Max joint speed: 180°/s (joints 1-3), 360°/s (joints 4-6)
# Max TCP speed: 1 m/s

# ==================== Smooth Motion Speeds (speedL / speedJ) ====================
SMOOTH_SPEED = 0.07         # m/s (70 mm/s) Keyboard jogging speed
SMOOTH_ACCEL = 0.25         # m/s^2 Keyboard acceleration
STOP_ACCEL = 1.20           # m/s^2 Deceleration to cut inertia on stop
ROT_SPEED = 0.55            # rad/s (~31.5 deg/s) Joint rotation speed
J_STREAM_ACCEL = 0.50       # rad/s^2 Wrist joint acceleration
STREAM_TIME_BUFFER = 0.04   # seconds (40 ms) Lookahead time window for speedJ/speedL
J_SPEED = 0.80              # rad/s for moveJ to Safe Home
J_ACCEL = 1.00              # rad/s^2 for moveJ to Safe Home

# Speeds and accelerations for VR Hand Tracking
VR_TRACKING_MAX_SPEED = 0.20    # m/s (200 mm/s) Max speed for VR hand tracking
VR_TRACKING_MAX_ROT = 3.80      # rad/s (~218 deg/s) Max wrist angular velocity
VR_BRAKE_ACCEL = 1.50           # m/s^2 Braking deceleration on stop


# ==================== Robotiq Gripper Settings ====================
GRIPPER_PORT = 63352
GRIPPER_SPEED = 128     # 50% max speed (0-255)
GRIPPER_FORCE = 128     # 50% max grip force (0-255)

# ==================== UR3e Home Pose ====================
# [Base, Shoulder, Elbow, Wrist1, Wrist2, Wrist3] in radians
# Home joint configuration:
# 1. Base = 0 deg: forward facing along +X
# 2. Shoulder = -90 deg: upper arm upright
# 3. Elbow = -90 deg: forearm pointing forward
# 4. Wrist 1 = -180 deg: folded parallel
# 5. Wrist 2 = -90 deg: perpendicular to avoid singularity
# 6. Wrist 3 = 180 deg: gripper pointing forward level
HOME_Q = [
    math.radians(0),      # Base:     0°
    math.radians(-90),    # Shoulder: -90°
    math.radians(-90),    # Elbow:    -90°
    math.radians(-180),   # Wrist 1:  -180°
    math.radians(-90),    # Wrist 2:  -90°
    math.radians(180),    # Wrist 3:  180°
]

# ==================== Safety Boundaries ====================
# Safety model matching robot_safety.py
JOINT_LIMITS = JOINT_LIMITS_DEG
MIN_BASE_RADIUS = SPHERE_MIN
MAX_REACH_XY = SPHERE_MAX

# 3. Warning Margin near limits
JOINT_WARNING_MARGIN = math.radians(15)  # Warn when within 15 deg of limit

# 4. Joint jump threshold
MAX_JOINT_JUMP = math.radians(15)  # Max allowable joint delta per step

# ==================== Joint Names ====================
JOINT_NAMES = ["Base", "Shoulder", "Elbow", "Wrist 1", "Wrist 2", "Wrist 3"]


class RobotiqGripper:
    """
    Controls Robotiq Gripper via UR port 63352.
    Supports continuous movement and instant stop.
    """
    def __init__(self, ip=ROBOT_IP, port=GRIPPER_PORT):
        self.ip = ip
        self.port = port
        self.sock = None
        self.connected = False
        self.contact_detected = False
        self.is_moving = False
        self.lock = threading.Lock()
        self._last_target_pos = -1
        self._cached_status = (0, "OBJ ?", 0, 0.0, "Disconnected")
        self._last_status_time = 0.0
        self._status_ttl = 0.08  # 80ms cache to reduce latency
        self.connect()

    def connect(self):
        try:
            self.sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            self.sock.settimeout(2.0)
            self.sock.connect((self.ip, self.port))
            self.connected = True

            # Check if activated; activate if needed
            self.ensure_activated()

            actual_pos, _, _, _, _ = self.get_status(force=True)
            print(f"✅ Robotiq Gripper connected (Port {self.port}, Initial pos: {actual_pos})")
        except Exception as e:
            print(f"⚠️ Failed to connect to Gripper: {e}")
            self.sock = None
            self.connected = False

    def is_activated(self):
        """ Check if Gripper is activated (ACT == 1 and STA == 3) """
        if not self.connected:
            return False
        act_res = self._send("GET ACT")
        sta_res = self._send("GET STA")
        return ("ACT 1" in act_res) and ("STA 3" in sta_res)

    def ensure_activated(self, timeout=10.0):
        """
        Check Gripper activation status.
        - If activated (ACT 1 & STA 3): ready immediately.
        - If faulted (FLT != 0): auto-reset fault.
        - If not activated: send SET ACT 1 and wait for homing (STA 3).
        """
        if not self.connected or not self.sock:
            return False

        act_res = self._send("GET ACT")
        sta_res = self._send("GET STA")
        flt_res = self._send("GET FLT")

        # Check fault code
        is_fault = False
        try:
            flt_code = flt_res.split()[1]
            is_fault = (int(flt_code, 16) != 0)
        except Exception:
            is_fault = False

        if is_fault:
            print(f"⚠️ Gripper Fault (FLT={flt_code}) -> Auto-resetting fault...")
            self._send("SET ACT 0")
            time.sleep(0.3)
            self._send("SET ACT 1")
            act_res = "ACT 1"
            sta_res = "STA 1"

        # 1. If already activated
        if "ACT 1" in act_res and "STA 3" in sta_res:
            print("   ✅ Gripper already activated (Ready)")
            self._send("SET GTO 1")
            self._send(f"SET SPE {GRIPPER_SPEED}")
            self._send(f"SET FOR {GRIPPER_FORCE}")
            return True

        # 2. If not activated, start activation sequence
        print("   ⚡ [Gripper] Activating (Auto-homing)...")
        self._send("SET ACT 1")
        self._send("SET GTO 1")
        self._send(f"SET SPE {GRIPPER_SPEED}")
        self._send(f"SET FOR {GRIPPER_FORCE}")

        start_time = time.time()
        while time.time() - start_time < timeout:
            sta_res = self._send("GET STA")
            if "STA 3" in sta_res:
                print("   ✅ [Gripper] Activation complete and ready!")
                return True
            time.sleep(0.3)

        print(f"   ❌ [Gripper] Activation timed out ({timeout}s)! Check Gripper status")
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

    def get_status(self, force=False):
        """
        Read POS, OBJ, COU and calculate grip force with caching.
        Returns: (actual_pos, obj_str, cou_val, force_n, status_desc)
        """
        if not self.connected:
            return 0, "OBJ ?", 0, 0.0, "Disconnected"

        now = time.time()
        if not force and (now - self._last_status_time < self._status_ttl):
            return self._cached_status

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

        # Compute grip force (N):
        # - OBJ 2: Contact detected during closing (actual grip force)
        # - OBJ 1: Contact detected during opening
        # - OBJ 3: In motion / reached target without contact (0.0 N)
        if "OBJ 2" in obj_str:
            force_n = 20.0 + (GRIPPER_FORCE / 255.0) * (235.0 - 20.0)
            status_desc = "🗜️ Gripping object firmly"
            self.contact_detected = True
        elif "OBJ 1" in obj_str:
            force_n = 20.0 + (GRIPPER_FORCE / 255.0) * (235.0 - 20.0)
            status_desc = "⚠️ Resistance detected while opening"
            self.contact_detected = True
        else:
            force_n = 0.0
            status_desc = "No resistance (Free motion)"
            self.contact_detected = False

        self._cached_status = (actual_pos, obj_str, cou_val, force_n, status_desc)
        self._last_status_time = now
        return self._cached_status

    def start_close(self):
        """ Start closing Gripper smoothly. """
        if not self.connected:
            return

        # Check activation state
        if not self.is_activated():
            if not self.ensure_activated():
                return

        actual_pos, obj_str, _, _, _ = self.get_status()
        if self.contact_detected and "OBJ 2" in obj_str:
            return  # Object clamped; hold position
        if actual_pos >= 250:
            return  # Fully closed

        self._send("SET GTO 1")
        self._send(f"SET SPE {GRIPPER_SPEED}")
        self._send(f"SET FOR {GRIPPER_FORCE}")
        self._send("SET POS 255")
        self.is_moving = True

    def start_open(self):
        """ Start opening Gripper smoothly. """
        if not self.connected:
            return

        # Check activation state
        if not self.is_activated():
            if not self.ensure_activated():
                return

        actual_pos, _, _, _, _ = self.get_status()
        if actual_pos <= 5:
            return  # Fully open

        self.contact_detected = False
        self._send("SET GTO 1")
        # Smooth opening speed (80)
        self._send("SET SPE 80")
        self._send("SET POS 0")
        self.is_moving = True

    def stop(self, is_opening=False):
        """
        Stop Gripper immediately on button release.
        - If opening: SET GTO 0 immediately to hold position.
        - If closing and gripped (OBJ 2): maintain GTO 1 to keep clamping force.
        - Otherwise: SET GTO 0 to stop motor.
        """
        if not self.connected:
            return

        # Maintain grip force if clamped on object (OBJ 2)
        if not is_opening:
            obj_res = self._send("GET OBJ")
            if "OBJ 2" in obj_res:
                self.contact_detected = True
                self.is_moving = False
                return

        # Stop motor at current position
        self._send("SET GTO 0")
        self.contact_detected = False
        self.is_moving = False

    def set_position(self, pos):
        """
        Set specific Gripper position (0 = fully open, 255 = fully closed).
        Used for VR Pinch Tracking.
        Direct SET POS command with low latency.
        """
        if not self.connected or not self.sock:
            return
        if not self.is_activated():
            if not self.ensure_activated():
                return
        pos = max(0, min(255, int(pos)))

        # Deduplicate redundant position commands
        if abs(self._last_target_pos - pos) < 2:
            return

        is_releasing = (self._last_target_pos != -1 and pos < self._last_target_pos)
        self._last_target_pos = pos

        # Use SPE 85 when opening smoothly
        spe = 85 if is_releasing else GRIPPER_SPEED
        self._send("SET GTO 1")
        self._send(f"SET SPE {spe}")
        self._send(f"SET POS {pos}")
        self.is_moving = True

    def _print_gauge(self, pos, force_n, cou_val, status_desc):
        """ Display gripper position bar and measured force. """
        pct = min(100, max(0, int(((pos - 3) / (228.0 - 3.0)) * 100)))
        bar_len = 16
        filled = int((pct / 100.0) * bar_len)
        bar = "█" * filled + "░" * (bar_len - filled)
        current_ma = cou_val * 10

        if force_n > 0:
            print(f"   [{bar}] {pct:3d}% | 🗜️ Grip Force: {force_n:5.1f} N ({current_ma} mA) | {status_desc}")
        else:
            print(f"   [{bar}] {pct:3d}% | 👐 Grip Force:   0.0 N (Free motion) | {status_desc}")

    def disconnect(self):
        if self.sock:
            try:
                self.sock.close()
            except Exception:
                pass
            self.connected = False


def check_cartesian_safety(pose):
    """
    Check if target coordinates (X, Y, Z) are within safe workspace.
    Returns (is_safe, reason).
    """
    x, y, z = pose[0], pose[1], pose[2]
    dz = z - SHOULDER_Z
    d_3d = math.hypot(x, y, dz)

    # 1. 3D Reach from Shoulder
    if d_3d > SPHERE_MAX:
        return False, f"Beyond max reach boundary! (3D Reach={d_3d*1000:.0f}mm > {SPHERE_MAX*1000:.0f}mm)"
    if d_3d < SPHERE_MIN:
        return False, f"Too close to robot base! (3D Reach={d_3d*1000:.0f}mm < {SPHERE_MIN*1000:.0f}mm)"

    # 2. Z-axis boundaries
    if z < MIN_Z:
        return False, f"Too low! (Z={z*1000:.0f}mm < {MIN_Z*1000:.0f}mm) Table collision risk"
    if z > MAX_Z:
        return False, f"Too high! (Z={z*1000:.0f}mm > {MAX_Z*1000:.0f}mm)"

    return True, "OK"


def check_joint_safety(q_current):
    """
    Check if joint angles are within safe limits.
    Returns (is_safe, violations_list, warnings_list).
    """
    violations = []
    warnings = []

    for i in range(6):
        deg = math.degrees(q_current[i])
        lo, hi = JOINT_LIMITS[i]
        lo_rad, hi_rad = JOINT_LIMITS_RAD[i]

        # Check limit violations
        if q_current[i] < lo_rad or q_current[i] > hi_rad:
            violations.append(f"{JOINT_NAMES[i]}: {deg:.1f} deg (Limits: {lo} to {hi} deg)")

        # Check warning threshold
        elif (q_current[i] - lo_rad) < JOINT_WARNING_MARGIN:
            warnings.append(f"{JOINT_NAMES[i]}: {deg:.1f} deg near lower limit ({lo} deg)")
        elif (hi_rad - q_current[i]) < JOINT_WARNING_MARGIN:
            warnings.append(f"{JOINT_NAMES[i]}: {deg:.1f} deg near upper limit ({hi} deg)")

    return len(violations) == 0, violations, warnings


def isPoseReachable(rtde_c, pose, qnear=None):
    """
    Check if target pose is reachable and safe (4-tier verification).
    1. Workspace safety boundaries
    2. Controller safety limits
    3. Inverse Kinematics feasibility near current pose
    4. Joint limits and self-collision check on computed joints
    Returns: (is_reachable, reason).
    """
    # Tier 1: Cartesian distance boundaries
    cart_safe, cart_reason = check_cartesian_safety(pose)
    if not cart_safe:
        return False, f"🛑 [Cartesian] {cart_reason}"

    # Tier 2: UR Controller safety limits and planes
    try:
        if not rtde_c.isPoseWithinSafetyLimits(pose):
            return False, "🛑 [Safety Limits] Pose is outside Controller Safety Limits"
    except Exception as e:
        return False, f"⚠️ Unable to verify Safety Limits: {e}"

    # Tier 3: IK solution feasibility
    try:
        if qnear is not None:
            has_sol = rtde_c.getInverseKinematicsHasSolution(pose, qnear)
        else:
            has_sol = rtde_c.getInverseKinematicsHasSolution(pose)

        if not has_sol:
            return False, "🛑 [IK Solver] No feasible IK solution near current pose"
    except Exception as e:
        return False, f"⚠️ Unable to verify IK solution: {e}"

    # Tier 4: Pre-computed joint safety check
    try:
        target_q = rtde_c.getInverseKinematics(pose, qnear if qnear else [])
        if target_q and len(target_q) == 6:
            joint_safe, violations, _ = check_joint_safety(target_q)
            if not joint_safe:
                return False, f"🛑 [Joint Safety] Joint limits violated: {', '.join(violations)}"
    except Exception as e:
        return False, f"⚠️ Unable to compute target joints: {e}"

    return True, "OK"


def check_joint_jump(q_before, q_after):
    """
    Detect joint jump or configuration flip.
    Dangerous if any joint rotates beyond MAX_JOINT_JUMP in a single step.
    Returns (is_safe, culprit_msg).
    """
    for i in range(6):
        delta = abs(q_after[i] - q_before[i])
        delta = min(delta, 2 * math.pi - delta)

        if delta > MAX_JOINT_JUMP:
            deg = math.degrees(delta)
            return False, f"{JOINT_NAMES[i]} joint jump {deg:.1f} deg (exceeded {math.degrees(MAX_JOINT_JUMP):.0f} deg)"

    return True, "OK"


def check_protective_stop(rtde_r):
    """ Check if robot entered Protective Stop. """
    return rtde_r.isProtectiveStopped()


def recover_from_protective_stop(rtde_c, rtde_r):
    """ Auto-recover from Protective Stop via Dashboard Server (port 29999). """
    print("🔧 Auto-recovering from Protective Stop via Dashboard Server...")
    try:
        s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        s.settimeout(3.0)
        s.connect((ROBOT_IP, 29999))
        s.recv(1024)
        s.sendall(b'close safety popup\n')
        s.recv(1024)
        s.sendall(b'close popup\n')
        s.recv(1024)
        s.sendall(b'unlock protective stop\n')
        s.recv(1024)
        
        # Wait for safety status to return to NORMAL
        for _ in range(12):
            time.sleep(0.5)
            s.sendall(b'safetystatus\n')
            st = s.recv(1024).decode().strip()
            if 'NORMAL' in st:
                print("   ✅ Robot Safety Status returned to NORMAL")
                break
        s.close()
        time.sleep(0.5)
        
        # Reload RTDE control script
        rtde_c.reuploadScript()
        time.sleep(0.5)
        if not rtde_r.isProtectiveStopped():
            print("✅ Recovery complete, ready for commands!")
            return True
    except Exception as e:
        print(f"⚠️ Auto-recovery failed: {e}")

    print("❌ Please press 'Resume' on Teach Pendant and press Enter here")
    return False


def move_to_safe_home(rtde_c, rtde_r):
    """
    Move to Safe Home pose using moveJ (Joint Space).
    moveJ is used to safely avoid singularities.
    """
    print("🏠 Moving to Forward Home (moveJ)...")
    print(f"   Target (deg): [{', '.join([f'{math.degrees(q):.0f}' for q in HOME_Q])}]")

    try:
        rtde_c.moveJ(HOME_Q, J_SPEED, J_ACCEL)
        time.sleep(0.3)

        actual_q = rtde_r.getActualQ()
        actual_pose = rtde_r.getActualTCPPose()
        print(f"✅ Reached Safe Home!")
        print(f"   📍 TCP: X={actual_pose[0]:.3f}, Y={actual_pose[1]:.3f}, Z={actual_pose[2]:.3f}")
        print(f"   🔧 Q (deg): [{', '.join([f'{math.degrees(q):.1f}' for q in actual_q])}]")
        return True
    except Exception as e:
        print(f"❌ Failed to reach Home: {e}")
        return False


def execute_auto_shake(rtde_c, rtde_r, vr_server=None, tare_func=None):
    """
    Automatic test tube shaking at current position:
    1. Read current joint angles (does not return to Home)
    2. Rotate Wrist 3 +/-180 deg for 3 cycles
    3. Auto-tare and notify Unity HUD when complete
    """
    print("\n🧪 [Auto Shake] Starting 3-cycle shaking at current position...")
    if vr_server:
        vr_server.send_shake_status(True)
        vr_server.send_alert("INFO", "🧪 Starting auto-shake at current position...")

    try:
        # Read current joint angles — stay at current position, only rotate wrist
        q_current = list(rtde_r.getActualQ())
        wrist3_original = q_current[5]

        # Compute inverted wrist angle (±π from current) ensuring we respect joint limits
        q_inverted = list(q_current)
        if wrist3_original - math.pi >= -2 * math.pi + 0.1:
            q_inverted[5] = wrist3_original - math.pi  # Rotate Wrist 3 by -180°
        else:
            q_inverted[5] = wrist3_original + math.pi  # Rotate Wrist 3 by +180°

        SHAKE_SPEED = 2.8   # rad/s (~160 deg/s)
        SHAKE_ACCEL = 3.5   # rad/s²

        for cycle in range(1, 4):
            print(f"   🔄 [Cycle {cycle}/3] Inverting test tube (180 deg)...")
            if vr_server:
                vr_server.send_alert("INFO", f"🧪 Shaking tube... Cycle {cycle}/3 (+180 deg)")
            rtde_c.moveJ(q_inverted, SHAKE_SPEED, SHAKE_ACCEL)
            time.sleep(0.20)

            print(f"   🔄 [Cycle {cycle}/3] Reversing tube (-180 deg)...")
            if vr_server:
                vr_server.send_alert("INFO", f"🧪 Shaking tube... Cycle {cycle}/3 (-180 deg)")
            rtde_c.moveJ(q_current, SHAKE_SPEED, SHAKE_ACCEL)
            time.sleep(0.20)

        # Stop trajectory motion and prepare for speedL
        try:
            rtde_c.speedStop(STOP_ACCEL)
        except Exception:
            pass

        time.sleep(0.10)
        if not rtde_c.isProgramRunning():
            try:
                rtde_c.reuploadScript()
                time.sleep(0.15)
            except Exception:
                pass

        print("✅ [Auto Shake] 3-cycle shaking completed successfully!")
        if vr_server:
            vr_server.send_alert("SUCCESS", "✅ 3-cycle shaking completed successfully!")

        if tare_func:
            tare_func("3-cycle shaking complete")

        # Send live TCP pose to Unity before SHAKE DONE
        try:
            fresh_pose = rtde_r.getActualTCPPose()
            if vr_server:
                vr_server.send_feedback(0, 0.0, 100, actual_tcp_pose=fresh_pose)
                time.sleep(0.05)
        except Exception:
            pass
    finally:
        if vr_server:
            vr_server.send_shake_status(False)

    return True


# ==================== Main Program ====================
if __name__ == "__main__":
    print(f"Connecting to UR3e robot at IP: {ROBOT_IP}...")

    try:
        rtde_c = rtde_control.RTDEControlInterface(ROBOT_IP)
        rtde_r = rtde_receive.RTDEReceiveInterface(ROBOT_IP)
        gripper = RobotiqGripper(ROBOT_IP, GRIPPER_PORT)

        # Variables for 6-DOF velocity, Target Pose, and VR Hand state (thread-safe)
        vr_vel_lock = threading.Lock()
        vr_vel = [0.0, 0.0, 0.0, 0.0, 0.0, 0.0]
        vr_pose_state = {
            "target_pose": None,
            "last_time": 0.0
        }
        vr_hand_state = {
            "grip_pos": 0,
            "tare_requested": False,
            "home_requested": False,
            "shake_requested": False
        }
        vr_invert = {
            "x": False, "y": False, "z": False,
            "roll": False, "pitch": False, "yaw": False
        }
        speed_level = 1.0

        def on_vr_pose(px, py, pz, prx, pry, prz):
            """Receive Target Pose (x, y, z, rx, ry, rz) from Unity VR Hand Tracking."""
            with vr_vel_lock:
                vr_pose_state["target_pose"] = [px, py, pz, prx, pry, prz]
                vr_pose_state["last_time"] = time.time()

        def on_vr_move(vx, vy, vz, wx=0.0, wy=0.0, wz=0.0):
            """Receive 6-DOF velocity from Unity VR Hand."""
            if vr_invert["x"]: vx = -vx
            if vr_invert["y"]: vy = -vy
            if vr_invert["z"]: vz = -vz
            if vr_invert["roll"]: wx = -wx
            if vr_invert["pitch"]: wy = -wy
            if vr_invert["yaw"]: wz = -wz
            with vr_vel_lock:
                vr_vel[0] = vx
                vr_vel[1] = vy
                vr_vel[2] = vz
                vr_vel[3] = wx
                vr_vel[4] = wy
                vr_vel[5] = wz

        def on_vr_grip(p):
            if vr_server is not None and getattr(vr_server, 'is_teleop_paused', False):
                return
            with vr_vel_lock:
                vr_hand_state["grip_pos"] = p
            gripper.set_position(p)

        def on_vr_tare():
            with vr_vel_lock:
                vr_hand_state["tare_requested"] = True

        def on_vr_home():
            with vr_vel_lock:
                vr_hand_state["home_requested"] = True

        def on_vr_shake():
            with vr_vel_lock:
                vr_hand_state["shake_requested"] = True

        def on_vr_speed(pct):
            global speed_level
            pct_clamped = max(0, min(100, int(pct)))
            speed_level = pct_clamped / 100.0
            print(f"\n⚡ [VR Speed] Speed adjusted from VR Menu: {pct_clamped}% (speed_level={speed_level:.2f})")

        # Start VR Bridge Server (TCP Port 5555 for Unity Meta Quest Hand Tracking)
        try:
            from vr_bridge_server import VRBridgeServer
            vr_server = VRBridgeServer(
                host="0.0.0.0", port=5555,
                on_grip_callback=on_vr_grip,
                on_move_callback=on_vr_move,
                on_tare_callback=on_vr_tare,
                on_pose_callback=on_vr_pose,
                on_home_callback=on_vr_home,
                on_shake_callback=on_vr_shake,
                on_speed_callback=on_vr_speed
            )
            vr_server.start()
        except Exception as e:
            print(f"⚠️ Failed to start VR Bridge Server: {e}")
            vr_server = None

        print("\n✅ System connected successfully!")
        print("==================================================")
        print("🚀 Continuous Smooth Jogging Mode:")
        print(f"   * Arm: Hold key for continuous jogging ({SMOOTH_SPEED} m/s), release to brake")
        print("   * VR Hand: Move hand in VR to control robot arm (speedL)")
        print("   * Gripper: Hold key or use VR pinch for Robotiq control")
        print("   * Home Pose: Base 0 deg, Shoulder -90 deg, Elbow -90 deg, Wrist1 -180 deg, Wrist2 -90 deg, Wrist3 180 deg")
        print(f"   • Cartesian Bounds: 3D Sphere R=[{SPHERE_MIN*1000:.0f}mm, {SPHERE_MAX*1000:.0f}mm], Z=[{MIN_Z*1000:.0f}mm, {MAX_Z*1000:.0f}mm]")
        print("   * Joint Limits: Wrist 1 extended to +85 deg, elbow bent >= 15 deg to avoid singularity")
        print(f"   * Active Speed Limiter: Joint speed <= {MAX_SAFE_JOINT_SPEED_DEG:.0f} deg/s + Damped Least Squares")
        print("   * Directional Protection: Locks forward motion at boundary while allowing 100% reverse speed")
        print("==================================================")
        print("🎮 Controls (Hold = jog, Release = smooth stop):")
        print("   [W] / [S]        : Forward / Backward (X-axis)")
        print("   [A] / [D]        : Left / Right (Y-axis)")
        print("   [Space] / [Ctrl] : Up / Down (Z-axis)")
        print("   [Numpad 8] / [2] : Wrist 1 pitch (+ / -)")
        print("   [Numpad 6] / [4] : Wrist 2 yaw (+ / -)")
        print("   [Numpad 9] / [7] : Wrist 3 roll (+ / -)")
        print("   [=]              : Gripper Close (Hold / Release)")
        print("   [-]              : Gripper Open (Hold / Release)")
        print("   [H]              : Return to Safe Home (moveJ)")
        print("   [ ] ]            : Increase speed (10% -> 25% -> 50% -> 75% -> 100%)")
        print("   [ [ ]            : Decrease speed")
        print("   [1] / [2] / [3]  : Toggle VR translation axes (1:X, 2:Y, 3:Z)")
        print("   [4] / [5] / [6]  : Toggle VR rotation axes (4:Roll, 5:Pitch, 6:Yaw)")
        print("   [T]              : Tare Fz force sensor")
        print("   [K]              : Auto-shake test tube (3 cycles +/-180 deg)")
        print("   * Auto-Tare      : Auto-calibrates Fz when fingers open (<= 35/255)")
        print("   [Q]              : Quit")
        print("   * Left Remote    : [X] Set Origin & Home | [Y] Pause/Resume | [Stick Click] Auto-shake 3 cycles")
        print("   * VR Hand        : 6-DOF tracking | Pinch = Gripper close")
        print("   * bHaptics       : Live Fz haptic feedback on glove")
        print("   * UKF Filter     : Unscented Kalman Filter for smooth noise-free Fz")
        print("==================================================")

        # Display initial state
        init_q = rtde_r.getActualQ()
        init_pose = rtde_r.getActualTCPPose()
        print(f"\n📍 Initial Pose: X={init_pose[0]:.3f}, Y={init_pose[1]:.3f}, Z={init_pose[2]:.3f}")
        print(f"🔧 Q (deg): [{', '.join([f'{math.degrees(q):.1f}' for q in init_q])}]")

        # 1. Move to Safe Home pose
        print("\n🏠 Moving to Safe Home pose...")
        move_to_safe_home(rtde_c, rtde_r)
        time.sleep(0.4)

        # 2. Initialize Unscented Kalman Filter (UKF) for Fz
        ukf_force = UnscentedKalmanFilter1D(q_force=3.5, q_dforce=12.0, r_measure=0.20, alpha=0.3)
        smooth_grip_force = 0.0
        cached_gripper_pos = 0
        speed_level = 1.0
        fz_tare = 0.0

        def perform_tare(reason="[T] key"):
            global fz_tare, smooth_grip_force
            try:
                tare_samples = [rtde_r.getActualTCPForce()[2] for _ in range(25)]
                fz_tare = sum(tare_samples) / len(tare_samples)
                ukf_force.reset(0.0)
                smooth_grip_force = 0.0
                print(f"🎯 [Tare Fz - {reason}]: Zeroed Fz = {fz_tare:+.2f} N")
                if 'vr_server' in locals() and vr_server:
                    sp = int(round(speed_level * 100)) if 'speed_level' in locals() else 100
                    try:
                        cur_p = rtde_r.getActualTCPPose()
                    except Exception:
                        cur_p = None
                    vr_server.send_feedback(cached_gripper_pos, 0.0, sp, actual_tcp_pose=cur_p)
                return True
            except Exception as e:
                print(f"⚠️ Failed to Tare Fz ({reason}): {e}")
                return False

        # 3. Auto-Tare Fz at Safe Home
        print("🎯 Calibrating Fz zero offset (Auto-Tare)...")
        perform_tare("Startup at Safe Home")

        # State variables for Auto-Tare (open fingers <= 35)
        vr_hand_rearmed_for_tare = False
        last_auto_tare_time = 0.0

        print("\n✅ Ready for commands! Click this window and press control keys.\n")

        # Smooth motion state variables
        was_moving_arm = False
        gripper_moving_close = False
        gripper_moving_open = False
        was_teleop_paused = False
        prev_h_pressed = False
        prev_k_pressed = False
        prev_1_pressed = False
        prev_2_pressed = False
        prev_3_pressed = False
        prev_4_pressed = False
        prev_5_pressed = False
        prev_6_pressed = False
        prev_t_pressed = False
        prev_bracket_open_pressed = False
        prev_bracket_close_pressed = False
        speed_level = 1.0  # 1.0 = 100% MAX, 0.5 = 50%, 0.25 = 25%
        speed_pct = int(round(speed_level * 100))
        smooth_grip_force = 0.0
        current_stream_qd = [0.0] * 6  # Joint velocity memory for ramp
        current_cart_cmd = [0.0] * 6   # Cartesian 6-DOF velocity memory for S-curve ramp
        is_pose_settled = False        # Translation settled state (Hysteresis deadband)
        is_rot_settled = False         # Rotation settled state

        # Timer for logging force (5 Hz)
        last_force_print = 0.0
        FORCE_PRINT_INTERVAL = 0.2

        while True:
            t_loop_start = time.time()
            speed_pct = max(0, min(100, int(round(speed_level * 100))))
            # 1. Check Protective Stop
            if check_protective_stop(rtde_r):
                print("\n🛑 PROTECTIVE STOP detected!")
                if 'vr_server' in locals() and vr_server:
                    vr_server.send_alert("ERROR", "UR3e Protective Stop Detected!")
                if was_moving_arm:
                    try:
                        rtde_c.speedStop(STOP_ACCEL)
                    except Exception:
                        pass
                    was_moving_arm = False
                current_cart_cmd = [0.0] * 6
                recover_from_protective_stop(rtde_c, rtde_r)
                print("   Press [H] to return to Safe Home\n")
                time.sleep(0.5)
                continue

            # 2. Read current pose and joints
            current_pose = rtde_r.getActualTCPPose()
            current_q = rtde_r.getActualQ()

            # 3. Check exit command
            if keyboard.is_pressed('q'):
                print("\nExiting program...")
                if was_moving_arm:
                    rtde_c.speedStop(STOP_ACCEL)
                current_cart_cmd = [0.0] * 6
                break

            # 4. Check Home command ([H] key or VR left hand pinch)
            with vr_vel_lock:
                vr_home_req = vr_hand_state["home_requested"]
                vr_hand_state["home_requested"] = False

            h_pressed = keyboard.is_pressed('h')
            if (h_pressed and not prev_h_pressed) or vr_home_req:
                trigger_source = "VR Left Hand Pinch" if vr_home_req else "[H] key"
                print(f"\n🏠 Received return to Safe Home command from: {trigger_source}")
                if was_moving_arm:
                    rtde_c.speedStop(STOP_ACCEL)
                    was_moving_arm = False
                current_cart_cmd = [0.0] * 6
                with vr_vel_lock:
                    vr_pose_state["target_pose"] = None
                    vr_vel = [0.0, 0.0, 0.0, 0.0, 0.0, 0.0]
                is_pose_settled = False
                is_rot_settled = False
                move_to_safe_home(rtde_c, rtde_r)
                time.sleep(0.3)
                current_pose = rtde_r.getActualTCPPose()
                if 'vr_server' in locals() and vr_server:
                    vr_server.send_feedback(cached_gripper_pos, smooth_grip_force, speed_pct, actual_tcp_pose=current_pose)
                # Auto-tare upon reaching Home
                perform_tare(f"Returned to Safe Home ({trigger_source})")
            prev_h_pressed = h_pressed

            # 4a. Check auto-shake command ([K] key or VR Thumbstick click)
            with vr_vel_lock:
                vr_shake_req = vr_hand_state["shake_requested"]
                vr_hand_state["shake_requested"] = False

            k_pressed = keyboard.is_pressed('k')
            if (k_pressed and not prev_k_pressed) or vr_shake_req:
                trigger_source = "VR Left Thumbstick Click" if vr_shake_req else "[K] key"
                print(f"\n🧪 Received 3-cycle auto-shake command from: {trigger_source}")
                if was_moving_arm:
                    rtde_c.speedStop(STOP_ACCEL)
                    was_moving_arm = False
                current_cart_cmd = [0.0] * 6
                with vr_vel_lock:
                    vr_pose_state["target_pose"] = None
                    vr_vel = [0.0, 0.0, 0.0, 0.0, 0.0, 0.0]
                is_pose_settled = False
                is_rot_settled = False
                execute_auto_shake(rtde_c, rtde_r, vr_server=vr_server, tare_func=perform_tare)
                time.sleep(0.10)
                current_pose = rtde_r.getActualTCPPose()
                with vr_vel_lock:
                    vr_pose_state["target_pose"] = list(current_pose)
                    vr_pose_state["last_time"] = time.time()
                    vr_vel = [0.0, 0.0, 0.0, 0.0, 0.0, 0.0]
                is_pose_settled = True
                is_rot_settled = True
                if 'vr_server' in locals() and vr_server:
                    vr_server.send_feedback(cached_gripper_pos, smooth_grip_force, speed_pct, actual_tcp_pose=current_pose)
            prev_k_pressed = k_pressed

            # 4b. Toggle VR translation axes
            p1 = keyboard.is_pressed('1') and not is_num_key(1)
            if p1 and not prev_1_pressed:
                vr_invert["x"] = not vr_invert["x"]
                state_str = "INVERTED" if vr_invert["x"] else "NORMAL"
                print(f"🔄 [VR Invert X (Forward/Backward)]: {state_str}")
            prev_1_pressed = p1

            p2 = keyboard.is_pressed('2') and not is_num_key(2)
            if p2 and not prev_2_pressed:
                vr_invert["y"] = not vr_invert["y"]
                state_str = "INVERTED" if vr_invert["y"] else "NORMAL"
                print(f"🔄 [VR Invert Y (Left/Right)]: {state_str}")
            prev_2_pressed = p2

            p3 = keyboard.is_pressed('3') and not is_num_key(3)
            if p3 and not prev_3_pressed:
                vr_invert["z"] = not vr_invert["z"]
                state_str = "INVERTED" if vr_invert["z"] else "NORMAL"
                print(f"🔄 [VR Invert Z (Up/Down)]: {state_str}")
            prev_3_pressed = p3

            # Toggle VR rotation axes
            p4 = keyboard.is_pressed('4') and not is_num_key(4)
            if p4 and not prev_4_pressed:
                vr_invert["roll"] = not vr_invert["roll"]
                state_str = "INVERTED" if vr_invert["roll"] else "NORMAL"
                print(f"🔄 [VR Invert Roll (Wrist Wx)]: {state_str}")
            prev_4_pressed = p4

            p5 = keyboard.is_pressed('5') and not is_num_key(5)
            if p5 and not prev_5_pressed:
                vr_invert["pitch"] = not vr_invert["pitch"]
                state_str = "INVERTED" if vr_invert["pitch"] else "NORMAL"
                print(f"🔄 [VR Invert Pitch (Wrist Wy)]: {state_str}")
            prev_5_pressed = p5

            p6 = keyboard.is_pressed('6') and not is_num_key(6)
            if p6 and not prev_6_pressed:
                vr_invert["yaw"] = not vr_invert["yaw"]
                state_str = "INVERTED" if vr_invert["yaw"] else "NORMAL"
                print(f"🔄 [VR Invert Yaw (Wrist Wz)]: {state_str}")
            prev_6_pressed = p6

            # 4c. Tare Fz
            p_t = keyboard.is_pressed('t')
            if p_t and not prev_t_pressed:
                perform_tare("[T] key")
            prev_t_pressed = p_t

            # Auto-Tare when opening fingers (<= 35/255)
            # Hysteresis: must grip > 42 before arming tare at <= 35
            with vr_vel_lock:
                current_vr_grip = vr_hand_state["grip_pos"]
                tare_req = vr_hand_state["tare_requested"]
                vr_hand_state["tare_requested"] = False

            if current_vr_grip >= 42:
                vr_hand_rearmed_for_tare = True

            now_t = time.time()
            if tare_req:
                perform_tare("TARE command from VR Hand (Open fingers <= 35)")
                last_auto_tare_time = now_t
                vr_hand_rearmed_for_tare = False
            elif vr_hand_rearmed_for_tare and current_vr_grip <= 35:
                if now_t - last_auto_tare_time >= 0.4:
                    vr_hand_rearmed_for_tare = False
                    last_auto_tare_time = now_t
                    perform_tare(f"Open fingers level {current_vr_grip}/255")

            # 4d. Adjust speed level ([ = decrease, ] = increase)
            SPEED_STEPS = [0.10, 0.25, 0.50, 0.75, 1.00]
            p_bracket_open = False
            p_bracket_close = False
            try:
                p_bracket_open = keyboard.is_pressed('[') or keyboard.is_pressed('{')
                p_bracket_close = keyboard.is_pressed(']') or keyboard.is_pressed('}')
            except Exception:
                pass

            if p_bracket_close and not prev_bracket_close_pressed:
                # Increase speed level
                curr_idx = min(range(len(SPEED_STEPS)), key=lambda i: abs(SPEED_STEPS[i] - speed_level))
                new_idx = min(len(SPEED_STEPS) - 1, curr_idx + 1)
                speed_level = SPEED_STEPS[new_idx]
                speed_pct = int(round(speed_level * 100))
                emoji = "🚀" if speed_level >= 1.0 else ("⚡" if speed_level >= 0.5 else "🐢")
                print(f"\n{emoji} [Speed Level]: {speed_pct}% (SMOOTH={SMOOTH_SPEED*speed_level*1000:.0f} mm/s, ROT={math.degrees(ROT_SPEED*speed_level):.1f} deg/s)\n")
            prev_bracket_close_pressed = p_bracket_close

            if p_bracket_open and not prev_bracket_open_pressed:
                # Decrease speed level
                curr_idx = min(range(len(SPEED_STEPS)), key=lambda i: abs(SPEED_STEPS[i] - speed_level))
                new_idx = max(0, curr_idx - 1)
                speed_level = SPEED_STEPS[new_idx]
                speed_pct = int(round(speed_level * 100))
                emoji = "🚀" if speed_level >= 1.0 else ("⚡" if speed_level >= 0.5 else "🐢")
                print(f"\n{emoji} [Speed Level]: {speed_pct}% (SMOOTH={SMOOTH_SPEED*speed_level*1000:.0f} mm/s, ROT={math.degrees(ROT_SPEED*speed_level):.1f} deg/s)\n")
            prev_bracket_open_pressed = p_bracket_open

            # 5. Gripper control (Hold = move, Release = stop)
            # Suspend Gripper commands if Paused
            if getattr(vr_server, 'is_teleop_paused', False):
                eq_pressed = False
                minus_pressed = False
            else:
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
                if gripper_moving_close or gripper_moving_open:
                    was_opening = gripper_moving_open
                    gripper_moving_close = False
                    gripper_moving_open = False
                    gripper.stop(is_opening=was_opening)
                    pos, _, cou, _, desc = gripper.get_status()
                    gripper._print_gauge(pos, smooth_grip_force, cou, desc)

            # 6. Compute linear and angular velocities based on speed level
            cur_smooth_speed = SMOOTH_SPEED * speed_level
            cur_rot_speed = ROT_SPEED * speed_level
            cur_smooth_accel = min(1.0, SMOOTH_ACCEL * speed_level)
            cur_stream_accel = min(1.0, J_STREAM_ACCEL * speed_level)
            vr_speed_cap = max(cur_smooth_speed, VR_TRACKING_MAX_SPEED * min(speed_level, 1.8))
            vr_rot_cap = max(cur_rot_speed * 1.5, VR_TRACKING_MAX_ROT * min(speed_level, 1.8))
            pos_dist = 0.0
            rot_dist = 0.0

            vx = 0.0
            vy = 0.0
            vz = 0.0
            wx = 0.0
            wy = 0.0
            wz = 0.0

            # Check Numpad keys
            def is_num_key(n, alt_name=None):
                try:
                    if keyboard.is_pressed(f'num {n}'):
                        return True
                    if alt_name and keyboard.is_pressed(alt_name):
                        return True
                except Exception:
                    pass
                return False

            # 6a. Keyboard velocity commands
            if keyboard.is_pressed('w'):
                vx += cur_smooth_speed
            if keyboard.is_pressed('s'):
                vx -= cur_smooth_speed

            if keyboard.is_pressed('a'):
                vy += cur_smooth_speed
            if keyboard.is_pressed('d'):
                vy -= cur_smooth_speed

            if keyboard.is_pressed('space'):
                vz += cur_smooth_speed
            if keyboard.is_pressed('ctrl'):
                vz -= cur_smooth_speed

            # 6b. VR Hand Tracking 6-DOF velocity commands
            # Supports 2 modes:
            #   (1) Absolute / Anchor Target Pose Mode (Closed-loop P-Control)
            #       - Tracks target position and orientation
            #       - Settles down to zero position and orientation error
            #   (2) Velocity Mode (MOVE vx vy vz wx wy wz)
            now_pose_t = time.time()
            with vr_vel_lock:
                tgt_p = list(vr_pose_state["target_pose"]) if vr_pose_state["target_pose"] is not None else None
                tgt_t = vr_pose_state["last_time"]
                vr_vx, vr_vy, vr_vz = vr_vel[0], vr_vel[1], vr_vel[2]
                vr_wx, vr_wy, vr_wz = vr_vel[3], vr_vel[4], vr_vel[5]

            is_pose_mode = (tgt_p is not None and (now_pose_t - tgt_t) < 3.0)

            if is_pose_mode:
                # Compute Cartesian Pose Error
                pos_err, rot_err = compute_pose_error(tgt_p, current_pose)

                # Axis inversion support
                if vr_invert["x"]: pos_err[0] = -pos_err[0]
                if vr_invert["y"]: pos_err[1] = -pos_err[1]
                if vr_invert["z"]: pos_err[2] = -pos_err[2]
                if vr_invert["roll"]: rot_err[0] = -rot_err[0]
                if vr_invert["pitch"]: rot_err[1] = -rot_err[1]
                if vr_invert["yaw"]: rot_err[2] = -rot_err[2]

                # 1. Position error magnitude
                pos_dist = math.sqrt(pos_err[0]**2 + pos_err[1]**2 + pos_err[2]**2)

                # Read actual TCP speed for active velocity damping (PD control)
                try:
                    act_tcp_speed = rtde_r.getActualTCPSpeed()
                except Exception:
                    act_tcp_speed = [0.0] * 6
                act_vx, act_vy, act_vz = act_tcp_speed[0], act_tcp_speed[1], act_tcp_speed[2]
                act_wx, act_wy, act_wz = act_tcp_speed[3], act_tcp_speed[4], act_tcp_speed[5]
                act_lin_spd = math.sqrt(act_vx**2 + act_vy**2 + act_vz**2)
                act_rot_spd = math.sqrt(act_wx**2 + act_wy**2 + act_wz**2)

                # Deadband with hysteresis and settling detection
                # Settles when distance is within deadband and speed is low
                # Eliminates overshoot and ringing
                if is_pose_settled:
                    if pos_dist > 0.0060:
                        is_pose_settled = False
                else:
                    if pos_dist <= 0.0035 and act_lin_spd < 0.020:
                        is_pose_settled = True

                if is_pose_settled:
                    v_p = [0.0, 0.0, 0.0]
                else:
                    # S-Curve Soft Landing Approach Speed with Critical Damping
                    kp_pos = min(4.2, 3.2 * math.sqrt(speed_level))
                    vr_speed_cap = max(cur_smooth_speed, VR_TRACKING_MAX_SPEED * min(speed_level, 1.4))
                    v_desired = min(pos_dist * kp_pos, vr_speed_cap)

                    # Velocity damping to prevent phase lag
                    kd_pos = 0.20
                    dir_p = [pos_err[i] / pos_dist for i in range(3)]
                    v_p = [dir_p[i] * v_desired - kd_pos * [act_vx, act_vy, act_vz][i] for i in range(3)]

                # 2. Rotation error magnitude with angular damping
                rot_dist = math.sqrt(rot_err[0]**2 + rot_err[1]**2 + rot_err[2]**2)

                if is_rot_settled:
                    if rot_dist > math.radians(2.00):
                        is_rot_settled = False
                else:
                    if rot_dist <= math.radians(0.85) and act_rot_spd < 0.08:
                        is_rot_settled = True

                if is_rot_settled:
                    w_p = [0.0, 0.0, 0.0]
                else:
                    # Snappy angular control (kp_rot capped at 5.0)
                    kp_rot = min(5.0, 3.6 * math.sqrt(speed_level))
                    vr_rot_cap = max(cur_rot_speed * 1.5, VR_TRACKING_MAX_ROT * min(speed_level, 1.8))
                    w_desired = min(rot_dist * kp_rot, vr_rot_cap)

                    kd_rot = 0.05
                    dir_r = [rot_err[i] / rot_dist for i in range(3)]
                    w_p = [dir_r[i] * w_desired - kd_rot * [act_wx, act_wy, act_wz][i] for i in range(3)]

                vx += v_p[0]
                vy += v_p[1]
                vz += v_p[2]
                wx += w_p[0]
                wy += w_p[1]
                wz += w_p[2]
            else:
                # Fallback: Open-loop MOVE velocity
                vx += vr_vx * speed_level
                vy += vr_vy * speed_level
                vz += vr_vz * speed_level
                wx += vr_wx * speed_level
                wy += vr_wy * speed_level
                wz += vr_wz * speed_level

            # Clamp total linear velocity
            speed_mag = math.sqrt(vx*vx + vy*vy + vz*vz)
            max_allowed_spd = vr_speed_cap if is_pose_mode else cur_smooth_speed
            if speed_mag > max_allowed_spd:
                scale = max_allowed_spd / speed_mag
                vx *= scale
                vy *= scale
                vz *= scale

            # Clamp total angular velocity
            rot_mag = math.sqrt(wx*wx + wy*wy + wz*wz)
            max_allowed_rot = vr_rot_cap if is_pose_mode else cur_rot_speed
            if rot_mag > max_allowed_rot:
                scale_rot = max_allowed_rot / rot_mag
                wx *= scale_rot
                wy *= scale_rot
                wz *= scale_rot

            # 6c. Numpad wrist joint control
            # 7 = wrist 3 (-) | 9 = wrist 3 (+)
            # 4 = wrist 2 (-) | 6 = wrist 2 (+)
            # 2 = wrist 1 (-) | 8 = wrist 1 (+)
            qd_wrist = [0.0, 0.0, 0.0, 0.0, 0.0, 0.0]

            # [Numpad 8] Wrist 1 (+) / [Numpad 2] Wrist 1 (-)
            if is_num_key(8, 'up'):
                qd_wrist[3] += cur_rot_speed
            if is_num_key(2, 'down'):
                qd_wrist[3] -= cur_rot_speed

            # [Numpad 6] Wrist 2 (+) / [Numpad 4] Wrist 2 (-)
            if is_num_key(6, 'right'):
                qd_wrist[4] += cur_rot_speed
            if is_num_key(4, 'left'):
                qd_wrist[4] -= cur_rot_speed

            # [Numpad 9] Wrist 3 (+) / [Numpad 7] Wrist 3 (-)
            if is_num_key(9, 'page up'):
                qd_wrist[5] += cur_rot_speed
            if is_num_key(7, 'home'):
                qd_wrist[5] -= cur_rot_speed

            # ------------------------------------------------------------
            # Dispatch motion commands (Joint Space: speedJ / Cartesian Space: speedL)
            # ------------------------------------------------------------
            # Pause check (from Y button on Quest Remote):
            # If paused: zero velocities, clear target pose, stop arm and gripper immediately
            is_paused_now = getattr(vr_server, 'is_teleop_paused', False)
            if is_paused_now:
                vx = vy = vz = wx = wy = wz = 0.0
                qd_wrist = [0.0] * 6
                current_stream_qd = [0.0] * 6
                current_cart_cmd = [0.0] * 6
                vr_pose_state["target_pose"] = None
                if was_moving_arm:
                    try:
                        rtde_c.speedStop(STOP_ACCEL)
                    except Exception:
                        pass
                    was_moving_arm = False
                if not was_teleop_paused:
                    # Entered Pause state -> stop gripper immediately
                    try:
                        gripper.stop()
                    except Exception:
                        pass
            was_teleop_paused = is_paused_now

            is_wrist_moving = any(q != 0.0 for q in qd_wrist)
            is_cart_moving = (vx != 0.0 or vy != 0.0 or vz != 0.0 or wx != 0.0 or wy != 0.0 or wz != 0.0)
            is_streaming_wrist = any(abs(q) > 1e-4 for q in current_stream_qd)

            # Verify RTDE control script status; auto-recover if disconnected
            if not rtde_c.isProgramRunning():
                try:
                    rtde_c.reuploadScript()
                    time.sleep(0.05)
                except Exception:
                    pass

            if is_wrist_moving or (is_streaming_wrist and not is_cart_moving):
                # Direct wrist joint control (Joint Space: speedJ) with smooth velocity ramp
                current_cart_cmd = [0.0] * 6
                if is_wrist_moving:
                    if is_cart_moving:
                        qd_actual = rtde_r.getActualQd()
                        fvx, fvy, fvz, fwx, fwy, fwz, _ = filter_cartesian_velocity(
                            vx, vy, vz, wx, wy, wz, current_pose=current_pose, current_q=current_q, qd_actual=qd_actual, dt=0.02
                        )
                        J, _ = compute_ur3e_jacobian(current_q)
                        qd_cart = solve_dls_system(J, [fvx, fvy, fvz, fwx, fwy, fwz], damping=0.02)
                        target_qd = [qd_cart[i] + qd_wrist[i] for i in range(6)]
                    else:
                        target_qd = qd_wrist

                    target_qd, safety_reason = filter_joint_velocity(target_qd, current_q, dt=0.02)
                else:
                    # Key released -> ramp down to zero
                    target_qd = [0.0] * 6

                # Velocity ramp calculation
                dt_loop = 0.02
                for i in range(6):
                    diff = target_qd[i] - current_stream_qd[i]
                    if target_qd[i] == 0.0:
                        max_step = 0.60 * dt_loop  # Smooth deceleration
                    else:
                        max_step = cur_stream_accel * dt_loop  # Gradual acceleration

                    if abs(diff) <= max_step:
                        current_stream_qd[i] = target_qd[i]
                    else:
                        current_stream_qd[i] += math.copysign(max_step, diff)

                if any(abs(q) > 1e-4 for q in current_stream_qd):
                    rtde_c.speedJ(current_stream_qd, cur_stream_accel, STREAM_TIME_BUFFER)
                    was_moving_arm = True
                else:
                    current_stream_qd = [0.0] * 6
                    if was_moving_arm:
                        rtde_c.speedStop(STOP_ACCEL)
                        was_moving_arm = False

            elif is_cart_moving or any(abs(c) > 1e-4 for c in current_cart_cmd):
                # 6-DOF Cartesian motion mode (WASD / VR: speedL)
                current_stream_qd = [0.0] * 6
                qd_actual = rtde_r.getActualQd()

                if is_cart_moving:
                    fvx, fvy, fvz, fwx, fwy, fwz, safety_reason = filter_cartesian_velocity(
                        vx, vy, vz, wx, wy, wz, current_pose=current_pose, current_q=current_q, qd_actual=qd_actual, dt=0.02
                    )
                    tgt_v = [fvx, fvy, fvz]
                    tgt_w = [fwx, fwy, fwz]
                else:
                    # Hand stopped -> Ramp down smoothly
                    tgt_v = [0.0, 0.0, 0.0]
                    tgt_w = [0.0, 0.0, 0.0]

                dt_loop = 0.02
                cur_v = current_cart_cmd[0:3]
                cur_w = current_cart_cmd[3:6]

                # 1. Linear velocity ramping (Asymmetric Accel & Decel):
                # - Braking/Reversing: fast braking acceleration to eliminate inertia
                # - Accelerating: smooth acceleration
                cur_v_mag = math.sqrt(cur_v[0]**2 + cur_v[1]**2 + cur_v[2]**2)
                tgt_v_mag = math.sqrt(tgt_v[0]**2 + tgt_v[1]**2 + tgt_v[2]**2)
                dot_v = cur_v[0]*tgt_v[0] + cur_v[1]*tgt_v[1] + cur_v[2]*tgt_v[2]
                is_braking_v = (tgt_v_mag < cur_v_mag) or (dot_v <= 0.0)

                if is_braking_v:
                    a_lin = 2.50  # m/s^2 Fast braking
                elif is_pose_mode:
                    a_lin = max(1.50, min(2.50, 1.20 * speed_level + 0.60))
                else:
                    a_lin = cur_smooth_accel

                # Ramping linear velocity
                diff_v = [tgt_v[i] - cur_v[i] for i in range(3)]
                diff_v_mag = math.sqrt(diff_v[0]**2 + diff_v[1]**2 + diff_v[2]**2)
                max_step_v = a_lin * dt_loop

                if diff_v_mag <= max_step_v:
                    cur_v = [tgt_v[i] for i in range(3)]
                else:
                    scale_v = max_step_v / diff_v_mag
                    cur_v = [cur_v[i] + diff_v[i] * scale_v for i in range(3)]

                # 2. Angular velocity ramping (Wrist Angular Rate Limiter):
                # - Braking/Reversing: fast stop to track hand closely
                # - Accelerating: responsive rotation
                cur_w_mag = math.sqrt(cur_w[0]**2 + cur_w[1]**2 + cur_w[2]**2)
                tgt_w_mag = math.sqrt(tgt_w[0]**2 + tgt_w[1]**2 + tgt_w[2]**2)
                dot_w = cur_w[0]*tgt_w[0] + cur_w[1]*tgt_w[1] + cur_w[2]*tgt_w[2]
                is_braking_w = (tgt_w_mag < cur_w_mag) or (dot_w <= 0.0)

                if is_braking_w:
                    a_rot = 35.0  # rad/s^2 Fast rotational stop
                elif is_pose_mode:
                    a_rot = min(20.0, 15.0 * speed_level)  # Angular accel limit
                else:
                    a_rot = cur_stream_accel * 5.0

                diff_w = [tgt_w[i] - cur_w[i] for i in range(3)]
                diff_w_mag = math.sqrt(diff_w[0]**2 + diff_w[1]**2 + diff_w[2]**2)
                max_step_w = a_rot * dt_loop

                if diff_w_mag <= max_step_w:
                    cur_w = [tgt_w[i] for i in range(3)]
                else:
                    scale_w = max_step_w / diff_w_mag
                    cur_w = [cur_w[i] + diff_w[i] * scale_w for i in range(3)]

                current_cart_cmd = [cur_v[0], cur_v[1], cur_v[2], cur_w[0], cur_w[1], cur_w[2]]

                # Check remaining velocity command
                cmd_total_spd = math.sqrt(cur_v[0]**2 + cur_v[1]**2 + cur_v[2]**2)
                cmd_total_rot = math.sqrt(cur_w[0]**2 + cur_w[1]**2 + cur_w[2]**2)

                if cmd_total_spd > 1e-4 or cmd_total_rot > 1e-4:
                    # Pre-send safety check: scale down if any joint approaches JOINT_MAX_SPEEDS
                    J_check, _ = compute_ur3e_jacobian(current_q)
                    qd_check = solve_dls_system(J_check, current_cart_cmd, damping=0.025)
                    arm_viol_scales = [JOINT_MAX_SPEEDS[j] / abs(qd_check[j]) for j in range(3) if abs(qd_check[j]) > JOINT_MAX_SPEEDS[j]]
                    wrist_viol_scales = [JOINT_MAX_SPEEDS[j] / abs(qd_check[j]) for j in range(3, 6) if abs(qd_check[j]) > JOINT_MAX_SPEEDS[j]]
                    if arm_viol_scales:
                        min_arm_s = min(arm_viol_scales)
                        current_cart_cmd = [c * min_arm_s for c in current_cart_cmd]
                    elif wrist_viol_scales:
                        min_wrist_s = min(wrist_viol_scales)
                        current_cart_cmd[3] *= min_wrist_s
                        current_cart_cmd[4] *= min_wrist_s
                        current_cart_cmd[5] *= min_wrist_s

                    exec_accel = 2.40 if is_pose_mode else cur_smooth_accel
                    rtde_c.speedL(current_cart_cmd, exec_accel, STREAM_TIME_BUFFER)
                    was_moving_arm = True
                else:
                    current_cart_cmd = [0.0] * 6
                    if was_moving_arm:
                        # speedStop (2.0 m/s^2) to hold position firmly
                        rtde_c.speedStop(2.0)
                        was_moving_arm = False

            elif was_moving_arm:
                current_stream_qd = [0.0] * 6
                current_cart_cmd = [0.0] * 6
                rtde_c.speedStop(2.0)
                was_moving_arm = False

            # 7. Read force at 50 Hz and filter via UKF
            force = rtde_r.getActualTCPForce()
            fx, fy, fz, tx, ty, tz = force
            # Tare offset and deadband calculation
            raw_delta = abs(fz - fz_tare)
            FORCE_DEADBAND = 0.8  # N (UR3e sensor noise margin)
            if raw_delta > FORCE_DEADBAND:
                clean_force = raw_delta - FORCE_DEADBAND
            else:
                clean_force = 0.0

            # Anti-ghost vibration check when fingers open or moving in air:
            # If Gripper is open (<= 35) and not pinching, cut force to 0.0 N
            with vr_vel_lock:
                current_vr_grip_cmd = vr_hand_state.get("grip_pos", 0)
            if cached_gripper_pos <= 35 and current_vr_grip_cmd <= 35:
                clean_force = 0.0
            elif getattr(gripper, 'contact_detected', False):
                # If clamped on object (OBJ 2), blend actual grip force
                contact_force_floor = 18.0 + (GRIPPER_FORCE / 255.0) * (25.0 - 18.0)
                clean_force = max(clean_force, contact_force_floor)

            smooth_grip_force = ukf_force.update(clean_force)
            if clean_force == 0.0 and smooth_grip_force < 0.08:
                smooth_grip_force = 0.0

            # Send smooth UKF force feedback to Unity VR Headset and bHaptics Glove
            # Broadcast speed level, actual pose, and safety proximity ratios
            speed_pct = max(0, min(100, int(round(speed_level * 100))))
            qd_for_safety = rtde_r.getActualQd()
            safety_prox = compute_safety_proximity(current_pose, current_q, qd_for_safety)
            if 'vr_server' in locals() and vr_server:
                vr_server.send_feedback(cached_gripper_pos, smooth_grip_force, speed_pct, actual_tcp_pose=current_pose, safety_proximity=safety_prox)
                if 'safety_reason' in locals() and safety_reason != "OK":
                    vr_server.send_alert("WARNING", safety_reason)
                elif safety_prox.get("rot_limit_ratio", 0.0) > 0.05:
                    w_j = safety_prox.get("worst_joint_name", "Wrist")
                    deg_l = safety_prox.get("worst_deg_left", 0.0)
                    vr_server.send_alert("WARNING", f"ROTATION LIMIT WARNING: {w_j} near rotation limit ({deg_l:.1f}° remaining) - Decelerating")

            current_time = time.time()
            if current_time - last_force_print >= FORCE_PRINT_INTERVAL:
                pos, _, cou, _, grip_desc = gripper.get_status()
                cached_gripper_pos = pos

                # Coordinate mm conversion for pendant display comparison
                x_mm = current_pose[0] * 1000.0
                y_mm = current_pose[1] * 1000.0
                z_mm = current_pose[2] * 1000.0
                z_pendant = z_mm - 400.0

                # Log Fz force (Raw vs UKF Filtered), current pose, and speed level
                speed_label = f"{speed_pct}% [MAX 🚀]" if speed_level >= 1.0 else f"{speed_pct}%"
                rot_hud = f" | Rot=[Wx:{math.degrees(wx):+.0f}, Wy:{math.degrees(wy):+.0f}, Wz:{math.degrees(wz):+.0f}]°/s" if (wx != 0 or wy != 0 or wz != 0) else ""
                print(f"⚡ [force] Fz_raw={fz:+.2f} N (UKF Grip={smooth_grip_force:5.1f} N) | Base(mm)=[X:{x_mm:.0f}, Y:{y_mm:.0f}, Z:{z_mm:.0f}]{rot_hud} | Pendant Z={z_pendant:.0f}mm | Speed={speed_label}")
                last_force_print = current_time

            # 50 Hz loop with execution time compensation (20 ms locked)
            elapsed = time.time() - t_loop_start
            if elapsed < 0.02:
                time.sleep(0.02 - elapsed)

    except Exception as e:
        print(f"\n❌ Error occurred: {e}")

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
            print("Disconnected")
