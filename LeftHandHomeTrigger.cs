using System;
using UnityEngine;

namespace UR3eVR
{
    /// <summary>
    /// LeftHandHomeTrigger: เมนูควบคุมด่วนเมื่อจีบมือซ้าย (Left Hand Quick Action Menu)
    /// 1.  SET HOME: สั่งหุ่นยนต์ UR3e กลับสู่ Safe Home พร้อม Reset จุด Center Point (Anchor) ใน Unity
    /// 2.  /  GRIPPER LOCK: ล็อกระดับการหนีบของ Gripper ไม่ให้เปลี่ยนตามนิ้วมือขวา (กดซ้ำที่ Icon เพื่อปลดล็อก)
    /// 
    /// รองรับการสั่งการ 3 รูปแบบ:
    /// - ใช้นิ้วชี้ขวาแตะที่ Icon โดยตรงในพื้นที่ 3D (Direct Touch / Poke)
    /// - จีบมือซ้ายค้างไว้แล้วเอียงมือซ้าย ซ้าย-ขวา เพื่อเลือก (One-handed Tilt & Hold Dwell)
    /// - กดปุ่มคีย์บอร์ด [H] สำหรับ Home และ [L] สำหรับ Gripper Lock (PC Editor Fallback)
    /// </summary>
    public class LeftHandHomeTrigger : MonoBehaviour
    {
        public enum ActionOption
        {
            None,
            Home,
            GripperLock
        }

        [Header("Target References")]
        public OVRHand leftOVRHand;
        public OVRSkeleton leftOVRSkeleton;
        public OVRHand rightOVRHand;
        public OVRSkeleton rightOVRSkeleton;
        public VRHandMover vrHandMover;
        public GripperBridge gripperBridge;
        public Transform headTransform;

        [Header("Menu & Button Settings")]
        [Tooltip("รัศมีของปุ่ม Icon แต่ละปุ่ม (เมตร)")]
        public float buttonRadius = 0.035f; // 3.5 cm

        [Tooltip("ระยะห่างจากปลายนิ้วชี้ขวาที่จะถือว่าแตะปุ่ม (เมตร)")]
        public float touchThreshold = 0.040f; // 4.0 cm

        [Tooltip("ระยะเวลาที่ต้องจีบมือซ้ายค้างเพื่อยืนยันแบบมือเดียว (วินาที)")]
        public float holdDuration = 1.3f;

        [Tooltip("ระยะเวลาที่เมนูลอยค้างอยู่หลังปล่อยมือซ้าย (วินาที) เพื่อให้มือขวาเอื้อมมากดได้")]
        public float menuStayDuration = 4.0f;

        [Tooltip("เกณฑ์ตรวจจับการจีบมือซ้าย (0.0 ถึง 1.0)")]
        [Range(0.3f, 0.95f)]
        public float pinchThreshold = 0.55f;

        [Header("Live Status (Read Only)")]
        public bool isLeftHandTracked = false;
        public bool isRightHandTracked = false;
        public bool isPinchingLeft = false;
        public bool isMenuOpen = false;
        public ActionOption currentHoveredOption = ActionOption.None;
        public float dwellTimer = 0f;
        [Range(0f, 1f)]
        public float dwellProgress = 0f;
        public bool isSuccessState = false;
        public string lastTriggeredAction = "";

        // Visual Hierarchy
        private GameObject menuRoot;
        private GameObject homeButtonObj;
        private LineRenderer homeBgCircle;
        private LineRenderer homeFillCircle;
        private TextMesh homeTextMesh;

        private GameObject lockButtonObj;
        private LineRenderer lockBgCircle;
        private LineRenderer lockFillCircle;
        private TextMesh lockTextMesh;

        private Transform leftThumbTip;
        private Transform leftIndexTip;
        private Transform rightIndexTip;

        private float menuTimer = 0f;
        private float touchCooldown = 0f;
        private float successFeedbackTimer = 0f;
        private bool isArmed = true;

        private const int SEGMENTS = 40;
        private const float BUTTON_SPACING = 0.065f; // 6.5 cm from center

        private void Start()
        {
            FindReferences();
            CreateMenuVisuals();
        }

