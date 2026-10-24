using System;

using System.Collections.Generic;

using UnityEngine;

using UnityEngine.UI;

using TMPro;

#if UNITY_EDITOR

using UnityEditor;

#endif



namespace UR3eVR

{

    /// <summary>

    /// VRCameraWindow: Apple Vision Pro OS Style Floating Webcam Monitor for Meta Quest VR

    /// - Deep obsidian dark-mode frosted glass with high contrast and zero eye fatigue

    /// - Multi-camera support: Click "+ Add Cam" to spawn multiple simultaneous live camera windows in VR

    /// - Dual-eye stereo rendering in XR Single Pass Instanced

    /// - Pixel-perfect raycast hitboxes for Switch Cam, Add Cam, Close, and Pill Grab Bar

    /// - Centered 16:9 RawImage viewport

    /// </summary>

    public class VRCameraWindow : MonoBehaviour

    {

        public static VRCameraWindow Instance { get; private set; }

        public static List<VRCameraWindow> ActiveWindows = new List<VRCameraWindow>();



        [Header("Window Configuration")]

        [Tooltip("Width of window in meters in VR")]

        public float windowWidthMeters = 0.68f;



        [Tooltip("Height of window in meters in VR")]

        public float windowHeightMeters = 0.44f;



        [Tooltip("Distance in front of user on spawn")]

        public float defaultDistance = 1.05f;



        [Header("State")]

        public bool isOpen = true;

        public int currentCameraIndex = 0;

        public bool isSecondaryWindow = false;



        [Header("3D Stereo Settings (ZED Mini / Side-by-Side)")]

        [Tooltip("สถานะว่ากำลังแสดงผลแบบ 3D Stereo (Side-by-Side) หรือไม่")]

        public bool isStereo3D = false;

        [Tooltip("สลับภาพตาซ้ายและตาขวา (ในกรณีกล้องติดตั้งกลับด้าน)")]

        public bool swapEyes = false;

        public Material stereoMaterial;

        private AspectRatioFitter aspectRatioFitter;

        private TextMeshProUGUI stereoBtnText;

        private Image stereoBtnImage;

        private bool hasConfiguredResolution = false;



        // UI References

        public GameObject canvasObj;

        private Canvas canvas;

        private RawImage videoRawImage;

        private TextMeshProUGUI titleText;

        private TextMeshProUGUI subtitleText;

        private Image liveDotImage;

        private GameObject grabBarObj;

        private VRWindowDragger dragger;



        // Vision Pro OS UI Kit Assets

        [Header("Vision Pro OS Asset References")]

        public Material visionWindowMaterial;

        public Material visionElementMaterial;

        public Sprite windowGlassSprite;

        public Sprite roundedRectSprite;

        public Sprite roundedRectHighlightSprite;

        public Sprite crossIconSprite;

        public TMP_FontAsset visionFont;



        // Webcam runtime

        private WebCamTexture webcamTexture;

        public WebCamDevice[] availableDevices { get; private set; }



        // VR References

        private Camera centerEyeCamera;

        private Transform leftControllerAnchor;

        private Transform rightControllerAnchor;



        public bool hasPositionedDefault = false;
        public int assignedSlotIndex = 0;

        private List<Button> interactiveButtons = new List<Button>();



        private void Awake()

        {

            if (Instance == null)

            {

                Instance = this;

            }

            if (!ActiveWindows.Contains(this))

            {

                ActiveWindows.Add(this);

            }



            LoadVisionProAssets();

        }



        private void OnDestroy()

        {

            if (ActiveWindows.Contains(this))

            {

                ActiveWindows.Remove(this);

            }

            if (Instance == this)

            {

                Instance = ActiveWindows.Count > 0 ? ActiveWindows[0] : null;

            }

            StopCurrentStream();

        }



        private void Start()

        {

            FindReferences();

            BuildCameraWindowUI();

            InitializeWebcam();

            SetWindowOpen(true);

        }



        private void LoadVisionProAssets()

