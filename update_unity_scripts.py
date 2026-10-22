import sys

# 1. Update VRHandMover.cs
path_vhm = r"C:\Users\dhamm\VR\Assets\Scripts\VRHandMover.cs"
with open(path_vhm, "r", encoding="utf-8") as f:
    content_vhm = f.read()

# Add Left Controller headers & fields
if "enableLeftControllerActions" not in content_vhm:
    old_fields = "public bool isUsingController = false;"
    new_fields = """public bool isUsingController = false;

        [Header("Left Touch Controller Actions (รีโมทซ้าย)")]
        [Tooltip("เปิดใช้งานการควบคุมผ่านปุ่มที่รีโมทด้านซ้าย (X: Set Origin/Home, Y: Pause/Resume, Thumbstick: Shake Test Tube)")]
        public bool enableLeftControllerActions = true;

        [Header("Teleoperation Pause State")]
        [Tooltip("สถานะ Pause หยุดส่งพิกัดไปยังแขนกลชั่วคราว (กดปุ่ม Y บนรีโมทซ้ายเพื่อสลับ)")]
        public bool isTeleoperationPaused = false;

        [Header("Auto Shake Test Tube State")]
        [Tooltip("สถานะกำลังเขย่าสารอัตโนมัติ (กดก้านโยก Thumbstick ซ้าย)")]
        public bool isShakingActive = false;"""
    assert old_fields in content_vhm, "old_fields not found in VRHandMover.cs"
    content_vhm = content_vhm.replace(old_fields, new_fields, 1)

# Add button checking and pause check at the beginning of Update
button_code = """
            // ========================================================
            // LEFT TOUCH CONTROLLER ACTIONS (ปุ่มบนรีโมทซ้าย)
            // ========================================================
            if (enableLeftControllerActions)
            {
                // 1. ปุ่ม X (Button Three / RawButton.X / KeyCode.X): Set Origin Point & Robot Safe Home
                bool xPressed = false;
                try
                {
                    xPressed = OVRInput.GetDown(OVRInput.Button.Three, OVRInput.Controller.LTouch) ||
                               OVRInput.GetDown(OVRInput.RawButton.X, OVRInput.Controller.LTouch) ||
                               Input.GetKeyDown(KeyCode.X);
                }
                catch { }

                if (xPressed)
                {
                    ExecuteSetOriginAndHome();
                }

                // 2. ปุ่ม Y (Button Four / RawButton.Y / KeyCode.Y): Pause / Resume Teleoperation
                bool yPressed = false;
                try
                {
                    yPressed = OVRInput.GetDown(OVRInput.Button.Four, OVRInput.Controller.LTouch) ||
                               OVRInput.GetDown(OVRInput.RawButton.Y, OVRInput.Controller.LTouch) ||
                               Input.GetKeyDown(KeyCode.Y);
                }
                catch { }

                if (yPressed)
                {
                    ToggleTeleoperationPause();
                }

                // 3. ปุ่มกดตรง Joy Stick ที่เลื่อน (Thumbstick Click / KeyCode.K): เขย่าสารอัตโนมัติ 3 รอบ
                bool thumbstickPressed = false;
                try
                {
                    thumbstickPressed = OVRInput.GetDown(OVRInput.Button.PrimaryThumbstick, OVRInput.Controller.LTouch) ||
                                         OVRInput.GetDown(OVRInput.RawButton.LThumbstick, OVRInput.Controller.LTouch) ||
                                         Input.GetKeyDown(KeyCode.K);
                }
                catch { }

                if (thumbstickPressed)
                {
                    ExecuteAutoShake();
                }
            }

            // หากอยู่ในสถานะ Pause: หยุดส่งพิกัดไปยังแขนกลทันที
            if (isTeleoperationPaused)
            {
                if (gripperBridge != null)
                {
                    gripperBridge.SetTargetVelocity(0f, 0f, 0f, 0f, 0f, 0f);
                }
                return;
            }
"""

