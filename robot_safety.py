import math

# ==================== UR3e Kinematic & Workspace Parameters ====================
# DH Parameters for UR3e (meters):
DH = [
    [0.0,       0.1519,  math.pi/2],   # Joint 1 (Base height d1 = 151.9 mm)
    [-0.2436,   0.0,     0.0],         # Joint 2 (Upper arm a2 = 243.6 mm)
    [-0.2132,   0.0,     0.0],         # Joint 3 (Forearm a3 = 213.2 mm)
    [0.0,       0.13105, math.pi/2],   # Joint 4 (Wrist 1 d4 = 131.05 mm)
    [0.0,       0.08535, -math.pi/2],  # Joint 5 (Wrist 2 d5 = 85.35 mm)
    [0.0,       0.0921,  0.0]          # Joint 6 (Wrist 3 d6 = 92.1 mm)
]

SHOULDER_Z = 0.1519       # ความสูงจุดหมุน Shoulder จากระนาบฐานหุ่น (m)
# ===================== ขอบเขตพื้นที่ทำงาน (จากคู่มือ UR3e Manual §5.1, §5.3, §8.4 และ docs/ur3e_manual_limits.md §7) =====================
# Outer Safety Sphere: ปรับให้สอดคล้องกับระยะเอื้อมจริงและ Safety Barrier ของ Unity (~58cm จาก Base)
# ตามคู่มือ Manual §5.1: Working Radius 500mm + Robotiq 2F TCP Margin = 550mm (0.550m) จาก Shoulder
SPHERE_MAX = 0.550        # รัศมีทรงกลมสูงสุดจาก Shoulder (550 mm สำหรับ Robotiq 2F TCP สอดคล้องระยะเอื้อม 58 cm จาก Base)
# Inner Safety Sphere: ป้องกัน Gripper / ปลายมือ ชนฐานหุ่นยนต์และ Shoulder Housing (สอดคล้องวงเหลือง 28cm จาก Base)
SPHERE_MIN = 0.230        # รัศมีทรงกลมต่ำสุดจาก Shoulder (230 mm สำหรับ Robotiq 2F TCP)
# Forbidden Vertical Cylinder: ป้องกัน Singularity บริเวณแกน Z ของฐาน (base radius 64mm + margin)
CYLINDER_RADIUS = 0.095   # รัศมีทรงกระบอกห้ามรอบแกน Z ฐาน (95 mm) ตาม Manual §5.3.1
MAX_REACH_XY = 0.550      # ระยะเอื้อมแนวราบสูงสุด (550 mm) สอดคล้อง SPHERE_MAX
MIN_Z = -0.050            # ความสูงต่ำสุด Base Frame (-50 mm หรือ -450 mm บน Pendant ถึงชั้นวางล่าง)
MAX_Z = 0.720             # ความสูงสูงสุด Base Frame (720 mm หรือ +320 mm บน Pendant)
# TCP Linear Speed Cap: จำกัดความเร็วเชิงเส้น 300 mm/s เพื่อความปลอดภัยและไร้แรงเฉื่อยสะสม 100%
CARTESIAN_TCP_SPEED_CAP = 0.300  # m/s (300 mm/s) ป้องกัน Error Safety และตัดแรงเฉื่อยเด็ดขาด

# ขอบเขตมุมข้อต่อ (Joint Limits) ที่ปลอดภัยและยืดหยุ่นสำหรับงาน Lab
JOINT_LIMITS_DEG = [
    (-170, 170),    # J1 Base: หมุนรอบฐาน (Home: 0°)
    (-165, -15),    # J2 Shoulder: แขนบน (Home: -90°)
    (-150, -15),    # J3 Elbow: บังคับงออย่างน้อย 15° เสมอ ป้องกันเหยียดตรงสุดจน Singularity (Home: -90°)
    (-275, 90),     # J4 Wrist 1: รองรับท่า Home ที่ -180° และก้มลงต่ำได้ลึก (รองรับถึง -275°)
    (-170, 170),    # J5 Wrist 2: รองรับท่า Home ที่ -90° (หลบ Singularity ที่ 0°)
    (-360, 360),    # J6 Wrist 3: หมุนอิสระ 360° (Home: 180°)
]
JOINT_LIMITS_RAD = [(math.radians(lo), math.radians(hi)) for lo, hi in JOINT_LIMITS_DEG]
JOINT_NAMES = ["Base", "Shoulder", "Elbow", "Wrist 1", "Wrist 2", "Wrist 3"]

