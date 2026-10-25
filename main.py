#!/usr/bin/env python3
"""
UR3e VR Bridge - Main Entry Point
=================================
ระบบเชื่อมต่อ Teleoperation ระหว่าง Meta Quest VR และหุ่นยนต์ UR3e Real-Time

การใช้งาน:
    python main.py              # เริ่มต้นระบบ Teleoperation ควบคุมหุ่นยนต์จริง (UR3e)
    python main.py --sim        # เริ่มต้นโหมดจำลอง (Offline VR Simulation Test)
    python main.py --help       # แสดงคำแนะนำการใช้งาน
"""

import sys
import os
import subprocess
import shutil
import argparse

# Enable UTF-8 encoding on Windows console
if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')
if hasattr(sys.stderr, 'reconfigure'):
    sys.stderr.reconfigure(encoding='utf-8')

# Ensure Python 3.12 is used if current environment lacks rtde_control
def ensure_python_environment():
    try:
        import rtde_control
        return
    except ModuleNotFoundError:
        cand_py312 = r"C:\Users\dhamm\AppData\Local\Programs\Python\Python312\python.exe"
        if os.path.exists(cand_py312) and cand_py312.lower() != sys.executable.lower():
            print(f"[Auto-Redirect] Switching to Python 3.12 ({cand_py312})...")
            sys.exit(subprocess.call([cand_py312] + sys.argv))
        elif shutil.which("py") and "3.12" not in sys.version:
            print("[Auto-Redirect] Switching to 'py -3.12'...")
            sys.exit(subprocess.call([shutil.which("py"), "-3.12"] + sys.argv))

ensure_python_environment()

def main():
    parser = argparse.ArgumentParser(
        description="UR3e VR Bridge - Teleoperation Controller & Simulation",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
ตัวอย่างคำสั่ง:
  python main.py             รันระบบเชื่อมต่อหุ่นยนต์ UR3e จริงผ่าน RTDE และ VR
  python main.py --sim       รันระบบทดสอบจำลอง (Simulation) 10 ขั้นตอนโดยไม่ต้องต่อหุ่นยนต์จริง
  python main.py --ip <IP>   ระบุ IP Address ของหุ่นยนต์ UR3e (ค่าเริ่มต้น: 192.168.1.20)
        """
    )
    parser.add_argument(
        "--sim", action="store_true",
        help="รันโหมดจำลอง VR Simulation (Offline testing)"
    )
    parser.add_argument(
        "--ip", type=str, default="192.168.1.20",
        help="IP Address ของหุ่นยนต์ UR3e (Default: 192.168.1.20)"
    )

    args = parser.parse_args()

    if args.sim:
        print("🚀 กำลังเริ่มโหมดจำลอง (Simulation Mode)...")
        from test_vr_simulation import run_simulation_tests
        run_simulation_tests()
    else:
        print(f"🤖 กำลังเริ่มระบบ Teleoperation เชื่อมต่อหุ่นยนต์ UR3e จริง (IP: {args.ip})...")
        os.environ["ROBOT_IP"] = args.ip
        cmd = [sys.executable, "test_move.py", args.ip]
        try:
            subprocess.run(cmd)
        except KeyboardInterrupt:
            print("\n🛑 หยุดการทำงานโดยผู้ใช้")

if __name__ == "__main__":
    main()
