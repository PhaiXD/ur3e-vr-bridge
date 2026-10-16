import math
import time
import keyboard
import rtde_control
import rtde_receive

ROBOT_IP = "192.168.1.20"

# กำหนดระยะทางที่จะขยับต่อการกด 1 ครั้ง (0.01 เมตร = 1 เซนติเมตร)
STEP = 0.01 
SPEED = 0.3
ACCEL = 0.3

# ==================== การตั้งค่าระบบความปลอดภัย (UR5e Safety Boundaries) ====================
# 1. รัศมีขั้นต่ำรอบเสาฐาน (Base Cylinder): ป้องกันแขน/ข้อมือถอยไปชนเสาตัวเอง
MIN_BASE_RADIUS = 0.18  # เมตร (18 ซม.)

# 2. ระยะเอื้อมสูงสุดจากฐาน: ป้องกันแขนเหยียดสุดจนติด Elbow Singularity (UR5e Reach สูงสุด ~0.85 ม.)
MAX_REACH = 0.78        # เมตร (78 ซม.)

# 3. ขอบเขตความสูงแกน Z: ป้องกันกระแทกโต๊ะทดลอง
MIN_Z = 0.02            # เมตร (2 ซม. เหนือระดับฐาน)
MAX_Z = 0.85            # เมตร

def check_cartesian_safety(pose):
    """
    ตรวจสอบว่าพิกัดเป้าหมาย (X, Y, Z) อยู่ในระยะปลอดภัยทางกายภาพหรือไม่
    คืนค่า (is_safe: bool, reason: str)
    """
    x, y, z = pose[0], pose[1], pose[2]
    
    # 1. เช็คระยะห่างจากเสาแกนกลาง (ฐานหุ่น)
    r_xy = math.hypot(x, y)
    if r_xy < MIN_BASE_RADIUS:
        return False, f"ใกล้เสาฐานเกินไป! (รัศมี={r_xy:.3f}m < {MIN_BASE_RADIUS}m) เสี่ยงชนตัวหุ่น"

    # 2. เช็คระยะเอื้อมรวม
    r_total = math.sqrt(x**2 + y**2 + z**2)
    if r_total > MAX_REACH:
        return False, f"เอื้อมไกลเกินไป! ({r_total:.3f}m > {MAX_REACH}m) เสี่ยงติด Elbow Singularity"

    # 3. เช็คระดับความสูงแกน Z
    if z < MIN_Z:
        return False, f"ต่ำเกินไป! (Z={z:.3f}m < {MIN_Z}m) เสี่ยงกระแทกโต๊ะ/พื้น"
    if z > MAX_Z:
        return False, f"สูงเกินไป! (Z={z:.3f}m > {MAX_Z}m)"

    return True, "OK"

def get_joint_warnings(q):
    """
    ตรวจเช็คองศาข้อต่อปัจจุบันว่าเข้าใกล้จุดวิกฤต (Singularity) หรือเสี่ยงขัดแขนตัวเองหรือไม่
    """
    warnings = []
    # ข้อศอก (Joint 2): ถ้าใกล้ 0 องศา คือแขนเหยียดตรงสุด (Elbow Singularity)
    elbow_deg = math.degrees(q[2])
    if abs(elbow_deg) < 6.0:
        warnings.append(f"ข้อศอกเหยียดตรงเกือบสุด ({elbow_deg:.1f}°)")

    # ข้อมือ 2 (Joint 4): ถ้าใกล้ 0 องศา คือ Wrist Singularity (สาเหตุหลักที่แขนหมุนตลบ)
    wrist2_deg = math.degrees(q[4])
    if abs(wrist2_deg) < 6.0:
        warnings.append(f"ข้อมือ 2 อยู่ในแนวตรง ({wrist2_deg:.1f}°) เสี่ยง Wrist Singularity")

    return warnings

print(f"กำลังเชื่อมต่อไปที่หุ่นยนต์ IP: {ROBOT_IP} ...")