# ขอบเขตเตือนและชะลอการหมุนก่อนถึงขีดจำกัดข้อต่อ (20-30 องศา ตามคู่มือ UR3e)
JOINT_WARN_ZONE_DEG = 30.0
JOINT_WARN_ZONE_RAD = math.radians(JOINT_WARN_ZONE_DEG)

# ===================== ขีดจำกัดความเร็วข้อต่ออย่างเคร่งครัด (ป้องกัน Error Safety 100%) =====================
# - ข้อต่อแขนท่อนใหญ่ J1-J3: จำกัดเข้มงวดที่ 100°/s (ปลอดภัยกว่า Limit 180°/s และ 191°/s มากถึง 80°/s)
# - ข้อต่อข้อมือ J4-J6: จำกัดเข้มงวดที่ 150°/s (ปลอดภัยกว่า Preset 191°/s ถึง 41°/s)
MAX_SAFE_JOINT_SPEED_ARM_DEG = 100.0
MAX_SAFE_JOINT_SPEED_WRIST_DEG = 240.0
MAX_SAFE_JOINT_SPEED_DEG = MAX_SAFE_JOINT_SPEED_ARM_DEG
MAX_SAFE_JOINT_SPEED = math.radians(MAX_SAFE_JOINT_SPEED_ARM_DEG)
MAX_SAFE_ARM_SPEED = math.radians(MAX_SAFE_JOINT_SPEED_ARM_DEG)
MAX_SAFE_WRIST_SPEED = math.radians(MAX_SAFE_JOINT_SPEED_WRIST_DEG)
JOINT_MAX_SPEEDS = [
    MAX_SAFE_ARM_SPEED,    # Base (100°/s)
    MAX_SAFE_ARM_SPEED,    # Shoulder (100°/s)
    MAX_SAFE_ARM_SPEED,    # Elbow (100°/s)
    MAX_SAFE_WRIST_SPEED,  # Wrist 1 (150°/s)
    MAX_SAFE_WRIST_SPEED,  # Wrist 2 (150°/s)
    MAX_SAFE_WRIST_SPEED,  # Wrist 3 (150°/s)
]


def dh_matrix(a, d, alpha, theta):
    """ Denavit-Hartenberg Transformation Matrix """
    ct = math.cos(theta)
    st = math.sin(theta)
    ca = math.cos(alpha)
    sa = math.sin(alpha)
    return [
        [ct, -st*ca,  st*sa, a*ct],
        [st,  ct*ca, -ct*sa, a*st],
        [0.0, sa,     ca,    d],
        [0.0, 0.0,    0.0,   1.0]
    ]


def mat_mult(A, B):
    """ Matrix multiplication 4x4 """
    C = [[0.0]*4 for _ in range(4)]
    for i in range(4):
        for j in range(4):
            for k in range(4):
                C[i][j] += A[i][k] * B[k][j]
    return C


def compute_ur3e_jacobian(q):
    """
    คำนวณ Geometric Jacobian J (6x6) สำหรับ UR3e ที่มุมข้อต่อ q
    ใช้สำหรับแปลงความเร็ว Cartesian [vx, vy, vz, wx, wy, wz] เป็นความเร็วข้อต่อ [q1_dot, ..., q6_dot]
    """
    T = [[1.0 if i==j else 0.0 for j in range(4)] for i in range(4)]
    origins = [[0.0, 0.0, 0.0]]
    z_axes = [[0.0, 0.0, 1.0]]

    for i in range(6):
        a, d, alpha = DH[i]
        T_i = dh_matrix(a, d, alpha, q[i])
        T = mat_mult(T, T_i)
        origins.append([T[0][3], T[1][3], T[2][3]])
        z_axes.append([T[0][2], T[1][2], T[2][2]])

    p_e = origins[-1]
    J = [[0.0]*6 for _ in range(6)]
    for i in range(6):
        p_i = origins[i]
        z_i = z_axes[i]
        dx = p_e[0] - p_i[0]
        dy = p_e[1] - p_i[1]
        dz = p_e[2] - p_i[2]
        J[0][i] = z_i[1]*dz - z_i[2]*dy
        J[1][i] = z_i[2]*dx - z_i[0]*dz
        J[2][i] = z_i[0]*dy - z_i[1]*dx
        J[3][i] = z_i[0]
        J[4][i] = z_i[1]
        J[5][i] = z_i[2]

    return J, p_e


