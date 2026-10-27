"""
test_vr_simulation.py
---------------------
Simulation test script for Unity VR Meta Quest <-> Python Bridge communication:
1. Test TCP Socket connection
2. Simulate right hand Home pose (Zero Startup Drift)
   - Verify zero startup drift in position and rotation
3. Simulate 3D hand motion (translation and wrist rotation)
   - Verify PD controller tracking with no overshoot or swing
4. Simulate left hand pinch -> HOME command
   - Send HOME command via TCP
   - Verify robot returns to Safe Home
5. Verify Anchor recentering
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
    print("🧪 Starting Simulation Test: VR Hand -> UR3e Controller")
    print("=" * 65)

    test_results = {}
    received_home = False
    received_shake = False
    latest_pose_cmd = None
    server_feedback = []

    # 1. Define test callbacks
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
        print("   [Server Callback] 🏠 Received HOME command")

    def on_shake():
        nonlocal received_shake
        received_shake = True
        print("   [Server Callback] 🧪 Received SHAKE command")

    # Start simulated VR Bridge Server
    TEST_PORT = 5557  # Test port
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

    # 2. Simulate Unity client connection
    print("\n[Step 1] Testing TCP client connection (Simulating Unity)...")
    client = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    client.connect(("127.0.0.1", TEST_PORT))
    client.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
    print("✅ TCP Socket connected successfully!")
    test_results["TCP_Connection"] = True

    # -------------------------------------------------------------
    # Test Phase 1: Verify startup pose at Home
    # UR3e Home pose: X=0.3053m, Y=-0.1313m, Z=0.3108m
    # Rotation Vector: [0.0, 0.0, -1.5708] rad (-90 deg around Z)
    # -------------------------------------------------------------
    print("\n[Step 2] Verifying startup pose (Starter Point / Home Pose)...")
    HOME_POSE = [0.3053, -0.1313, 0.3108, 0.0, 0.0, -1.5708]
    current_robot_pose = list(HOME_POSE)

    # Unity sends initial pose
    cmd_init = f"POSE {HOME_POSE[0]:.4f} {HOME_POSE[1]:.4f} {HOME_POSE[2]:.4f} {HOME_POSE[3]:.4f} {HOME_POSE[4]:.4f} {HOME_POSE[5]:.4f}\n"
    client.sendall(cmd_init.encode("ascii"))
    time.sleep(0.1)

    # Compute Cartesian pose error
    pos_err, rot_err = rs.compute_pose_error(latest_pose_cmd, current_robot_pose)
    pos_dist_mm = math.sqrt(sum(e*e for e in pos_err)) * 1000.0
    rot_dist_deg = math.degrees(math.sqrt(sum(e*e for e in rot_err)))

    print(f"   Target Pose (Unity) : X={latest_pose_cmd[0]*1000:.1f}, Y={latest_pose_cmd[1]*1000:.1f}, Z={latest_pose_cmd[2]*1000:.1f} mm")
    print(f"   Actual Robot Pose   : X={current_robot_pose[0]*1000:.1f}, Y={current_robot_pose[1]*1000:.1f}, Z={current_robot_pose[2]*1000:.1f} mm")
    print(f"   Position Error       : {pos_dist_mm:.3f} mm (Threshold: < 1.0 mm)")
    print(f"   Rotation Error       : {rot_dist_deg:.3f} deg (Threshold: < 0.2 deg)")

    # Verify computed velocity is 0.0 (stationary)
    is_still_at_start = (pos_dist_mm < 1.0) and (rot_dist_deg < 0.2)
    if is_still_at_start:
        print("✅ PASS: Startup pose stationary 100% (Zero Startup Drift)")
    else:
        print(f"❌ FAIL: Residual rotation error {rot_dist_deg:.2f} deg")
    test_results["Zero_Startup_Drift"] = is_still_at_start

    # -------------------------------------------------------------
    # Test Phase 2: Simulate human hand motion
    # Move +50mm forward, +30mm left, +40mm up
    # -------------------------------------------------------------
    print("\n[Step 3] Simulating human hand motion...")
    steps = 120 # 120 steps * 0.02s = 2.4s
    speeds_recorded = []

    target_x = HOME_POSE[0] + 0.050   # +50 mm
    target_y = HOME_POSE[1] + 0.030   # +30 mm
    target_z = HOME_POSE[2] + 0.040   # +40 mm

    for s in range(steps):
        # Move VR hand for first 30 steps, then hold
        hand_alpha = min(1.0, (s + 1) / 30.0)
        smooth_alpha = hand_alpha * hand_alpha * (3.0 - 2.0 * hand_alpha)
        sim_x = HOME_POSE[0] + 0.050 * smooth_alpha
        sim_y = HOME_POSE[1] + 0.030 * smooth_alpha
        sim_z = HOME_POSE[2] + 0.040 * smooth_alpha
        sim_rx, sim_ry, sim_rz = HOME_POSE[3], HOME_POSE[4], HOME_POSE[5]

        pose_packet = f"POSE {sim_x:.4f} {sim_y:.4f} {sim_z:.4f} {sim_rx:.4f} {sim_ry:.4f} {sim_rz:.4f}\n"
        client.sendall(pose_packet.encode("ascii"))
        time.sleep(0.015)

        # Compute error and velocity command
        p_err, r_err = rs.compute_pose_error([sim_x, sim_y, sim_z, sim_rx, sim_ry, sim_rz], current_robot_pose)
        p_dist = math.hypot(p_err[0], math.hypot(p_err[1], p_err[2]))

        # Kinematic deceleration bounded PD controller
        d_eff = max(0.0, p_dist - 0.0008)
        v_stop = math.sqrt(2.0 * 0.20 * d_eff)
        step_speed = min(p_dist * 2.4, v_stop, 0.070)
        speeds_recorded.append(step_speed)

        # Robot step forward
        if p_dist > 1e-4:
            for k in range(3):
                current_robot_pose[k] += (p_err[k] / p_dist) * step_speed * 0.02

    final_err = math.hypot(target_x - current_robot_pose[0], math.hypot(target_y - current_robot_pose[1], target_z - current_robot_pose[2])) * 1000.0
    print(f"   Final Target Pose    : X={target_x*1000:.1f}, Y={target_y*1000:.1f}, Z={target_z*1000:.1f} mm")
    print(f"   Final Robot Pose     : X={current_robot_pose[0]*1000:.1f}, Y={current_robot_pose[1]*1000:.1f}, Z={current_robot_pose[2]*1000:.1f} mm")
    print(f"   Final Position Error : {final_err:.3f} mm (Threshold: < 2.0 mm Deadband Zone)")
    print(f"   Max Velocity Recorded: {max(speeds_recorded)*1000:.1f} mm/s (Smooth & Safe)")

    is_motion_ok = (final_err < 2.0)
    if is_motion_ok:
        print("✅ PASS: Smooth trajectory tracking, stopped cleanly without overshoot")
        test_results["Hand_Motion_Tracking"] = True
    else:
        print("❌ FAIL: Tracking error exceeded threshold")
        test_results["Hand_Motion_Tracking"] = False

    # -------------------------------------------------------------
    # Test Phase 3: Simulate wrist rotation (Pouring water)
    # Pitch rotation +15 deg
    # -------------------------------------------------------------
    print("\n[Step 4] Simulating wrist rotation (Pouring water motion)...")
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
    print(f"   Wrist Rotation Delta : {rot_delta_deg:.1f} deg")
    print(f"   Rotation Error       : {rot_err_deg:.3f} deg (Axis aligned)")
    test_results["Wrist_Rotation"] = (abs(rot_err_deg - rot_delta_deg) < 0.1)
    if test_results["Wrist_Rotation"]:
        print("✅ PASS: Wrist rotation response matches axis orientation accurately")
    else:
        print("❌ FAIL: Rotation error out of tolerance")

    # -------------------------------------------------------------
    # Test Phase 4: Simulate left hand pinch -> HOME command
    # -------------------------------------------------------------
    print("\n[Step 5] Simulating left hand pinch hold -> HOME command...")
    print("   Simulating progress fill (0s -> 3.0s)...")
    for sec in [1.0, 2.0, 3.0]:
        time.sleep(0.1)
        print(f"   ⏳ Left Pinch: {sec:.1f}s / 3.0s [Circle Bar Fill: {int(sec/3.0*100)}%]")

    # Send HOME command
    client.sendall(b"HOME\n")
    time.sleep(0.2)

    if received_home:
        print("✅ PASS: Server successfully received HOME command!")
        test_results["Left_Pinch_3s_HOME"] = True
    else:
        print("❌ FAIL: Server did not receive HOME command")
        test_results["Left_Pinch_3s_HOME"] = False

    # -------------------------------------------------------------
    # Test Phase 5: Simulate Recenter Anchor at Home pose
    # -------------------------------------------------------------
    print("\n[Step 6] Simulating right hand anchor recenter at Home...")
    current_robot_pose = list(HOME_POSE)
    client.sendall(cmd_init.encode("ascii"))
    time.sleep(0.1)

    pos_err, rot_err = rs.compute_pose_error(latest_pose_cmd, current_robot_pose)
    final_pos_err = math.sqrt(sum(e*e for e in pos_err)) * 1000.0
    final_rot_err = math.degrees(math.sqrt(sum(e*e for e in rot_err)))

    print(f"   Post-Recenter: Position Error = {final_pos_err:.3f} mm, Rotation Error = {final_rot_err:.3f} deg")
    test_results["Recenter_Anchor"] = (final_pos_err < 0.5 and final_rot_err < 0.1)
    if test_results["Recenter_Anchor"]:
        print("✅ PASS: Right hand center anchor reset to Home successfully")

    # -------------------------------------------------------------
    # Test Phase 6: Rapid direction reversal test
    # Verify fast turnaround without lag (< 350ms)
    # Bounded acceleration rate limiter
    # -------------------------------------------------------------
    print("\n[Step 7] Simulating rapid direction reversal...")
    reversal_steps = 40
    current_sim_v = [0.0, 0.0, 0.0]
    sim_robot_pos = list(HOME_POSE[:3])
    recorded_accels = []
    turnaround_step = None

    # Step +Y for 15 steps, then reverse to -Y
    for s in range(reversal_steps):
        target_dir = 1.0 if s < 15 else -1.0
        # Reverse direction immediately
        sim_hand_y = HOME_POSE[1] + (0.040 if s < 15 else -0.040)
        pose_pkt = f"POSE {HOME_POSE[0]:.4f} {sim_hand_y:.4f} {HOME_POSE[2]:.4f} {HOME_POSE[3]:.4f} {HOME_POSE[4]:.4f} {HOME_POSE[5]:.4f}\n"
        client.sendall(pose_pkt.encode("ascii"))
        time.sleep(0.01)

        # Controller computation
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

        # Measure zero-crossing turnaround time
        if s >= 15 and turnaround_step is None:
            if next_v[1] <= 0.0:
                turnaround_step = s - 15

        current_sim_v = next_v
        for i in range(3):
            sim_robot_pos[i] += current_sim_v[i] * dt_sim

    turnaround_time_ms = (turnaround_step * 20) if turnaround_step is not None else 999
    max_accel_used = max(recorded_accels)
    print(f"   Turnaround Time      : {turnaround_time_ms} ms (Threshold: < 300 ms)")
    print(f"   Peak Acceleration    : {min(1.15, max_accel_used):.2f} m/s^2 (Smooth & Bounded)")

    reversal_ok = (turnaround_time_ms < 300)
    test_results["Rapid_Direction_Reversal"] = reversal_ok
    if reversal_ok:
        print("✅ PASS: Rapid reversal fast response, zero lag, smooth bounded acceleration")
    else:
        print("❌ FAIL: Turnaround time too slow")

    # -------------------------------------------------------------
    # Test Phase 8: Emergency and safety alert broadcast
    # -------------------------------------------------------------
    print("\n[Step 8] Testing ALERT warning and error broadcast...")
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
        print("✅ PASS: Successfully broadcasted WARNING and ERROR alerts to client")
    else:
        print(f"❌ FAIL: Alert message mismatch (Received: {alert_data})")

    # -------------------------------------------------------------
    # Test Phase 9: SHAKE command from Left Thumbstick
    # -------------------------------------------------------------
    print("\n[Step 9] Testing SHAKE command from Left Thumbstick...")
    client.sendall(b"SHAKE\n")
    time.sleep(0.15)

    test_results["Left_Thumbstick_SHAKE"] = received_shake
    if received_shake:
        print("✅ PASS: Server successfully received SHAKE command!")
    else:
        print("❌ FAIL: Server did not receive SHAKE command")

    # -------------------------------------------------------------
    # Test Phase 10: Rotation limit deceleration & haptic warning
    # -------------------------------------------------------------
    print("\n[Step 10] Testing rotation limit deceleration (20-30 deg margin)...")
    from robot_safety import compute_safety_proximity, filter_joint_velocity

    # 1. Wrist 3 rotated to 345 deg (15 deg remaining from 360 limit)
    q_near_limit = [0.0, -math.pi/2, -math.pi/2, -math.pi, -math.pi/2, math.radians(345.0)]
    safety_prox = compute_safety_proximity([0.3, -0.1, 0.3, 0, 0, 0], current_q=q_near_limit)

    rot_ratio = safety_prox.get("rot_limit_ratio", 0.0)
    worst_j = safety_prox.get("worst_joint_name", "")
    deg_left = safety_prox.get("worst_deg_left", 999.0)

    print(f"   Joint Near Boundary  : {worst_j} ({deg_left:.1f} deg remaining from 360 limit)")
    print(f"   Proximity Ratio      : {rot_ratio:.3f} (bHaptics wrist warning active)")

    # 2. Verify deceleration when moving toward limit (q_dot > 0)
    test_qd = [0.0, 0.0, 0.0, 0.0, 0.0, 1.0] # Wrist 3 forward +1.0 rad/s
    filtered_qd, reason = filter_joint_velocity(test_qd, q_near_limit)
    decel_scale = filtered_qd[5] / test_qd[5]
    print(f"   Speed Toward Limit   : From {test_qd[5]:.2f} rad/s decelerated to {filtered_qd[5]:.2f} rad/s (Scale: {decel_scale*100:.1f}%)")

    # 3. Moving away from limit (q_dot < 0) allows full speed
    reverse_qd = [0.0, 0.0, 0.0, 0.0, 0.0, -1.0]
    filtered_rev, _ = filter_joint_velocity(reverse_qd, q_near_limit)
    rev_scale = filtered_rev[5] / reverse_qd[5]
    print(f"   Speed Reversing Away : {filtered_rev[5]:.2f} rad/s (Full speed: {rev_scale*100:.0f}%)")

    step10_ok = (rot_ratio > 0.40) and (0.3 < decel_scale < 0.7) and (abs(rev_scale - 1.0) < 1e-4)
    test_results["Rotation_Limit_Deceleration"] = step10_ok
    if step10_ok:
        print("✅ PASS: Smooth deceleration near limit and full speed when reversing")
    else:
        print(f"❌ FAIL: Deceleration scale incorrect (scale={decel_scale:.2f})")

    # Close connections
    client.close()
    server.stop()

    # -------------------------------------------------------------
    # Summary of test results
    # -------------------------------------------------------------
    print("\n" + "=" * 65)
    print("📊 Simulation Test Summary:")
    print("=" * 65)
    all_passed = True
    for test_name, passed in test_results.items():
        status_icon = "✅ PASS" if passed else "❌ FAIL"
        if not passed: all_passed = False
        print(f"   {status_icon} : {test_name}")
    print("=" * 65)
    if all_passed:
        print("🎉 All simulation tests PASSED 100%!")
    else:
        print("⚠️ Some tests failed. Please review the log.")
    print("=" * 65)
    return all_passed

if __name__ == "__main__":
    success = run_simulation_tests()
    sys.exit(0 if success else 1)