        public void FindReferences()
        {
            if (vrHandMover == null) vrHandMover = FindAnyObjectByType<VRHandMover>();
            if (gripperBridge == null) gripperBridge = FindAnyObjectByType<GripperBridge>();

            if (headTransform == null)
            {
                if (Camera.main != null) headTransform = Camera.main.transform;
                else
                {
                    GameObject centerEye = GameObject.Find("CenterEyeAnchor");
                    if (centerEye != null) headTransform = centerEye.transform;
                }
            }

            // ค้นหาทั้งมือซ้ายและมือขวา
            OVRHand[] hands = FindObjectsByType<OVRHand>(FindObjectsSortMode.None);
            foreach (var h in hands)
            {
                if (h == null) continue;
                bool isLeft = (h.GetHand() == OVRPlugin.Hand.HandLeft);
                if (!isLeft)
                {
                    OVRSkeleton s = h.GetComponent<OVRSkeleton>();
                    if (s != null)
                    {
                        var skelType = s.GetSkeletonType();
                        isLeft = (skelType == OVRSkeleton.SkeletonType.HandLeft || skelType == OVRSkeleton.SkeletonType.XRHandLeft);
                    }
                }
                if (!isLeft)
                {
                    string hName = h.gameObject.name.ToLower();
                    if (h.transform.parent != null) hName += " " + h.transform.parent.name.ToLower();
                    isLeft = hName.Contains("left");
                }

                if (isLeft)
                {
                    if (leftOVRHand == null) leftOVRHand = h;
                    if (leftOVRSkeleton == null) leftOVRSkeleton = h.GetComponent<OVRSkeleton>();
                }
                else
                {
                    if (rightOVRHand == null) rightOVRHand = h;
                    if (rightOVRSkeleton == null) rightOVRSkeleton = h.GetComponent<OVRSkeleton>();
                }
            }
        }

        private void FindFingerTips()
        {
            // ปลายนิ้วมือซ้าย
            if (leftOVRSkeleton != null && leftOVRSkeleton.IsInitialized)
            {
                foreach (var bone in leftOVRSkeleton.Bones)
                {
                    if (bone == null || bone.Transform == null) continue;
                    string bName = bone.Transform.name;
                    if (bone.Id == OVRSkeleton.BoneId.XRHand_ThumbTip || bName.Contains("ThumbTip") || bName.Contains("thumb_tip"))
                        leftThumbTip = bone.Transform;
                    else if (bone.Id == OVRSkeleton.BoneId.XRHand_IndexTip || bName.Contains("IndexTip") || bName.Contains("index_tip"))
                        leftIndexTip = bone.Transform;
                }
            }

            // ปลายนิ้วชี้มือขวาสำหรับจิ้มแตะ
            if (rightOVRSkeleton != null && rightOVRSkeleton.IsInitialized)
            {
                foreach (var bone in rightOVRSkeleton.Bones)
                {
                    if (bone == null || bone.Transform == null) continue;
                    string bName = bone.Transform.name;
                    if (bone.Id == OVRSkeleton.BoneId.XRHand_IndexTip || bName.Contains("IndexTip") || bName.Contains("index_tip"))
                        rightIndexTip = bone.Transform;
                }
            }
        }