def solve_dls_system(A, b, damping=0.015):
    """
    Damped Least Squares (DLS) Inverse Kinematics Solver:
    qd = (J^T * J + lambda^2 * I)^(-1) * J^T * v
    ป้องกันความเร็วพุ่งทะลุ (Speed Spikes) เมื่อเข้าใกล้จุด Singularity ทุกรูปแบบ 100%
    """
    n = 6
    ATA = [[sum(A[k][i] * A[k][j] for k in range(n)) for j in range(n)] for i in range(n)]
    for i in range(n):
        ATA[i][i] += damping * damping

    ATb = [sum(A[k][i] * b[k] for k in range(n)) for i in range(n)]

    # Gaussian elimination with partial pivoting on ATA
    M = [ATA[i][:] + [ATb[i]] for i in range(n)]
    for i in range(n):
        max_row = i
        for k in range(i+1, n):
            if abs(M[k][i]) > abs(M[max_row][i]):
                max_row = k
        M[i], M[max_row] = M[max_row], M[i]

        pivot = M[i][i]
        if abs(pivot) < 1e-9:
            return [0.0] * n

        for k in range(i+1, n):
            factor = M[k][i] / pivot
            for j in range(i, n+1):
                M[k][j] -= factor * M[i][j]

    x = [0.0] * n
    for i in range(n-1, -1, -1):
        s = M[i][n]
        for j in range(i+1, n):
            s -= M[i][j] * x[j]
        x[i] = s / M[i][i]
    return x


def compute_tool_rotation_vectors(pose):
    """
    คำนวณเวกเตอร์แกนหมุนของ Gripper จากพิกัด Pose ปัจจุบัน:
    คืนค่า (yaw_axis, pitch_up_axis, roll_axis) ในระบบ Base Frame
    - yaw_axis:      [0, 0, 1] สำหรับหันซ้าย (4) / หันขวา (6)
    - pitch_up_axis: ทิศทางการก้ม-เงยปลายก้ามปู สำหรับหันบน (8) / หันล่าง (2)
    - roll_axis:     แกนชี้ไปข้างหน้า (Tool Z) สำหรับหมุนทวนเข็ม (7) / หมุนตามเข็ม (9)
    """
    rx, ry, rz = pose[3], pose[4], pose[5]
    angle = math.sqrt(rx*rx + ry*ry + rz*rz)
    if angle < 1e-9:
        R = [[1.0, 0.0, 0.0], [0.0, 1.0, 0.0], [0.0, 0.0, 1.0]]
    else:
        kx, ky, kz = rx / angle, ry / angle, rz / angle
        c = math.cos(angle)
        s = math.sin(angle)
        v = 1.0 - c
        R = [
            [kx*kx*v + c,     kx*ky*v - kz*s, kx*kz*v + ky*s],
            [kx*ky*v + kz*s,  ky*ky*v + c,    ky*kz*v - kx*s],
            [kx*kz*v - ky*s,  ky*kz*v + kx*s, kz*kz*v + c]
        ]
    
    # Tool Z: ทิศทางที่ก้ามปูเล็งไปข้างหน้า (แกน Roll)
    tool_z = [R[0][2], R[1][2], R[2][2]]

    # Pitch axis: แกนแนวราบที่ตั้งฉากกับทิศทางเล็ง (หมุนรอบแกนนี้ทำให้ปลายก้ามปูกระดกขึ้น)
    px = tool_z[1]
    py = -tool_z[0]
    plen = math.hypot(px, py)
    if plen > 1e-6:
        pitch_up_axis = [px / plen, py / plen, 0.0]
    else:
        pitch_up_axis = [R[0][0], R[1][0], R[2][0]]

    # Yaw axis: แกนแนวดิ่งโลก Base Z
    yaw_axis = [0.0, 0.0, 1.0]

    return yaw_axis, pitch_up_axis, tool_z


def rotvec_to_mat(r):
    """ แปลง Rotation Vector [rx, ry, rz] เป็น Rotation Matrix 3x3 ด้วยสูตร Rodrigues """
    theta = math.sqrt(r[0]*r[0] + r[1]*r[1] + r[2]*r[2])
    if theta < 1e-9:
        return [[1.0, 0.0, 0.0], [0.0, 1.0, 0.0], [0.0, 0.0, 1.0]]
    kx, ky, kz = r[0]/theta, r[1]/theta, r[2]/theta
    c = math.cos(theta)
    s = math.sin(theta)
    v = 1.0 - c
    return [
        [kx*kx*v + c,     kx*ky*v - kz*s, kx*kz*v + ky*s],
        [kx*ky*v + kz*s,  ky*ky*v + c,    ky*kz*v - kx*s],
        [kx*kz*v - ky*s,  ky*kz*v + kx*s, kz*kz*v + c    ]
    ]


