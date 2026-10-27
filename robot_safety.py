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

SHOULDER_Z = 0.1519       # Shoulder pivot height from base plane (m)
# ===================== Workspace Safety Boundaries (UR3e Manual §5.1, §5.3, §8.4) =====================
# Outer Safety Sphere: aligned with actual reach + Unity safety barrier (~58cm from base)
SPHERE_MAX = 0.550        # Max sphere radius from shoulder (550 mm, Robotiq 2F TCP)
# Inner Safety Sphere: prevent gripper collision with base/shoulder housing
SPHERE_MIN = 0.230        # Min sphere radius from shoulder (230 mm, Robotiq 2F TCP)
# Forbidden Vertical Cylinder: prevent singularity near base Z-axis
CYLINDER_RADIUS = 0.095   # Forbidden cylinder radius around base Z-axis (95 mm)
MAX_REACH_XY = 0.550      # Max horizontal reach (550 mm)
MIN_Z = -0.050            # Min Z height in base frame (-50 mm)
MAX_Z = 0.720             # Max Z height in base frame (720 mm)
# TCP Linear Speed Cap: limit to 300 mm/s for safety
CARTESIAN_TCP_SPEED_CAP = 0.300  # m/s (300 mm/s)

# Joint angle limits (safe range for lab operations)
JOINT_LIMITS_DEG = [
    (-170, 170),    # J1 Base (Home: 0°)
    (-165, -15),    # J2 Shoulder (Home: -90°)
    (-150, -15),    # J3 Elbow: min 15° bend to prevent singularity (Home: -90°)
    (-275, 90),     # J4 Wrist 1: supports Home at -180° (Home: -180°)
    (-170, 170),    # J5 Wrist 2: supports Home at -90° (avoids 0° singularity)
    (-360, 360),    # J6 Wrist 3: free 360° rotation (Home: 180°)
]
JOINT_LIMITS_RAD = [(math.radians(lo), math.radians(hi)) for lo, hi in JOINT_LIMITS_DEG]
JOINT_NAMES = ["Base", "Shoulder", "Elbow", "Wrist 1", "Wrist 2", "Wrist 3"]

# Warning zone before hitting joint limits (20-30° per UR3e manual)
JOINT_WARN_ZONE_DEG = 30.0
JOINT_WARN_ZONE_RAD = math.radians(JOINT_WARN_ZONE_DEG)

# ===================== Joint Speed Limits (prevent safety errors) =====================
# Arm joints J1-J3: capped at 100°/s (well below 180°/s hardware limit)
# Wrist joints J4-J6: capped at 240°/s (below 191°/s preset)
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
    MAX_SAFE_WRIST_SPEED,  # Wrist 1 (240°/s)
    MAX_SAFE_WRIST_SPEED,  # Wrist 2 (240°/s)
    MAX_SAFE_WRIST_SPEED,  # Wrist 3 (240°/s)
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
    Compute geometric Jacobian J (6x6) for UR3e at joint angles q.
    Used to convert Cartesian velocity [vx,vy,vz,wx,wy,wz] to joint velocity [q1_dot..q6_dot].
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
    Damped Least Squares (DLS) IK solver:
    qd = (J^T * J + lambda^2 * I)^(-1) * J^T * v
    Prevents speed spikes near singularities.
    """
    n = 6
    ATA = [[sum(A[k][i] * A[k][j] for k in range(n)) for j in range(n)] for i in range(n)]
    for i in range(n):
        ATA[i][i] += damping * damping

    ATb = [sum(A[k][i] * b[k] for k in range(n)) for i in range(n)]

    # Gaussian elimination with partial pivoting
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
    Compute gripper rotation axes from current pose.
    Returns (yaw_axis, pitch_up_axis, roll_axis) in base frame.
    - yaw_axis:      [0,0,1] for left/right rotation
    - pitch_up_axis: perpendicular to tool Z for tilt up/down
    - roll_axis:     tool Z axis for CW/CCW roll
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
    
    # Tool Z: direction gripper is pointing (roll axis)
    tool_z = [R[0][2], R[1][2], R[2][2]]

    # Pitch axis: horizontal axis perpendicular to tool direction
    px = tool_z[1]
    py = -tool_z[0]
    plen = math.hypot(px, py)
    if plen > 1e-6:
        pitch_up_axis = [px / plen, py / plen, 0.0]
    else:
        pitch_up_axis = [R[0][0], R[1][0], R[2][0]]

    # Yaw axis: world vertical (base Z)
    yaw_axis = [0.0, 0.0, 1.0]

    return yaw_axis, pitch_up_axis, tool_z


def rotvec_to_mat(r):
    """ Convert rotation vector [rx,ry,rz] to 3x3 rotation matrix (Rodrigues) """
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
    """ 3x3 matrix multiplication """
    return [
        [sum(A[i][k]*B[k][j] for k in range(3)) for j in range(3)]
        for i in range(3)
    ]


def mat_transpose(A):
    """ 3x3 matrix transpose (= inverse for rotation matrices) """
    return [[A[j][i] for j in range(3)] for i in range(3)]


def mat_to_rotvec(R):
    """ Convert 3x3 rotation matrix to rotation vector [rx,ry,rz] (radians) """
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
    Compute error between target and current pose in base frame.
    Returns:
      - pos_err: [ex, ey, ez] (meters)
      - rot_err: [erx, ery, erz] (axis-angle, radians)
    """
    pos_err = [target_pose[i] - current_pose[i] for i in range(3)]
    R_t = rotvec_to_mat(target_pose[3:6])
    R_c = rotvec_to_mat(current_pose[3:6])
    R_err = mat_mult3(R_t, mat_transpose(R_c))
    rot_err = mat_to_rotvec(R_err)
    return pos_err, rot_err