        {

            if (visionWindowMaterial == null)

            {

#if UNITY_EDITOR

                visionWindowMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/JetXR/UI Kit For Vision Pro OS/Runtime/Materials/WindowBlurredBackground.mat")

                                    ?? AssetDatabase.LoadAssetAtPath<Material>("Assets/JetXR/UI Kit For Vision Pro OS/Runtime/Materials/WindowOverlayBackground.mat");

#endif

                if (visionWindowMaterial == null)

                {

                    visionWindowMaterial = Resources.Load<Material>("VisionUI/WindowBlurredBackground")

                                        ?? Resources.Load<Material>("VisionUI/WindowOverlayBackground");

                }

            }



            if (visionElementMaterial == null)

            {

#if UNITY_EDITOR

                visionElementMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/JetXR/UI Kit For Vision Pro OS/Runtime/Materials/DarkElementBackground.mat")

                                     ?? AssetDatabase.LoadAssetAtPath<Material>("Assets/JetXR/UI Kit For Vision Pro OS/Runtime/Materials/LightElementBackground.mat");

#endif

                if (visionElementMaterial == null)

                {

                    visionElementMaterial = Resources.Load<Material>("VisionUI/DarkElementBackground")

                                         ?? Resources.Load<Material>("VisionUI/LightElementBackground");

                }

            }



            if (windowGlassSprite == null)

            {

#if UNITY_EDITOR

                windowGlassSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/JetXR/UI Kit For Vision Pro OS/Runtime/Sprites/Windows/WindowGlass.png")

                                 ?? AssetDatabase.LoadAssetAtPath<Sprite>("Assets/JetXR/UI Kit For Vision Pro OS/Runtime/Sprites/Windows/WindowGlassNoAlpha.png");

#endif

                if (windowGlassSprite == null)

                    windowGlassSprite = Resources.Load<Sprite>("VisionUI/WindowGlass");

            }



            if (roundedRectSprite == null)

            {

#if UNITY_EDITOR

                roundedRectSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/JetXR/UI Kit For Vision Pro OS/Runtime/Sprites/Buttons/RoundedRectBackground.png");

#endif

                if (roundedRectSprite == null)

                    roundedRectSprite = Resources.Load<Sprite>("VisionUI/RoundedRectBackground");

            }



            if (roundedRectHighlightSprite == null)

            {

#if UNITY_EDITOR

                roundedRectHighlightSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/JetXR/UI Kit For Vision Pro OS/Runtime/Sprites/Buttons/RoundedRectHighlight.png");

#endif

                if (roundedRectHighlightSprite == null)

                    roundedRectHighlightSprite = Resources.Load<Sprite>("VisionUI/RoundedRectHighlight");

            }



            if (crossIconSprite == null)

            {

#if UNITY_EDITOR

                crossIconSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/JetXR/UI Kit For Vision Pro OS/Runtime/Sprites/Icons/Cross Icon.png");

#endif

                if (crossIconSprite == null)

                    crossIconSprite = Resources.Load<Sprite>("VisionUI/Cross Icon");

            }



            if (visionFont == null)

            {

#if UNITY_EDITOR

                visionFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/JetXR/UI Kit For Vision Pro OS/Runtime/Fonts/Inter-SemiBold SDF.asset")

                          ?? AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/JetXR/UI Kit For Vision Pro OS/Runtime/Fonts/Inter-Bold SDF.asset");

#endif

                if (visionFont == null)

                    visionFont = Resources.Load<TMP_FontAsset>("Inter-SemiBold SDF") ?? Resources.Load<TMP_FontAsset>("Inter-Bold SDF");

            }

        }



        private void FindReferences()

        {

            if (centerEyeCamera == null)

            {

                var cec = GameObject.Find("CenterEyeAnchor");

                if (cec != null) centerEyeCamera = cec.GetComponent<Camera>();

                if (centerEyeCamera == null && Camera.main != null) centerEyeCamera = Camera.main;

            }



            if (leftControllerAnchor == null)

            {

                var lca = GameObject.Find("LeftControllerAnchor");

                if (lca != null) leftControllerAnchor = lca.transform;

            }



            if (rightControllerAnchor == null)

            {

                var rca = GameObject.Find("RightControllerAnchor");

                if (rca != null) rightControllerAnchor = rca.transform;

            }

        }



        private void BuildCameraWindowUI()

