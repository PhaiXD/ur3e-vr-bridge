using System;
using UnityEngine;
using UnityEngine.UI;

namespace UR3eVR
{
    /// <summary>
    /// VRWristTelemetry: Apple Watch / Cyberpunk style World-Space Telemetry HUD mounted on the Left Wrist.
    /// Automatically detects when user rotates their left wrist to glance at it (like checking a smartwatch)
    /// and smoothly fades in live UR3e status: Connection, Speed, Gripper Lock, Force, and Control Mode.
    /// </summary>
    public class VRWristTelemetry : MonoBehaviour
    {
        public static VRWristTelemetry Instance { get; private set; }

        [Header("Target Tracking References")]
        public Transform leftWristAnchor;
        public Transform headTransform;

        [Header("Data References")]
        public GripperBridge gripperBridge;
        public VRHandMover vrHandMover;
        public UR3eStarterPoint starterPoint;
        public PinchDetector pinchDetector;

        [Header("Wrist Gaze Activation Settings")]
        [Tooltip("มุมระหว่างหน้าปัดนาฬิกากับสายตาผู้ใช้ (องศา) ที่จะเริ่มแสดงผล")]
        [Range(30f, 85f)]
        public float glanceAngleThreshold = 65f;

        [Tooltip("มุมที่จะซ่อนหน้าจอเมื่อหันข้อมือออก")]
        [Range(60f, 100f)]
        public float hideAngleThreshold = 80f;

        [Header("Controller Mode Placement (Left Remote)")]
        [Tooltip("ตำแหน่งหน้าปัดบนข้อมือเมื่อถือรีโมทซ้าย (ลอยอยู่หลังด้ามรีโมท ไม่ซ้อนกับโมเดล)")]
        public Vector3 controllerWristOffset = new Vector3(-0.015f, 0.075f, -0.15f);
        public Vector3 controllerRotationOffset = new Vector3(60f, -115f, -20f);

        [Header("Bare Hand Mode Placement (Optical Tracking)")]
        [Tooltip("ตำแหน่งหน้าปัดบนข้อมือเมื่อเป็นมือเปล่า")]
        public Vector3 handWristOffset = new Vector3(0.00f, 0.055f, -0.06f);
        public Vector3 handRotationOffset = new Vector3(60f, -115f, -20f);

        [Header("Display Alignment")]
        [Tooltip("ปรับหน้าจอให้หันเข้าหาสายตาผู้ใช้โดยอัตโนมัติเมื่อพลิกข้อมือมอง เพื่อให้อ่านง่าย 100% ตัวหนังสือไม่กลับด้าน")]
        public bool alignToEyesOnGlance = true;

        [Header("HUD Scale")]
        public float hudScale = 0.00045f;

        [Header("UI References")]
        private Canvas canvas;
        private CanvasGroup canvasGroup;
        private Text robotStatusText;
        private Text modeBadgeText;
        private Text posAndErrorText;
        private Text speedText;
        private Text gripperText;
        private Text forceAndStateText;
        private Image speedBarFill;
        private Image bgImg;
        private Outline bgOl;
        private Font hudFont;
        private Sprite roundedRectSprite;

        private bool isVisible = false;
        private float currentAlpha = 0f;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            LoadResources();
            BuildWristHUD();
        }

        private void Start()
        {
            FindReferences();
            SetTheme(PlayerPrefs.GetInt("VR_UI_DARK_MODE", 1) == 1);
        }