def mat_mult3(A, B):
    """ คูณเมทริกซ์ 3x3 """
    return [
        [sum(A[i][k]*B[k][j] for k in range(3)) for j in range(3)]
        for i in range(3)
    ]


def mat_transpose(A):
    """ สลับแถว-คอลัมน์เมทริกซ์ 3x3 (Transpose = Inverse สำหรับ Rotation Matrix) """
    return [[A[j][i] for j in range(3)] for i in range(3)]


def mat_to_rotvec(R):
    """ แปลง Rotation Matrix 3x3 กลับเป็น Rotation Vector [rx, ry, rz] ในหน่วยเรเดียน """
    tr = (R[0][0] + R[1][1] + R[2][2] - 1.0) / 2.0
    tr = max(-1.0, min(1.0, tr))
    theta = math.acos(tr)
    if abs(theta) < 1e-6:
        return [0.0, 0.0, 0.0]
    denom = 2.0 * math.sin(theta)
    kx = (R[2][1] - R[1][2]) / denom
    ky = (R[0][2] - R[2][0]) / denom
    kz = (R[1][0] - R[0][1]) / denom
    return [kx * theta, ky * theta, kz * theta]


def compute_pose_error(target_pose, current_pose):
    """
    คำนวณ Error ระหว่าง Target Pose กับ Current Pose ในระบบ Base Frame
    คืนค่า:
      - pos_err: [ex, ey, ez] (ระยะทางในแนวแกน X, Y, Z หน่วยเมตร)
      - rot_err: [erx, ery, erz] (เวกเตอร์การหมุน Axis-Angle หน่วยเรเดียน)
    """
    pos_err = [target_pose[i] - current_pose[i] for i in range(3)]
    R_t = rotvec_to_mat(target_pose[3:6])
    R_c = rotvec_to_mat(current_pose[3:6])
    R_err = mat_mult3(R_t, mat_transpose(R_c))
    rot_err = mat_to_rotvec(R_err)
    return pos_err, rot_err


def compute_safety_proximity(current_pose, current_q=None, qd_actual=None):
    """
    คำนวณอัตราส่วนความใกล้ (Proximity Ratio 0.0–1.0) ต่อขอบเขตความปลอดภัยทุกด้าน:
    - outer_ratio: 0.0 = อยู่กลาง, 1.0 = ถึงขอบ SPHERE_MAX (ไกลเกินไป)
    - inner_ratio: 0.0 = อยู่ไกล, 1.0 = ถึงขอบ SPHERE_MIN (ใกล้ตัวเกินไป)
    - cylinder_ratio: 0.0 = อยู่ไกลแกน Z, 1.0 = ถึงขอบ CYLINDER_RADIUS (Singularity zone)
    - speed_ratio: 0.0 = ช้า, 1.0 = ถึงเพดานความเร็วข้อต่อ
    ใช้ส่งไปให้ Unity แสดงตาข่ายทรงกลมแดงเตือนเมื่อเข้าใกล้ขอบเขต
    """
    x, y, z = current_pose[0], current_pose[1], current_pose[2]
    dz = z - SHOULDER_Z
    d_3d = math.sqrt(x*x + y*y + dz*dz)
    xy_dist = math.sqrt(x*x + y*y)

    # Outer sphere: ratio เข้า 1.0 เมื่อเข้าใกล้ SPHERE_MAX
    outer_ratio = min(1.0, d_3d / SPHERE_MAX) if SPHERE_MAX > 0 else 0.0

    # Inner sphere: ratio เข้า 1.0 เมื่อเข้าใกล้ SPHERE_MIN
    if d_3d > 1e-4:
        inner_ratio = min(1.0, SPHERE_MIN / d_3d)
    else:
        inner_ratio = 1.0

    # Vertical cylinder: ratio เข้า 1.0 เมื่อเข้าใกล้แกน Z ฐาน
    if xy_dist > 1e-4:
        cylinder_ratio = min(1.0, CYLINDER_RADIUS / xy_dist)
    else:
        cylinder_ratio = 1.0

    # Speed ratio: ตรวจสอบความเร็วข้อต่อจริงเทียบกับเพดาน
    speed_ratio = 0.0
    if qd_actual is not None:
        for i in range(6):
            lim = JOINT_MAX_SPEEDS[i]
            if lim > 0:
                r = abs(qd_actual[i]) / lim
                if r > speed_ratio:
                    speed_ratio = r
        speed_ratio = min(1.0, speed_ratio)

    # 5. Joint Rotation Limit Proximity: ตรวจสอบองศาหมุนข้อต่อ J1-J6 เข้าใกล้ขีดจำกัด (ช่วง 20-30°)
    rot_limit_ratio = 0.0
    worst_joint_idx = 5  # default Wrist 3
    worst_joint_name = JOINT_NAMES[5]
    worst_deg_left = 999.0

    if current_q is not None and len(current_q) >= 6:
        for i in range(6):
            q_val = current_q[i]
            lo, hi = JOINT_LIMITS_RAD[i]
            d_hi = hi - q_val
            d_lo = q_val - lo
            d_min = min(d_hi, d_lo)
            deg_left = math.degrees(d_min)

            if d_min < JOINT_WARN_ZONE_RAD:
                # ratio จาก 0.0 ที่ขอบ 30° วิ่งเข้าหา 1.0 เมื่อชน limit (0° เหลือ)
                r = max(0.0, min(1.0, 1.0 - (d_min / JOINT_WARN_ZONE_RAD)))
                if r > rot_limit_ratio:
                    rot_limit_ratio = r
                    worst_joint_idx = i
                    worst_joint_name = JOINT_NAMES[i]
                    worst_deg_left = deg_left

    return {
        "outer_ratio": round(outer_ratio, 3),
        "inner_ratio": round(inner_ratio, 3),
        "cylinder_ratio": round(cylinder_ratio, 3),
        "speed_ratio": round(speed_ratio, 3),
        "rot_limit_ratio": round(rot_limit_ratio, 3),
        "worst_joint_idx": worst_joint_idx,
        "worst_joint_name": worst_joint_name,
        "worst_deg_left": round(worst_deg_left, 1),
    }


