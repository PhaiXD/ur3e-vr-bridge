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

        [Header("Wrist Gaze Activation Settings")]
        [Tooltip("มุมระหว่างหน้าปัดนาฬิกากับสายตาผู้ใช้ (องศา) ที่จะเริ่มแสดงผล")]
        [Range(30f, 85f)]
        public float glanceAngleThreshold = 65f;

        [Tooltip("มุมที่จะซ่อนหน้าจอเมื่อหันข้อมือออก")]
        [Range(60f, 100f)]
        public float hideAngleThreshold = 80f;

        [Header("HUD Placement Offset")]
        public Vector3 wristOffset = new Vector3(0.015f, 0.045f, -0.055f);
        public Vector3 wristRotationOffset = new Vector3(-35f, 90f, 90f);
        public float hudScale = 0.00045f;

        [Header("UI References")]
        private Canvas canvas;
        private CanvasGroup canvasGroup;
        private Text robotStatusText;
        private Text modeBadgeText;
        private Text speedText;
        private Text gripperText;
        private Text forceAndStateText;
        private Image speedBarFill;
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
            cRT.sizeDelta = new Vector2(240f, 150f);
            cRT.localScale = Vector3.one * hudScale;

            // Background Frosted Obsidian Card
            GameObject bgObj = new GameObject("HUD_Background");
            bgObj.transform.SetParent(hudObj.transform, false);
            RectTransform bgRT = bgObj.AddComponent<RectTransform>();
            bgRT.anchorMin = Vector2.zero;
            bgRT.anchorMax = Vector2.one;
            bgRT.offsetMin = Vector2.zero;
            bgRT.offsetMax = Vector2.zero;

            Image bgImg = bgObj.AddComponent<Image>();
            if (roundedRectSprite != null)
            {
                bgImg.sprite = roundedRectSprite;
                bgImg.type = Image.Type.Sliced;
            }
            bgImg.material = null;
            bgImg.color = new Color(0.04f, 0.07f, 0.12f, 0.95f);

            Outline bgOl = bgObj.AddComponent<Outline>();
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

            // 2. Middle Row: Speed Display with Mini Bar
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
            if (leftWristAnchor == null || headTransform == null)
            {
                FindReferences();
                if (leftWristAnchor == null || headTransform == null) return;
            }

            // Follow Left Wrist Position & Orientation
            transform.position = leftWristAnchor.TransformPoint(wristOffset);
            transform.rotation = leftWristAnchor.rotation * Quaternion.Euler(wristRotationOffset);

            // Check Smartwatch Glance Angle (vector from wrist to eyes vs wrist display normal)
            Vector3 displayNormal = transform.forward;
            Vector3 toEyes = (headTransform.position - transform.position).normalized;
            float angle = Vector3.Angle(displayNormal, toEyes);

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

            // Smooth Fade In / Out
            float targetAlpha = isVisible ? 1.0f : 0.0f;
            currentAlpha = Mathf.MoveTowards(currentAlpha, targetAlpha, Time.deltaTime * (isVisible ? 7f : 5f));
            if (canvasGroup != null) canvasGroup.alpha = currentAlpha;

            // Only update live metrics when visible or fading
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
                robotStatusText.text = isConn ? "● UR3e: CONNECTED" : "○ UR3e: DISCONNECTED";
                robotStatusText.color = isConn ? new Color(0.0f, 1.0f, 0.55f) : new Color(1.0f, 0.35f, 0.35f);
            }

            if (modeBadgeText != null)
            {
                bool isCtrl = (vrHandMover != null && vrHandMover.isUsingController);
                modeBadgeText.text = isCtrl ? "[TOUCH REMOTE]" : "[BARE HAND]";
            }

            int spdPct = (gripperBridge != null) ? gripperBridge.currentSpeedPct : 100;
            if (speedText != null)
            {
                speedText.text = $"⚡ SPD: {spdPct}%";
            }
            if (speedBarFill != null)
            {
                speedBarFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(spdPct / 100f), 1f);
                if (spdPct < 50) speedBarFill.color = new Color(0.0f, 1.0f, 0.55f);
                else if (spdPct < 75) speedBarFill.color = new Color(1.0f, 0.80f, 0.0f);
                else speedBarFill.color = new Color(1.0f, 0.25f, 0.25f);
            }

            if (gripperText != null)
            {
                int gripPos = (gripperBridge != null) ? gripperBridge.actualGripperPos : 0;
                bool isLocked = (gripperBridge != null && gripperBridge.isGripperLocked);
                string lockBadge = isLocked ? "<color=#FFD700>🔒 LOCKED</color>" : "<color=#00E5FF>🔓 FREE</color>";
                gripperText.text = $"🖐️ GRIP: {gripPos:D3}/255 | {lockBadge}";
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