        private void LoadResources()
        {
            try { hudFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
#if UNITY_EDITOR
            if (roundedRectSprite == null)
                roundedRectSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/JetXR/UI Kit For Vision Pro OS/Runtime/Sprites/Buttons/RoundedRectBackground.png");
#endif
            if (roundedRectSprite == null) roundedRectSprite = Resources.Load<Sprite>("VisionUI/RoundedRectBackground");
        }

        private void FindReferences()
        {
            if (gripperBridge == null) gripperBridge = FindAnyObjectByType<GripperBridge>();
            if (vrHandMover == null) vrHandMover = FindAnyObjectByType<VRHandMover>();
            if (starterPoint == null) starterPoint = FindAnyObjectByType<UR3eStarterPoint>();
            if (pinchDetector == null) pinchDetector = FindAnyObjectByType<PinchDetector>();

            var rig = FindAnyObjectByType<OVRCameraRig>();
            if (rig != null)
            {
                if (headTransform == null) headTransform = rig.centerEyeAnchor;
                if (leftWristAnchor == null)
                {
                    leftWristAnchor = rig.leftControllerInHandAnchor ?? rig.leftControllerAnchor ?? rig.leftHandAnchor;
                }
            }

            if (leftWristAnchor == null)
            {
                string[] possible = { "LeftControllerInHandAnchor", "LeftControllerAnchor", "LeftHandAnchor" };
                foreach (var p in possible)
                {
                    var found = GameObject.Find(p);
                    if (found != null) { leftWristAnchor = found.transform; break; }
                }
            }

            if (headTransform == null && Camera.main != null)
            {
                headTransform = Camera.main.transform;
            }
        }

        private void BuildWristHUD()
        {
            GameObject hudObj = new GameObject("WristTelemetry_Canvas");
            hudObj.transform.SetParent(transform, false);

            canvas = hudObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            canvasGroup = hudObj.AddComponent<CanvasGroup>();
            canvasGroup.alpha = 0f;

            RectTransform cRT = hudObj.GetComponent<RectTransform>();
            cRT.sizeDelta = new Vector2(250f, 175f);
            cRT.localScale = Vector3.one * hudScale;

            // Background Frosted Obsidian Card
            GameObject bgObj = new GameObject("HUD_Background");
            bgObj.transform.SetParent(hudObj.transform, false);
            RectTransform bgRT = bgObj.AddComponent<RectTransform>();
            bgRT.anchorMin = Vector2.zero;
            bgRT.anchorMax = Vector2.one;
            bgRT.offsetMin = Vector2.zero;
            bgRT.offsetMax = Vector2.zero;

            bgImg = bgObj.AddComponent<Image>();
            if (roundedRectSprite != null)
            {
                bgImg.sprite = roundedRectSprite;
                bgImg.type = Image.Type.Sliced;
            }
            bgImg.material = null;
            bgImg.color = new Color(0.04f, 0.07f, 0.12f, 0.95f);

            bgOl = bgObj.AddComponent<Outline>();
            bgOl.effectColor = new Color(0.0f, 0.85f, 1.0f, 0.45f);
            bgOl.effectDistance = new Vector2(1f, -1f);

            // Container with VerticalLayoutGroup
            GameObject content = new GameObject("ContentContainer");
            content.transform.SetParent(bgObj.transform, false);
            RectTransform contRT = content.AddComponent<RectTransform>();
            contRT.anchorMin = Vector2.zero;
            contRT.anchorMax = Vector2.one;
            contRT.offsetMin = new Vector2(10f, 8f);
            contRT.offsetMax = new Vector2(-10f, -8f);

            var vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 4;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            // 1. Top Row: Robot Connection & Mode Badges
            GameObject topRow = new GameObject("TopRow");
            topRow.transform.SetParent(content.transform, false);
            var hlgTop = topRow.AddComponent<HorizontalLayoutGroup>();
            hlgTop.childForceExpandWidth = true;
            hlgTop.childForceExpandHeight = false;

            robotStatusText = CreateText(topRow.transform, "● UR3e: DISCONNECTED", 12, new Color(1.0f, 0.3f, 0.3f), TextAnchor.MiddleLeft);
            robotStatusText.fontStyle = FontStyle.Bold;

            modeBadgeText = CreateText(topRow.transform, "[REMOTE]", 11, new Color(0.7f, 0.85f, 1.0f), TextAnchor.MiddleRight);

            // 2. Robot Position & Safe Home Row
            posAndErrorText = CreateText(content.transform, "📍 [OFF-HOME] 0, 0, 0 mm (Δ0mm)", 11, new Color(0.80f, 0.95f, 1.0f), TextAnchor.MiddleLeft);

            // 3. Speed Display with Mini Bar
            GameObject speedRow = new GameObject("SpeedRow");
            speedRow.transform.SetParent(content.transform, false);
            var hlgSpeed = speedRow.AddComponent<HorizontalLayoutGroup>();
            hlgSpeed.spacing = 6;
            hlgSpeed.childForceExpandWidth = false;
            hlgSpeed.childForceExpandHeight = false;

            speedText = CreateText(speedRow.transform, "⚡ SPD: 100%", 12, Color.white, TextAnchor.MiddleLeft);

            // Mini Speed Bar Track
            GameObject sBar = new GameObject("SpeedBarTrack");
            sBar.transform.SetParent(speedRow.transform, false);
            RectTransform sbarRT = sBar.AddComponent<RectTransform>();
            sbarRT.sizeDelta = new Vector2(90f, 10f);
            var leSBar = sBar.AddComponent<LayoutElement>();
            leSBar.minWidth = 90f;
            leSBar.preferredWidth = 90f;
            leSBar.minHeight = 10f;
            leSBar.preferredHeight = 10f;
            Image sbarImg = sBar.AddComponent<Image>();
            sbarImg.color = new Color(0.15f, 0.20f, 0.28f, 0.95f);

            GameObject sFill = new GameObject("SpeedBarFill");
            sFill.transform.SetParent(sBar.transform, false);
            RectTransform sfillRT = sFill.AddComponent<RectTransform>();
            sfillRT.anchorMin = Vector2.zero;
            sfillRT.anchorMax = new Vector2(1f, 1f);
            sfillRT.offsetMin = Vector2.zero;
            sfillRT.offsetMax = Vector2.zero;
            speedBarFill = sFill.AddComponent<Image>();
            speedBarFill.color = new Color(1.0f, 0.25f, 0.25f, 0.95f);

            // 3. Third Row: Gripper Status & Lock Badge
            gripperText = CreateText(content.transform, "🖐️ GRIP: 0/255 | 🔓 FREE", 12, Color.white, TextAnchor.MiddleLeft);

            // 4. Bottom Row: Force, Shaking, or Pause Banner
            forceAndStateText = CreateText(content.transform, "⚖️ Fz: +0.00 N | READY", 11, new Color(0.0f, 0.9f, 1.0f), TextAnchor.MiddleLeft);
        }

        private void LateUpdate()
        {
            bool isController = VRHandStereoFixer.IsLeftControllerInHand;

            // Pick appropriate tracking anchor based on mode
            Transform currentAnchor = null;
            var rig = FindAnyObjectByType<OVRCameraRig>();
            if (isController)
            {
                if (rig != null) currentAnchor = rig.leftControllerInHandAnchor ?? rig.leftControllerAnchor;
                if (currentAnchor == null) currentAnchor = GameObject.Find("LeftControllerAnchor")?.transform;
            }
            else
            {
                if (rig != null) currentAnchor = rig.leftHandAnchor;
                if (currentAnchor == null) currentAnchor = GameObject.Find("[BuildingBlock] Hand Tracking left")?.transform;
            }

            if (currentAnchor == null) currentAnchor = leftWristAnchor;
            if (currentAnchor == null || headTransform == null)
            {
                FindReferences();
                if (leftWristAnchor == null || headTransform == null) return;
                currentAnchor = leftWristAnchor;
            }

            // 1. Position on wrist (completely clear of controller body)
            Vector3 targetOffset = isController ? controllerWristOffset : handWristOffset;
            transform.position = currentAnchor.TransformPoint(targetOffset);

            // 2. Physical Wrist Glance Detection
            Vector3 activeRotEuler = isController ? controllerRotationOffset : handRotationOffset;
            Quaternion wristRot = currentAnchor.rotation * Quaternion.Euler(activeRotEuler);
            Vector3 wristScreenNormal = wristRot * Vector3.back; // FRONT of screen

            Vector3 toEyes = (headTransform.position - transform.position).normalized;
            float angle = Vector3.Angle(wristScreenNormal, toEyes);

            bool editorOverride = false;
            try { editorOverride = Input.GetKey(KeyCode.U); } catch { }

            if (angle < glanceAngleThreshold || editorOverride)
            {
                isVisible = true;
            }
            else if (angle > hideAngleThreshold && !editorOverride)
            {
                isVisible = false;
            }

            // 3. Rotation: If alignToEyesOnGlance and visible, gently face user's eyes for 100% legibility;
            // otherwise follow physical wrist rotation.
            if (alignToEyesOnGlance && (isVisible || currentAlpha > 0.05f))
            {
                Vector3 lookDir = transform.position - headTransform.position;
                if (lookDir.sqrMagnitude > 1e-4f)
                {
                    Quaternion targetEyeRot = Quaternion.LookRotation(lookDir, headTransform.up);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetEyeRot, Time.deltaTime * 14f);
                }
            }
            else
            {
                transform.rotation = wristRot;
            }

            // 4. Smooth Fade In / Out
            float targetAlpha = isVisible ? 1.0f : 0.0f;
            currentAlpha = Mathf.MoveTowards(currentAlpha, targetAlpha, Time.deltaTime * (isVisible ? 7f : 5f));
            if (canvasGroup != null) canvasGroup.alpha = currentAlpha;

            // 5. Only update live metrics when visible or fading
            if (currentAlpha > 0.01f)
            {
                UpdateMetrics();
            }
        }