        private void Update()
        {
            if (leftOVRHand == null || rightOVRHand == null)
            {
                FindReferences();
            }

            if (leftThumbTip == null || leftIndexTip == null || rightIndexTip == null)
            {
                FindFingerTips();
            }

            isLeftHandTracked = (leftOVRHand != null && leftOVRHand.IsTracked);
            isRightHandTracked = (rightOVRHand != null && rightOVRHand.IsTracked);

            // 1. ตรวจจับการจีบมือซ้าย
            bool pinchingNow = false;
            if (isLeftHandTracked)
            {
                float strength = leftOVRHand.GetFingerPinchStrength(OVRHand.HandFinger.Index);
                bool mlPinch = leftOVRHand.GetFingerIsPinching(OVRHand.HandFinger.Index) || strength >= pinchThreshold;

                float distPinch = 0f;
                if (leftThumbTip != null && leftIndexTip != null)
                {
                    float d = Vector3.Distance(leftThumbTip.position, leftIndexTip.position);
                    distPinch = Mathf.InverseLerp(0.060f, 0.024f, d);
                }
                pinchingNow = mlPinch || (distPinch > 0.70f);
            }

            // Keyboard Fallback
            CheckKeyboardFallback();
            bool kHL = false; try { kHL = Input.GetKey(KeyCode.H) || Input.GetKey(KeyCode.L); } catch {} if (kHL)
            {
                pinchingNow = true;
            }

            isPinchingLeft = pinchingNow;

            // 2. จัดการสถานะการเปิด/ปิดเมนู
            if (isPinchingLeft)
            {
                isMenuOpen = true;
                menuTimer = menuStayDuration; // รีเซ็ตเวลานับถอยหลังการค้างเมนู
            }
            else if (menuTimer > 0f)
            {
                menuTimer -= Time.deltaTime;
                if (menuTimer <= 0f)
                {
                    isMenuOpen = false;
                }
            }

            if (touchCooldown > 0f)
            {
                touchCooldown -= Time.deltaTime;
            }

            if (successFeedbackTimer > 0f)
            {
                successFeedbackTimer -= Time.deltaTime;
                if (successFeedbackTimer <= 0f)
                {
                    isSuccessState = false;
                    lastTriggeredAction = "";
                }
            }

            // 3. ปรับตำแหน่งและทิศทางของเมนู
            if (isMenuOpen || isSuccessState)
            {
                UpdateMenuTransform();

                // 4. ตรวจจับการแตะด้วยนิ้วชี้ขวา (Direct Touch Interaction)
                CheckTouchInteraction();

                // 5. ตรวจจับการเลือกแบบมือเดียวด้วยการเอียงมือซ้ายและจีบค้าง (One-handed Dwell)
                CheckOneHandedDwell();

                // 6. อัปเดตการแสดงผลและสีสันของปุ่มทั้งสอง
                UpdateButtonsVisuals();
            }
            else
            {
                if (menuRoot != null && menuRoot.activeSelf)
                {
                    menuRoot.SetActive(false);
                }
                dwellTimer = 0f;
                dwellProgress = 0f;
                currentHoveredOption = ActionOption.None;
            }
        }

        private void CheckTouchInteraction()
        {
            if (rightIndexTip == null || touchCooldown > 0f) return;

            Vector3 touchPos = rightIndexTip.position;

            // ตรวจสอบระยะห่างกับปุ่ม Set Home
            if (homeButtonObj != null)
            {
                float dHome = Vector3.Distance(touchPos, homeButtonObj.transform.position);
                if (dHome <= touchThreshold)
                {
                    TriggerHomeAndRecenter();
                    touchCooldown = 0.5f;
                    return;
                }
            }

            // ตรวจสอบระยะห่างกับปุ่ม Gripper Lock
            if (lockButtonObj != null)
            {
                float dLock = Vector3.Distance(touchPos, lockButtonObj.transform.position);
                if (dLock <= touchThreshold)
                {
                    ToggleGripperLock();
                    touchCooldown = 0.5f;
                    return;
                }
            }
        }

        private void CheckOneHandedDwell()
        {
            if (!isPinchingLeft)
            {
                dwellTimer = 0f;
                dwellProgress = 0f;
                currentHoveredOption = ActionOption.None;
                isArmed = true;
                return;
            }

            // คำนวณทิศทางการเอียงของมือซ้าย
            ActionOption selectedTilt = ActionOption.None;
            if (leftOVRHand != null)
            {
                // ตรวจสอบการเอียงแกน Right ของมือซ้ายเทียบกับระนาบสายตา
                Vector3 handRight = leftOVRHand.transform.right;
                if (headTransform != null)
                {
                    handRight = headTransform.InverseTransformDirection(handRight);
                }

                if (handRight.y > 0.15f || handRight.x < -0.15f)
                {
                    selectedTilt = ActionOption.Home; // เอียงไปทางซ้าย -> Home
                }
                else if (handRight.y < -0.15f || handRight.x > 0.15f)
                {
                    selectedTilt = ActionOption.GripperLock; // เอียงไปทางขวา -> Lock
                }
            }

            currentHoveredOption = selectedTilt;

            if (currentHoveredOption != ActionOption.None && isArmed)
            {
                dwellTimer += Time.deltaTime;
                dwellProgress = Mathf.Clamp01(dwellTimer / holdDuration);

                if (dwellTimer >= holdDuration)
                {
                    if (currentHoveredOption == ActionOption.Home)
                    {
                        TriggerHomeAndRecenter();
                    }
                    else if (currentHoveredOption == ActionOption.GripperLock)
                    {
                        ToggleGripperLock();
                    }
                    dwellTimer = 0f;
                    dwellProgress = 1f;
                    isArmed = false; // ป้องกันการเรียกซ้ำจนกว่าจะปล่อยมือ
                }
            }
            else
            {
                dwellTimer = 0f;
                dwellProgress = 0f;
            }
        }