def compute_safety_proximity(current_pose, current_q=None, qd_actual=None):
    """
    Compute proximity ratios (0.0-1.0) to each safety boundary:
    - outer_ratio: 0.0 = center, 1.0 = at SPHERE_MAX (too far)
    - inner_ratio: 0.0 = far, 1.0 = at SPHERE_MIN (too close to base)
    - cylinder_ratio: 0.0 = far from Z-axis, 1.0 = at CYLINDER_RADIUS (singularity zone)
    - speed_ratio: 0.0 = slow, 1.0 = at joint speed limit
    Used by Unity to display safety boundary warnings.
    """
    x, y, z = current_pose[0], current_pose[1], current_pose[2]
    dz = z - SHOULDER_Z
    d_3d = math.sqrt(x*x + y*y + dz*dz)
    xy_dist = math.sqrt(x*x + y*y)

    # Outer sphere: approaches 1.0 near SPHERE_MAX
    outer_ratio = min(1.0, d_3d / SPHERE_MAX) if SPHERE_MAX > 0 else 0.0

    # Inner sphere: approaches 1.0 near SPHERE_MIN
    if d_3d > 1e-4:
        inner_ratio = min(1.0, SPHERE_MIN / d_3d)
    else:
        inner_ratio = 1.0

    # Vertical cylinder: approaches 1.0 near base Z-axis
    if xy_dist > 1e-4:
        cylinder_ratio = min(1.0, CYLINDER_RADIUS / xy_dist)
    else:
        cylinder_ratio = 1.0

    # Speed ratio: actual joint speed vs limit
    speed_ratio = 0.0
    if qd_actual is not None:
        for i in range(6):
            lim = JOINT_MAX_SPEEDS[i]
            if lim > 0:
                r = abs(qd_actual[i]) / lim
                if r > speed_ratio:
                    speed_ratio = r
        speed_ratio = min(1.0, speed_ratio)

    # 5. Joint rotation limit proximity (warning zone 20-30°)
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
                # ratio: 0.0 at 30° boundary, 1.0 at limit (0° remaining)
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
    Full 6-DOF velocity filter with kinematic safety & limit prevention:
    1. 3D spherical workspace clamping
    2. Z-axis height limits
    3. DLS + active joint speed limiter
    4. Directional joint protection (blocks only offending direction, allows retreat)
    """
    if vx == 0.0 and vy == 0.0 and vz == 0.0 and wx == 0.0 and wy == 0.0 and wz == 0.0:
        return 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, "OK"

    x, y, z = current_pose[0], current_pose[1], current_pose[2]
    dz = z - SHOULDER_Z
    d_3d = math.sqrt(x*x + y*y + dz*dz)
    xy_dist = math.sqrt(x*x + y*y)

    # 1. Check 3D sphere boundary from shoulder
    if d_3d > 1e-4:
        nx, ny, nz = x / d_3d, y / d_3d, dz / d_3d
    else:
        nx, ny, nz = 1.0, 0.0, 0.0

    v_rad = vx * nx + vy * ny + vz * nz
    pred_d_3d = d_3d + v_rad * dt

    reason = "OK"

    if pred_d_3d > SPHERE_MAX and v_rad > 0:
        # Block outward velocity, allow tangent/inward
        vx -= v_rad * nx
        vy -= v_rad * ny
        vz -= v_rad * nz
        reason = f"🛑 3D Reach limit ({d_3d*1000:.0f}mm) - blocked outward"
    elif pred_d_3d < SPHERE_MIN and v_rad < 0:
        vx -= v_rad * nx
        vy -= v_rad * ny
        vz -= v_rad * nz
        reason = f"🛑 Too close to shoulder ({d_3d*1000:.0f}mm) - blocked inward"

    # 1b. Forbidden vertical cylinder (singularity zone around base Z-axis)
    if xy_dist > 1e-4:
        cyl_nx, cyl_ny = x / xy_dist, y / xy_dist
    else:
        cyl_nx, cyl_ny = 1.0, 0.0
    v_cyl_inward = -(vx * cyl_nx + vy * cyl_ny)  # positive = moving toward Z-axis
    pred_xy = xy_dist - v_cyl_inward * dt
    if pred_xy < CYLINDER_RADIUS and v_cyl_inward > 0:
        # Block inward only, allow escape
        vx += v_cyl_inward * cyl_nx
        vy += v_cyl_inward * cyl_ny
        reason = f"🛑 Singularity zone ({xy_dist*1000:.0f}mm from Z-axis) - blocked inward"

    # 2. Z-axis height limits
    pred_z = z + vz * dt
    if pred_z < MIN_Z and vz < 0:
        vz = 0.0
        reason = f"🛑 Min Z limit ({z*1000:.0f}mm) - blocked downward"
    elif pred_z > MAX_Z and vz > 0:
        vz = 0.0
        reason = f"🛑 Max Z limit ({z*1000:.0f}mm) - blocked upward"

    # 2b. TCP linear speed cap
    tcp_spd = math.sqrt(vx*vx + vy*vy + vz*vz)
    if tcp_spd > CARTESIAN_TCP_SPEED_CAP:
        tcp_scale = CARTESIAN_TCP_SPEED_CAP / tcp_spd
        vx *= tcp_scale; vy *= tcp_scale; vz *= tcp_scale
        reason = f"⚡ TCP Speed Cap ({tcp_spd*1000:.0f}mm/s > {CARTESIAN_TCP_SPEED_CAP*1000:.0f}mm/s)"

    # 3. Predictive joint speed check via Jacobian + DLS
    v_cmd = [vx, vy, vz, wx, wy, wz]
    J, _ = compute_ur3e_jacobian(current_q)
    qd_pred = solve_dls_system(J, v_cmd, damping=0.025)

    # 3a. Joint speed limiter (separate arm vs wrist scaling)
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
        reason = f"⚡ Arm speed limited: {JOINT_NAMES[worst_arm_idx]} ({math.degrees(abs(qd_pred[worst_arm_idx])/worst_arm_scale):.1f}°/s > {math.degrees(JOINT_MAX_SPEEDS[worst_arm_idx]):.0f}°/s)"

    if wrist_scales:
        worst_w_scale, worst_w_idx = min(wrist_scales, key=lambda x: x[0])
        wx *= worst_w_scale
        wy *= worst_w_scale
        wz *= worst_w_scale
        for j in range(3, 6):
            qd_pred[j] *= worst_w_scale
        if not arm_scales:
            reason = f"⚡ Wrist speed limited: {JOINT_NAMES[worst_w_idx]} ({math.degrees(abs(qd_pred[worst_w_idx])/worst_w_scale):.1f}°/s > {math.degrees(JOINT_MAX_SPEEDS[worst_w_idx]):.0f}°/s)"

    # 3b. Directional joint limit check with soft deceleration
    for i in range(6):
        q_val = current_q[i]
        lo, hi = JOINT_LIMITS_RAD[i]
        q_dot = qd_pred[i]
        pred_q = q_val + q_dot * dt

        # Moving toward upper limit
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

        # Moving toward lower limit
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

        # Prevent Wrist 2 singularity at 0° (|q5| < 3°)
        if i == 4:
            SINGULARITY_MARGIN = math.radians(3.0)
            if abs(pred_q) < SINGULARITY_MARGIN and abs(pred_q) < abs(q_val):
                scale = max(0.0, min(1.0, (abs(q_val) - SINGULARITY_MARGIN) / (abs(q_dot) * dt + 1e-6)))
                wx *= scale; wy *= scale; wz *= scale
                reason = f"🛑 Wrist 2 approaching Singularity 0° ({math.degrees(q_val):.1f}°) - Block inward"

    # 4. Active feedback damping from actual joint speeds
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
    Filter joint velocities with safety limits:
    - Soft braking near joint limits (20-30° zone)
    - Wrist 2 singularity protection near 0° (|q5| < 15°)
    - Joint speed caps (arm 100°/s, wrist 240°/s)
    """
    filtered_qd = list(qd)
    reason = "OK"

    for i in range(6):
        q_val = current_q[i]
        lo, hi = JOINT_LIMITS_RAD[i]
        q_dot = filtered_qd[i]
        lim = JOINT_MAX_SPEEDS[i]

        # 1. Clamp max speed
        if abs(q_dot) > lim:
            filtered_qd[i] = math.copysign(lim, q_dot)
            q_dot = filtered_qd[i]

        # 2. Soft braking near joint limits (30° zone)
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

        # 3. Wrist 2 singularity protection at 0° (|q5| < 15°)
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