if "ExecuteSetOriginAndHome()" not in content_vhm:
    old_update_entry = """        private void Update()
        {
            Transform activeTransform = GetActiveTrackingTransform();"""
    new_update_entry = """        private void Update()
        {
            Transform activeTransform = GetActiveTrackingTransform();
""" + button_code
    assert old_update_entry in content_vhm, "old_update_entry not found in VRHandMover.cs"
    content_vhm = content_vhm.replace(old_update_entry, new_update_entry, 1)

# Add helper methods before closing class brace
helper_methods = """
        /// <summary>
        /// ปุ่ม X: Set origin point (ทั้ง center point ใน unity และ home ของแขนกล)
        /// </summary>
        public void ExecuteSetOriginAndHome()
        {
            RecenterAnchor();
            if (gripperBridge != null)
            {
                gripperBridge.RequestHome();
            }
            if (isTeleoperationPaused)
            {
                isTeleoperationPaused = false;
            }
            TriggerLeftControllerHaptic(0.6f, 0.15f);
            Debug.Log("[VRHandMover] [Button X] Set Origin Point (Center Point & Safe Home) executed!");
        }

        /// <summary>
        /// ปุ่ม Y: Pause / Resume การเคลื่อนไหวแขนกล (หยุดส่งพิกัด / กลับมาเคลื่อนไหวตาม)
        /// </summary>
        public void ToggleTeleoperationPause()
        {
            isTeleoperationPaused = !isTeleoperationPaused;
            if (isTeleoperationPaused)
            {
                if (gripperBridge != null)
                {
                    gripperBridge.SetTargetVelocity(0f, 0f, 0f, 0f, 0f, 0f);
                }
                TriggerLeftControllerHaptic(0.4f, 0.12f);
                Debug.Log("[VRHandMover] [Button Y] ⏸️ ROBOT TELEOPERATION PAUSED");
            }
            else
            {
                SyncAnchorOnResume();
                TriggerLeftControllerHaptic(0.8f, 0.18f);
                Debug.Log("[VRHandMover] [Button Y] ▶️ ROBOT TELEOPERATION RESUMED");
            }
        }

        /// <summary>
        /// ซิงค์ Anchor ใหม่เมื่อ Unpause เพื่อไม่ให้แขนกลกระตุกหรือพุ่งตามมือ (Zero Jerk Resume)
        /// </summary>
        public void SyncAnchorOnResume()
        {
            Transform activeTransform = GetActiveTrackingTransform();
            if (activeTransform == null) return;

            anchorHandWorldPos = activeTransform.position;
            if (useHeadsetOrientation && headTransform != null)
            {
                anchorHeadYaw = Quaternion.Euler(0f, headTransform.eulerAngles.y, 0f);
            }
            anchorHandRot = Quaternion.Inverse(anchorHeadYaw) * activeTransform.rotation;

            if (hasReceivedRobotPose)
            {
                anchorRobotPos = actualRobotPos;
                anchorRobotRot = actualRobotRot;
                currentTargetPos = actualRobotPos;
                currentTargetRotAxisAngle = actualRobotRot;
            }
            else
            {
                anchorRobotPos = homeRobotPos;
                anchorRobotRot = homeRobotRotAxisAngle;
                currentTargetPos = homeRobotPos;
                currentTargetRotAxisAngle = homeRobotRotAxisAngle;
            }

            posFilter.Reset();
            rotFilter.Reset();
            hasPrevState = false;

            if (starterPoint != null)
            {
                starterPoint.UpdateAnchor(anchorHandWorldPos, anchorHeadYaw, anchorRobotPos);
            }
        }

        /// <summary>
        /// ปุ่มกดตรง Joy Stick: เขย่าสารอัตโนมัติ 3 รอบ (คว่ำ 180° และหงายกลับ -180°)
        /// </summary>
        public void ExecuteAutoShake()
        {
            if (gripperBridge != null)
            {
                gripperBridge.RequestShake();
            }
            TriggerLeftControllerHaptic(0.7f, 0.25f);
            Debug.Log("[VRHandMover] [Thumbstick Click] 🧪 Automated Test Tube Shake (3 cycles) requested!");
        }

        /// <summary>
        /// ส่ง Haptic สั่นยืนยันที่รีโมทซ้าย
        /// </summary>
        public void TriggerLeftControllerHaptic(float amplitude, float duration)
        {
            try
            {
                StartCoroutine(LeftControllerHapticRoutine(amplitude, duration));
            }
            catch { }
        }

        private System.Collections.IEnumerator LeftControllerHapticRoutine(float amplitude, float duration)
        {
            try { OVRInput.SetControllerVibration(amplitude, amplitude, OVRInput.Controller.LTouch); } catch { }
            yield return new WaitForSeconds(duration);
            try { OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.LTouch); } catch { }
        }
    }
}
"""