        private void CheckKeyboardFallback()
        {
            bool kH = false; try { kH = Input.GetKeyDown(KeyCode.H); } catch {} if (kH)
            {
                TriggerHomeAndRecenter();
            }
            bool kLG = false; try { kLG = Input.GetKeyDown(KeyCode.L) || Input.GetKeyDown(KeyCode.G); } catch {} if (kLG)
            {
                ToggleGripperLock();
            }
        }

        public void TriggerHomeAndRecenter()
        {
            Debug.Log("[LeftActionMenu]  Trigger SET HOME & RECENTER");

            // 1. สั่งหุ่นยนต์ UR3e กลับ Safe Home (ส่ง HOME ไป Python)
            if (gripperBridge != null)
            {
                gripperBridge.RequestHome();
            }

            // 2. Reset จุด Center / Anchor ของมือขวาใน Unity ทันที
            if (vrHandMover != null)
            {
                vrHandMover.RecenterAnchor();
            }

            isSuccessState = true;
            lastTriggeredAction = "HOME";
            successFeedbackTimer = 1.5f;
            menuTimer = 1.5f; // แสดงผลสำเร็จแล้วค่อยปิด
        }

        public void ToggleGripperLock()
        {
            bool locked = false;
            if (gripperBridge != null)
            {
                locked = gripperBridge.ToggleGripperLock();
            }

            Debug.Log(string.Format("[LeftActionMenu]  Toggle Gripper Lock: {0}", locked ? "LOCKED" : "UNLOCKED"));

            isSuccessState = true;
            lastTriggeredAction = locked ? "LOCKED" : "UNLOCKED";
            successFeedbackTimer = 1.5f;
            menuTimer = menuStayDuration; // ให้เมนูยังคงเปิดอยู่เพื่อให้ผู้ใช้เห็นสถานะ
        }

        private void CreateMenuVisuals()
        {
            if (menuRoot != null) return;

            menuRoot = new GameObject("LeftHand_ActionMenu");
            menuRoot.transform.SetParent(transform);

            Shader unlitShader = Shader.Find("UR3eVR/StereoUnlitColor");
            if (unlitShader == null) unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlitShader == null) unlitShader = Shader.Find("UR3eVR/URPHandOutline");
            if (unlitShader == null) unlitShader = Shader.Find("Sprites/Default");

            // ==================== 1. ปุ่ม Home (ซ้าย) ====================
            homeButtonObj = new GameObject("Btn_Home");
            homeButtonObj.transform.SetParent(menuRoot.transform);
            homeButtonObj.transform.localPosition = new Vector3(-BUTTON_SPACING, 0f, 0f);

            // เส้นขอบวงกลม Background
            GameObject homeBgObj = new GameObject("BG_Circle");
            homeBgObj.transform.SetParent(homeButtonObj.transform);
            homeBgCircle = homeBgObj.AddComponent<LineRenderer>();
            SetupCircleLine(homeBgCircle, unlitShader, buttonRadius, 0.003f, new Color(0.1f, 0.5f, 0.7f, 0.4f));

            // เส้น Progress Fill
            GameObject homeFillObj = new GameObject("Fill_Circle");
            homeFillObj.transform.SetParent(homeButtonObj.transform);
            homeFillCircle = homeFillObj.AddComponent<LineRenderer>();
            SetupFillLine(homeFillCircle, unlitShader, buttonRadius, 0.0045f);

            // ข้อความ 3D Text
            GameObject homeTextObj = new GameObject("Text_Home");
            homeTextObj.transform.SetParent(homeButtonObj.transform);
            homeTextObj.transform.localPosition = new Vector3(0f, 0f, -0.002f);
            homeTextObj.transform.localScale = Vector3.one * 0.0012f;
            homeTextMesh = homeTextObj.AddComponent<TextMesh>();
            SetupTextMesh(homeTextMesh, "SET HOME\n(UR3e + Center)");

            // ==================== 2. ปุ่ม Gripper Lock (ขวา) ====================
            lockButtonObj = new GameObject("Btn_Lock");
            lockButtonObj.transform.SetParent(menuRoot.transform);
            lockButtonObj.transform.localPosition = new Vector3(BUTTON_SPACING, 0f, 0f);

            // เส้นขอบวงกลม Background
            GameObject lockBgObj = new GameObject("BG_Circle");
            lockBgObj.transform.SetParent(lockButtonObj.transform);
            lockBgCircle = lockBgObj.AddComponent<LineRenderer>();
            SetupCircleLine(lockBgCircle, unlitShader, buttonRadius, 0.003f, new Color(0.1f, 0.5f, 0.7f, 0.4f));

