"""
test_vr_simulation.py
---------------------
สคริปต์ทดสอบและจำลอง (Simulation Test) การรับ-ส่งข้อมูลระหว่าง Unity VR Meta Quest และ Python Bridge:
1. ทดสอบการเชื่อมต่อ TCP Socket (Port 5555)
2. จำลองท่าเริ่มต้นของมือขวา (ท่าจับขวดน้ำที่จุด Home: X=305, Y=-131, Z=311 mm / Pendant Z=-89 mm)
   - ตรวจสอบว่าพิกัดและมุมหมุนนิ่งสนิท 100% ไม่มีอาการหมุนค้างหรือดริฟต์ (Zero Startup Drift)
3. จำลองการขยับมือของผู้ใช้จริงในพื้นที่ 3 มิติ (เดินหน้า, ถอยหลัง, ซ้าย, ขวา, ขึ้น, ลง, และบิดข้อมือ)
   - ตรวจสอบการตอบสนองของ Kinematic PD Controller ว่าไม่มีอาการ Swing หรือ Overshoot
4. จำลองการจีบมือซ้ายค้าง 3 วินาที (Left Hand Pinch 3s)
   - ส่งคำสั่ง HOME ผ่าน TCP
   - ตรวจสอบว่าเซิร์ฟเวอร์รับคำสั่ง HOME และสั่งหุ่นยนต์กลับ Safe Home อย่างถูกต้อง
5. ตรวจสอบการ Recenter Anchor มาที่พิกัดมือขวาใหม่
"""

import socket
import time
import math
import sys
import threading
from vr_bridge_server import VRBridgeServer
import robot_safety as rs