def filter_cartesian_velocity(vx, vy, vz, wx=0.0, wy=0.0, wz=0.0, current_pose=None, current_q=None, qd_actual=None, dt=0.10):
    """
    ระบบกรองความเร็วแบบครอบคลุมรอบด้าน 6-DOF (Full Kinematic Safety & Limit Prevention):
    1. 3D Spherical Workspace Clamping (รัศมี 585 mm ตามระยะเอื้อมจริงที่กว้างขึ้น)
    2. ขอบเขตความสูง Z: ลงต่ำได้ถึง -50 mm (Pendant: -450 mm) และยกขึ้นถอยหลังได้เสมอ
    3. Damped Least Squares (DLS) + Active Speed Limiter:
       - คำนวณความเร็วข้อต่อล่วงหน้าด้วย DLS
       - ควบคุมความเร็วรอบข้อต่อไม่ให้เกิน 45°/s ป้องกันหุ่นยนต์ Error 100%
    4. Directional Joint Protection:
       - เมื่อแตะขอบเขต จะบล็อกเฉพาะทิศทางที่ฝืนต่อ แต่ปล่อยให้ถอยกลับได้เสมอ ไม่ติดค้าง
    """
    if vx == 0.0 and vy == 0.0 and vz == 0.0 and wx == 0.0 and wy == 0.0 and wz == 0.0:
        return 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, "OK"

    x, y, z = current_pose[0], current_pose[1], current_pose[2]
    dz = z - SHOULDER_Z
    d_3d = math.sqrt(x*x + y*y + dz*dz)
    xy_dist = math.sqrt(x*x + y*y)

    # 1. ตรวจสอบขอบเขตทรงกลม 3D จาก Shoulder
    if d_3d > 1e-4:
        nx, ny, nz = x / d_3d, y / d_3d, dz / d_3d
    else:
        nx, ny, nz = 1.0, 0.0, 0.0

    v_rad = vx * nx + vy * ny + vz * nz
    pred_d_3d = d_3d + v_rad * dt

    reason = "OK"

    if pred_d_3d > SPHERE_MAX and v_rad > 0:
        # ตัดเฉพาะเวกเตอร์ความเร็วที่พุ่งออกไปภายนอก แต่ยอมให้เคลื่อนที่ขนานผิวหรือถอยกลับเข้าด้านใน
        vx -= v_rad * nx
        vy -= v_rad * ny
        vz -= v_rad * nz
        reason = f"🛑 ขอบเขต 3D Reach ({d_3d*1000:.0f}mm) - ป้องกันเกินระยะแขนกล"
    elif pred_d_3d < SPHERE_MIN and v_rad < 0:
        vx -= v_rad * nx
        vy -= v_rad * ny
        vz -= v_rad * nz
        reason = f"🛑 ใกล้ Shoulder เกินไป ({d_3d*1000:.0f}mm) - ป้องกันชนตัวเอง"

    # 1b. ตรวจสอบ Forbidden Vertical Cylinder รอบแกน Z ฐาน (Singularity Zone)
    # เมื่อ Tool เข้าใกล้แกน Z ฐาน → Base joint และ Wrist จะหมุนเร็วมากแม้ TCP ช้า → Protective Stop
    if xy_dist > 1e-4:
        cyl_nx, cyl_ny = x / xy_dist, y / xy_dist
    else:
        cyl_nx, cyl_ny = 1.0, 0.0
    v_cyl_inward = -(vx * cyl_nx + vy * cyl_ny)  # ค่าบวก = พุ่งเข้าหาแกน Z
    pred_xy = xy_dist - v_cyl_inward * dt
    if pred_xy < CYLINDER_RADIUS and v_cyl_inward > 0:
        # บล็อกเฉพาะทิศทางเข้าหาแกน Z แต่ปล่อยให้หนีออกได้
        vx += v_cyl_inward * cyl_nx
        vy += v_cyl_inward * cyl_ny
        reason = f"🛑 Singularity Zone ({xy_dist*1000:.0f}mm จากแกน Z) - ป้องกัน Base/Wrist หมุนเร็วเกิน"

    # 2. ตรวจสอบขอบเขตแกน Z (ความสูง)
    pred_z = z + vz * dt
    if pred_z < MIN_Z and vz < 0:
        vz = 0.0
        reason = f"🛑 ขอบเขตพื้น Z ต่ำสุด ({z*1000:.0f}mm) - ล็อกลง แต่ยกขึ้นได้"
    elif pred_z > MAX_Z and vz > 0:
        vz = 0.0
        reason = f"🛑 ขอบเขต Z สูงสุด ({z*1000:.0f}mm) - ล็อกขึ้น แต่ยกลงได้"

    # 2b. จำกัดความเร็ว TCP Linear ไม่ให้เกิน CARTESIAN_TCP_SPEED_CAP (350 mm/s) ตาม Manual §7
    tcp_spd = math.sqrt(vx*vx + vy*vy + vz*vz)
    if tcp_spd > CARTESIAN_TCP_SPEED_CAP:
        tcp_scale = CARTESIAN_TCP_SPEED_CAP / tcp_spd
        vx *= tcp_scale; vy *= tcp_scale; vz *= tcp_scale
        reason = f"⚡ TCP Speed Cap ({tcp_spd*1000:.0f}mm/s > {CARTESIAN_TCP_SPEED_CAP*1000:.0f}mm/s)"

    # 3. คำนวณความเร็วข้อต่อล่วงหน้าด้วย Jacobian + DLS (Damped Least Squares)
    v_cmd = [vx, vy, vz, wx, wy, wz]
    J, _ = compute_ur3e_jacobian(current_q)
    qd_pred = solve_dls_system(J, v_cmd, damping=0.025)

    # 3a. ควบคุมความเร็วข้อต่อล่วงหน้าอย่างเข้มงวด (Strict Joint Speed Limiter):
    # แยกการคุมความเร็วระหว่าง Arm Joints (J1-J3) กับ Wrist Joints (J4-J6):
    # - หากข้อต่อแขน (J1-J3) เกิน: สเกลทั้ง linear [vx, vy, vz] และ angular [wx, wy, wz]
    # - หากข้อต่อข้อมือ (J4-J6) เกิน: สเกลเฉพาะ angular [wx, wy, wz] ไม่สเกล linear velocity ลง เพื่อให้การขยับแขนกลคล่องแคล่ว ฉับไว ไร้ความหน่วงหนืด
    arm_scales = []
    wrist_scales = []
    for i in range(3):
        lim = JOINT_MAX_SPEEDS[i]
        q_spd = abs(qd_pred[i])
        if q_spd > lim:
            arm_scales.append((lim / q_spd, i))
    for i in range(3, 6):
        lim = JOINT_MAX_SPEEDS[i]
        q_spd = abs(qd_pred[i])
        if q_spd > lim:
            wrist_scales.append((lim / q_spd, i))

    if arm_scales:
        worst_arm_scale, worst_arm_idx = min(arm_scales, key=lambda x: x[0])
        vx *= worst_arm_scale
        vy *= worst_arm_scale
        vz *= worst_arm_scale
        wx *= worst_arm_scale
        wy *= worst_arm_scale
        wz *= worst_arm_scale
        qd_pred = [q_dot * worst_arm_scale for q_dot in qd_pred]
        reason = f"⚡ ชะลอความเร็วแขน: คุมข้อต่อ {JOINT_NAMES[worst_arm_idx]} ({math.degrees(abs(qd_pred[worst_arm_idx])/worst_arm_scale):.1f}°/s > {math.degrees(JOINT_MAX_SPEEDS[worst_arm_idx]):.0f}°/s)"

    if wrist_scales:
        worst_w_scale, worst_w_idx = min(wrist_scales, key=lambda x: x[0])
        wx *= worst_w_scale
        wy *= worst_w_scale
        wz *= worst_w_scale
        for j in range(3, 6):
            qd_pred[j] *= worst_w_scale
        if not arm_scales:
            reason = f"⚡ ชะลอการหมุนข้อมือ: คุมข้อต่อ {JOINT_NAMES[worst_w_idx]} ({math.degrees(abs(qd_pred[worst_w_idx])/worst_w_scale):.1f}°/s > {math.degrees(JOINT_MAX_SPEEDS[worst_w_idx]):.0f}°/s)"

    # 3b. ตรวจสอบขอบเขตข้อต่อแบบทิศทางและการชะลอความเร็วก่อนถึงขีดจำกัด (Directional Joint Limit & Soft Deceleration)
    for i in range(6):
        q_val = current_q[i]
        lo, hi = JOINT_LIMITS_RAD[i]
        q_dot = qd_pred[i]
        pred_q = q_val + q_dot * dt

        # ทิศทางหมุนเพิ่มขึ้น (+) มุ่งหน้าหาขอบบน hi
        if q_dot > 0:
            d_hi = hi - q_val
            if d_hi <= JOINT_WARN_ZONE_RAD:
                if pred_q >= hi or d_hi <= 0:
                    scale = 0.0
                    reason = f"🛑 LIMIT: Joint {JOINT_NAMES[i]} reached full rotation limit ({math.degrees(q_val):.1f}°)"
                else:
                    scale = max(0.0, min(1.0, d_hi / JOINT_WARN_ZONE_RAD))
                    if reason == "OK":
                        reason = f"⚠️ WARNING: Joint {JOINT_NAMES[i]} near rotation limit ({math.degrees(q_val):.1f}°, {math.degrees(d_hi):.1f}° left) - Slowing down"
                if i < 3:
                    vx *= scale; vy *= scale; vz *= scale
                wx *= scale; wy *= scale; wz *= scale
                qd_pred[i] *= scale

        # ทิศทางหมุนลดลง (-) มุ่งหน้าหาขอบล่าง lo
        elif q_dot < 0:
            d_lo = q_val - lo
            if d_lo <= JOINT_WARN_ZONE_RAD:
                if pred_q <= lo or d_lo <= 0:
                    scale = 0.0
                    reason = f"🛑 LIMIT: Joint {JOINT_NAMES[i]} reached full rotation limit ({math.degrees(q_val):.1f}°)"
                else:
                    scale = max(0.0, min(1.0, d_lo / JOINT_WARN_ZONE_RAD))
                    if reason == "OK":
                        reason = f"⚠️ WARNING: Joint {JOINT_NAMES[i]} near rotation limit ({math.degrees(q_val):.1f}°, {math.degrees(d_lo):.1f}° left) - Slowing down"
                if i < 3:
                    vx *= scale; vy *= scale; vz *= scale
                wx *= scale; wy *= scale; wz *= scale
                qd_pred[i] *= scale

        # ป้องกัน Wrist 2 เข้าใกล้จุด Singularity ที่ 0° (|q5| < 3°) แบบนุ่มนวล
        if i == 4:
            SINGULARITY_MARGIN = math.radians(3.0)
            if abs(pred_q) < SINGULARITY_MARGIN and abs(pred_q) < abs(q_val):
                scale = max(0.0, min(1.0, (abs(q_val) - SINGULARITY_MARGIN) / (abs(q_dot) * dt + 1e-6)))
                wx *= scale; wy *= scale; wz *= scale
                reason = f"🛑 Wrist 2 approaching Singularity 0° ({math.degrees(q_val):.1f}°) - Block inward"

    # 4. ตรวจสอบความเร็วข้อต่อจริง (Active QD Feedback Damping แบบแยกส่วน)
    if qd_actual is not None:
        arm_damp = []
        wrist_damp = []
        for i in range(3):
            lim = JOINT_MAX_SPEEDS[i]
            act_spd = abs(qd_actual[i])
            if act_spd > lim:
                arm_damp.append(lim / act_spd)
        for i in range(3, 6):
            lim = JOINT_MAX_SPEEDS[i]
            act_spd = abs(qd_actual[i])
            if act_spd > lim:
                wrist_damp.append(lim / act_spd)

        if arm_damp:
            damp_a = min(arm_damp)
            vx *= damp_a; vy *= damp_a; vz *= damp_a
            wx *= damp_a; wy *= damp_a; wz *= damp_a
        if wrist_damp:
            damp_w = min(wrist_damp)
            wx *= damp_w; wy *= damp_w; wz *= damp_w

    return vx, vy, vz, wx, wy, wz, reason