        private void UpdateMetrics()
        {
            bool isConn = (gripperBridge != null && gripperBridge.isConnected);
            if (robotStatusText != null)
            {
                bool isPy = (VRPythonBridgeManager.Instance != null && VRPythonBridgeManager.Instance.IsRunning);
                string pyTag = isPy ? "<color=#00FFAA>[PY:ON]</color>" : "<color=#FF6666>[PY:OFF]</color>";
                robotStatusText.text = isConn ? $"● UR3e: ON | {pyTag}" : $"○ UR3e: OFF | {pyTag}";
                robotStatusText.color = isConn ? new Color(0.0f, 1.0f, 0.55f) : new Color(1.0f, 0.35f, 0.35f);
            }

            if (modeBadgeText != null)
            {
                bool isCtrl = (vrHandMover != null && vrHandMover.isUsingController);
                modeBadgeText.text = isCtrl ? "[TOUCH REMOTE]" : "[BARE HAND]";
            }

            if (posAndErrorText != null)
            {
                Vector3 actMm = (gripperBridge != null) ? gripperBridge.actualRobotPos * 1000f : Vector3.zero;
                Vector3 tgtMm = (vrHandMover != null) ? vrHandMover.currentTargetPos * 1000f : Vector3.zero;
                float posErrMm = Vector3.Distance(tgtMm, actMm);
                bool isHome = (starterPoint != null && starterPoint.isAtHome);
                string homeBadge = isHome ? "<color=#00FF88>HOME</color>" : "<color=#00D4FF>OFF-HOME</color>";
                posAndErrorText.text = $"📍 [{homeBadge}] {actMm.x:F0},{actMm.y:F0},{actMm.z:F0}mm (Δ{posErrMm:F0}mm)";
            }

            int spdPct = (gripperBridge != null) ? Mathf.Clamp(gripperBridge.currentSpeedPct, 0, 100) : Mathf.Clamp(PlayerPrefs.GetInt("UR3eVR_MotionSpeed", 100), 0, 100);
            if (speedText != null)
            {
                string maxTag = (spdPct >= 100) ? " <color=#FF3344>[MAX]</color>" : "";
                speedText.text = $"⚡ SPD: {spdPct}%{maxTag}";
            }
            if (speedBarFill != null)
            {
                float fillRatio = Mathf.Clamp01(spdPct / 100f);
                speedBarFill.rectTransform.anchorMax = new Vector2(fillRatio, 1f);
                if (spdPct < 50) speedBarFill.color = new Color(0.0f, 1.0f, 0.55f);
                else if (spdPct < 75) speedBarFill.color = new Color(1.0f, 0.80f, 0.0f);
                else speedBarFill.color = new Color(1.0f, 0.25f, 0.25f);
            }

            if (gripperText != null)
            {
                int gripPos = (gripperBridge != null) ? gripperBridge.actualGripperPos : 0;
                bool isLocked = (gripperBridge != null && gripperBridge.isGripperLocked);
                string lockBadge = isLocked ? "<color=#FFD700>🔒 LOCKED</color>" : "<color=#00E5FF>🔓 FREE</color>";
                string pinchInfo = (pinchDetector != null) ? $" ({pinchDetector.normalizedPinch * 100f:F0}%)" : "";
                gripperText.text = $"🖐️ GRIP: {gripPos:D3}/255{pinchInfo} | {lockBadge}";
            }

            if (forceAndStateText != null)
            {
                float force = (gripperBridge != null) ? gripperBridge.actualGripForce : 0.0f;
                bool isShaking = (gripperBridge != null && gripperBridge.isShaking);
                bool isPaused = (vrHandMover != null && vrHandMover.isTeleoperationPaused);

                string stateMsg;
                if (isShaking) stateMsg = "<color=#FF8800>🧪 SHAKING TUBE</color>";
                else if (isPaused) stateMsg = "<color=#FFFF00>⏸️ PAUSED</color>";
                else stateMsg = "<color=#00FF88>▶ TELEOP ACTIVE</color>";

                forceAndStateText.text = $"⚖️ Fz: {force:+0.00;-0.00;+0.00} N | {stateMsg}";
            }
        }

