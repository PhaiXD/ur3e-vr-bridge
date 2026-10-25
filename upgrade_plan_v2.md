# UR3e VR Teleoperation — Upgrade Plan v2 (11 Items)

> [!IMPORTANT]
> แผนงาน 11 ข้อ แบ่งเป็น 4 เฟส ตามลำดับ dependencies เพื่อให้แต่ละเฟส compile ผ่านและทดสอบได้ก่อนเข้าเฟสถัดไป

---

## Phase 1 — Bug Fixes & Quick Wins (ข้อ 7, 11, 3)

### ✅ Item 7: Fix Add Cam Spawn Position Accumulation
**ปัญหา**: `SpawnAdditionalWindow()` ใน [VRCameraWindow.cs](file:///C:/Users/dhamm/VR/Assets/Scripts/VRCameraWindow.cs#L1373-L1449) คำนวณ `offsetDist = 0.72f * Mathf.Max(1, ActiveWindows.Count - 1)` — เมื่อหน้าต่างเก่าถูกลบ `ActiveWindows.Count` ไม่ลดลง ทำให้ spawn ไกลขึ้นเรื่อยๆ

**แก้ไข**:
- เปลี่ยน offset logic ให้วาง relative to **head position** แทน `Instance.canvasObj.transform` โดยใช้ `CenterEyeAnchor`
- offset ระยะคงที่ 0.72m ไปทางขวาของหัวเสมอ ไม่ขึ้นกับจำนวน ActiveWindows สะสม
- สร้าง `List<VRCameraWindow>` ที่ remove ตัวเองเมื่อ `OnDestroy()` เพื่อให้ Count ถูกต้อง

**ไฟล์**: `VRCameraWindow.cs`

---

### ✅ Item 11: Fix Pause/Resume Re-triggering Set Origin
**ปัญหา**: `ToggleTeleoperationPause()` ใน [VRHandMover.cs](file:///C:/Users/dhamm/VR/Assets/Scripts/VRHandMover.cs#L724-L743) — เมื่อ Resume เรียก `SyncAnchorOnResume()` ซึ่งจะ re-anchor เสมอ

**วิเคราะห์**: `SyncAnchorOnResume()` (line 748-783) **ไม่ได้** reset ไปที่ `homeRobotPos` — มันใช้ `actualRobotPos` ถ้ามี `hasReceivedRobotPose`  
→ ดังนั้นมันควรจะ **ไม่** กลับไปที่ origin ถ้าหุ่นยนต์ส่ง pose กลับมาแล้ว  
→ **ปัญหาที่แท้จริง**: เมื่อ resume จะ re-anchor hand → ทำให้ velocity เป็น 0 → หุ่นหยุดแวบ → แล้ว hand ต้องขยับใหม่

**แก้ไข**:
- เมื่อ resume (**ไม่ใช่** กด set origin) ให้ `SyncAnchorOnResume()` เก็บ hand position + robot position ปัจจุบันเป็น anchor ใหม่แบบ zero-jerk โดย **ไม่ส่ง HOME**
- ตรวจสอบว่า `SyncAnchorOnResume()` ใช้ `hasReceivedRobotPose` path เสมอ (ถ้า robot connected)
- ลบ `gripperBridge.SendPause(false)` ออกจาก `ExecuteSetOriginAndHome()` ที่ไม่จำเป็น (pause state ควรแยก)

**ไฟล์**: `VRHandMover.cs`

---

### ✅ Item 3: Gripper Lock via Grip Button (Remote) แทน Pinch Detection
**ปัญหา**: ปัจจุบัน gripper lock ถูก trigger โดยการจีบมือซ้าย (LeftHandHomeTrigger ใน [LeftHandHomeTrigger.cs](file:///C:/Users/dhamm/VR/Assets/Scripts/LeftHandHomeTrigger.cs)) ต้องเปลี่ยนมาใช้ **Grip Button** บนรีโมทขวา (middle finger grip button)

**วิเคราะห์**: ปัจจุบัน `GripperBridge.cs` (line 247) ใช้ `OVRInput.Button.One` (A button) สำหรับ toggle lock

**แก้ไข**:
- เปลี่ยนจาก `OVRInput.Button.One` (A) → `OVRInput.Button.PrimaryHandTrigger` (Grip Button ปุ่มนิ้วกลาง) บนรีโมทขวา (`OVRInput.Controller.RTouch`)
- เพิ่ม visual feedback: วงกลม ring indicator แสดงสถานะ lock/unlock บน wrist telemetry (Item 6)
- เก็บ `LeftHandHomeTrigger.cs` ไว้แต่ปิด gripper lock ทาง pinch detection (ให้เหลือแค่ Home action)

**ไฟล์**: `GripperBridge.cs`, `LeftHandHomeTrigger.cs`

---

## Phase 2 — Gripper & Shaking Improvements (ข้อ 8, 9)

### ✅ Item 8: Local Shaking Without Returning to Home + Auto-Lock Gripper
**ปัญหา**: เมื่อเรียก `ExecuteAutoShake()` ใน [VRHandMover.cs](file:///C:/Users/dhamm/VR/Assets/Scripts/VRHandMover.cs#L788-L796) มันส่ง `gripperBridge.RequestShake()` ซึ่งส่ง `SHAKE` command ไป Python → Python สั่ง `movel()` ไปตำแหน่ง home แล้วหมุน

**แก้ไข (Python side)**: `test_move.py` 
- เปลี่ยน shake logic ให้ใช้ **current TCP position** แทน home position
- หมุน wrist (joint 5) ±180° ที่ตำแหน่ง TCP ปัจจุบัน
- ไม่ต้อง `movel()` ไป home ก่อน

**แก้ไข (Unity side)**: `GripperBridge.cs` + `VRHandMover.cs`
- เมื่อกด shake → auto-lock gripper ที่ force ปัจจุบัน
- ปิด teleoperation input ชั่วคราวขณะ shake (ป้องกันมือผู้ใช้ส่งค่าแทรก)
- เมื่อ shake เสร็จ (รับ response จาก Python) → unlock gripper กลับมาตาม input ของมือ

**ไฟล์**: `test_move.py`, `GripperBridge.cs`, `VRHandMover.cs`, `vr_bridge_server.py`

---

### ✅ Item 9: Gripper Deadzone / Hysteresis Threshold
**ปัญหา**: Gripper ส่งค่าไปตลอดทุกเฟรม แม้การเปลี่ยนแปลงเล็กน้อย → เปลืองแบนด์วิดท์ + สั่น

**วิเคราะห์**: ปัจจุบัน [GripperBridge.cs](file:///C:/Users/dhamm/VR/Assets/Scripts/GripperBridge.cs#L218-L223) มี `deadband` อยู่แล้ว (`Mathf.Abs(currentPos - lastSentPos) >= deadband`) 

**แก้ไข**:
- เพิ่ม **temporal hold filter**: ถ้าค่า gripper คงที่ (±threshold) เกิน 1 วินาที แล้วมีการเปลี่ยนเล็กน้อย (<3mm) → ส่งค่าใหม่ (สำหรับ precision grip)
- เพิ่ม `[SerializeField] int gripperDeadband = 5;` (ประมาณ 5/255 ≈ 3mm ของช่วง gripper 85mm) ค่าที่เหมาะสม
- เพิ่ม `float gripperHoldTime = 0f;` — timer ที่นับเวลา gripper position คงที่ เมื่อ timer > 1.0s → ลด deadband เป็น 2 เพื่อให้ precision mode ทำงาน
- เพิ่ม rate limit: ส่งค่า gripper ได้สูงสุด 10 ครั้ง/วินาที (100ms interval)

**ไฟล์**: `GripperBridge.cs`

---

## Phase 3 — Menu & UI Overhaul (ข้อ 1, 2, 4, 5, 10)

### ✅ Item 1: Button UI Magnetic Laser Snapping
**ปัญหา**: เส้น laser เอียงๆ ยากต่อการกดปุ่ม UI เล็กๆ ใน VR

**แก้ไข**: เพิ่ม **magnetic snap** ใน [VRRemoteLaserPointer.cs](file:///C:/Users/dhamm/VR/Assets/Scripts/VRRemoteLaserPointer.cs#L383-L412)
- เมื่อ raycast hit ใกล้ปุ่ม (ภายในระยะ margin 15% ของขนาดปุ่ม) → ดูดเส้น laser ให้ endpoint snap ไปกลางปุ่ม
- ใช้ `marginRatio = 0.15f` ใน `RaycastRectTransformQuad()` สำหรับ Selectables (ปัจจุบันเป็น 0.0f)
- เมื่อ snap แล้ว ให้ laser endpoint ค่อยๆ lerp ไปที่จุดกลางปุ่มด้วย speed 20f * deltaTime
- เพิ่ม reticle scale pulse เมื่อ hover บนปุ่ม (1.0 → 1.3 → 1.0) เพื่อ visual feedback

**ไฟล์**: `VRRemoteLaserPointer.cs`

---

### ✅ Item 2: Window Resize Handles (Edge Drag to Scale)
**แก้ไข**: เพิ่ม resize handles ที่ขอบ UI window
- สร้าง 4 invisible RectTransform handles ที่ 4 มุม + 4 ขอบ ของ canvas
- เมื่อ laser hit handle → เปลี่ยน reticle เป็น resize cursor
- Drag: ปรับ `canvasRT.localScale` ตาม drag delta (min 0.6x, max 2.0x)
- เพิ่ม component `VRWindowResizer.cs` ใหม่ ที่ attach กับ resize handles

**ไฟล์**: `VRWindowResizer.cs` (ใหม่), `VRCameraWindow.cs`, `VRMenuManager.cs`

---

### ✅ Item 4: Menu Restructure — Settings + Speed Panels
**ปัญหา**: เมนูมี: Set Origin, Open New Cam, Open Log, Background → ต้องเปลี่ยนเป็น: Set Origin, Add Cam, Open Log, **Settings**, **Speed**

**แก้ไข**: ใน [VRMenuManager.cs](file:///C:/Users/dhamm/VR/Assets/Scripts/VRMenuManager.cs)

**4a. ลบ Background จากเมนูหลัก** (ย้ายไป Settings → Appearance)
**4b. เพิ่มปุ่ม Settings** → เปิด panel ใหม่:
  - **Control** tab: Set Keybind (แสดง current keybinds + ปุ่มเปลี่ยน)
  - **Appearance** tab: Dark/Light mode toggle + Skybox selector (ย้ายจาก Background panel เดิม)

**4c. เพิ่มปุ่ม Speed** → เปิด panel ใหม่:
  - **Slider Bar 0-100%** (mapping: 0% = Python `speed_level=0`, 100% = Python `speed_level=3.00`)
  - Snap points ที่ 0%, 25%, 50%, 75%, 100%
  - สี slider track:
    - `0-49%` = **สีเขียว** (safe)
    - `50-74%` = **สีเหลือง** (caution ⚠️)
    - `75-100%` = **สีแดง** (danger 🔴)
  - ส่ง `SPEED <level>` command ไป Python
  - แสดงค่าปัจจุบันเป็น text: e.g. `Speed: 75% ⚠️`

**Protocol addition**: เพิ่ม `SPEED <pct>` command ใน:
  - `GripperBridge.cs` → `SendSpeed(int pct)` method
  - `vr_bridge_server.py` → parse `SPEED` command → set `speed_level`
  - `test_move.py` → read `speed_level` from server

**4d. เปลี่ยนชื่อ**: "Open New Cam" → "Add Cam"

**ไฟล์**: `VRMenuManager.cs`, `GripperBridge.cs`, `vr_bridge_server.py`, `test_move.py`

---

### ✅ Item 5: Modern Rounded UI Headers & Close Buttons
**ปัญหา**: Header bar เหลี่ยม + ปุ่ม X กากบาทดูไม่สวย

**แก้ไข**: ปรับ `CreateHeader()` ใน [VRMenuManager.cs](file:///C:/Users/dhamm/VR/Assets/Scripts/VRMenuManager.cs#L1576-L1648)
- ใช้ `roundedRectSprite` + `visionElementMaterial` สำหรับ header background (แทน raw Image)
- เปลี่ยนปุ่ม X เป็น `crossIconSprite` (มี sprite อยู่แล้ว) + rounded background
- เพิ่ม padding ให้ header ไม่ชิดขอบ
- เปลี่ยน title names:
  - "Quick Menu" → ไม่ต้องเปลี่ยน (เป็น Main Menu header)
  - "Cam Feed" → "Cam Select"
  - Header font size ปรับให้ proportional
- เพิ่ม subtle outline glow ที่ header + close button hover effect

**ไฟล์**: `VRMenuManager.cs`, `VRCameraWindow.cs`

---

### ✅ Item 10: Joystick Push/Pull for UI Window Distance
**ปัญหา**: ลากเข้า/ออก UI ด้วยการดึงมือลำบาก

**แก้ไข**: ใน [VRRemoteLaserPointer.cs](file:///C:/Users/dhamm/VR/Assets/Scripts/VRRemoteLaserPointer.cs) + [VRWindowDragger.cs](file:///C:/Users/dhamm/VR/Assets/Scripts/VRWindowDragger.cs)
- เมื่อ `activeDraggingWindow != null` (กำลัง grab window) → อ่าน Left Thumbstick Y-axis:
  - Push forward (Y > 0.3) → เพิ่ม `activeDragDistance` (ไกลขึ้น)
  - Pull back (Y < -0.3) → ลด `activeDragDistance` (ใกล้ขึ้น)
  - Speed: 1.5m/s * thumbstick magnitude
  - Clamp distance: 0.3m to 5.0m
- อัปเดต window position ตาม `rayOrigin + rayDir * activeDragDistance`

**ไฟล์**: `VRRemoteLaserPointer.cs`

---

## Phase 4 — Wrist Telemetry HUD (ข้อ 6)

### ✅ Item 6: Wrist-Mounted Telemetry Display
**ปัญหา**: ข้อมูล CONNECT/DISCONNECT, Speed, Grip, Mode อยู่ใน Quick Menu → ต้องย้ายมาที่ข้อมือ

**แก้ไข**: สร้าง `VRWristTelemetry.cs` ใหม่
- สร้าง World Space Canvas ขนาดเล็ก (200x120 units, localScale 0.0005f) 
- Attach ที่ `LeftHandAnchor` หรือ `LeftControllerAnchor` (ข้อมือซ้าย)
- **Show/Hide** ตาม wrist angle:
  - วัดมุมระหว่าง `-wrist.forward` กับ `head.forward`
  - ถ้ามุม < 60° (หันข้อมือขึ้นมาดู) → แสดง canvas (fade in 0.15s)
  - ถ้ามุม > 80° → ซ่อน (fade out 0.2s)
- แสดงข้อมูล:
  - `UR3e: CONNECTED` / `DISCONNECTED` (สีเขียว/แดง)
  - `Speed: 75%` (+ color bar)
  - `Grip: 128/255 | LOCKED 🔒` / `FREE`
  - `Mode: Touch Remote` / `Bare Hand`
- อ่านค่าจาก `GripperBridge` + `VRHandMover` ทุกเฟรม
- ลบ status text ออกจาก `UpdateMainStatusText()` ใน VRMenuManager (ย้ายมาที่นี่)

**ไฟล์**: `VRWristTelemetry.cs` (ใหม่), `VRHandMover.cs` (attach component), `VRMenuManager.cs` (ลบ status)

---

## Summary — Execution Order

| Phase | Items | Files Modified | Files Created | Est. LOC |
|-------|-------|----------------|---------------|----------|
| **1** | 7, 11, 3 | `VRCameraWindow.cs`, `VRHandMover.cs`, `GripperBridge.cs`, `LeftHandHomeTrigger.cs` | — | ~80 |
| **2** | 8, 9 | `test_move.py`, `vr_bridge_server.py`, `GripperBridge.cs`, `VRHandMover.cs` | — | ~120 |
| **3** | 1, 2, 4, 5, 10 | `VRRemoteLaserPointer.cs`, `VRMenuManager.cs`, `VRCameraWindow.cs`, `GripperBridge.cs`, `vr_bridge_server.py`, `test_move.py` | `VRWindowResizer.cs` | ~600 |
| **4** | 6 | `VRHandMover.cs`, `VRMenuManager.cs` | `VRWristTelemetry.cs` | ~250 |

> [!TIP]
> **Speed Mapping (ข้อ 4)**:
> Python ใช้ `speed_level` จาก 0.25 ถึง 3.00 (25% ถึง 300%)
> ผู้ใช้ต้องการ UI แสดงเป็น 0-100% โดย:
> - UI 0% = Python 0.00 (หยุด)
> - UI 25% = Python 0.75
> - UI 50% = Python 1.50
> - UI 75% = Python 2.25
> - UI 100% = Python 3.00
> สูตร: `pythonSpeedLevel = (uiPercent / 100.0) * 3.0`

> [!WARNING]
> **ข้อควรระวัง**:
> - ปุ่ม Grip button (Item 3) บน Meta Quest Touch Remote เป็นปุ่มเดียวกับที่ใช้ grab UI (VRWindowDragger) → ต้องแยก logic: **มือขวา = gripper lock, มือซ้าย = UI grab**
> - Item 8 (shake) ต้องแก้ทั้ง Python และ Unity → ต้อง test ทั้ง 2 ฝั่ง
> - Item 4 (speed) ต้องเพิ่ม protocol command ใหม่ `SPEED` ใน server