            // เส้น Progress Fill
            GameObject lockFillObj = new GameObject("Fill_Circle");
            lockFillObj.transform.SetParent(lockButtonObj.transform);
            lockFillCircle = lockFillObj.AddComponent<LineRenderer>();
            SetupFillLine(lockFillCircle, unlitShader, buttonRadius, 0.0045f);

            // ข้อความ 3D Text
            GameObject lockTextObj = new GameObject("Text_Lock");
            lockTextObj.transform.SetParent(lockButtonObj.transform);
            lockTextObj.transform.localPosition = new Vector3(0f, 0f, -0.002f);
            lockTextObj.transform.localScale = Vector3.one * 0.0012f;
            lockTextMesh = lockTextObj.AddComponent<TextMesh>();
            SetupTextMesh(lockTextMesh, "GRIPPER\nFREE\n(Tap to Lock)");

            menuRoot.SetActive(false);
        }

        private void SetupCircleLine(LineRenderer lr, Shader sh, float r, float width, Color col)
        {
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.positionCount = SEGMENTS;
            lr.startWidth = width;
            lr.endWidth = width;
            lr.material = new Material(sh);
            lr.material.enableInstancing = true;
            lr.startColor = col;
            lr.endColor = col;
            for (int i = 0; i < SEGMENTS; i++)
            {
                float rad = (i / (float)SEGMENTS) * Mathf.PI * 2f;
                lr.SetPosition(i, new Vector3(Mathf.Cos(rad) * r, Mathf.Sin(rad) * r, 0f));
            }
        }

        private void SetupFillLine(LineRenderer lr, Shader sh, float r, float width)
        {
            lr.useWorldSpace = false;
            lr.loop = false;
            lr.positionCount = 0;
            lr.startWidth = width;
            lr.endWidth = width;
            lr.material = new Material(sh);
            lr.material.enableInstancing = true;
        }