        public void SetTheme(bool darkMode)
        {
            if (bgImg != null)
            {
                bgImg.material = null;
                bgImg.color = darkMode ? new Color(0.04f, 0.07f, 0.12f, 0.95f) : new Color(0.92f, 0.94f, 0.98f, 0.96f);
            }
            if (bgOl != null)
            {
                bgOl.effectColor = darkMode ? new Color(0.0f, 0.85f, 1.0f, 0.45f) : new Color(0.18f, 0.26f, 0.38f, 0.60f);
            }
            Color textCol = darkMode ? Color.white : new Color(0.08f, 0.12f, 0.22f);
            if (robotStatusText != null) robotStatusText.color = textCol;
            if (speedText != null) speedText.color = textCol;
            if (gripperText != null) gripperText.color = textCol;
            if (forceAndStateText != null) forceAndStateText.color = textCol;
            if (posAndErrorText != null) posAndErrorText.color = darkMode ? new Color(0.75f, 0.85f, 0.95f) : new Color(0.15f, 0.25f, 0.35f);
        }

        private Text CreateText(Transform parent, string text, int fontSize, Color color, TextAnchor alignment)
        {
            GameObject tObj = new GameObject("Text");
            tObj.transform.SetParent(parent, false);
            Text t = tObj.AddComponent<Text>();
            t.material = null;
            t.font = hudFont;
            t.fontSize = fontSize;
            t.color = color;
            t.alignment = alignment;
            t.text = text;
            return t;
        }
    }
}