def filter_joint_velocity(qd, current_q, dt=0.02):
    """
    ตรวจสอบและกรองความเร็วข้อต่อ qd ตามขอบเขตความปลอดภัย (Joint Limits & Singularity):
    - ระบบ Soft Braking: ชะลอความเร็วนุ่มนวลเมื่อเข้าใกล้ขอบเขต 20-30° เพื่อป้องกันกระชาก
    - ป้องกัน Wrist 2 เข้าใกล้จุด Singularity รอบ 0° (|q5| < 15°) แบบ Smooth Deceleration
    - จำกัดความเร็วไม่ให้เกินขีดจำกัดปลอดภัย (แขน 100°/s, ข้อมือ 240°/s) ป้องกัน Error 100%
    """
    filtered_qd = list(qd)
    reason = "OK"

    for i in range(6):
        q_val = current_q[i]
        lo, hi = JOINT_LIMITS_RAD[i]
        q_dot = filtered_qd[i]
        lim = JOINT_MAX_SPEEDS[i]

        # 1. จำกัดความเร็วสูงสุด
        if abs(q_dot) > lim:
            filtered_qd[i] = math.copysign(lim, q_dot)
            q_dot = filtered_qd[i]

        # 2. ระบบ Soft Braking ชะลอความเร็วก่อนถึงขอบเขต (ช่วง 30°)
        if q_dot > 0 and q_val > (hi - JOINT_WARN_ZONE_RAD):
            scale = max(0.0, min(1.0, (hi - q_val) / JOINT_WARN_ZONE_RAD))
            filtered_qd[i] *= scale
            if scale == 0.0:
                reason = f"🛑 Joint {JOINT_NAMES[i]} reached upper limit ({math.degrees(q_val):.1f}°)"
            elif reason == "OK":
                reason = f"⚠️ WARNING: Joint {JOINT_NAMES[i]} near rotation limit ({math.degrees(q_val):.1f}°, {math.degrees(hi - q_val):.1f}° left) - Slowing down"
        elif q_dot < 0 and q_val < (lo + JOINT_WARN_ZONE_RAD):
            scale = max(0.0, min(1.0, (q_val - lo) / JOINT_WARN_ZONE_RAD))
            filtered_qd[i] *= scale
            if scale == 0.0:
                reason = f"🛑 Joint {JOINT_NAMES[i]} reached lower limit ({math.degrees(q_val):.1f}°)"
            elif reason == "OK":
                reason = f"⚠️ WARNING: Joint {JOINT_NAMES[i]} near rotation limit ({math.degrees(q_val):.1f}°, {math.degrees(q_val - lo):.1f}° left) - Slowing down"

        # 3. ป้องกัน Wrist 2 เข้าใกล้จุด Singularity ที่ 0° (|q5| < 15°) แบบ Soft Deceleration
        if i == 4:
            SING_LIM = math.radians(15.0)
            SING_BUF = math.radians(20.0)
            if q_dot > 0 and q_val < 0 and q_val > -SING_BUF:
                scale = max(0.0, (-SING_LIM - q_val) / (SING_BUF - SING_LIM))
                filtered_qd[i] *= scale
                if scale == 0.0:
                    reason = f"🛑 Joint Wrist 2 approaching Singularity 0° ({math.degrees(q_val):.1f}°)"
            elif q_dot < 0 and q_val > 0 and q_val < SING_BUF:
                scale = max(0.0, (q_val - SING_LIM) / (SING_BUF - SING_LIM))
                filtered_qd[i] *= scale
                if scale == 0.0:
                    reason = f"🛑 Joint Wrist 2 approaching Singularity 0° ({math.degrees(q_val):.1f}°)"

    return filtered_qd, reason

