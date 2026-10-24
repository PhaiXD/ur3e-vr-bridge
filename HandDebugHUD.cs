using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UR3eVR
{
    /// <summary>
    /// HandDebugHUD: Apple Vision Pro OS Style Floating Telemetry Card for Meta Quest VR
    /// - Styled using JetXR UI Kit For Vision Pro OS (frosted glass, pill grab bar, sleek typography)
    /// - Dual-eye stereo rendering in XR Single Pass Instanced (renders in BOTH eyes)
    /// - Accurate 3D Quad raycast hitbox for laser-based repositioning
    /// </summary>
    public class HandDebugHUD : MonoBehaviour
    {
        public static HandDebugHUD Instance { get; private set; }

        [Header("Components to Monitor")]
        public PinchDetector pinchDetector;
        public GripperBridge gripperBridge;
        public VRHandMover vrHandMover;
        public UR3eStarterPoint starterPoint;
        public SafetyBoundaryVisualizer safetyVisualizer;
        public script webcamScript;

        [Header("HUD Transform Settings")]
        public float hudDistance = 0.85f;
        public float hudSideOffset = -0.38f;
        public float hudHeightOffset = -0.05f;

        // UI Canvas References
        private GameObject hudCanvasObj;
        private Canvas hudCanvas;
        private Text titleText;
        private Text pauseBannerText;
        private Text telemetryText;
        private Image backgroundImage;
        private Transform centerEyeAnchor;
        private bool hasPositionedDefault = false;

        // Vision Pro OS UI Kit Assets
        private Material visionWindowMaterial;
        private Material visionElementMaterial;
        private Sprite windowGlassSprite;
        private Sprite roundedRectSprite;

        [Header("HUD Display Settings")]
        [Tooltip("When false (default), the floating 'UR3e Teleoperation Telemetry' window is disabled. Live telemetry is viewed directly on the left wrist via VRWristTelemetry.")]
        public bool showFloatingHUD = false;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // Remove any legacy floating canvas if it already exists
            var existingCanvas = GameObject.Find("StereoHandDebugHUD_Canvas");
            if (existingCanvas != null && !showFloatingHUD)
            {
                Destroy(existingCanvas);
            }

            if (showFloatingHUD)
            {
                LoadVisionProAssets();
            }
        }

        private void Start()
        {
            FindReferences();

            if (showFloatingHUD)
            {
                BuildStereoHUDCanvas();
            }
            else
            {
                // Ensure live telemetry is active on wrist
                if (FindAnyObjectByType<VRWristTelemetry>() == null)
                {
                    GameObject wristObj = new GameObject("VR_WristTelemetry");
                    wristObj.AddComponent<VRWristTelemetry>();
                }
            }
        }

        private void LoadVisionProAssets()
        {
#if UNITY_EDITOR
            visionWindowMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/JetXR/UI Kit For Vision Pro OS/Runtime/Materials/WindowBlurredBackground.mat")
                                ?? AssetDatabase.LoadAssetAtPath<Material>("Assets/JetXR/UI Kit For Vision Pro OS/Runtime/Materials/WindowOverlayBackground.mat");
            visionElementMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/JetXR/UI Kit For Vision Pro OS/Runtime/Materials/LightElementBackground.mat")
                                 ?? AssetDatabase.LoadAssetAtPath<Material>("Assets/JetXR/UI Kit For Vision Pro OS/Runtime/Materials/DarkElementBackground.mat");
            windowGlassSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/JetXR/UI Kit For Vision Pro OS/Runtime/Sprites/Windows/WindowGlass.png");
            roundedRectSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/JetXR/UI Kit For Vision Pro OS/Runtime/Sprites/Buttons/RoundedRectBackground.png");
#endif
            if (visionWindowMaterial == null) visionWindowMaterial = Resources.Load<Material>("VisionUI/WindowBlurredBackground");
            if (visionElementMaterial == null) visionElementMaterial = Resources.Load<Material>("VisionUI/LightElementBackground");
            if (windowGlassSprite == null) windowGlassSprite = Resources.Load<Sprite>("VisionUI/WindowGlass");
            if (roundedRectSprite == null) roundedRectSprite = Resources.Load<Sprite>("VisionUI/RoundedRectBackground");
        }

        private void FindReferences()
        {
            if (pinchDetector == null) pinchDetector = FindAnyObjectByType<PinchDetector>();
            if (gripperBridge == null) gripperBridge = FindAnyObjectByType<GripperBridge>();
            if (vrHandMover == null) vrHandMover = FindAnyObjectByType<VRHandMover>();
            if (starterPoint == null) starterPoint = FindAnyObjectByType<UR3eStarterPoint>();
            if (safetyVisualizer == null) safetyVisualizer = FindAnyObjectByType<SafetyBoundaryVisualizer>();
            if (webcamScript == null) webcamScript = FindAnyObjectByType<script>(FindObjectsInactive.Include);

            if (centerEyeAnchor == null)
            {
                var cec = GameObject.Find("CenterEyeAnchor");
                if (cec != null) centerEyeAnchor = cec.transform;
                if (centerEyeAnchor == null && Camera.main != null) centerEyeAnchor = Camera.main.transform;
            }
        }

        private void BuildStereoHUDCanvas()
        {
            hudCanvasObj = new GameObject("StereoHandDebugHUD_Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            hudCanvasObj.transform.SetParent(null, false);

            hudCanvas = hudCanvasObj.GetComponent<Canvas>();
            hudCanvas.renderMode = RenderMode.WorldSpace;
            if (centerEyeAnchor != null)
            {
                var cam = centerEyeAnchor.GetComponent<Camera>();
                if (cam != null) hudCanvas.worldCamera = cam;
            }

            var scaler = hudCanvasObj.GetComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 10f;

            RectTransform canvasRT = hudCanvasObj.GetComponent<RectTransform>();
            canvasRT.sizeDelta = new Vector2(480f, 320f);
            canvasRT.localScale = Vector3.one * 0.0016f; // ~0.77m wide in VR

            // Vision Pro Frosted Glass Background Panel
            GameObject bgObj = new GameObject("Background", typeof(RectTransform), typeof(Image));
            bgObj.transform.SetParent(hudCanvasObj.transform, false);
            RectTransform bgRT = bgObj.GetComponent<RectTransform>();
            bgRT.anchorMin = Vector2.zero;
            bgRT.anchorMax = Vector2.one;
            bgRT.offsetMin = Vector2.zero;
            bgRT.offsetMax = Vector2.zero;

            backgroundImage = bgObj.GetComponent<Image>();
            if (windowGlassSprite != null)
            {
                backgroundImage.sprite = windowGlassSprite;
                backgroundImage.type = Image.Type.Sliced;
                backgroundImage.pixelsPerUnitMultiplier = 2f;
                backgroundImage.material = visionWindowMaterial;
                if (visionWindowMaterial != null) visionWindowMaterial.enableInstancing = true;
                backgroundImage.color = new Color(0.06f, 0.08f, 0.14f, 0.96f); // Deep Obsidian Glass
            }
            else
            {
                backgroundImage.material = null;
                backgroundImage.color = new Color(0.06f, 0.09f, 0.14f, 0.92f);
            }

            var ol = bgObj.AddComponent<Outline>();
            ol.effectColor = new Color(0.20f, 0.65f, 1.0f, 0.35f);
            ol.effectDistance = new Vector2(1.5f, -1.5f);

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Font.CreateDynamicFontFromOSFont("Arial", 16);

            // Title
            GameObject titleObj = new GameObject("Title", typeof(RectTransform), typeof(Text));
            titleObj.transform.SetParent(bgObj.transform, false);
            RectTransform titleRT = titleObj.GetComponent<RectTransform>();
            titleRT.anchorMin = new Vector2(0.05f, 0.86f);
            titleRT.anchorMax = new Vector2(0.95f, 0.98f);
            titleRT.offsetMin = Vector2.zero;
            titleRT.offsetMax = Vector2.zero;

            titleText = titleObj.GetComponent<Text>();
            titleText.material = null; // Standard CanvasRenderer renders to BOTH EYES
            titleText.font = font;
            titleText.fontSize = 18;
            titleText.fontStyle = FontStyle.Bold;
            titleText.alignment = TextAnchor.MiddleLeft;
            titleText.color = Color.white;
            titleText.text = "UR3e Teleoperation Telemetry";

            // Pause Banner
            GameObject pauseObj = new GameObject("PauseBanner", typeof(RectTransform), typeof(Text));
            pauseObj.transform.SetParent(bgObj.transform, false);
            RectTransform pauseRT = pauseObj.GetComponent<RectTransform>();
            pauseRT.anchorMin = new Vector2(0.55f, 0.86f);
            pauseRT.anchorMax = new Vector2(0.95f, 0.98f);
            pauseRT.offsetMin = Vector2.zero;
            pauseRT.offsetMax = Vector2.zero;

            pauseBannerText = pauseObj.GetComponent<Text>();
            pauseBannerText.material = null;
            pauseBannerText.font = font;
            pauseBannerText.fontSize = 15;
            pauseBannerText.fontStyle = FontStyle.Bold;
            pauseBannerText.alignment = TextAnchor.MiddleRight;
            pauseBannerText.color = Color.yellow;
            pauseBannerText.text = "";

            // Main Telemetry Text
            GameObject bodyObj = new GameObject("TelemetryText", typeof(RectTransform), typeof(Text));
            bodyObj.transform.SetParent(bgObj.transform, false);
            RectTransform bodyRT = bodyObj.GetComponent<RectTransform>();
            bodyRT.anchorMin = new Vector2(0.05f, 0.16f);
            bodyRT.anchorMax = new Vector2(0.95f, 0.84f);
            bodyRT.offsetMin = Vector2.zero;
            bodyRT.offsetMax = Vector2.zero;

            telemetryText = bodyObj.GetComponent<Text>();
            telemetryText.material = null;
            telemetryText.font = font;
            telemetryText.fontSize = 14;
            telemetryText.lineSpacing = 1.2f;
            telemetryText.alignment = TextAnchor.UpperLeft;
            telemetryText.color = new Color(0.9f, 0.95f, 1.0f, 0.95f);

            // VisionOS Bottom Pill Grab Handle Bar with VRWindowDragger
            GameObject grabBarObj = new GameObject("GrabHandleBar", typeof(RectTransform), typeof(Image), typeof(VRWindowDragger));
            grabBarObj.transform.SetParent(bgObj.transform, false);
            RectTransform grabRT = grabBarObj.GetComponent<RectTransform>();
            grabRT.anchorMin = new Vector2(0.24f, 0.03f);
            grabRT.anchorMax = new Vector2(0.76f, 0.11f);
            grabRT.offsetMin = Vector2.zero;
            grabRT.offsetMax = Vector2.zero;
            grabRT.pivot = new Vector2(0.5f, 0.5f);

            Image grabImg = grabBarObj.GetComponent<Image>();
            if (roundedRectSprite != null)
            {
                grabImg.sprite = roundedRectSprite;
                grabImg.type = Image.Type.Sliced;
                grabImg.material = visionElementMaterial;
                if (visionElementMaterial != null) visionElementMaterial.enableInstancing = true;
                grabImg.color = new Color(0.25f, 0.32f, 0.44f, 0.92f);
            }
            else
            {
                grabImg.material = null;
                grabImg.color = new Color(0.18f, 0.22f, 0.32f, 0.90f);
            }

            Outline grabOutline = grabBarObj.AddComponent<Outline>();
            grabOutline.effectColor = new Color(0.30f, 0.70f, 1.0f, 0.35f);
            grabOutline.effectDistance = new Vector2(1f, -1f);

            var dragger = grabBarObj.GetComponent<VRWindowDragger>();
            dragger.windowTransform = hudCanvasObj.transform;
            dragger.handleImage = grabImg;

            GameObject grabLabel = new GameObject("Label", typeof(RectTransform), typeof(Text));
            grabLabel.transform.SetParent(grabBarObj.transform, false);
            RectTransform glRT = grabLabel.GetComponent<RectTransform>();
            glRT.anchorMin = Vector2.zero;
            glRT.anchorMax = Vector2.one;
            glRT.offsetMin = Vector2.zero;
            glRT.offsetMax = Vector2.zero;
            Text glText = grabLabel.GetComponent<Text>();
            glText.material = null;
            glText.font = font;
            glText.fontSize = 11;
            glText.fontStyle = FontStyle.Bold;
            glText.alignment = TextAnchor.MiddleCenter;
            glText.color = new Color(0.9f, 0.95f, 1.0f, 0.90f);
            glText.text = "---  GRAB TO MOVE  ---";
        }

        public void RepositionDefault()
        {
            if (centerEyeAnchor == null) FindReferences();
            if (centerEyeAnchor == null || hudCanvasObj == null) return;

            Vector3 headForward = centerEyeAnchor.forward;
            headForward.y = 0f;
            if (headForward.sqrMagnitude > 0.001f) headForward.Normalize();
            else headForward = Vector3.forward;

            Vector3 headRight = Vector3.Cross(Vector3.up, headForward).normalized;

            Vector3 targetPos = centerEyeAnchor.position
                              + headForward * hudDistance
                              + headRight * hudSideOffset
                              + Vector3.up * hudHeightOffset;

            hudCanvasObj.transform.position = targetPos;
            hudCanvasObj.transform.rotation = Quaternion.LookRotation(hudCanvasObj.transform.position - centerEyeAnchor.position, Vector3.up);
        }

        private void LateUpdate()
        {
            if (!showFloatingHUD || hudCanvasObj == null) return;

            if (centerEyeAnchor == null) FindReferences();

            // Refresh World Camera for Stereo Rendering in Both Eyes
            if (hudCanvas != null && centerEyeAnchor != null)
            {
                var cam = centerEyeAnchor.GetComponent<Camera>();
                if (cam != null && hudCanvas.worldCamera != cam)
                {
                    hudCanvas.worldCamera = cam;
                }
            }

            // Position once on startup, then stay completely still in World Space (Draggable via Left Laser)
            if (!hasPositionedDefault && centerEyeAnchor != null && hudCanvasObj != null)
            {
                RepositionDefault();
                hasPositionedDefault = true;
            }

            // Update Telemetry Display
            UpdateTelemetryContent();
        }

        private void UpdateTelemetryContent()
        {
            if (telemetryText == null) return;

            bool isConnected = gripperBridge != null && gripperBridge.isConnected;
            bool isPaused = vrHandMover != null && vrHandMover.isTeleoperationPaused;
            bool isEngaged = vrHandMover != null && vrHandMover.isTrackingEngaged;

            if (pauseBannerText != null)
            {
                if (!isConnected)
                {
                    pauseBannerText.text = "[BRIDGE OFFLINE]";
                    pauseBannerText.color = new Color(1f, 0.3f, 0.3f, 1f);
                }
                else if (isPaused)
                {
                    pauseBannerText.text = "[PAUSED - PRESS Y / P]";
                    pauseBannerText.color = new Color(1f, 0.8f, 0.1f, 1f);
                }
                else if (!isEngaged)
                {
                    pauseBannerText.text = "[LOCKED - DOCK HOME / PRESS X]";
                    pauseBannerText.color = new Color(0.2f, 0.85f, 1f, 1f);
                }
                else
                {
                    pauseBannerText.text = "[TRACKING ACTIVE]";
                    pauseBannerText.color = new Color(0.2f, 1f, 0.4f, 1f);
                }
            }

            string bridgeStatus = (gripperBridge != null && gripperBridge.isConnected) ? "<color=#00FF88>CONNECTED</color>" : "<color=#FF4444>OFFLINE</color>";
            string modeStatus = "CLOSED-LOOP 6-DOF POSE";

            Vector3 tgtMm = (vrHandMover != null) ? vrHandMover.currentTargetPos * 1000f : Vector3.zero;
            Vector3 actMm = (gripperBridge != null) ? gripperBridge.actualRobotPos * 1000f : Vector3.zero;
            float posErrMm = Vector3.Distance(tgtMm, actMm);

            string homeStatus = (starterPoint != null && starterPoint.isAtHome) ? "<color=#00FF88>READY AT HOME</color>" : "<color=#00D4FF>OFF-HOME</color>";
            string pinchDesc = (pinchDetector != null) ? $"Pinch: {pinchDetector.normalizedPinch * 100f:F0}% (POS: {pinchDetector.gripperPos})" : "Pinch: N/A";
            string barrierDesc = (safetyVisualizer != null) ? $"Dist to Base: {safetyVisualizer.distToBase * 100f:F1} cm | Limit: {safetyVisualizer.outerRadius * 100f:F0} cm" : "";

            telemetryText.text = 
                $"Bridge: {bridgeStatus} | Mode: {modeStatus}\n" +
                $"Starter Point: {homeStatus} | {barrierDesc}\n\n" +
                $"<color=#00F0FF>Target XYZ:</color>  X:{tgtMm.x,5:F0}  Y:{tgtMm.y,5:F0}  Z:{tgtMm.z,5:F0} mm\n" +
                $"<color=#00FF88>Actual XYZ:</color>  X:{actMm.x,5:F0}  Y:{actMm.y,5:F0}  Z:{actMm.z,5:F0} mm  (Err: {posErrMm:F1} mm)\n\n" +
                $"{pinchDesc}";
        }

        public void SetFloatingHUDVisible(bool visible)
        {
            showFloatingHUD = visible;
            if (showFloatingHUD)
            {
                if (hudCanvasObj == null)
                {
                    LoadVisionProAssets();
                    BuildStereoHUDCanvas();
                }
                else
                {
                    hudCanvasObj.SetActive(true);
                }
            }
            else
            {
                if (hudCanvasObj != null)
                {
                    hudCanvasObj.SetActive(false);
                }
            }
        }

        private void OnDestroy()
        {
            if (hudCanvasObj != null)
            {
                Destroy(hudCanvasObj);
            }
        }
    }
}