        {

            // Root WorldSpace Canvas

            canvasObj = new GameObject("VR_Camera_Window_Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

            canvasObj.transform.SetParent(transform, false);



            canvas = canvasObj.GetComponent<Canvas>();

            canvas.renderMode = RenderMode.WorldSpace;

            if (centerEyeCamera != null) canvas.worldCamera = centerEyeCamera;



            var scaler = canvasObj.GetComponent<CanvasScaler>();

            scaler.dynamicPixelsPerUnit = 10f;



            RectTransform canvasRT = canvasObj.GetComponent<RectTransform>();

            canvasRT.sizeDelta = new Vector2(680f, 440f);

            canvasRT.localScale = Vector3.one * (windowWidthMeters / 680f);



            // 1. Deep Obsidian Dark-Mode Frosted Glass Window Frame (Dark, Sleek, High-Contrast)

            Image bgImg = canvasObj.AddComponent<Image>();

            if (windowGlassSprite != null)

            {

                bgImg.sprite = windowGlassSprite;

                bgImg.type = Image.Type.Sliced;

                bgImg.pixelsPerUnitMultiplier = 2f;

                bgImg.material = visionWindowMaterial;

                if (visionWindowMaterial != null) visionWindowMaterial.enableInstancing = true;

                bgImg.color = new Color(0.06f, 0.08f, 0.14f, 0.96f); // Deep Obsidian Glass (Darker, non-glare)

            }

            else

            {

                bgImg.material = null;

                bgImg.color = new Color(0.06f, 0.08f, 0.12f, 0.96f);

            }



            Outline bgOutline = canvasObj.AddComponent<Outline>();

            bgOutline.effectColor = new Color(0.20f, 0.65f, 1.0f, 0.35f); // Subtle Neon Cyan Rim

            bgOutline.effectDistance = new Vector2(1.5f, -1.5f);



            // 2. Vision Pro OS Navigation Bar (Header)

            GameObject header = new GameObject("HeaderBar", typeof(RectTransform));

            header.transform.SetParent(canvasObj.transform, false);

            RectTransform hRT = header.GetComponent<RectTransform>();

            hRT.anchorMin = new Vector2(0f, 0.84f);

            hRT.anchorMax = new Vector2(1f, 1f);

            hRT.offsetMin = Vector2.zero;

            hRT.offsetMax = Vector2.zero;



            // 2.1 Live Indicator Dot (Apple Vision Green Dot)

            GameObject liveDot = new GameObject("LiveDot", typeof(RectTransform), typeof(Image));

            liveDot.transform.SetParent(header.transform, false);

            RectTransform dotRT = liveDot.GetComponent<RectTransform>();

            dotRT.anchorMin = new Vector2(0.035f, 0.50f);

            dotRT.anchorMax = new Vector2(0.035f, 0.50f);

            dotRT.pivot = new Vector2(0.5f, 0.5f);

            dotRT.sizeDelta = new Vector2(10f, 10f);

            liveDotImage = liveDot.GetComponent<Image>();

            liveDotImage.material = null;

            liveDotImage.color = new Color(0.19f, 0.82f, 0.35f, 1.0f);



            // 2.2 Title & Subtitle Texts (TMP with Vision font)

            GameObject titleObj = new GameObject("TitleText", typeof(RectTransform), typeof(TextMeshProUGUI));

            titleObj.transform.SetParent(header.transform, false);

            RectTransform titleRT = titleObj.GetComponent<RectTransform>();

            titleRT.anchorMin = new Vector2(0.06f, 0.48f);

            titleRT.anchorMax = new Vector2(0.40f, 0.92f);

            titleRT.offsetMin = Vector2.zero;

            titleRT.offsetMax = Vector2.zero;

            titleText = titleObj.GetComponent<TextMeshProUGUI>();

            if (visionFont != null) titleText.font = visionFont;

            titleText.fontSize = 16;

            titleText.fontStyle = FontStyles.Bold;

            titleText.color = Color.white;

            titleText.text = "Cam Select";



            GameObject subObj = new GameObject("SubtitleText", typeof(RectTransform), typeof(TextMeshProUGUI));

            subObj.transform.SetParent(header.transform, false);

            RectTransform subRT = subObj.GetComponent<RectTransform>();

            subRT.anchorMin = new Vector2(0.06f, 0.08f);

            subRT.anchorMax = new Vector2(0.36f, 0.48f);

            subRT.offsetMin = Vector2.zero;

            subRT.offsetMax = Vector2.zero;

            subtitleText = subObj.GetComponent<TextMeshProUGUI>();

            if (visionFont != null) subtitleText.font = visionFont;

            subtitleText.fontSize = 11;

            subtitleText.color = new Color(0.7f, 0.82f, 0.95f, 0.90f);

            subtitleText.text = "Detecting video devices...";



            // 2.3 Action Buttons on Header (Clean, Non-Overlapping Hitboxes)
            // (A) 3D Stereo Toggle Button: anchors 0.38 to 0.52
            CreateVisionPillButton(header.transform, "StereoBtn", "3D: OFF",
                new Vector2(0.38f, 0.16f), new Vector2(0.52f, 0.84f),
                new Color(0.12f, 0.16f, 0.24f, 0.95f), () => ToggleStereo3D(), out stereoBtnText, out stereoBtnImage);

            // (B) Switch Camera Button: anchors 0.54 to 0.72
            CreateVisionPillButton(header.transform, "SwitchCam", "Switch Cam",
                new Vector2(0.54f, 0.16f), new Vector2(0.72f, 0.84f),
                new Color(0.12f, 0.20f, 0.32f, 0.95f), () => CycleNextCamera());

            // (C) + Add Cam Button: anchors 0.74 to 0.87
            CreateVisionPillButton(header.transform, "AddCam", "+ Add Cam",
                new Vector2(0.74f, 0.16f), new Vector2(0.87f, 0.84f),
                new Color(0.08f, 0.28f, 0.25f, 0.95f), () => SpawnAdditionalWindow());

            // (D) Close Button: anchors 0.89 to 0.97
            CreateVisionCloseButton(header.transform, "CloseBtn",
                new Vector2(0.89f, 0.16f), new Vector2(0.97f, 0.84f), () => CloseThisWindow());

            // 3. Center Screen: Video Container with RectMask2D (PERFECTLY CENTERED, NO SKEW)

            GameObject videoContainer = new GameObject("VideoContainer", typeof(RectTransform), typeof(Image), typeof(RectMask2D));

            videoContainer.transform.SetParent(canvasObj.transform, false);

            RectTransform contRT = videoContainer.GetComponent<RectTransform>();

            contRT.anchorMin = new Vector2(0.035f, 0.13f);

            contRT.anchorMax = new Vector2(0.965f, 0.83f);

            contRT.offsetMin = Vector2.zero;

            contRT.offsetMax = Vector2.zero;

            contRT.pivot = new Vector2(0.5f, 0.5f);



            Image contBg = videoContainer.GetComponent<Image>();

            contBg.material = null;

            contBg.color = new Color(0.02f, 0.03f, 0.05f, 0.98f);



            // Video Feed (RawImage) strictly centered and fitted to 16:9

            GameObject videoObj = new GameObject("VideoDisplay", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));

            videoObj.transform.SetParent(videoContainer.transform, false);

            RectTransform vidRT = videoObj.GetComponent<RectTransform>();

            vidRT.anchorMin = Vector2.zero;

            vidRT.anchorMax = Vector2.one;

            vidRT.offsetMin = Vector2.zero;

            vidRT.offsetMax = Vector2.zero;

            vidRT.pivot = new Vector2(0.5f, 0.5f);



            videoRawImage = videoObj.GetComponent<RawImage>();

            videoRawImage.color = Color.white;

            videoRawImage.material = null;



            aspectRatioFitter = videoObj.GetComponent<AspectRatioFitter>();

            aspectRatioFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;

            aspectRatioFitter.aspectRatio = 16f / 9f;



            // Video Border Frame

            Outline vidOutline = videoObj.AddComponent<Outline>();

            vidOutline.effectColor = new Color(1f, 1f, 1f, 0.15f);

            vidOutline.effectDistance = new Vector2(1f, -1f);



            // 4. Apple Vision Pro OS Pill Grab Handle Bar

            grabBarObj = new GameObject("GrabHandleBar", typeof(RectTransform), typeof(Image), typeof(VRWindowDragger));

            grabBarObj.transform.SetParent(canvasObj.transform, false);

            RectTransform grabRT = grabBarObj.GetComponent<RectTransform>();

            grabRT.anchorMin = new Vector2(0.30f, 0.025f);

            grabRT.anchorMax = new Vector2(0.70f, 0.095f);

            grabRT.offsetMin = Vector2.zero;

            grabRT.offsetMax = Vector2.zero;

            grabRT.pivot = new Vector2(0.5f, 0.5f);



            Image grabImg = grabBarObj.GetComponent<Image>();

            if (roundedRectSprite != null)

            {

                grabImg.sprite = roundedRectSprite;

                grabImg.type = Image.Type.Sliced;

                grabImg.material = visionElementMaterial;

                grabImg.color = new Color(0.12f, 0.16f, 0.24f, 0.95f); // Dark Slate Pill

            }

            else

            {

                grabImg.material = null;

                grabImg.color = new Color(0.10f, 0.14f, 0.20f, 0.92f);

            }



            Outline grabOutline = grabBarObj.AddComponent<Outline>();

            grabOutline.effectColor = new Color(0.30f, 0.70f, 1.0f, 0.35f);

            grabOutline.effectDistance = new Vector2(1f, -1f);



            // Grab Bar Label

            GameObject grabLabelObj = new GameObject("GrabLabel", typeof(RectTransform), typeof(TextMeshProUGUI));

            grabLabelObj.transform.SetParent(grabBarObj.transform, false);

            RectTransform glRT = grabLabelObj.GetComponent<RectTransform>();

            glRT.anchorMin = Vector2.zero;

            glRT.anchorMax = Vector2.one;

            glRT.offsetMin = Vector2.zero;

            glRT.offsetMax = Vector2.zero;

            var grabLabel = grabLabelObj.GetComponent<TextMeshProUGUI>();

            if (visionFont != null) grabLabel.font = visionFont;

            grabLabel.fontSize = 11;

            grabLabel.alignment = TextAlignmentOptions.Center;

            grabLabel.color = new Color(0.85f, 0.92f, 1.0f, 0.90f);

            grabLabel.text = "---  GRAB TO MOVE  ---";



            // Attach VRWindowDragger

            dragger = grabBarObj.GetComponent<VRWindowDragger>();

            dragger.windowTransform = canvasObj.transform;

            dragger.handleImage = grabImg;

            // Bottom-Right Corner Resize Handle (Item 2)
            GameObject resizeHandleObj = new GameObject("WindowResizeHandle", typeof(RectTransform), typeof(Image), typeof(VRWindowResizer));
            resizeHandleObj.transform.SetParent(canvasObj.transform, false);
            RectTransform resRT = resizeHandleObj.GetComponent<RectTransform>();
            resRT.anchorMin = new Vector2(0.92f, 0.025f);
            resRT.anchorMax = new Vector2(0.97f, 0.095f);
            resRT.offsetMin = Vector2.zero;
            resRT.offsetMax = Vector2.zero;

            Image resImg = resizeHandleObj.GetComponent<Image>();
            if (roundedRectSprite != null)
            {
                resImg.sprite = roundedRectSprite;
                resImg.type = Image.Type.Sliced;
            }
            resImg.material = null;
            resImg.color = new Color(0.15f, 0.75f, 0.95f, 0.50f);

            var resizer = resizeHandleObj.GetComponent<VRWindowResizer>();
            resizer.targetCanvasRT = canvasRT;
            resizer.handleImage = resImg;



            // Initial Position in Front of User

            if (!hasPositionedDefault)

            {

                RepositionWindowDefault();

            }

        }