try:
    rtde_c = rtde_control.RTDEControlInterface(ROBOT_IP)
    rtde_r = rtde_receive.RTDEReceiveInterface(ROBOT_IP)
    
    print("\n✅ เชื่อมต่อสำเร็จ! พร้อมใช้งาน WASD + ระบบป้องกันการขัดแขนตัวเอง")
    print("--------------------------------------------------")
    print("🛡️ ขอบเขตความปลอดภัย (Safety Boundaries):")
    print(f"  - กั้นรัศมีรอบฐาน (Base Cylinder): >= {MIN_BASE_RADIUS} m")
    print(f"  - กั้นระยะเอื้อมสูงสุด (Max Reach): <= {MAX_REACH} m")
    print(f"  - กั้นความสูง Z: {MIN_Z} m ถึง {MAX_Z} m")
    print("--------------------------------------------------")
    print("🎮 การควบคุม:")
    print("  [W] / [S]        : เดินหน้า / ถอยหลัง (แกน X)")
    print("  [A] / [D]        : ซ้าย / ขวา (แกน Y)")
    print("  [Space] / [Ctrl] : ขึ้น / ลง (แกน Z)")
    print("  [Q]              : ออกจากโปรแกรม")
    print("--------------------------------------------------")

    # แสดงสถานะเริ่มต้นของหุ่นยนต์
    init_pose = rtde_r.getActualTCPPose()
    init_q = rtde_r.getActualQ()
    print(f"📍 พิกัดเริ่มต้น: X={init_pose[0]:.3f}, Y={init_pose[1]:.3f}, Z={init_pose[2]:.3f}")
    
    init_warnings = get_joint_warnings(init_q)
    if init_warnings:
        print("⚠️ ข้อควรระวังในท่าปัจจุบัน:")
        for w in init_warnings:
            print(f"   - {w}")
        print("   (แนะนำ: ขยับหุ่นให้ออกจากแนวตรง เพื่อหลีกเลี่ยงการหมุนตลบของข้อต่อ)")

    print("\nคลิกที่หน้าต่าง CMD ก่อนกดปุ่มควบคุมครับ\n")

    while True:
        # อ่านตำแหน่งปัจจุบันทุกรอบลูป
        current_pose = rtde_r.getActualTCPPose()
        target_pose = current_pose.copy()
        moved = False

        # ตรวจสอบการกดปุ่ม (แกน X: หน้า-หลัง)
        if keyboard.is_pressed('w'):
            target_pose[0] += STEP
            moved = True
        elif keyboard.is_pressed('s'):
            target_pose[0] -= STEP
            moved = True

        # ตรวจสอบการกดปุ่ม (แกน Y: ซ้าย-ขวา)
        if keyboard.is_pressed('a'):
            target_pose[1] += STEP
            moved = True
        elif keyboard.is_pressed('d'):
            target_pose[1] -= STEP
            moved = True

        # ตรวจสอบการกดปุ่ม (แกน Z: ขึ้น-ลง)
        if keyboard.is_pressed('space'):
            target_pose[2] += STEP
            moved = True
        elif keyboard.is_pressed('ctrl'):
            target_pose[2] -= STEP
            moved = True

        # ออกจากลูปเมื่อกด Q
        if keyboard.is_pressed('q'):
            print("กำลังออกจากโปรแกรม...")
            break

        # ถ้ามีการกดปุ่มทิศทาง ให้ตรวจสอบความปลอดภัยก่อนสั่งหุ่นยนต์ขยับ
        if moved:
            is_safe, reason = check_cartesian_safety(target_pose)
            
            if is_safe:
                # พิกัดปลอดภัย: สั่งขยับ
                rtde_c.moveL(target_pose, SPEED, ACCEL)
                
                # อ่านสถานะหลังขยับ
                actual_pose = rtde_r.getActualTCPPose()
                actual_q = rtde_r.getActualQ()
                print(f"📍 พิกัด: X={actual_pose[0]:.3f}, Y={actual_pose[1]:.3f}, Z={actual_pose[2]:.3f}")
                
                # แจ้งเตือนหากข้อต่อเข้าใกล้จุดวิกฤต
                warnings = get_joint_warnings(actual_q)
                if warnings:
                    print(f"   ⚠️ เตือน: {', '.join(warnings)}")
            else:
                # ปฏิเสธคำสั่งพร้อมบอกสาเหตุที่ชัดเจน
                print(f"🛑 [SAFETY BLOCKED] {reason}")

            # หน่วงเวลาเล็กน้อยให้หุ่นขยับเสร็จก่อนรับคำสั่งถัดไป
            time.sleep(0.05) 

except Exception as e:
    print(f"❌ เกิดข้อผิดพลาด: {e}")

finally:
    if 'rtde_c' in locals():
        rtde_c.stopScript()
        print("ปิดการเชื่อมต่อแล้ว")