if "ExecuteSetOriginAndHome" not in content_vhm or "SyncAnchorOnResume" not in content_vhm:
    # Replace last closing braces
    last_brace_idx = content_vhm.rfind("}")
    prev_brace_idx = content_vhm.rfind("}", 0, last_brace_idx)
    content_vhm = content_vhm[:prev_brace_idx] + helper_methods

with open(path_vhm, "w", encoding="utf-8") as f:
    f.write(content_vhm)

print("Updated VRHandMover.cs successfully")


# 2. Update HandDebugHUD.cs
path_hud = r"C:\Users\dhamm\VR\Assets\Scripts\HandDebugHUD.cs"
with open(path_hud, "r", encoding="utf-8") as f:
    content_hud = f.read()

if "isTeleoperationPaused" not in content_hud:
    old_hud_top = """            GUILayout.BeginArea(new Rect(20, 20, 460, 710), GUI.skin.box);
            GUILayout.Label("VR-HaptiLab | Hazardous Chemical Mixing", headerStyle);
            GUILayout.Space(2);"""

    new_hud_top = """            GUILayout.BeginArea(new Rect(20, 20, 460, 740), GUI.skin.box);
            GUILayout.Label("VR-HaptiLab | Hazardous Chemical Mixing", headerStyle);
            GUILayout.Space(2);

            // Left Remote Pause Status Banner
            if (vrHandMover != null && vrHandMover.isTeleoperationPaused)
            {
                GUI.color = new Color(1f, 0.45f, 0.15f);
                GUILayout.Box("⏸️ ROBOT TELEOPERATION PAUSED\\n(กดปุ่ม [Y] บนรีโมทซ้ายเพื่อกลับมาเคลื่อนไหวตาม)", warningStyle);
                GUI.color = Color.white;
                GUILayout.Space(3);
            }"""

    assert old_hud_top in content_hud, "old_hud_top not found in HandDebugHUD.cs"
    content_hud = content_hud.replace(old_hud_top, new_hud_top, 1)

# Add Left Remote help note in HUD bottom if not present
if "Left Remote: [X]" not in content_hud:
    old_hud_help = """            GUILayout.EndArea();
        }"""
    new_hud_help = """            GUILayout.Space(4);
            GUI.color = new Color(0.7f, 0.9f, 1f);
            GUILayout.Label("🎮 Left Remote: [X] Set Origin/Home | [Y] Pause/Resume | [L-Stick] เขย่าสาร 3 รอบ", subLabelStyle);
            GUI.color = Color.white;
            GUILayout.EndArea();
        }"""
    assert old_hud_help in content_hud, "old_hud_help not found in HandDebugHUD.cs"
    content_hud = content_hud.replace(old_hud_help, new_hud_help, 1)

with open(path_hud, "w", encoding="utf-8") as f:
    f.write(content_hud)

print("Updated HandDebugHUD.cs successfully")