        private void CreateVisionPillButton(Transform parent, string name, string label, Vector2 min, Vector2 max, Color normalColor, Action onClick, out TextMeshProUGUI labelOut, out Image bgOut)

        {

            GameObject btnObj = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));

            btnObj.transform.SetParent(parent, false);



            RectTransform rt = btnObj.GetComponent<RectTransform>();

            rt.anchorMin = min;

            rt.anchorMax = max;

            rt.offsetMin = Vector2.zero;

            rt.offsetMax = Vector2.zero;

            rt.pivot = new Vector2(0.5f, 0.5f);



            Image img = btnObj.GetComponent<Image>();

            if (roundedRectSprite != null)

            {

                img.sprite = roundedRectSprite;

                img.type = Image.Type.Sliced;

                img.material = visionElementMaterial;

                if (visionElementMaterial != null) visionElementMaterial.enableInstancing = true;

                img.color = normalColor;

            }

            else

            {

                img.material = null;

                img.color = normalColor;

            }

            img.raycastTarget = true;

            bgOut = img;



            Button btn = btnObj.GetComponent<Button>();

            ColorBlock cb = btn.colors;

            cb.normalColor = Color.white;

            cb.highlightedColor = new Color(0.25f, 0.45f, 0.70f, 1f);

            cb.pressedColor = new Color(0.08f, 0.12f, 0.20f, 1f);