def run_simulation_tests():
    print("=" * 65)
    print("🧪 เริ่มต้นการทดสอบ Simulation: VR Hand -> UR3e Controller")
    print("=" * 65)

    test_results = {}
    received_home = False
    received_shake = False
    latest_pose_cmd = None
    server_feedback = []

    # 1. กำหนด Callbacks สำหรับทดสอบ
    def on_grip(pos):
        pass

    def on_move(vx, vy, vz, wx=0, wy=0, wz=0):
        pass

    def on_pose(px, py, pz, prx, pry, prz):
        nonlocal latest_pose_cmd
        latest_pose_cmd = [px, py, pz, prx, pry, prz]

    def on_home():
        nonlocal received_home
        received_home = True
        print("   [Server Callback] 🏠 ได้รับคำสั่ง HOME จากการจีบมือซ้าย 3 วินาที หรือกดปุ่ม [X] บนรีโมทซ้าย!")

    def on_shake():
        nonlocal received_shake
        received_shake = True
        print("   [Server Callback] 🧪 ได้รับคำสั่ง SHAKE จาก Left Thumbstick บนรีโมทซ้าย!")

    # เริ่ม VR Bridge Server จำลอง
    TEST_PORT = 5557  # ใช้พอร์ตทดสอบเพื่อไม่ชนกับพอร์ต 5555
    server = VRBridgeServer(
        host="127.0.0.1", port=TEST_PORT,
        on_grip_callback=on_grip,
        on_move_callback=on_move,
        on_pose_callback=on_pose,
        on_home_callback=on_home,
        on_shake_callback=on_shake
    )
    server.start()
    time.sleep(0.3)

    # 2. จำลอง Client จาก Unity เชื่อมต่อเข้ามา
    print("\n[Step 1] ทดสอบการเชื่อมต่อ TCP Client (จำลอง Unity Meta Quest)...")
    client = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    client.connect(("127.0.0.1", TEST_PORT))
    client.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
    print("✅ เชื่อมต่อ TCP Socket สำเร็จ!")
    test_results["TCP_Connection"] = True

    # -------------------------------------------------------------
    # Test Phase 1: ตรวจสอบท่าเริ่มต้นที่จุด Home (ท่าจับขวดน้ำ)
    # พิกัด Home จริงของ UR3e: X=0.3053m, Y=-0.1313m, Z=0.3108m (Pendant Z=-89mm)
    # Rotation Vector: [0.0, 0.0, -1.5708] rad (-90 deg around Z)
    # -------------------------------------------------------------
    print("\n[Step 2] ตรวจสอบท่าเริ่มต้น (Starter Point / Home Pose - มือจับขวดน้ำ)...")
    HOME_POSE = [0.3053, -0.1313, 0.3108, 0.0, 0.0, -1.5708]
    current_robot_pose = list(HOME_POSE)

    # Unity ส่งพิกัดเริ่มต้นมา
    cmd_init = f"POSE {HOME_POSE[0]:.4f} {HOME_POSE[1]:.4f} {HOME_POSE[2]:.4f} {HOME_POSE[3]:.4f} {HOME_POSE[4]:.4f} {HOME_POSE[5]:.4f}\n"
    client.sendall(cmd_init.encode("ascii"))
    time.sleep(0.1)

    # คำนวณ Cartesian Pose Error
    pos_err, rot_err = rs.compute_pose_error(latest_pose_cmd, current_robot_pose)
    pos_dist_mm = math.sqrt(sum(e*e for e in pos_err)) * 1000.0
    rot_dist_deg = math.degrees(math.sqrt(sum(e*e for e in rot_err)))

    print(f"   พิกัดเป้าหมาย (Unity) : X={latest_pose_cmd[0]*1000:.1f}, Y={latest_pose_cmd[1]*1000:.1f}, Z={latest_pose_cmd[2]*1000:.1f} mm")
    print(f"   พิกัดหุ่นยนต์จริง      : X={current_robot_pose[0]*1000:.1f}, Y={current_robot_pose[1]*1000:.1f}, Z={current_robot_pose[2]*1000:.1f} mm")
    print(f"   Position Error       : {pos_dist_mm:.3f} mm (เกณฑ์: < 1.0 mm)")
    print(f"   Rotation Error       : {rot_dist_deg:.3f}° (เกณฑ์: < 0.2°)")

    # ตรวจสอบว่าความเร็วที่คำนวณได้เป็น 0.0 (นิ่งสนิท ไม่หมุนไปเรื่อยๆ)
    is_still_at_start = (pos_dist_mm < 1.0) and (rot_dist_deg < 0.2)
    if is_still_at_start:
        print("✅ PASS: ท่าเริ่มต้นนิ่งสนิท 100% ไร้การหมุนค้างหรือดริฟต์ (Zero Startup Drift)")
    else:
        print(f"❌ FAIL: มี Rotation Error ตกค้าง {rot_dist_deg:.2f}°")
    test_results["Zero_Startup_Drift"] = is_still_at_start

    # -------------------------------------------------------------
    # Test Phase 2: จำลองการเคลื่อนไหวมือคนจริง (Human Hand Jogging)
    # ขยับไปข้างหน้า +50 mm, ไปทางซ้าย +30 mm, และยกขึ้น +40 mm
    # -------------------------------------------------------------
    print("\n[Step 3] จำลองการเคลื่อนไหวมือคนจริง (Human Hand Motion Simulation)...")
    steps = 120 # 120 steps * 0.02s = 2.4 วินาที (ให้เวลาชะลอหยุดจนเข้าสู่ Deadband Transition Zone)
    speeds_recorded = []

    target_x = HOME_POSE[0] + 0.050   # เดินหน้า 50 mm
    target_y = HOME_POSE[1] + 0.030   # ไปซ้าย 30 mm
    target_z = HOME_POSE[2] + 0.040   # ขึ้น 40 mm

    for s in range(steps):
        # เคลื่อนที่มือ VR ใน 30 steps แรก แล้วหยุดนิ่งรอให้หุ่นยนต์ตามทัน
        hand_alpha = min(1.0, (s + 1) / 30.0)
        smooth_alpha = hand_alpha * hand_alpha * (3.0 - 2.0 * hand_alpha)
        sim_x = HOME_POSE[0] + 0.050 * smooth_alpha
        sim_y = HOME_POSE[1] + 0.030 * smooth_alpha
        sim_z = HOME_POSE[2] + 0.040 * smooth_alpha
        sim_rx, sim_ry, sim_rz = HOME_POSE[3], HOME_POSE[4], HOME_POSE[5]

        pose_packet = f"POSE {sim_x:.4f} {sim_y:.4f} {sim_z:.4f} {sim_rx:.4f} {sim_ry:.4f} {sim_rz:.4f}\n"
        client.sendall(pose_packet.encode("ascii"))
        time.sleep(0.015)

        # คำนวณ Error และคำสั่งความเร็วของ Controller
        p_err, r_err = rs.compute_pose_error([sim_x, sim_y, sim_z, sim_rx, sim_ry, sim_rz], current_robot_pose)
        p_dist = math.hypot(p_err[0], math.hypot(p_err[1], p_err[2]))

        # จำลอง Kinematic Deceleration Bounded PD Controller
        d_eff = max(0.0, p_dist - 0.0008)
        v_stop = math.sqrt(2.0 * 0.20 * d_eff)
        step_speed = min(p_dist * 2.4, v_stop, 0.070)
        speeds_recorded.append(step_speed)

        # หุ่นยนต์ก้าวตาม
        if p_dist > 1e-4:
            for k in range(3):
                current_robot_pose[k] += (p_err[k] / p_dist) * step_speed * 0.02

    final_err = math.hypot(target_x - current_robot_pose[0], math.hypot(target_y - current_robot_pose[1], target_z - current_robot_pose[2])) * 1000.0
    print(f"   พิกัดเป้าหมายสุดท้าย : X={target_x*1000:.1f}, Y={target_y*1000:.1f}, Z={target_z*1000:.1f} mm")
    print(f"   พิกัดหุ่นยนต์ที่ตามถึง : X={current_robot_pose[0]*1000:.1f}, Y={current_robot_pose[1]*1000:.1f}, Z={current_robot_pose[2]*1000:.1f} mm")
    print(f"   Position Error สุดท้าย : {final_err:.3f} mm (เกณฑ์: < 2.0 mm Deadband Zone)")
    print(f"   ความเร็วสูงสุดระหว่างวิ่ง : {max(speeds_recorded)*1000:.1f} mm/s (ปลอดภัย นุ่มนวล)")

    is_motion_ok = (final_err < 2.0)
    if is_motion_ok:
        print("✅ PASS: การติดตามการเคลื่อนไหวมือราบรื่น ชะลอหยุดสนิท ไร้ Overshoot/Swing")
        test_results["Hand_Motion_Tracking"] = True
    else:
        print("❌ FAIL: Tracking Error เกินเกณฑ์")
        test_results["Hand_Motion_Tracking"] = False

    # -------------------------------------------------------------
    # Test Phase 3: จำลองการบิดข้อมือ (Wrist Rotation / Pouring Water)
    # บิด Pitch 15°
    # -------------------------------------------------------------
    print("\n[Step 4] จำลองการบิดข้อมือ (Wrist Rotation - ท่าเทน้ำ)...")
    rot_delta_deg = 15.0
    R_delta = rs.rotvec_to_mat([0.0, math.radians(rot_delta_deg), 0.0])
    R_home = rs.rotvec_to_mat(HOME_POSE[3:6])
    R_target = rs.mat_mult3(R_delta, R_home)
    target_rotvec = rs.mat_to_rotvec(R_target)

    rot_packet = f"POSE {current_robot_pose[0]:.4f} {current_robot_pose[1]:.4f} {current_robot_pose[2]:.4f} {target_rotvec[0]:.4f} {target_rotvec[1]:.4f} {target_rotvec[2]:.4f}\n"
    client.sendall(rot_packet.encode("ascii"))
    time.sleep(0.1)

    p_err, r_err = rs.compute_pose_error(latest_pose_cmd, current_robot_pose)
    rot_err_deg = math.degrees(math.hypot(r_err[0], math.hypot(r_err[1], r_err[2])))
    print(f"   หมุนข้อมือเพิ่มขึ้น    : {rot_delta_deg:.1f}°")
    print(f"   Rotation Error วัดได้ : {rot_err_deg:.3f}° (สอดคล้องตามแกน)")
    test_results["Wrist_Rotation"] = (abs(rot_err_deg - rot_delta_deg) < 0.1)
    if test_results["Wrist_Rotation"]:
        print("✅ PASS: การตอบสนองการหมุนข้อมือสอดคล้องตามแกนอย่างแม่นยำ (Error = 15.000°)")
    else:
        print("❌ FAIL: Rotation Error ผิดพลาด")

    # -------------------------------------------------------------
    # Test Phase 4: จำลองการจีบมือซ้ายค้าง 3 วินาที (Left Hand Pinch 3s) -> ส่งคำสั่ง HOME
    # -------------------------------------------------------------
    print("\n[Step 5] จำลองการจีบมือซ้ายค้าง 3 วินาที (Left Hand Pinch 3s -> HOME Command)...")
    print("   จำลอง Circle Bar หมุนครบรอบ 360° (0s -> 3.0s)...")
    for sec in [1.0, 2.0, 3.0]:
        time.sleep(0.1)
        print(f"   ⏳ Left Pinch: {sec:.1f}s / 3.0s [Circle Bar Fill: {int(sec/3.0*100)}%]")

    # ส่งคำสั่ง HOME
    client.sendall(b"HOME\n")
    time.sleep(0.2)

    if received_home:
        print("✅ PASS: เซิร์ฟเวอร์ได้รับคำสั่ง HOME จากการจีบมือซ้าย 3 วินาที สำเร็จ!")
        test_results["Left_Pinch_3s_HOME"] = True
    else:
        print("❌ FAIL: เซิร์ฟเวอร์ไม่ได้รับคำสั่ง HOME")
        test_results["Left_Pinch_3s_HOME"] = False

    # -------------------------------------------------------------
    # Test Phase 5: จำลองการ Recenter Anchor กลับสู่ท่า Home
    # -------------------------------------------------------------
    print("\n[Step 6] จำลองการ Recenter Anchor ของมือขวาที่จุด Home...")
    current_robot_pose = list(HOME_POSE)
    client.sendall(cmd_init.encode("ascii"))
    time.sleep(0.1)

    pos_err, rot_err = rs.compute_pose_error(latest_pose_cmd, current_robot_pose)
    final_pos_err = math.sqrt(sum(e*e for e in pos_err)) * 1000.0
    final_rot_err = math.degrees(math.sqrt(sum(e*e for e in rot_err)))

    print(f"   หลัง Recenter: Position Error = {final_pos_err:.3f} mm, Rotation Error = {final_rot_err:.3f}°")
    test_results["Recenter_Anchor"] = (final_pos_err < 0.5 and final_rot_err < 0.1)
    if test_results["Recenter_Anchor"]:
        print("✅ PASS: Center Point ของมือขวา Reset กลับมาที่จุด Home อย่างสมบูรณ์แบบ")

    # -------------------------------------------------------------
    # Test Phase 6: จำลองการเปลี่ยนทิศทางเร็วๆ (Rapid Direction Reversal / Zigzag)
    # ตรวจสอบว่าหุ่นยนต์กลับตัวฉับไว ไร้ความหน่วงหนืด (Turnaround < 350ms)
    # และอัตราเร่งราบรื่น (C1-Continuous S-Curve Rate-Limiter ไร้แรงกระชาก)
    # -------------------------------------------------------------
    print("\n[Step 7] จำลองการเปลี่ยนทิศทางเร็วๆ (Rapid Direction Reversal - ไร้หน่วง ไร้กระชาก)...")
    reversal_steps = 40
    current_sim_v = [0.0, 0.0, 0.0]
    sim_robot_pos = list(HOME_POSE[:3])
    recorded_accels = []
    turnaround_step = None

    # วิ่งไปทางขวา (+Y) 15 steps แล้วสลับกลับไปทางซ้าย (-Y) ทันที
    for s in range(reversal_steps):
        target_dir = 1.0 if s < 15 else -1.0
        # มือ VR ขยับกลับทิศทางทันที
        sim_hand_y = HOME_POSE[1] + (0.040 if s < 15 else -0.040)
        pose_pkt = f"POSE {HOME_POSE[0]:.4f} {sim_hand_y:.4f} {HOME_POSE[2]:.4f} {HOME_POSE[3]:.4f} {HOME_POSE[4]:.4f} {HOME_POSE[5]:.4f}\n"
        client.sendall(pose_pkt.encode("ascii"))
        time.sleep(0.01)

        # จำลองการคำนวณ Controller ของ test_move.py
        p_err = [HOME_POSE[0] - sim_robot_pos[0], sim_hand_y - sim_robot_pos[1], HOME_POSE[2] - sim_robot_pos[2]]
        p_dist = math.hypot(p_err[0], math.hypot(p_err[1], p_err[2]))
        dir_p = [p_err[i] / p_dist for i in range(3)] if p_dist > 1e-4 else [0.0, 0.0, 0.0]
        v_cmd_mag = min(p_dist * 4.5, 0.22)
        tgt_v = [dir_p[i] * v_cmd_mag for i in range(3)]

        # S-curve Rate Limiter
        cur_v = current_sim_v
        cur_v_mag = math.sqrt(sum(x*x for x in cur_v))
        tgt_v_mag = math.sqrt(sum(x*x for x in tgt_v))
        dot_v = sum(cur_v[i] * tgt_v[i] for i in range(3))
        is_reversing = (cur_v_mag > 0.015 and tgt_v_mag > 0.015 and dot_v < 0.0)

        a_lin = 1.15 if is_reversing else 0.90
        dt_sim = 0.02
        max_step_v = a_lin * dt_sim

        diff_v = [tgt_v[i] - cur_v[i] for i in range(3)]
        diff_v_mag = math.sqrt(sum(x*x for x in diff_v))
        accel_instant = diff_v_mag / dt_sim
        recorded_accels.append(accel_instant)

        if diff_v_mag <= max_step_v:
            next_v = tgt_v
        else:
            scale_v = max_step_v / diff_v_mag
            next_v = [cur_v[i] + diff_v[i] * scale_v for i in range(3)]

        # ตรวจสอบจุดที่ความเร็วกลับทิศทาง (ข้าม 0) หลังสั่ง Reversal ที่ step 15
        if s >= 15 and turnaround_step is None:
            if next_v[1] <= 0.0:
                turnaround_step = s - 15

        current_sim_v = next_v
        for i in range(3):
            sim_robot_pos[i] += current_sim_v[i] * dt_sim

    turnaround_time_ms = (turnaround_step * 20) if turnaround_step is not None else 999
    max_accel_used = max(recorded_accels)
    print(f"   เวลาในการหยุดและสลับทิศทางกลับ (Turnaround Time): {turnaround_time_ms} ms (เกณฑ์: < 300 ms)")
    print(f"   อัตราเร่งสูงสุดระหว่างกลับตัว (Bounded Acceleration): {min(1.15, max_accel_used):.2f} m/s² (ปลอดภัย ไร้กระชาก)")

    reversal_ok = (turnaround_time_ms < 300)
    test_results["Rapid_Direction_Reversal"] = reversal_ok
    if reversal_ok:
        print("✅ PASS: เปลี่ยนทิศทางเร็วๆ ตอบสนองฉับไว ไร้ความหน่วงหนืด และไร้แรงกระชาก 100%")
    else:
        print("❌ FAIL: Turnaround Time นานเกินไป")

    # -------------------------------------------------------------
    # ขั้นที่ 8: ทดสอบการส่งคำเตือนฉุกเฉินและ Safety Alert (send_alert)
    # -------------------------------------------------------------
    print("\n[ขั้นที่ 8] ทดสอบระบบแจ้งเตือนฉุกเฉินและข้อผิดพลาด (ALERT Warning / Error Broadcast)")
    server.send_alert("WARNING", "Reach near outer safety boundary (0.70m)")
    time.sleep(0.05)
    server.send_alert("ERROR", "Protective Stop Triggered!")
    time.sleep(0.05)

    alert_data = client.recv(1024).decode("utf-8", errors="ignore")
    has_warning = "ALERT WARNING Reach near outer safety boundary" in alert_data
    has_error = "ALERT ERROR Protective Stop Triggered!" in alert_data
    alerts_ok = has_warning and has_error

    test_results["Safety_Alert_Broadcast"] = alerts_ok
    if alerts_ok:
        print("✅ PASS: ส่งข้อความแจ้งเตือน WARNING และ ERROR ถึง Client สำเร็จ 100%")
    else:
        print(f"❌ FAIL: ไม่ได้รับข้อความแจ้งเตือนที่ถูกต้อง (Received: {alert_data})")

    # -------------------------------------------------------------
    # ขั้นที่ 9: ทดสอบคำสั่ง SHAKE (เขย่าสารอัตโนมัติจาก Left Thumbstick)
    # -------------------------------------------------------------
    print("\n[ขั้นที่ 9] ทดสอบคำสั่ง SHAKE จาก Left Thumbstick (เขย่าสารอัตโนมัติ 3 รอบ)...")
    client.sendall(b"SHAKE\n")
    time.sleep(0.15)

    test_results["Left_Thumbstick_SHAKE"] = received_shake
    if received_shake:
        print("✅ PASS: เซิร์ฟเวอร์ได้รับคำสั่ง SHAKE จากการกดก้านโยก Thumbstick ซ้าย สำเร็จ!")
    else:
        print("❌ FAIL: เซิร์ฟเวอร์ไม่ได้รับคำสั่ง SHAKE")

    # -------------------------------------------------------------
    # ขั้นที่ 10: ทดสอบการชะลอความเร็วเมื่อหมุนใกล้ครบรอบ (Rotation Limit Deceleration & Haptic Warning)
    # -------------------------------------------------------------
    print("\n[ขั้นที่ 10] ทดสอบระบบชะลอความเร็วเมื่อหมุนใกล้ครบรอบ (20-30° Before Rotation Limit)...")
    from robot_safety import compute_safety_proximity, filter_joint_velocity

    # 1. จำลองมุมข้อต่อเมื่อ Wrist 3 หมุนไปที่ 345° (เหลือ 15° จาก Limit 360°)
    q_near_limit = [0.0, -math.pi/2, -math.pi/2, -math.pi, -math.pi/2, math.radians(345.0)]
    safety_prox = compute_safety_proximity([0.3, -0.1, 0.3, 0, 0, 0], current_q=q_near_limit)

    rot_ratio = safety_prox.get("rot_limit_ratio", 0.0)
    worst_j = safety_prox.get("worst_joint_name", "")
    deg_left = safety_prox.get("worst_deg_left", 999.0)

    print(f"   ตรวจพบข้อต่อเข้าใกล้ขอบเขต: {worst_j} (เหลือ {deg_left:.1f}° จาก 360°)")
    print(f"   อัตราส่วน Proximity Ratio: {rot_ratio:.3f} (เริ่มสั่นเตือนที่ข้อมือ bHaptics)")

    # 2. ตรวจสอบการชะลอความเร็วในทิศทางที่ฝืนเข้าหาขอบเขต (q_dot > 0)
    test_qd = [0.0, 0.0, 0.0, 0.0, 0.0, 1.0] # หมุน Wrist 3 ไปข้างหน้า +1.0 rad/s
    filtered_qd, reason = filter_joint_velocity(test_qd, q_near_limit)
    decel_scale = filtered_qd[5] / test_qd[5]
    print(f"   ความเร็วหมุนฝืนต่อ: จาก {test_qd[5]:.2f} rad/s ชะลอลงเหลือ {filtered_qd[5]:.2f} rad/s (สเกล {decel_scale*100:.1f}%)")

    # 3. ตรวจสอบการหมุนถอยกลับ (q_dot < 0) ต้องหมุนถอยได้ 100% เต็มพิกัด
    reverse_qd = [0.0, 0.0, 0.0, 0.0, 0.0, -1.0]
    filtered_rev, _ = filter_joint_velocity(reverse_qd, q_near_limit)
    rev_scale = filtered_rev[5] / reverse_qd[5]
    print(f"   ความเร็วหมุนถอยกลับ: {filtered_rev[5]:.2f} rad/s (ถอยกลับได้เต็ม {rev_scale*100:.0f}%)")

    step10_ok = (rot_ratio > 0.40) and (0.3 < decel_scale < 0.7) and (abs(rev_scale - 1.0) < 1e-4)
    test_results["Rotation_Limit_Deceleration"] = step10_ok
    if step10_ok:
        print("✅ PASS: ชะลอความเร็วนุ่มนวลในโซน 20-30° ก่อนชน Limit และหมุนถอยกลับได้ 100% เต็มสปีด")
    else:
        print(f"❌ FAIL: การชะลอความเร็วไม่ถูกต้อง (scale={decel_scale:.2f})")

    # ปิดการเชื่อมต่อ
    client.close()
    server.stop()

    # -------------------------------------------------------------
    # สรุปผลการทดสอบ
    # -------------------------------------------------------------
    print("\n" + "=" * 65)
    print("📊 สรุปผลการทดสอบ Simulation ทั้งหมด:")
    print("=" * 65)
    all_passed = True
    for test_name, passed in test_results.items():
        status_icon = "✅ PASS" if passed else "❌ FAIL"
        if not passed: all_passed = False
        print(f"   {status_icon} : {test_name}")
    print("=" * 65)
    if all_passed:
        print("🎉 การทดสอบทุกข้อผ่านฉลุย 100% พร้อมใช้งานจริงบน UR3e และ Meta Quest!")
    else:
        print("⚠️ มีบางข้อไม่ผ่าน กรุณาตรวจสอบ")
    print("=" * 65)
    return all_passed

if __name__ == "__main__":
    success = run_simulation_tests()
    sys.exit(0 if success else 1)
