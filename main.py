#!/usr/bin/env python3
"""
UR3e VR Bridge - Main Entry Point
=================================
Teleoperation bridge between Meta Quest VR and UR3e robot in real-time.

Usage:
    python main.py              # Start real robot teleoperation (UR3e)
    python main.py --sim        # Start offline VR simulation test
    python main.py --help       # Show usage help
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
Examples:
  python main.py             Run real UR3e teleoperation via RTDE and VR
  python main.py --sim       Run simulation test (10 steps, no real robot needed)
  python main.py --ip <IP>   Specify UR3e robot IP address (default: 192.168.1.20)
        """
    )
    parser.add_argument(
        "--sim", action="store_true",
        help="Run VR simulation mode (offline testing)"
    )
    parser.add_argument(
        "--ip", type=str, default="192.168.1.20",
        help="UR3e robot IP address (default: 192.168.1.20)"
    )

    args = parser.parse_args()

    if args.sim:
        print("🚀 Starting simulation mode...")
        from test_vr_simulation import run_simulation_tests
        run_simulation_tests()
    else:
        print(f"🤖 Starting UR3e teleoperation (IP: {args.ip})...")
        os.environ["ROBOT_IP"] = args.ip
        cmd = [sys.executable, "test_move.py", args.ip]
        try:
            subprocess.run(cmd)
        except KeyboardInterrupt:
            print("\n🛑 Stopped by user")

if __name__ == "__main__":
    main()