            btn.colors = cb;

            btn.onClick.AddListener(() => onClick());



            Outline ol = btnObj.AddComponent<Outline>();

            ol.effectColor = new Color(0.3f, 0.7f, 1.0f, 0.35f);

            ol.effectDistance = new Vector2(1f, -1f);



            GameObject txtObj = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));

            txtObj.transform.SetParent(btnObj.transform, false);

            RectTransform tRT = txtObj.GetComponent<RectTransform>();

            tRT.anchorMin = Vector2.zero;

            tRT.anchorMax = Vector2.one;

            tRT.offsetMin = Vector2.zero;

            tRT.offsetMax = Vector2.zero;

            TextMeshProUGUI txt = txtObj.GetComponent<TextMeshProUGUI>();

            if (visionFont != null) txt.font = visionFont;

            txt.fontSize = 12;

            txt.fontStyle = FontStyles.Bold;

            txt.alignment = TextAlignmentOptions.Center;

            txt.color = Color.white;

            txt.text = label;

            labelOut = txt;

        }



        private void CreateVisionPillButton(Transform parent, string name, string label, Vector2 min, Vector2 max, Color normalColor, Action onClick)

        {

            TextMeshProUGUI dummyTxt;

            Image dummyImg;

            CreateVisionPillButton(parent, name, label, min, max, normalColor, onClick, out dummyTxt, out dummyImg);

        }



        private void _OriginalCreateVisionPillButton_Removed(Transform parent, string name, string label, Vector2 min, Vector2 max, Color normalColor, Action onClick)

        {

            GameObject btnObj = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));

            btnObj.transform.SetParent(parent, false);



            RectTransform rt = btnObj.GetComponent<RectTransform>();

            rt.anchorMin = min;

            rt.anchorMax = max;

            rt.offsetMin = Vector2.zero;

            rt.offsetMax = Vector2.zero;

            rt.pivot = new Vector2(0.5f, 0.5f);



            Image img = btnObj.GetComponent<Image>();

            if (roundedRectSprite != null)

            {

                img.sprite = roundedRectSprite;

                img.type = Image.Type.Sliced;

                img.material = visionElementMaterial;

                if (visionElementMaterial != null) visionElementMaterial.enableInstancing = true;

                img.color = normalColor;

            }

            else

            {

                img.material = null;

                img.color = normalColor;

            }

            img.raycastTarget = true;



            Button btn = btnObj.GetComponent<Button>();

            ColorBlock cb = btn.colors;

            cb.normalColor = Color.white;

            cb.highlightedColor = new Color(0.25f, 0.45f, 0.70f, 1f);

            cb.pressedColor = new Color(0.08f, 0.12f, 0.20f, 1f);

            btn.colors = cb;

            btn.onClick.AddListener(() => onClick());



            Outline ol = btnObj.AddComponent<Outline>();

            ol.effectColor = new Color(0.3f, 0.7f, 1.0f, 0.35f);

            ol.effectDistance = new Vector2(1f, -1f);



            GameObject txtObj = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));

            txtObj.transform.SetParent(btnObj.transform, false);

            RectTransform tRT = txtObj.GetComponent<RectTransform>();

            tRT.anchorMin = Vector2.zero;

            tRT.anchorMax = Vector2.one;

            tRT.offsetMin = Vector2.zero;

            tRT.offsetMax = Vector2.zero;

            TextMeshProUGUI txt = txtObj.GetComponent<TextMeshProUGUI>();

            if (visionFont != null) txt.font = visionFont;

            txt.fontSize = 12;

            txt.fontStyle = FontStyles.Bold;

            txt.alignment = TextAlignmentOptions.Center;

            txt.color = Color.white;

            txt.raycastTarget = false;

            txt.text = label;



            interactiveButtons.Add(btn);

        }



        private void CreateVisionCloseButton(Transform parent, string name, Vector2 min, Vector2 max, Action onClick)

        {

            GameObject btnObj = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));

            btnObj.transform.SetParent(parent, false);



            RectTransform rt = btnObj.GetComponent<RectTransform>();

            rt.anchorMin = min;

            rt.anchorMax = max;

            rt.offsetMin = Vector2.zero;

            rt.offsetMax = Vector2.zero;

            rt.pivot = new Vector2(0.5f, 0.5f);



            Image img = btnObj.GetComponent<Image>();

            if (roundedRectSprite != null)

            {

                img.sprite = roundedRectSprite;

                img.type = Image.Type.Sliced;

                img.material = visionElementMaterial;

                if (visionElementMaterial != null) visionElementMaterial.enableInstancing = true;

                img.color = new Color(0.45f, 0.14f, 0.18f, 0.95f);

            }

            else

            {

                img.material = null;

                img.color = new Color(0.38f, 0.12f, 0.15f, 0.95f);

            }

            img.raycastTarget = true;



            Button btn = btnObj.GetComponent<Button>();

            ColorBlock cb = btn.colors;

            cb.normalColor = Color.white;

            cb.highlightedColor = new Color(0.70f, 0.20f, 0.25f, 1f);

            cb.pressedColor = new Color(0.20f, 0.05f, 0.08f, 1f);

            btn.colors = cb;

            btn.onClick.AddListener(() => onClick());



            Outline ol = btnObj.AddComponent<Outline>();

            ol.effectColor = new Color(1f, 0.3f, 0.3f, 0.35f);

            ol.effectDistance = new Vector2(1f, -1f);



            GameObject txtObj = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));

            txtObj.transform.SetParent(btnObj.transform, false);

            RectTransform tRT = txtObj.GetComponent<RectTransform>();

            tRT.anchorMin = Vector2.zero;

            tRT.anchorMax = Vector2.one;

            tRT.offsetMin = Vector2.zero;

            tRT.offsetMax = Vector2.zero;

            TextMeshProUGUI txt = txtObj.GetComponent<TextMeshProUGUI>();

            if (visionFont != null) txt.font = visionFont;

            txt.fontSize = 14;

            txt.fontStyle = FontStyles.Bold;

            txt.alignment = TextAlignmentOptions.Center;

            txt.color = Color.white;

            txt.raycastTarget = false;

            txt.text = "X";



            interactiveButtons.Add(btn);

        }



        public void RepositionWindowDefault()

        {

            if (centerEyeCamera == null) FindReferences();

            if (centerEyeCamera == null && Camera.main != null) centerEyeCamera = Camera.main;



            if (canvasObj == null) return;



            if (centerEyeCamera != null)

            {

                Vector3 headPos = centerEyeCamera.transform.position;

                Vector3 headFwd = centerEyeCamera.transform.forward;

                headFwd.y = 0;

                if (headFwd.sqrMagnitude < 0.01f) headFwd = Vector3.forward;

                headFwd.Normalize();



                Vector3 targetPos = headPos + headFwd * defaultDistance + Vector3.up * 0.12f;

                canvasObj.transform.position = targetPos;

                canvasObj.transform.rotation = Quaternion.LookRotation(headFwd, Vector3.up);

            }

            else

            {

                canvasObj.transform.localPosition = new Vector3(0f, 1.25f, defaultDistance);

                canvasObj.transform.localRotation = Quaternion.identity;

            }



            if (canvas != null && centerEyeCamera != null)

            {

                canvas.worldCamera = centerEyeCamera;

            }

        }



        private void InitializeWebcam()

        {

            availableDevices = WebCamTexture.devices;

            if (availableDevices == null || availableDevices.Length == 0)

            {

                if (subtitleText != null) subtitleText.text = "No Physical Webcam Detected";

                if (liveDotImage != null) liveDotImage.color = new Color(1f, 0.7f, 0.1f, 1f);

                return;

            }



            OpenWebcamIndex(currentCameraIndex);

        }



        public void OpenWebcamIndex(int idx)

        {

            if (availableDevices == null || availableDevices.Length == 0)

                availableDevices = WebCamTexture.devices;



            if (availableDevices == null || availableDevices.Length == 0) return;



            currentCameraIndex = Mathf.Clamp(idx, 0, availableDevices.Length - 1);

            string devName = availableDevices[currentCameraIndex].name;



            StopCurrentStream();



            try

            {

                hasConfiguredResolution = false;

                bool isZed = devName.IndexOf("ZED", StringComparison.OrdinalIgnoreCase) >= 0;

                int reqW = isZed ? 2560 : 1280;

                int reqH = 720;

                int reqFps = isZed ? 60 : 30;



                webcamTexture = new WebCamTexture(devName, reqW, reqH, reqFps);

                if (videoRawImage != null)

                {

                    videoRawImage.texture = webcamTexture;

                }



                if (Graphic.defaultGraphicMaterial.mainTexture != null)

                {

                    Graphic.defaultGraphicMaterial.mainTexture = null;

                }



                webcamTexture.Play();



                if (titleText != null) titleText.text = $"Camera [{currentCameraIndex + 1}/{availableDevices.Length}]";

                if (subtitleText != null) subtitleText.text = $"{devName} (Streaming)";

                if (liveDotImage != null) liveDotImage.color = new Color(0.19f, 0.82f, 0.35f, 1.0f);

            }

            catch (Exception ex)

            {

                if (subtitleText != null) subtitleText.text = $"Error: {ex.Message}";

                if (liveDotImage != null) liveDotImage.color = Color.red;

            }

        }



        public void CycleNextCamera()

        {

            if (availableDevices == null || availableDevices.Length <= 1) return;

            int next = (currentCameraIndex + 1) % availableDevices.Length;

            OpenWebcamIndex(next);

        }



        /// <summary>

        /// Spawn an additional independent Camera Monitor window in VR space!

        /// Enables watching multiple live cameras side-by-side simultaneously.

        /// </summary>

        public static VRCameraWindow SpawnAdditionalWindow(int targetIndex = -1)
        {
            // If the primary Instance exists but is closed, re-open it at center instead of creating an orphan secondary window
            if (Instance != null && !Instance.isOpen)
            {
                Instance.SetWindowOpen(true);
                Instance.RepositionWindowDefault();
                if (targetIndex >= 0) Instance.OpenWebcamIndex(targetIndex);
                return Instance;
            }

            GameObject newObj = new GameObject("VRCameraWindow_" + (ActiveWindows.Count + 1));
            var newWin = newObj.AddComponent<VRCameraWindow>();
            newWin.isSecondaryWindow = true;

            if (Instance != null)
            {
                newWin.visionWindowMaterial = Instance.visionWindowMaterial;
                newWin.visionElementMaterial = Instance.visionElementMaterial;
                newWin.windowGlassSprite = Instance.windowGlassSprite;
                newWin.roundedRectSprite = Instance.roundedRectSprite;
                newWin.roundedRectHighlightSprite = Instance.roundedRectHighlightSprite;
                newWin.crossIconSprite = Instance.crossIconSprite;
                newWin.visionFont = Instance.visionFont;

                if (targetIndex >= 0)
                {
                    newWin.currentCameraIndex = targetIndex;
                }
                else if (Instance.availableDevices != null && Instance.availableDevices.Length > 0)
                {
                    newWin.currentCameraIndex = (Instance.currentCameraIndex + ActiveWindows.Count) % Instance.availableDevices.Length;
                }
            }

            // =========================================================================
            // Curved Cylindrical Arc Slot Placement (Constant Radius, Zero Accumulation)
            // =========================================================================
            // 1. Identify which slots are currently occupied by open, active windows
            HashSet<int> occupiedSlots = new HashSet<int>();
            foreach (var w in ActiveWindows)
            {
                if (w != null && w != newWin && w.isOpen && w.gameObject != null)
                {
                    occupiedSlots.Add(w.assignedSlotIndex);
                }
            }

            // 2. Assign the lowest available slot (starting from slot 1 for secondary windows)
            int slot = 1;
            while (occupiedSlots.Contains(slot))
            {
                slot++;
            }
            newWin.assignedSlotIndex = slot;

            // 3. Calculate slot angle on the cylinder arc:
            // Slot 1: +39° (Right 1), Slot 2: -39° (Left 1), Slot 3: +78° (Right 2), Slot 4: -78° (Left 2)...
            int pairIndex = (slot + 1) / 2;
            float sign = (slot % 2 != 0) ? 1.0f : -1.0f;
            float slotAngle = sign * (pairIndex * 39.0f);

            // 4. Determine head position and forward reference
            Camera cam = (Instance != null && Instance.centerEyeCamera != null)
                ? Instance.centerEyeCamera
                : (Camera.main ?? FindAnyObjectByType<Camera>());

            Vector3 headPos = (cam != null) ? cam.transform.position : Vector3.zero;
            Vector3 baseFwd = Vector3.forward;

            if (Instance != null && Instance.canvasObj != null)
            {
                Vector3 toPrimary = Instance.canvasObj.transform.position - headPos;
                toPrimary.y = 0;
                if (toPrimary.sqrMagnitude > 0.04f) baseFwd = toPrimary.normalized;
                else if (cam != null) baseFwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            }
            else if (cam != null)
            {
                baseFwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            }

            if (baseFwd.sqrMagnitude < 0.01f) baseFwd = Vector3.forward;

            // 5. Position on cylindrical arc around user at the exact same defaultDistance (never accumulates!)
            float windowDist = (Instance != null && Instance.defaultDistance > 0.5f) ? Instance.defaultDistance : 1.05f;
            Quaternion slotRot = Quaternion.AngleAxis(slotAngle, Vector3.up);
            Vector3 slotDir = slotRot * baseFwd;
            float yOffset = (Instance != null && Instance.canvasObj != null)
                ? Instance.canvasObj.transform.position.y
                : (headPos.y + 0.12f);

            Vector3 spawnPos = headPos + slotDir * windowDist;
            spawnPos.y = yOffset;

            newObj.transform.position = spawnPos;
            newObj.transform.rotation = Quaternion.LookRotation(slotDir, Vector3.up);
            newWin.hasPositionedDefault = true;

            Debug.Log($"[VRCameraWindow] Spawned Camera Window #{ActiveWindows.Count} in Arc Slot {slot} (Angle: {slotAngle:F0}°, Distance: {windowDist:F2}m)");

            return newWin;
        }



        public void CloseThisWindow()

        {

            if (isSecondaryWindow)

            {

                Destroy(gameObject);

            }

            else

            {

                SetWindowOpen(false);

            }

        }



        private void StopCurrentStream()

        {

            if (webcamTexture != null)

            {

                if (webcamTexture.isPlaying) webcamTexture.Stop();

                Destroy(webcamTexture);

                webcamTexture = null;

            }

        }



        private void Update()

        {

            if (!isOpen) return;



            // Single Pass Instanced: Continuously guarantee CenterEyeCamera on World Space Canvas

            if (canvas != null && centerEyeCamera != null)

            {

                if (canvas.worldCamera != centerEyeCamera)

                {

                    canvas.worldCamera = centerEyeCamera;

                }

            }

            else if (canvas != null && canvas.worldCamera == null)

            {

                FindReferences();

                if (centerEyeCamera != null) canvas.worldCamera = centerEyeCamera;

            }



            if (!hasPositionedDefault)

            {

                FindReferences();

                if (centerEyeCamera != null || Camera.main != null)

                {

                    RepositionWindowDefault();

                    hasPositionedDefault = true;

                }

            }



            // Live Pulsing Green Dot

            if (liveDotImage != null && webcamTexture != null && webcamTexture.isPlaying)

            {

                float alpha = 0.5f + Mathf.PingPong(Time.time * 2f, 0.5f);

                liveDotImage.color = new Color(0.19f, 0.82f, 0.35f, alpha);

            }



            // Auto-detect resolution & 3D SBS format once valid video frames arrive

            if (!hasConfiguredResolution && webcamTexture != null && webcamTexture.isPlaying && webcamTexture.width > 16)

            {

                hasConfiguredResolution = true;

                string devName = (availableDevices != null && currentCameraIndex < availableDevices.Length) ? availableDevices[currentCameraIndex].name : "";

                float aspect = (float)webcamTexture.width / webcamTexture.height;



                // Auto-detect Stereo 3D:

                // ZED Mini, Stereo Webcams, or any camera with Aspect Ratio >= 1.9f (SBS dual image)

                bool isSbs = (aspect >= 1.9f) ||

                             devName.IndexOf("ZED", StringComparison.OrdinalIgnoreCase) >= 0 ||

                             devName.IndexOf("Stereo", StringComparison.OrdinalIgnoreCase) >= 0;



                SetStereoMode(isSbs);

            }

        }



        public void SetWindowOpen(bool open)

        {

            isOpen = open;

            if (canvasObj != null) canvasObj.SetActive(open);

            if (open)

            {

                if (webcamTexture != null && !webcamTexture.isPlaying) webcamTexture.Play();

            }

            else

            {

                if (webcamTexture != null && webcamTexture.isPlaying) webcamTexture.Pause();

            }

        }



        public void SetStereoMode(bool enable)

        {

            isStereo3D = enable;



            if (stereoMaterial == null)

            {

                Material resMat = Resources.Load<Material>("Materials/StereoRawImageSBS");

                if (resMat != null)

                {

                    stereoMaterial = new Material(resMat);

                }

                else

                {

                    Shader s = Shader.Find("UR3eVR/StereoRawImageSBS");

                    if (s != null) stereoMaterial = new Material(s);

                }

            }



            if (videoRawImage != null)

            {

                if (stereoMaterial != null)

                {

                    stereoMaterial.SetFloat("_IsStereo", isStereo3D ? 1.0f : 0.0f);

                    stereoMaterial.SetFloat("_SwapEyes", swapEyes ? 1.0f : 0.0f);

                    videoRawImage.material = stereoMaterial;

                }

                else

                {

                    videoRawImage.material = null;

                }

            }



            if (aspectRatioFitter != null && webcamTexture != null && webcamTexture.height > 0)

            {

                if (isStereo3D)

                {

                    // In SBS stereo, each eye displays half the width (e.g. 2560/2 = 1280x720 = 16:9)

                    aspectRatioFitter.aspectRatio = (float)(webcamTexture.width * 0.5f) / webcamTexture.height;

                }

                else

                {

                    aspectRatioFitter.aspectRatio = (float)webcamTexture.width / webcamTexture.height;

                }

            }



            // Update UI Button and Subtitle

            string devName = (availableDevices != null && currentCameraIndex < availableDevices.Length) ? availableDevices[currentCameraIndex].name : "Camera";

            if (isStereo3D)

            {

                if (stereoBtnText != null) stereoBtnText.text = "3D: ON";

                if (stereoBtnImage != null) stereoBtnImage.color = new Color(0.00f, 0.45f, 0.65f, 0.95f); // Cyan Glow

                if (subtitleText != null && webcamTexture != null)

                {

                    int eyeW = webcamTexture.width > 16 ? webcamTexture.width / 2 : 1280;

                    int eyeH = webcamTexture.height > 16 ? webcamTexture.height : 720;

                    subtitleText.text = $"[3D Stereo] {devName} • {eyeW}x{eyeH}";

                }

            }

            else

            {

                if (stereoBtnText != null) stereoBtnText.text = "3D: OFF";

                if (stereoBtnImage != null) stereoBtnImage.color = new Color(0.12f, 0.16f, 0.24f, 0.95f); // Dark Slate

                if (subtitleText != null && webcamTexture != null)

                {

                    int w = webcamTexture.width > 16 ? webcamTexture.width : 1280;

                    int h = webcamTexture.height > 16 ? webcamTexture.height : 720;

                    subtitleText.text = $"[2D Mono] {devName} • {w}x{h}";

                }

            }

        }



        public void ToggleStereo3D()

        {

            SetStereoMode(!isStereo3D);

        }

    }

}

