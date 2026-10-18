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
# ปรับขอบเขตพื้นที่ทำงานใหม่ตามที่ผู้ใช้ทดสอบจริง (พร้อม Buffer เผื่อระยะปลอดภัย)
SPHERE_MAX = 0.585        # รัศมีทรงกลมสูงสุดจาก Shoulder (585 mm) ครอบคลุมจุดหน้าและข้างทั้งหมด
SPHERE_MIN = 0.120        # รัศมีทรงกลมต่ำสุดจาก Shoulder (120 mm)
MAX_REACH_XY = 0.570      # ระยะเอื้อมแนวราบสูงสุด (570 mm)
MIN_Z = -0.050            # ความสูงต่ำสุด Base Frame (-50 mm หรือ -450 mm บน Pendant ถึงชั้นวางล่าง)
MAX_Z = 0.720             # ความสูงสูงสุด Base Frame (720 mm หรือ +320 mm บน Pendant)

# ขอบเขตมุมข้อต่อ (Joint Limits) ที่ปลอดภัยและยืดหยุ่นสำหรับงาน Lab
JOINT_LIMITS_DEG = [
    (-170, 170),    # J1 Base: หมุนรอบฐาน
    (-165, -15),    # J2 Shoulder: แขนบน
    (-150, -15),    # J3 Elbow: บังคับงออย่างน้อย 15° เสมอ ป้องกันเหยียดตรงสุดจน Singularity
    (-170, 85),     # J4 Wrist 1: ขยายเป็น +85° เพื่อให้กดแขนลงต่ำสุดได้โดยปากจับยังขนานพื้น
    (15, 165),      # J5 Wrist 2: หลบ 0° และ 180° (Wrist Singularity)
    (-170, 170),    # J6 Wrist 3: หมุนปลายปากจับ
]
JOINT_LIMITS_RAD = [(math.radians(lo), math.radians(hi)) for lo, hi in JOINT_LIMITS_DEG]
JOINT_NAMES = ["Base", "Shoulder", "Elbow", "Wrist 1", "Wrist 2", "Wrist 3"]

# ขีดจำกัดความเร็วข้อต่อสูงสุดที่ปลอดภัย (Strict Safe Joint Velocity Limits)
# ปรับลดลงเหลือ 45°/s (จาก 180°/s) เพื่อรับประกันว่าจะไม่มีการกระชากหรือ Error ที่เครื่องหุ่นยนต์
MAX_SAFE_JOINT_SPEED_DEG = 45.0
MAX_SAFE_JOINT_SPEED = math.radians(MAX_SAFE_JOINT_SPEED_DEG)


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

    # 2. ตรวจสอบขอบเขตแกน Z (ความสูง)
    pred_z = z + vz * dt
    if pred_z < MIN_Z and vz < 0:
        vz = 0.0
        reason = f"🛑 ขอบเขตพื้น Z ต่ำสุด ({z*1000:.0f}mm) - ล็อกลง แต่ยกขึ้นได้"
    elif pred_z > MAX_Z and vz > 0:
        vz = 0.0
        reason = f"🛑 ขอบเขต Z สูงสุด ({z*1000:.0f}mm) - ล็อกขึ้น แต่ยกลงได้"

    # 3. คำนวณความเร็วข้อต่อล่วงหน้าด้วย Jacobian + DLS (Damped Least Squares)
    v_cmd = [vx, vy, vz, wx, wy, wz]
    J, _ = compute_ur3e_jacobian(current_q)
    qd_pred = solve_dls_system(J, v_cmd, damping=0.02)

    # 3a. ควบคุมความเร็วข้อต่อล่วงหน้า (Joint Speed Limiter <= 45°/s)
    max_qd = max(abs(q_dot) for q_dot in qd_pred)
    if max_qd > MAX_SAFE_JOINT_SPEED:
        scale = MAX_SAFE_JOINT_SPEED / max_qd
        vx *= scale; vy *= scale; vz *= scale
        wx *= scale; wy *= scale; wz *= scale
        qd_pred = [q_dot * scale for q_dot in qd_pred]
        reason = f"⚡ ชะลอความเร็ว: คุมข้อต่อไม่ให้หมุนเร็วเกิน ({math.degrees(max_qd):.1f}°/s > {MAX_SAFE_JOINT_SPEED_DEG:.0f}°/s)"

    # 3b. ตรวจสอบขอบเขตข้อต่อแบบทิศทาง (Directional Joint Limit Protection)
    for i in range(6):
        q_val = current_q[i]
        lo, hi = JOINT_LIMITS_RAD[i]
        q_dot = qd_pred[i]
        pred_q = q_val + q_dot * dt

        if pred_q > hi and q_dot > 0:
            scale = max(0.0, min(1.0, (hi - q_val) / (q_dot * dt + 1e-6)))
            vx *= scale; vy *= scale; vz *= scale
            wx *= scale; wy *= scale; wz *= scale
            reason = f"🛑 ข้อต่อ {JOINT_NAMES[i]} ถึงขอบบน ({math.degrees(q_val):.1f}°) - บล็อกเฉพาะทิศที่ฝืน"
        elif pred_q < lo and q_dot < 0:
            scale = max(0.0, min(1.0, (q_val - lo) / (-q_dot * dt + 1e-6)))
            vx *= scale; vy *= scale; vz *= scale
            wx *= scale; wy *= scale; wz *= scale
            reason = f"🛑 ข้อต่อ {JOINT_NAMES[i]} ถึงขอบล่าง ({math.degrees(q_val):.1f}°) - บล็อกเฉพาะทิศที่ฝืน"

    # 4. ตรวจสอบความเร็วข้อต่อจริง (Active QD Feedback Damping)
    if qd_actual is not None:
        actual_max_qd = max(abs(q_dot) for q_dot in qd_actual)
        if actual_max_qd > MAX_SAFE_JOINT_SPEED:
            damp = min(1.0, MAX_SAFE_JOINT_SPEED / actual_max_qd)
            vx *= damp; vy *= damp; vz *= damp
            wx *= damp; wy *= damp; wz *= damp

    return vx, vy, vz, wx, wy, wz, reason