        private void SetupTextMesh(TextMesh tm, string initialText)
        {
            tm.alignment = TextAlignment.Center;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.fontSize = 28;
            tm.fontStyle = FontStyle.Bold;
            tm.color = Color.cyan;
            tm.text = initialText;

            var mr = tm.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                Shader s = Shader.Find("UR3eVR/StereoTextShader");
                if (s != null)
                {
                    Texture fTex = (tm.font != null && tm.font.material != null) ? tm.font.material.mainTexture : null;
                    Material mat = new Material(s);
                    if (fTex != null) mat.mainTexture = fTex;
                    mat.enableInstancing = true;
                    mr.material = mat;
                }
            }
        }

        private void UpdateMenuTransform()
        {
            if (menuRoot == null) return;

            Vector3 targetPos = Vector3.zero;
            if (leftThumbTip != null && leftIndexTip != null)
            {
                targetPos = (leftThumbTip.position + leftIndexTip.position) * 0.5f + Vector3.up * 0.08f;
            }
            else if (leftOVRHand != null && leftOVRHand.transform != null)
            {
                targetPos = leftOVRHand.transform.position + leftOVRHand.transform.forward * 0.06f + Vector3.up * 0.08f;
            }
            else if (headTransform != null)
            {
                targetPos = headTransform.position + headTransform.forward * 0.50f - headTransform.right * 0.15f;
            }

            menuRoot.transform.position = targetPos;

            // หันหน้าเข้าหาศีรษะเสมอ (Billboard)
            if (headTransform != null)
            {
                Vector3 lookDir = menuRoot.transform.position - headTransform.position;
                if (lookDir.sqrMagnitude > 1e-4f)
                {
                    menuRoot.transform.rotation = Quaternion.LookRotation(lookDir);
                }
            }
        }

        private void UpdateButtonsVisuals()
        {
            if (menuRoot == null) return;
            if (!menuRoot.activeSelf) menuRoot.SetActive(true);

            bool isLocked = (gripperBridge != null && gripperBridge.isGripperLocked);
            int gripPct = (gripperBridge != null) ? Mathf.RoundToInt((gripperBridge.lockedGripPos / 255f) * 100f) : 0;

            // ==================== 1. อัปเดตปุ่ม Home ====================
            if (isSuccessState && lastTriggeredAction == "HOME")
            {
                homeTextMesh.text = "HOMED!\n(Center Reset)";
                homeTextMesh.color = Color.green;
                homeBgCircle.startColor = Color.green;
                homeBgCircle.endColor = Color.green;
                UpdateArcFill(homeFillCircle, 1f, Color.green);
            }
            else
            {
                bool isHomeHovered = (currentHoveredOption == ActionOption.Home);
                Color homeColor = isHomeHovered ? Color.yellow : Color.cyan;
                homeTextMesh.text = isHomeHovered ? string.Format("SET HOME\n[{0}%]", Mathf.RoundToInt(dwellProgress * 100f)) : "SET HOME\n(UR3e + Center)";
                homeTextMesh.color = homeColor;
                homeBgCircle.startColor = new Color(homeColor.r, homeColor.g, homeColor.b, 0.5f);
                homeBgCircle.endColor = new Color(homeColor.r, homeColor.g, homeColor.b, 0.5f);
                UpdateArcFill(homeFillCircle, isHomeHovered ? dwellProgress : 0f, homeColor);
            }

            // ==================== 2. อัปเดตปุ่ม Gripper Lock ====================
            if (isSuccessState && (lastTriggeredAction == "LOCKED" || lastTriggeredAction == "UNLOCKED"))
            {
                if (lastTriggeredAction == "LOCKED")
                {
                    lockTextMesh.text = string.Format("LOCKED!\n({0}%)", gripPct);
                    lockTextMesh.color = new Color(1f, 0.8f, 0.1f);
                    lockBgCircle.startColor = new Color(1f, 0.8f, 0.1f);
                    lockBgCircle.endColor = new Color(1f, 0.8f, 0.1f);
                    UpdateArcFill(lockFillCircle, 1f, new Color(1f, 0.8f, 0.1f));
                }
                else
                {
                    lockTextMesh.text = "UNLOCKED!\n(Live Tracking)";
                    lockTextMesh.color = Color.cyan;
                    lockBgCircle.startColor = Color.cyan;
                    lockBgCircle.endColor = Color.cyan;
                    UpdateArcFill(lockFillCircle, 1f, Color.cyan);
                }
            }
            else
            {
                bool isLockHovered = (currentHoveredOption == ActionOption.GripperLock);
                Color lockColor;

                if (isLocked)
                {
                    // สถานะล็อกอยู่: สีส้มทองสว่าง พร้อมบอกให้กดอีกครั้งเพื่อปลดล็อก
                    lockColor = isLockHovered ? Color.yellow : new Color(1f, 0.75f, 0.1f);
                    float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * 6f);
                    lockTextMesh.text = isLockHovered 
                        ? string.Format("UNLOCK?\n[{0}%]", Mathf.RoundToInt(dwellProgress * 100f))
                        : string.Format("LOCKED ({0}%)\n(Tap to Unlock)", gripPct);
                    lockTextMesh.color = lockColor * pulse;
                    lockBgCircle.startColor = lockColor;
                    lockBgCircle.endColor = lockColor;
                }
                else
                {
                    // สถานะปกติ (ยังไม่ล็อก)
                    lockColor = isLockHovered ? Color.yellow : new Color(0.2f, 0.8f, 1f);
                    lockTextMesh.text = isLockHovered 
                        ? string.Format("LOCK GRIP\n[{0}%]", Mathf.RoundToInt(dwellProgress * 100f))
                        : "GRIPPER\nFREE\n(Tap to Lock)";
                    lockTextMesh.color = lockColor;
                    lockBgCircle.startColor = new Color(lockColor.r, lockColor.g, lockColor.b, 0.5f);
                    lockBgCircle.endColor = new Color(lockColor.r, lockColor.g, lockColor.b, 0.5f);
                }

                UpdateArcFill(lockFillCircle, isLockHovered ? dwellProgress : 0f, lockColor);
            }
        }

        private void UpdateArcFill(LineRenderer lr, float pct, Color col)
        {
            if (lr == null) return;

            int activePoints = Mathf.Clamp(Mathf.RoundToInt(pct * SEGMENTS), 0, SEGMENTS);
            if (activePoints < 2)
            {
                lr.positionCount = 0;
            }
            else
            {
                lr.positionCount = activePoints + 1;
                for (int i = 0; i <= activePoints; i++)
                {
                    float rad = (Mathf.PI * 0.5f) - (i / (float)SEGMENTS) * Mathf.PI * 2f;
                    lr.SetPosition(i, new Vector3(Mathf.Cos(rad) * buttonRadius, Mathf.Sin(rad) * buttonRadius, 0f));
                }
            }

            lr.startColor = col;
            lr.endColor = col;
        }
    }
}
