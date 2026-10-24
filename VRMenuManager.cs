using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace UR3eVR
{
    [System.Serializable]
    public class SkyboxOption
    {
        public string displayName;
        public string category;
        public Material skyboxMaterial;
    }

    /// <summary>
    /// VRMenuManager: à¸£à¸°à¸šà¸š Quick Menu à¸¥à¸­à¸¢à¹ƒà¸™à¸žà¸·à¹‰à¸™à¸—à¸µà¹ˆ 3D à¸ªà¸³à¸«à¸£à¸±à¸š Meta Quest Touch Remote
    /// - à¸›à¸¸à¹ˆà¸¡ Menu à¸£à¸µà¹‚à¸¡à¸—à¸¡à¸·à¸­à¸‹à¹‰à¸²à¸¢ (3 à¸‚à¸µà¸” / Hamburger): OVRInput.Button.Start / KeyCode.M
    /// - à¸ªà¸µà¹ˆà¹€à¸¡à¸™à¸¹à¸«à¸¥à¸±à¸: Set Origin, Open New Cam, Open Log (Auto-popup à¹€à¸¡à¸·à¹ˆà¸­à¸¡à¸µà¹€à¸•à¸·à¸­à¸™à¹€à¸«à¸¥à¸·à¸­à¸‡/à¹à¸”à¸‡), Background
    /// - à¸›à¸¸à¹ˆà¸¡à¸›à¸´à¸” [X] à¸­à¸¢à¸¹à¹ˆà¸¡à¸¸à¸¡à¸‚à¸§à¸²à¸šà¸™à¸‚à¸­à¸‡à¸—à¸¸à¸à¸«à¸™à¹‰à¸²à¸•à¹ˆà¸²à¸‡
    /// - à¹à¸à¹‰à¹„à¸‚à¸›à¸±à¸à¸«à¸² Single Pass Instanced: à¸à¸³à¸«à¸™à¸” worldCamera à¹à¸¥à¸° Material Instancing à¹ƒà¸«à¹‰à¸¡à¸­à¸‡à¹€à¸«à¹‡à¸™à¸„à¸£à¸šà¸—à¸±à¹‰à¸‡ 2 à¸•à¸²
    /// - à¸¥à¸³à¹à¸ªà¸‡à¹€à¸¥à¹€à¸‹à¸­à¸£à¹Œ (Laser Beam) à¸¢à¸´à¸‡à¸žà¸¸à¹ˆà¸‡à¸­à¸­à¸à¸ˆà¸²à¸à¸›à¸¥à¸²à¸¢à¸£à¸µà¹‚à¸¡à¸—à¸„à¸­à¸™à¹‚à¸—à¸£à¸¥à¹€à¸¥à¸­à¸£à¹Œà¸‚à¸§à¸²à¸­à¸¢à¹ˆà¸²à¸‡à¹à¸¡à¹ˆà¸™à¸¢à¸³ à¸žà¸£à¹‰à¸­à¸¡à¸ˆà¸¸à¸” Reticle
    /// </summary>
    public class VRMenuManager : MonoBehaviour
    {
        public static VRMenuManager Instance { get; private set; }

        [Header("Menu Key & Controller Bindings")]
        [Tooltip("ปุ่ม Menu บนรีโมทซ้าย (ขีด 3 ขีดบน LTouch)")]
        public OVRInput.Button menuButton = OVRInput.Button.Start;
        public OVRInput.RawButton rawMenuButton = OVRInput.RawButton.Start;
        public KeyCode keyboardMenuKey = KeyCode.M;

        [Header("References")]
        public Transform headTransform;
        public Camera centerEyeCamera;
        public Transform rightControllerAnchor;
        public Transform leftControllerAnchor;
        public VRHandMover vrHandMover;
        public GripperBridge gripperBridge;
        public script webcamScript;
        public PinchDetector pinchDetector;

        [Header("Menu State")]
        public bool isMenuOpen = false;
        public enum MenuPanel { Main, Camera, Log, Background, Settings, Speed }
        public MenuPanel currentPanel = MenuPanel.Main;

        [Header("Skybox & Environment Options")]
        public List<SkyboxOption> skyboxOptions = new List<SkyboxOption>();
        public int currentBgPage = 0;
        public string currentActiveSkyboxName = "Solid Black (#000000)";
        private Text activeBgStatusText;
        private GameObject bgGridContainer;
        private Text bgPageIndicatorText;
        private List<GameObject> activePageButtons = new List<GameObject>();

        [Header("Laser Pointer Settings")]
        public bool enableLaserPointer = true;
        public float laserLength = 3.0f;
        public Color laserColor = new Color(0.0f, 0.90f, 1.0f, 0.95f); // Bright Electric Cyan

        // UI Materials (Single Pass Instanced Stereo Fix)
        private Material stereoUIMaterial;
        private Material visionWindowMaterial;
        private Material visionElementMaterial;
        private Sprite windowGlassSprite;
        private Sprite roundedRectSprite;
        private Sprite crossIconSprite;
        private Material laserMaterial;

        // UI GameObjects
        private GameObject canvasObj;
        private Canvas canvas;
        private GraphicRaycaster raycaster;
        private Font uiFont;

        // Panels
        private GameObject panelMain;
        private GameObject panelCamera;
        private GameObject panelLog;
        private GameObject panelBg;
        private GameObject panelSettings;
        private GameObject panelSpeed;

        // Main Panel UI
        private Text statusText;
        private Text toastText;
        private float toastTimer = 0f;

        // Camera Panel UI
        private Text camStatusText;
        private Transform camButtonContainer;
        private List<GameObject> dynamicCamButtons = new List<GameObject>();

        // Log Panel UI
        private GameObject alertBannerObj;
        private Text alertBannerText;
        private Image alertBannerImage;
        private Text logContentText;
        private ScrollRect logScrollRect;
        private float bannerDismissTimer = 0f;
        private List<string> logEntries = new List<string>();

        // Laser Pointer Components
        private GameObject laserObj;
        private LineRenderer laserLine;
        private GameObject reticleObj;
        private Button hoveredButton = null;
        private Button lastHoveredButton = null;
        private List<Button> allMenuButtons = new List<Button>();
        private float lastHandPokeTime = 0f;
        private Color lastBtnOrigColor = Color.clear;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            FindReferences();
            LoadFont();
            EnsureStereoMaterials();
            InitSkyboxes();
            LoadSavedSkyboxPreference();
            BuildVRCanvas();
            BuildLaserPointer();

            // Subscribe to GripperBridge alert and log events
            GripperBridge.OnAlertReceived += HandleAlertReceived;
            GripperBridge.OnLogReceived += HandleLogReceived;

            // Start closed by default
            SetMenuOpen(false);
            isDarkMode = PlayerPrefs.GetInt("VR_UI_DARK_MODE", 1) == 1;
            ApplyTheme(isDarkMode);
            currentSpeedPercent = Mathf.Clamp(PlayerPrefs.GetInt("UR3eVR_MotionSpeed", 100), 0, 100);
            RefreshSpeedUI();
            AddLog("SYSTEM", "VR Menu System Initialized (Stereo Dual-Eye Active).");
        }

        private void FindReferences()
        {
            if (centerEyeCamera == null)
            {
                var cec = GameObject.Find("CenterEyeAnchor");
                if (cec != null) centerEyeCamera = cec.GetComponent<Camera>();
                if (centerEyeCamera == null && Camera.main != null) centerEyeCamera = Camera.main;
            }

            if (headTransform == null)
            {
                if (centerEyeCamera != null) headTransform = centerEyeCamera.transform;
                else
                {
                    var cec = GameObject.Find("CenterEyeAnchor");
                    if (cec != null) headTransform = cec.transform;
                }
            }

            if (rightControllerAnchor == null)
            {
                GameObject rca = GameObject.Find("RightControllerAnchor");
                if (rca != null) rightControllerAnchor = rca.transform;
            }

            if (leftControllerAnchor == null)
            {
                GameObject lca = GameObject.Find("LeftControllerAnchor");
                if (lca != null) leftControllerAnchor = lca.transform;
            }

            if (vrHandMover == null) vrHandMover = FindAnyObjectByType<VRHandMover>();
            if (gripperBridge == null) gripperBridge = FindAnyObjectByType<GripperBridge>();
            if (webcamScript == null) webcamScript = FindAnyObjectByType<script>();
            if (pinchDetector == null) pinchDetector = FindAnyObjectByType<PinchDetector>();

            // Ensure EventSystem exists for UI clicks
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                GameObject esObj = new GameObject("EventSystem");
                esObj.AddComponent<EventSystem>();
                esObj.AddComponent<StandaloneInputModule>();
            }

            if (VRKeybindManager.Instance != null)
            {
                VRKeybindManager.Instance.onKeybindsChanged += RefreshKeybindUI;
                VRKeybindManager.Instance.onListeningStateChanged += OnKeybindListeningChanged;
            }
        }

        private void OnDestroy()
        {
            GripperBridge.OnAlertReceived -= HandleAlertReceived;
            GripperBridge.OnLogReceived -= HandleLogReceived;

            if (VRKeybindManager.Instance != null)
            {
                VRKeybindManager.Instance.onKeybindsChanged -= RefreshKeybindUI;
                VRKeybindManager.Instance.onListeningStateChanged -= OnKeybindListeningChanged;
            }
        }

        private void OnKeybindListeningChanged(bool isListening, VRAction action)
        {
            RefreshKeybindUI();
        }

        private void LoadFont()
        {
            try
            {
                uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            catch
            {
                uiFont = null;
            }
        }

        private void EnsureStereoMaterials()
        {
            // CRITICAL UI STEREO & COLOR FIX:
            // For UI elements (Image, Text, Button), leaving material = null tells Unity uGUI
            // to use Graphic.defaultGraphicMaterial (UI/Default shader).
            // UI/Default natively handles vertex color tinting, font glyph atlases, and alpha blending.
            // When Canvas.worldCamera is assigned to CenterEyeAnchor, it renders in BOTH eyes in VR.
            stereoUIMaterial = null;
#if UNITY_EDITOR
            if (visionWindowMaterial == null)
                visionWindowMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/JetXR/UI Kit For Vision Pro OS/Runtime/Materials/WindowBlurredBackground.mat")
                                    ?? UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/JetXR/UI Kit For Vision Pro OS/Runtime/Materials/WindowOverlayBackground.mat");
            if (visionElementMaterial == null)
                visionElementMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/JetXR/UI Kit For Vision Pro OS/Runtime/Materials/LightElementBackground.mat")
                                     ?? UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/JetXR/UI Kit For Vision Pro OS/Runtime/Materials/DarkElementBackground.mat");
            if (windowGlassSprite == null)
                windowGlassSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/JetXR/UI Kit For Vision Pro OS/Runtime/Sprites/Windows/WindowGlass.png");
            if (roundedRectSprite == null)
                roundedRectSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/JetXR/UI Kit For Vision Pro OS/Runtime/Sprites/Buttons/RoundedRectBackground.png");
            if (crossIconSprite == null)
                crossIconSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/JetXR/UI Kit For Vision Pro OS/Runtime/Sprites/Icons/Cross Icon.png");
#endif
            if (visionWindowMaterial == null) visionWindowMaterial = Resources.Load<Material>("VisionUI/WindowBlurredBackground");
            if (visionElementMaterial == null) visionElementMaterial = Resources.Load<Material>("VisionUI/LightElementBackground");
            if (windowGlassSprite == null) windowGlassSprite = Resources.Load<Sprite>("VisionUI/WindowGlass");
            if (roundedRectSprite == null) roundedRectSprite = Resources.Load<Sprite>("VisionUI/RoundedRectBackground");
            if (crossIconSprite == null) crossIconSprite = Resources.Load<Sprite>("VisionUI/Cross Icon");

            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit")
                              ?? Shader.Find("UR3eVR/StereoUnlitColor")
                              ?? Shader.Find("Unlit/Color");

            if (laserMaterial == null && unlitShader != null)
            {
                laserMaterial = new Material(unlitShader);
                laserMaterial.name = "Runtime_LaserMat";
                laserMaterial.enableInstancing = true;
                if (laserMaterial.HasProperty("_BaseColor")) laserMaterial.SetColor("_BaseColor", laserColor);
                if (laserMaterial.HasProperty("_Color")) laserMaterial.SetColor("_Color", laserColor);
            }
        }

        private void Update()
        {
            // 1. Toggle Menu Button: Rebindable via VRKeybindManager (Default: Left Menu / M Key)
            bool menuPressed = false;
            try { menuPressed = VRKeybindManager.IsActionDown(VRAction.OpenMenu); } catch { }
            if (!menuPressed)
            {
                try
                {
                    if (OVRInput.GetDown(menuButton, OVRInput.Controller.LTouch) ||
                        OVRInput.GetDown(rawMenuButton) ||
                        OVRInput.GetDown(menuButton, OVRInput.Controller.Touch) ||
                        Input.GetKeyDown(keyboardMenuKey))
                    {
                        menuPressed = true;
                    }
                }
                catch { }
            }

            if (menuPressed)
            {
                ToggleMenu();
            }

            // Keyboard direct shortcuts for quick Editor testing
            if (isMenuOpen)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1)) ActionSetOrigin();
                if (Input.GetKeyDown(KeyCode.Alpha2)) ShowPanel(MenuPanel.Camera);
                if (Input.GetKeyDown(KeyCode.Alpha3)) ShowPanel(MenuPanel.Log);
                if (Input.GetKeyDown(KeyCode.Alpha4)) ShowPanel(MenuPanel.Background);
                if (Input.GetKeyDown(KeyCode.Escape)) SetMenuOpen(false);
            }

            // Toast message timer
            if (toastTimer > 0f)
            {
                toastTimer -= Time.deltaTime;
                if (toastTimer <= 0f && toastText != null)
                {
                    toastText.gameObject.SetActive(false);
                }
            }

            // Alert banner timer
            if (bannerDismissTimer > 0f)
            {
                bannerDismissTimer -= Time.deltaTime;
                if (bannerDismissTimer <= 0f && alertBannerObj != null)
                {
                    alertBannerObj.SetActive(false);
                }
            }

            // Update Status text on Main Panel
            if (isMenuOpen && currentPanel == MenuPanel.Main && statusText != null)
            {
                UpdateMainStatusText();
            }

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

            // Update Laser Pointer interaction
            UpdateLaserPointer();

            // Direct Hand Poke interaction (à¹ƒà¸Šà¹‰à¸™à¸´à¹‰à¸§à¸ˆà¸´à¹‰à¸¡à¸›à¸¸à¹ˆà¸¡à¹‚à¸”à¸¢à¸•à¸£à¸‡)
            CheckDirectHandPoke();
        }

        public void ToggleMenu()
        {
            SetMenuOpen(!isMenuOpen);
        }

        public void SetMenuOpen(bool open)
        {
            isMenuOpen = open;
            if (canvasObj != null)
            {
                canvasObj.SetActive(open);
            }

            if (open)
            {
                RepositionMenuInFrontOfHead();
                ShowPanel(MenuPanel.Main);
                HapticPulse(0.3f, 0.1f);
            }
            else
            {
                if (laserLine != null) laserLine.enabled = false;
                if (reticleObj != null) reticleObj.SetActive(false);
                if (lastHoveredButton != null)
                {
                    var prevImg = lastHoveredButton.GetComponent<Image>();
                    if (prevImg != null && lastBtnOrigColor != Color.clear)
                    {
                        prevImg.color = lastBtnOrigColor;
                    }
                    lastHoveredButton = null;
                }
            }
        }

        public void RepositionMenuInFrontOfHead()
        {
            if (headTransform == null) FindReferences();
            if (headTransform == null || canvasObj == null) return;

            Vector3 fwd = headTransform.forward;
            Vector3 fwdFlat = Vector3.ProjectOnPlane(fwd, Vector3.up).normalized;
            if (fwdFlat.sqrMagnitude < 0.01f) fwdFlat = fwd;

            // Place 0.85m directly in front, slightly down by 0.05m
            canvasObj.transform.position = headTransform.position + fwdFlat * 0.85f + Vector3.up * -0.05f;
            canvasObj.transform.rotation = Quaternion.LookRotation(fwdFlat, Vector3.up);

            // Re-verify worldCamera for stereo rendering
            if (canvas != null && canvas.worldCamera == null && centerEyeCamera != null)
            {
                canvas.worldCamera = centerEyeCamera;
            }
        }

        public void ShowPanel(MenuPanel panel)
        {
            currentPanel = panel;
            if (panelMain != null) panelMain.SetActive(panel == MenuPanel.Main);
            if (panelCamera != null) panelCamera.SetActive(panel == MenuPanel.Camera);
            if (panelLog != null) panelLog.SetActive(panel == MenuPanel.Log);
            if (panelBg != null)
            {
                panelBg.SetActive(panel == MenuPanel.Background);
                if (panel == MenuPanel.Background)
                {
                    RefreshBgPageButtons();
                }
            }
            if (panelSettings != null) panelSettings.SetActive(panel == MenuPanel.Settings);
            if (panelSpeed != null)
            {
                panelSpeed.SetActive(panel == MenuPanel.Speed);
                if (panel == MenuPanel.Speed) RefreshSpeedUI();
            }

            if (panel == MenuPanel.Camera)
            {
                PopulateCameraList();
            }
            else if (panel == MenuPanel.Log)
            {
                RefreshLogText();
            }
        }

        // =========================================================================
        // Button Actions
        // =========================================================================

        public void ActionSetOrigin()
        {
            if (vrHandMover == null) vrHandMover = FindAnyObjectByType<VRHandMover>();
            if (vrHandMover != null)
            {
                vrHandMover.RecenterAnchor();
                ShowToast("Origin Recentered to Current Pose!");
                HapticPulse(0.7f, 0.2f);
                AddLog("ACTION", "Origin Recentered to current hand/controller pose.");
            }
            else
            {
                ShowToast("Warning: VRHandMover not found!");
            }
        }

        public void ActionOpenNewCam()
        {
            ShowPanel(MenuPanel.Camera);
            if (VRCameraWindow.Instance != null)
            {
                VRCameraWindow.Instance.SetWindowOpen(true);
                VRCameraWindow.Instance.RepositionWindowDefault();
            }
        }

        public void ActionOpenLog()
        {
            ShowPanel(MenuPanel.Log);
        }

        public void ActionOpenSettings()
        {
            ShowPanel(MenuPanel.Settings);
        }

        public void ActionOpenSpeed()
        {
            ShowPanel(MenuPanel.Speed);
        }

        public void ActionBackground()
        {
            ShowPanel(MenuPanel.Background);
        }

        public void ActionCloseMenu()
        {
            SetMenuOpen(false);
        }

        public void ActionBackToMain()
        {
            ShowPanel(MenuPanel.Main);
        }

        public void ActionClearLogs()
        {
            logEntries.Clear();
            AddLog("SYSTEM", "Logs cleared by user.");
            RefreshLogText();
        }

        // =========================================================================
        // Camera Selector Logic
        // =========================================================================

        private void PopulateCameraList()
        {
            if (camButtonContainer == null) return;

            // Clear old dynamic buttons
            foreach (var btn in dynamicCamButtons)
            {
                if (btn != null) Destroy(btn);
            }
            dynamicCamButtons.Clear();

            if (webcamScript == null) webcamScript = FindAnyObjectByType<script>(FindObjectsInactive.Include);

            var devices = WebCamTexture.devices;
            int activeIndex = -1;
            string activeName = "None";

            if (VRCameraWindow.Instance != null && VRCameraWindow.Instance.isOpen)
            {
                activeIndex = VRCameraWindow.Instance.currentCameraIndex;
                if (devices != null && activeIndex >= 0 && activeIndex < devices.Length)
                {
                    activeName = devices[activeIndex].name;
                }
            }
            else if (webcamScript != null)
            {
                activeIndex = webcamScript.CurrentDeviceIndex;
                activeName = webcamScript.CurrentDeviceName;
            }

            if (camStatusText != null)
            {
                camStatusText.text = $"Streaming: {activeName} (Index: {activeIndex})";
            }

            if (devices == null || devices.Length == 0)
            {
                CreateTextItem(camButtonContainer, "No webcam devices detected on system.", 16, Color.gray);
                return;
            }

            var addBtn = CreateSimpleButton(camButtonContainer, "<b>+ Open Additional Camera Window</b>", 50, new Color(0.08f, 0.42f, 0.36f, 0.95f), Color.white, () =>
            {
                VRCameraWindow.SpawnAdditionalWindow();
                ShowToast("Spawned New Camera Window!");
                AddLog("CAMERA", "Opened additional camera monitor in VR");
                HapticPulse(0.6f, 0.15f);
                PopulateCameraList();
            });
            dynamicCamButtons.Add(addBtn);

            for (int i = 0; i < devices.Length; i++)
            {
                int deviceIdx = i;
                string devName = devices[i].name;
                string devLower = devName.ToLower();

                // Filter out non-physical virtual drivers (Meta Quest 2, 3, Pro virtual capture)
                if (devLower.Contains("meta quest") || devLower.Contains("virtual") || devLower.Contains("unity"))
                {
                    continue;
                }

                bool isActive = (i == activeIndex);

                string label = isActive ? $"[ACTIVE] {deviceIdx}: {devName}" : $"[CAM] {deviceIdx}: {devName}";
                Color bgCol = isActive 
                    ? (isDarkMode ? new Color(0.0f, 0.45f, 0.40f, 0.95f) : new Color(0.70f, 0.88f, 0.98f, 0.98f))
                    : (isDarkMode ? new Color(0.12f, 0.16f, 0.24f, 0.95f) : new Color(0.82f, 0.88f, 0.96f, 0.98f));
                Color textCol = isActive 
                    ? (isDarkMode ? new Color(0.2f, 1.0f, 0.8f, 1.0f) : new Color(0.02f, 0.30f, 0.25f, 1.0f))
                    : (isDarkMode ? Color.white : new Color(0.08f, 0.12f, 0.22f));

                var btnObj = CreateSimpleButton(camButtonContainer, label, 46, bgCol, textCol, () =>
                {
                    if (VRCameraWindow.Instance != null)
                    {
                        VRCameraWindow.Instance.SetWindowOpen(true);
                        VRCameraWindow.Instance.OpenWebcamIndex(deviceIdx);
                        VRCameraWindow.Instance.RepositionWindowDefault();
                    }
                    if (webcamScript != null)
                    {
                        webcamScript.OpenDeviceByIndex(deviceIdx);
                    }
                    ShowToast($"Switched to Camera {deviceIdx}: {devName}");
                    AddLog("CAMERA", $"Switched active camera to [{deviceIdx}] {devName}");
                    HapticPulse(0.5f, 0.15f);
                    PopulateCameraList(); // Refresh UI highlight
                });

                dynamicCamButtons.Add(btnObj);
            }
        }

        // =========================================================================
        // Log & Auto-Popup on Alert Logic
        // =========================================================================

        private void HandleAlertReceived(string level, string message)
        {
            string cleanLevel = string.IsNullOrEmpty(level) ? "WARNING" : level.ToUpper();
            AddLog(cleanLevel, message);

            // Auto-popup Log Viewer UI immediately when warning or error arrives
            if (cleanLevel == "WARNING" || cleanLevel == "ERROR")
            {
                ShowAlertPopup(cleanLevel, message);
            }
        }

        private void HandleLogReceived(string message)
        {
            AddLog("LOG", message);
        }

        public void ShowAlertPopup(string level, string message)
        {
            if (!isMenuOpen)
            {
                SetMenuOpen(true);
            }

            ShowPanel(MenuPanel.Log);

            if (alertBannerObj != null && alertBannerText != null && alertBannerImage != null)
            {
                alertBannerObj.SetActive(true);
                bool isError = (level == "ERROR");
                alertBannerImage.color = isError ? new Color(0.9f, 0.15f, 0.15f, 0.95f) : new Color(0.95f, 0.70f, 0.05f, 0.95f);
                string icon = isError ? "[ERROR]" : "[WARNING]";
                alertBannerText.text = $"{icon} {message}";
                bannerDismissTimer = 8.0f;
            }

            HapticPulse(level == "ERROR" ? 0.9f : 0.6f, 0.35f);
        }

        public void AddLog(string tag, string message)
        {
            string timeStr = DateTime.Now.ToString("HH:mm:ss");
            string entry = $"[{timeStr}] [{tag}] {message}";
            logEntries.Add(entry);
            if (logEntries.Count > 100) logEntries.RemoveAt(0);

            if (currentPanel == MenuPanel.Log)
            {
                RefreshLogText();
            }
        }

        private void RefreshLogText()
        {
            if (logContentText == null) return;
            var sb = new System.Text.StringBuilder();
            for (int i = Mathf.Max(0, logEntries.Count - 25); i < logEntries.Count; i++)
            {
                string line = logEntries[i];
                if (isDarkMode)
                {
                    if (line.Contains("[ERROR]"))
                        sb.AppendLine($"<color=#FF5555>{line}</color>");
                    else if (line.Contains("[WARNING]"))
                        sb.AppendLine($"<color=#FFD54F>{line}</color>");
                    else if (line.Contains("[ACTION]") || line.Contains("[CAMERA]"))
                        sb.AppendLine($"<color=#4DD0E1>{line}</color>");
                    else
                        sb.AppendLine($"<color=#CCCCCC>{line}</color>");
                }
                else
                {
                    if (line.Contains("[ERROR]"))
                        sb.AppendLine($"<color=#C62828>{line}</color>");
                    else if (line.Contains("[WARNING]"))
                        sb.AppendLine($"<color=#E65100>{line}</color>");
                    else if (line.Contains("[ACTION]") || line.Contains("[CAMERA]"))
                        sb.AppendLine($"<color=#00695C>{line}</color>");
                    else
                        sb.AppendLine($"<color=#222222>{line}</color>");
                }
            }
            logContentText.text = sb.ToString();

            if (logScrollRect != null)
            {
                Canvas.ForceUpdateCanvases();
                logScrollRect.verticalNormalizedPosition = 0f;
            }
        }

        public void ShowToast(string message)
        {
            if (toastText == null) return;
            toastText.text = message;
            toastText.gameObject.SetActive(true);
            toastTimer = 2.5f;
        }

        private void UpdateMainStatusText()
        {
            if (statusText == null) return;
            string conn = (gripperBridge != null && gripperBridge.isConnected) ? "<color=#00E5FF>CONNECTED</color>" : "<color=#FF5555>DISCONNECTED</color>";
            int speed = (gripperBridge != null) ? Mathf.Clamp(gripperBridge.currentSpeedPct, 0, 100) : Mathf.Clamp(currentSpeedPercent, 0, 100);
            int grip = (gripperBridge != null) ? gripperBridge.actualGripperPos : 0;
            string trackingSource = (vrHandMover != null && vrHandMover.isUsingController) ? "Touch Remote" : "Bare Hand";
            statusText.text = $"UR3e Bridge: {conn}  |  Speed: {speed}%  |  Grip: {grip}/255  |  Mode: {trackingSource}";
        }

        private void HapticPulse(float amplitude, float duration)
        {
            try
            {
                OVRInput.SetControllerVibration(amplitude, amplitude, OVRInput.Controller.RTouch);
                OVRInput.SetControllerVibration(amplitude, amplitude, OVRInput.Controller.LTouch);
                StartCoroutine(StopHapticsCoroutine(duration));
            }
            catch { }
        }

        private IEnumerator StopHapticsCoroutine(float duration)
        {
            yield return new WaitForSeconds(duration);
            try
            {
                OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.RTouch);
                OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.LTouch);
            }
            catch { }
        }

        // =========================================================================
        // VR Laser Pointer Implementation (Shoots from Remote Controller Tip)
        // =========================================================================

        private void BuildLaserPointer()
        {
            laserObj = new GameObject("VR_LaserPointer");
            laserObj.transform.SetParent(transform, false);

            laserLine = laserObj.AddComponent<LineRenderer>();
            laserLine.useWorldSpace = true;
            laserLine.startWidth = 0.005f;
            laserLine.endWidth = 0.002f;
            laserLine.material = laserMaterial;
            laserLine.startColor = laserColor;
            laserLine.endColor = new Color(laserColor.r, laserColor.g, laserColor.b, 0.25f);
            laserLine.positionCount = 2;
            laserLine.enabled = false;

            reticleObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            reticleObj.name = "VR_LaserReticle";
            reticleObj.transform.SetParent(transform, false);
            reticleObj.transform.localScale = Vector3.one * 0.015f;
            Collider col = reticleObj.GetComponent<Collider>();
            if (col != null) Destroy(col);
            var rend = reticleObj.GetComponent<Renderer>();
            rend.material = laserMaterial;
            reticleObj.SetActive(false);
        }

        
        private void CheckDirectHandPoke()
        {
            if (!isMenuOpen || canvasObj == null) return;
            if (pinchDetector == null || pinchDetector.indexTipTransform == null)
            {
                if (pinchDetector == null) pinchDetector = FindAnyObjectByType<PinchDetector>();
                if (pinchDetector == null || pinchDetector.indexTipTransform == null) return;
            }

            if (Time.time - lastHandPokeTime < 0.35f) return;

            Vector3 fingerPos = pinchDetector.indexTipTransform.position;

            foreach (var btn in allMenuButtons)
            {
                if (btn == null || !btn.gameObject.activeInHierarchy || !btn.interactable) continue;

                RectTransform rt = btn.GetComponent<RectTransform>();
                if (rt == null) continue;

                Vector3 localPos = rt.InverseTransformPoint(fingerPos);
                Rect r = rt.rect;

                // à¸•à¸£à¸§à¸ˆà¸ªà¸­à¸šà¸§à¹ˆà¸²à¸›à¸¥à¸²à¸¢à¸™à¸´à¹‰à¸§à¸­à¸¢à¸¹à¹ˆà¹ƒà¸™à¸à¸£à¸­à¸šà¸›à¸¸à¹ˆà¸¡ à¹à¸¥à¸°à¹à¸•à¸°à¸—à¸µà¹ˆà¸£à¸°à¸™à¸²à¸šà¸›à¸¸à¹ˆà¸¡ (< 2.5 à¸‹à¸¡.)
                if (r.Contains(new Vector2(localPos.x, localPos.y)) && Mathf.Abs(localPos.z) <= 0.025f)
                {
                    btn.onClick.Invoke();
                    lastHandPokeTime = Time.time;
                    HapticPulse(0.35f, 0.08f);
                    break;
                }
            }
        }

        private void UpdateLaserPointer()
        {
            if (VRRemoteLaserPointer.Instance != null)
            {
                if (laserLine != null) laserLine.enabled = false;
                if (reticleObj != null) reticleObj.SetActive(false);
                return;
            }

            if (!enableLaserPointer || !isMenuOpen || canvasObj == null)
            {
                if (laserLine != null) laserLine.enabled = false;
                if (reticleObj != null) reticleObj.SetActive(false);
                return;
            }

            // Determine Ray origin and forward direction:
            // When controller is tracked -> Shoots straight out of the front tip of the Remote!
            Vector3 rayOrigin;
            Vector3 rayDir;

            bool controllerTracked = rightControllerAnchor != null && OVRInput.GetControllerPositionTracked(OVRInput.Controller.RTouch);
            if (controllerTracked)
            {
                // Tip of Remote Controller (slightly offset forward from grip center)
                rayOrigin = rightControllerAnchor.position + rightControllerAnchor.forward * 0.06f + rightControllerAnchor.up * 0.02f;
                rayDir = rightControllerAnchor.forward;
            }
            else
            {
                // Headset fallback if remote is not held
                Transform fallback = headTransform != null ? headTransform : Camera.main?.transform;
                if (fallback == null) return;
                rayOrigin = fallback.position + fallback.forward * 0.1f + Vector3.down * 0.05f;
                rayDir = fallback.forward;
            }

            laserLine.enabled = true;
            laserLine.SetPosition(0, rayOrigin);

            // Raycast against Canvas Plane
            Ray ray = new Ray(rayOrigin, rayDir);
            Plane canvasPlane = new Plane(-canvasObj.transform.forward, canvasObj.transform.position);

            Button hitButton = null;
            if (canvasPlane.Raycast(ray, out float enter) && enter <= laserLength && enter > 0f)
            {
                Vector3 hitPoint = ray.GetPoint(enter);
                laserLine.SetPosition(1, hitPoint);
                reticleObj.SetActive(true);
                reticleObj.transform.position = hitPoint;

                // Find if hitting any Button in Canvas
                Vector3 localHit = canvasObj.transform.InverseTransformPoint(hitPoint);
                var buttons = canvasObj.GetComponentsInChildren<Button>();
                foreach (var btn in buttons)
                {
                    if (!btn.gameObject.activeInHierarchy || !btn.interactable) continue;
                    RectTransform rt = btn.GetComponent<RectTransform>();
                    if (rt == null) continue;

                    Vector3 btnLocalPos = canvasObj.transform.InverseTransformPoint(rt.position);
                    Rect r = rt.rect;
                    float scaleX = rt.lossyScale.x / canvasObj.transform.lossyScale.x;
                    float scaleY = rt.lossyScale.y / canvasObj.transform.lossyScale.y;

                    float halfW = (r.width * scaleX) * 0.5f;
                    float halfH = (r.height * scaleY) * 0.5f;

                    if (Mathf.Abs(localHit.x - btnLocalPos.x) <= halfW &&
                        Mathf.Abs(localHit.y - btnLocalPos.y) <= halfH)
                    {
                        hitButton = btn;
                        break;
                    }
                }
            }
            else
            {
                laserLine.SetPosition(1, rayOrigin + rayDir * laserLength);
                reticleObj.SetActive(false);
            }

            hoveredButton = hitButton;

            // Laser Beam Hover Visual Feedback (Button Glow)
            if (hitButton != lastHoveredButton)
            {
                if (lastHoveredButton != null)
                {
                    var prevImg = lastHoveredButton.GetComponent<Image>();
                    if (prevImg != null && lastBtnOrigColor != Color.clear)
                    {
                        prevImg.color = lastBtnOrigColor;
                    }
                }

                if (hitButton != null)
                {
                    var newImg = hitButton.GetComponent<Image>();
                    if (newImg != null)
                    {
                        lastBtnOrigColor = newImg.color;
                        newImg.color = new Color(
                            Mathf.Min(1f, lastBtnOrigColor.r * 1.5f + 0.15f),
                            Mathf.Min(1f, lastBtnOrigColor.g * 1.5f + 0.15f),
                            Mathf.Min(1f, lastBtnOrigColor.b * 1.5f + 0.25f),
                            1f
                        );
                        HapticPulse(0.15f, 0.03f);
                    }
                }

                lastHoveredButton = hitButton;
            }

            // Trigger click detection (Right Controller Index Trigger or Button One / A or Left Mouse Click)
            bool triggerClicked = false;
            try
            {
                if (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch) ||
                    OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch) ||
                    OVRInput.GetDown(OVRInput.RawButton.RIndexTrigger))
                {
                    triggerClicked = true;
                }
            }
            catch { }

            try
            {
                if (!triggerClicked && Input.GetMouseButtonDown(0))
                {
                    triggerClicked = true;
                }
            }
            catch { }

            if (triggerClicked && hoveredButton != null)
            {
                hoveredButton.onClick.Invoke();
                HapticPulse(0.4f, 0.1f);
            }
        }

        // =========================================================================
        // Dynamic World-Space UI Builder (High-contrast obsidian aesthetics + Stereo Fix)
        // =========================================================================

        private void BuildVRCanvas()
        {
            canvasObj = new GameObject("VR_Menu_Canvas");
            canvasObj.transform.SetParent(transform, false);

            canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            // CRITICAL FOR SINGLE PASS INSTANCED VR: Assign CenterEyeAnchor Camera!
            if (centerEyeCamera != null)
            {
                canvas.worldCamera = centerEyeCamera;
            }

            var scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 10f;

            raycaster = canvasObj.AddComponent<GraphicRaycaster>();

            RectTransform canvasRT = canvasObj.GetComponent<RectTransform>();
            canvasRT.sizeDelta = new Vector2(760f, 500f);
            canvasRT.localScale = Vector3.one * 0.0011f;

            // 1. Build Main Panel
            panelMain = CreatePanel(canvasRT, "Panel_MainMenu");
            BuildMainPanelContent(panelMain.transform);

            // 2. Build Camera Selection Panel
            panelCamera = CreatePanel(canvasRT, "Panel_CameraSelect");
            BuildCameraPanelContent(panelCamera.transform);

            // 3. Build Log Viewer Panel
            panelLog = CreatePanel(canvasRT, "Panel_LogViewer");
            BuildLogPanelContent(panelLog.transform);

            // 4. Build Background Selector Panel
            panelBg = CreatePanel(canvasRT, "Panel_Background");
            BuildBackgroundPanelContent(panelBg.transform);

            // 5. Build Settings Panel (Item 4b)
            panelSettings = CreatePanel(canvasRT, "Panel_Settings");
            BuildSettingsPanelContent(panelSettings.transform);

            // 6. Build Speed Control Panel (Item 4c)
            panelSpeed = CreatePanel(canvasRT, "Panel_Speed");
            BuildSpeedPanelContent(panelSpeed.transform);

            // 7. Meta Horizon OS Bottom Pill Grab Handle Bar
            GameObject grabBar = new GameObject("GrabHandleBar");
            grabBar.transform.SetParent(canvasObj.transform, false);
            RectTransform grabRT = grabBar.AddComponent<RectTransform>();
            grabRT.anchorMin = new Vector2(0.26f, -0.09f);
            grabRT.anchorMax = new Vector2(0.74f, -0.02f);
            grabRT.offsetMin = Vector2.zero;
            grabRT.offsetMax = Vector2.zero;

            Image grabImg = grabBar.AddComponent<Image>();
            if (roundedRectSprite != null)
            {
                grabImg.sprite = roundedRectSprite;
                grabImg.type = Image.Type.Sliced;
                grabImg.material = visionElementMaterial;
                grabImg.color = new Color(0.25f, 0.32f, 0.44f, 0.92f);
            }
            else
            {
                grabImg.material = null;
                grabImg.color = new Color(0.18f, 0.22f, 0.32f, 0.90f);
            }

            Outline grabOutline = grabBar.AddComponent<Outline>();
            grabOutline.effectColor = new Color(1f, 1f, 1f, 0.25f);
            grabOutline.effectDistance = new Vector2(1f, -1f);

            GameObject grabLabelObj = new GameObject("GrabLabel");
            grabLabelObj.transform.SetParent(grabBar.transform, false);
            RectTransform glRT = grabLabelObj.AddComponent<RectTransform>();
            glRT.anchorMin = Vector2.zero;
            glRT.anchorMax = Vector2.one;
            glRT.offsetMin = Vector2.zero;
            glRT.offsetMax = Vector2.zero;
            Text grabLabel = grabLabelObj.AddComponent<Text>();
            grabLabel.font = uiFont;
            grabLabel.fontSize = 13;
            grabLabel.alignment = TextAnchor.MiddleCenter;
            grabLabel.color = new Color(0.8f, 0.9f, 1.0f, 0.85f);
            grabLabel.text = "---  GRAB TO MOVE  ---";

            var dragger = grabBar.AddComponent<VRWindowDragger>();
            dragger.windowTransform = canvasObj.transform;
            dragger.handleImage = grabImg;

            // Bottom-Right Corner Resize Handle (Item 2)
            GameObject resizeHandleObj = new GameObject("WindowResizeHandle");
            resizeHandleObj.transform.SetParent(canvasObj.transform, false);
            RectTransform resRT = resizeHandleObj.AddComponent<RectTransform>();
            resRT.anchorMin = new Vector2(0.92f, -0.09f);
            resRT.anchorMax = new Vector2(0.98f, -0.02f);
            resRT.offsetMin = Vector2.zero;
            resRT.offsetMax = Vector2.zero;

            Image resImg = resizeHandleObj.AddComponent<Image>();
            if (roundedRectSprite != null)
            {
                resImg.sprite = roundedRectSprite;
                resImg.type = Image.Type.Sliced;
            }
            resImg.material = null;
            resImg.color = new Color(0.15f, 0.75f, 0.95f, 0.50f);

            var resizer = resizeHandleObj.AddComponent<VRWindowResizer>();
            resizer.targetCanvasRT = canvasRT;
            resizer.handleImage = resImg;

            grabBarObj = grabBar;
            this.resizeHandleObj = resizeHandleObj;

            Text resText = CreateText(resizeHandleObj.transform, "⤡", 16, Color.white, TextAnchor.MiddleCenter);
            resText.fontStyle = FontStyle.Bold;
        }

        private GameObject CreatePanel(Transform parent, string name)
        {
            GameObject p = new GameObject(name);
            p.transform.SetParent(parent, false);

            RectTransform rt = p.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            // Vision Pro Frosted Glass Window Frame
            Image img = p.AddComponent<Image>();
            if (roundedRectSprite != null)
            {
                img.sprite = roundedRectSprite;
                img.type = Image.Type.Sliced;
            }
            else if (windowGlassSprite != null)
            {
                img.sprite = windowGlassSprite;
                img.type = Image.Type.Sliced;
                img.pixelsPerUnitMultiplier = 2f;
            }
            img.material = null; // Stereo-safe UI/Default shader respects vertex color!
            img.color = isDarkMode ? new Color(0.07f, 0.09f, 0.14f, 0.96f) : new Color(0.93f, 0.95f, 0.98f, 0.98f);

            Outline outline = p.AddComponent<Outline>();
            outline.effectColor = isDarkMode ? new Color(0.0f, 0.80f, 1.0f, 0.35f) : new Color(0.20f, 0.30f, 0.45f, 0.45f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);

            return p;
        }

        private void BuildMainPanelContent(Transform p)
        {
            CreateHeader(p, "UR3e Quick Menu", () => ActionCloseMenu());

            // 5 Main Buttons Container (Item 4)
            GameObject btnGrid = new GameObject("ButtonGrid");
            btnGrid.transform.SetParent(p, false);
            RectTransform gridRT = btnGrid.AddComponent<RectTransform>();
            gridRT.anchorMin = new Vector2(0.08f, 0.16f);
            gridRT.anchorMax = new Vector2(0.92f, 0.84f);
            gridRT.offsetMin = Vector2.zero;
            gridRT.offsetMax = Vector2.zero;

            var vlg = btnGrid.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 10;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = true;

            // 1. Set Origin
            btnMainSetOrigin = CreateSimpleButton(btnGrid.transform, "Set Origin (Calibrate to Hand Pose)", 46,
                isDarkMode ? new Color(0.0f, 0.38f, 0.55f, 0.95f) : new Color(0.80f, 0.90f, 0.98f, 0.98f),
                isDarkMode ? Color.white : new Color(0.04f, 0.24f, 0.44f), ActionSetOrigin);

            // 2. Add Cam (Item 4d: renamed from Open New Cam)
            btnMainAddCam = CreateSimpleButton(btnGrid.transform, "Add Cam (Webcam Selector)", 46,
                isDarkMode ? new Color(0.12f, 0.22f, 0.40f, 0.95f) : new Color(0.85f, 0.88f, 0.98f, 0.98f),
                isDarkMode ? Color.white : new Color(0.10f, 0.16f, 0.46f), ActionOpenNewCam);

            // 3. Open Log
            btnMainOpenLog = CreateSimpleButton(btnGrid.transform, "Open Log (UR3e & Python Monitor)", 46,
                isDarkMode ? new Color(0.24f, 0.18f, 0.38f, 0.95f) : new Color(0.92f, 0.86f, 0.98f, 0.98f),
                isDarkMode ? Color.white : new Color(0.26f, 0.10f, 0.46f), ActionOpenLog);

            // 4. Settings (Item 4b: Controls & Appearance)
            btnMainSettings = CreateSimpleButton(btnGrid.transform, "Settings (Controls & Appearance)", 46,
                isDarkMode ? new Color(0.14f, 0.26f, 0.32f, 0.95f) : new Color(0.84f, 0.91f, 0.94f, 0.98f),
                isDarkMode ? Color.white : new Color(0.08f, 0.24f, 0.32f), ActionOpenSettings);

            // 5. Speed (Item 4c: Velocity Control & Snap Presets)
            btnMainSpeed = CreateSimpleButton(btnGrid.transform, "Speed (Robot Motion Velocity)", 46,
                isDarkMode ? new Color(0.10f, 0.34f, 0.28f, 0.95f) : new Color(0.82f, 0.94f, 0.90f, 0.98f),
                isDarkMode ? Color.white : new Color(0.06f, 0.30f, 0.22f), ActionOpenSpeed);

            // Status Bar at Bottom
            GameObject statObj = new GameObject("StatusBar");
            statObj.transform.SetParent(p, false);
            RectTransform statRT = statObj.AddComponent<RectTransform>();
            statRT.anchorMin = new Vector2(0.05f, 0.04f);
            statRT.anchorMax = new Vector2(0.95f, 0.16f);
            statRT.offsetMin = Vector2.zero;
            statRT.offsetMax = Vector2.zero;

            statusText = statObj.AddComponent<Text>();
            statusText.material = null;
            statusText.font = uiFont;
            statusText.fontSize = 15;
            statusText.alignment = TextAnchor.MiddleCenter;
            statusText.color = new Color(0.85f, 0.85f, 0.85f, 1f);

            // Toast overlay
            GameObject toastObj = new GameObject("Toast");
            toastObj.transform.SetParent(p, false);
            RectTransform toastRT = toastObj.AddComponent<RectTransform>();
            toastRT.anchorMin = new Vector2(0.15f, 0.15f);
            toastRT.anchorMax = new Vector2(0.85f, 0.25f);
            toastRT.offsetMin = Vector2.zero;
            toastRT.offsetMax = Vector2.zero;

            Image tImg = toastObj.AddComponent<Image>();
            tImg.material = null;
            tImg.color = new Color(0.0f, 0.5f, 0.3f, 0.95f);
            toastText = CreateText(toastObj.transform, "", 16, Color.white, TextAnchor.MiddleCenter);
            toastObj.SetActive(false);
        }

        private void BuildCameraPanelContent(Transform p)
        {
            CreateHeader(p, "Select Camera Feed", () => ActionCloseMenu(), () => ActionBackToMain());

            GameObject subObj = new GameObject("CamStatus");
            subObj.transform.SetParent(p, false);
            RectTransform subRT = subObj.AddComponent<RectTransform>();
            subRT.anchorMin = new Vector2(0.06f, 0.74f);
            subRT.anchorMax = new Vector2(0.94f, 0.83f);
            subRT.offsetMin = Vector2.zero;
            subRT.offsetMax = Vector2.zero;
            camStatusText = subObj.AddComponent<Text>();
            camStatusText.material = null;
            camStatusText.font = uiFont;
            camStatusText.fontSize = 16;
            camStatusText.alignment = TextAnchor.MiddleLeft;
            camStatusText.color = new Color(0.0f, 0.85f, 1.0f, 1f);

            GameObject scrollObj = new GameObject("CamScroll");
            scrollObj.transform.SetParent(p, false);
            RectTransform scrollRT = scrollObj.AddComponent<RectTransform>();
            scrollRT.anchorMin = new Vector2(0.06f, 0.16f);
            scrollRT.anchorMax = new Vector2(0.94f, 0.72f);
            scrollRT.offsetMin = Vector2.zero;
            scrollRT.offsetMax = Vector2.zero;

            scrollObj.AddComponent<RectMask2D>();

            var vlg = scrollObj.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 10;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            camButtonContainer = scrollObj.transform;

            GameObject bottomBar = new GameObject("BottomBar");
            bottomBar.transform.SetParent(p, false);
            RectTransform bRT = bottomBar.AddComponent<RectTransform>();
            bRT.anchorMin = new Vector2(0.06f, 0.04f);
            bRT.anchorMax = new Vector2(0.94f, 0.13f);
            bRT.offsetMin = Vector2.zero;
            bRT.offsetMax = Vector2.zero;

            var hlg = bottomBar.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 16;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;

            CreateSimpleButton(bottomBar.transform, "Refresh Cameras", 38,
                new Color(0.18f, 0.22f, 0.32f, 0.95f), Color.white, PopulateCameraList);

            CreateSimpleButton(bottomBar.transform, "< Back to Menu", 38,
                new Color(0.24f, 0.14f, 0.18f, 0.95f), Color.white, ActionBackToMain);
        }

        private void BuildLogPanelContent(Transform p)
        {
            CreateHeader(p, "UR3e & Python System Log", () => ActionCloseMenu(), () => ActionBackToMain());

            alertBannerObj = new GameObject("AlertBanner");
            alertBannerObj.transform.SetParent(p, false);
            RectTransform abRT = alertBannerObj.AddComponent<RectTransform>();
            abRT.anchorMin = new Vector2(0.06f, 0.74f);
            abRT.anchorMax = new Vector2(0.94f, 0.84f);
            abRT.offsetMin = Vector2.zero;
            abRT.offsetMax = Vector2.zero;

            alertBannerImage = alertBannerObj.AddComponent<Image>();
            alertBannerImage.material = null; // stereo-safe
            alertBannerImage.color = new Color(0.95f, 0.70f, 0.05f, 0.95f);
            alertBannerText = CreateText(alertBannerObj.transform, "[!] SYSTEM ALERT", 16, Color.black, TextAnchor.MiddleCenter);
            alertBannerText.fontStyle = FontStyle.Bold;
            alertBannerObj.SetActive(false);

            GameObject scrollObj = new GameObject("LogScroll");
            scrollObj.transform.SetParent(p, false);
            RectTransform scrollRT = scrollObj.AddComponent<RectTransform>();
            scrollRT.anchorMin = new Vector2(0.06f, 0.16f);
            scrollRT.anchorMax = new Vector2(0.94f, 0.72f);
            scrollRT.offsetMin = Vector2.zero;
            scrollRT.offsetMax = Vector2.zero;

            Image scBg = scrollObj.AddComponent<Image>();
            scBg.material = null; // stereo-safe
            scBg.color = new Color(0.03f, 0.04f, 0.06f, 0.95f);
            scrollObj.AddComponent<Mask>().showMaskGraphic = true;

            logScrollRect = scrollObj.AddComponent<ScrollRect>();
            logScrollRect.horizontal = false;
            logScrollRect.vertical = true;

            GameObject contentObj = new GameObject("LogContent");
            contentObj.transform.SetParent(scrollObj.transform, false);
            RectTransform contentRT = contentObj.AddComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0f, 0f);
            contentRT.anchorMax = new Vector2(1f, 1f);
            contentRT.pivot = new Vector2(0.5f, 0f);
            contentRT.offsetMin = new Vector2(12f, 12f);
            contentRT.offsetMax = new Vector2(-12f, -12f);

            logContentText = contentObj.AddComponent<Text>();
            logContentText.material = null; // stereo-safe
            logContentText.font = uiFont;
            logContentText.fontSize = 14;
            logContentText.lineSpacing = 1.15f;
            logContentText.color = Color.white;
            logContentText.alignment = TextAnchor.LowerLeft;

            var csf = contentObj.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            logScrollRect.content = contentRT;

            GameObject bottomBar = new GameObject("LogBottomBar");
            bottomBar.transform.SetParent(p, false);
            RectTransform bRT = bottomBar.AddComponent<RectTransform>();
            bRT.anchorMin = new Vector2(0.06f, 0.04f);
            bRT.anchorMax = new Vector2(0.94f, 0.13f);
            bRT.offsetMin = Vector2.zero;
            bRT.offsetMax = Vector2.zero;

            var hlg = bottomBar.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 16;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;

            CreateSimpleButton(bottomBar.transform, "Clear Logs", 38,
                new Color(0.24f, 0.18f, 0.22f, 0.95f), Color.white, ActionClearLogs);

            CreateSimpleButton(bottomBar.transform, "< Back to Menu", 38,
                new Color(0.18f, 0.22f, 0.32f, 0.95f), Color.white, ActionBackToMain);
        }

        public void InitSkyboxes()
        {
            if (skyboxOptions == null || skyboxOptions.Count == 0)
            {
                skyboxOptions = new List<SkyboxOption>();

                // Option 0: Solid Black Lab Mode
                skyboxOptions.Add(new SkyboxOption { 
                    displayName = "Solid Black", 
                    category = "Minimalist Void", 
                    skyboxMaterial = null 
                });

// Day & Atmosphere
                AddSkyboxOption("Casual Day", "Clear Daylight", "Assets/SkySeries Freebie/CasualDay.mat");
                AddSkyboxOption("Cloudy Morning", "Sunrise Pastels", "Assets/SkySeries Freebie/Cloudymorning.mat");
                AddSkyboxOption("Clouded Sun Glow", "Golden Hour", "Assets/SkySeries Freebie/CloudedSunGlow.mat");
                AddSkyboxOption("Day In The Clouds", "Cloudtop Horizon", "Assets/SkySeries Freebie/DayInTheClouds.mat");
                AddSkyboxOption("Fluffball Day", "Sunny Clouds", "Assets/SkySeries Freebie/FluffballDay.mat");

                // Weather & High Skies
                AddSkyboxOption("Skyhigh Fluffy Cloud", "High Altitude", "Assets/SkySeries Freebie/SkyhighFluffycloudField.mat");
                AddSkyboxOption("Sunless Cirrus", "Soft Overcast", "Assets/SkySeries Freebie/SunlessCirruscover.mat");
                AddSkyboxOption("Dark Storm", "Thunderstorm", "Assets/SkySeries Freebie/DarkStorm.mat");
                AddSkyboxOption("Mega Sun", "Dramatic Sun", "Assets/SkySeries Freebie/MegaSun.mat");
                AddSkyboxOption("Ambience Exposure", "Studio HDRI", "Assets/SkySeries Freebie/AmbienceExposure.mat");
                AddSkyboxOption("High Fantasy", "Dreamscape", "Assets/SkySeries Freebie/HighFantasy.mat");

                // Space, Night & Aquatic
                AddSkyboxOption("Coriolis Night 4K", "Midnight Stars", "Assets/SkySeries Freebie/CoriolisNight4k.mat");
                AddSkyboxOption("Cosmic Cool Cloud", "Deep Nebula", "Assets/SkySeries Freebie/CosmicCoolCloud_Eq.mat");
                AddSkyboxOption("Planetary Earth", "Orbital Space", "Assets/SkySeries Freebie/PlanetaryEarth.mat");
                AddSkyboxOption("Unearthly Red", "Alien World", "Assets/SkySeries Freebie/UnearthlyRed.mat");
                AddSkyboxOption("Under The Sea 4K", "Aquatic Abyss", "Assets/SkySeries Freebie/UnderTheSea4k.mat");
                AddSkyboxOption("6-Sided Cosmic Cloud", "Cosmic 360", "Assets/SkySeries Freebie/6sidedCosmicCoolCloud.mat");

            }
        }

        private void AddSkyboxOption(string name, string category, string assetPath)
        {
            Material mat = null;
#if UNITY_EDITOR
            mat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(assetPath);
#endif
            if (mat == null)
            {
                string resName = System.IO.Path.GetFileNameWithoutExtension(assetPath);
                mat = Resources.Load<Material>("Skyboxes/" + resName);
            }

            if (mat != null)
            {
                skyboxOptions.Add(new SkyboxOption {
                    displayName = name,
                    category = category,
                    skyboxMaterial = mat
                });
            }
        }

        public void LoadSavedSkyboxPreference()
        {
            string saved = PlayerPrefs.GetString("UR3eVR_SelectedSkybox", "");
            if (!string.IsNullOrEmpty(saved))
            {
                var found = skyboxOptions.Find(s => s.displayName == saved);
                if (found != null)
                {
                    ApplySkybox(found);
                    return;
                }
            }

            // Default to Casual Day or first available skybox
            var defaultOpt = skyboxOptions.Find(s => s.displayName.Contains("Casual Day")) 
                          ?? skyboxOptions.Find(s => s.skyboxMaterial != null);
            if (defaultOpt != null)
            {
                ApplySkybox(defaultOpt);
            }
        }

        private void BuildBackgroundPanelContent(Transform p)
        {
            CreateHeader(p, "Scene & Skybox Environment", () => ActionCloseMenu(), () => ActionBackToMain());

            // 1. Active Skybox Status Indicator Card
            GameObject activeCard = new GameObject("ActiveBgCard");
            activeCard.transform.SetParent(p, false);
            RectTransform aRT = activeCard.AddComponent<RectTransform>();
            aRT.anchorMin = new Vector2(0.06f, 0.74f);
            aRT.anchorMax = new Vector2(0.94f, 0.85f);
            aRT.offsetMin = Vector2.zero;
            aRT.offsetMax = Vector2.zero;

            Image aImg = activeCard.AddComponent<Image>();
            aImg.material = null;
            aImg.color = new Color(0.06f, 0.12f, 0.20f, 0.95f);

            Outline aOl = activeCard.AddComponent<Outline>();
            aOl.effectColor = new Color(0.0f, 0.85f, 1.0f, 0.50f);
            aOl.effectDistance = new Vector2(1.5f, -1.5f);

            activeBgStatusText = CreateText(activeCard.transform, 
                $"Active Skybox: <b><color=#00E5FF>{currentActiveSkyboxName}</color></b>   <size=13><color=#00FF88>[Applied to all VR Eyes]</color></size>",
                16, Color.white, TextAnchor.MiddleCenter);

            // 2. 2-Column Buttons Grid Container
            bgGridContainer = new GameObject("BgGridContainer");
            bgGridContainer.transform.SetParent(p, false);
            RectTransform gRT = bgGridContainer.AddComponent<RectTransform>();
            gRT.anchorMin = new Vector2(0.06f, 0.22f);
            gRT.anchorMax = new Vector2(0.94f, 0.72f);
            gRT.offsetMin = Vector2.zero;
            gRT.offsetMax = Vector2.zero;

            var glg = bgGridContainer.AddComponent<GridLayoutGroup>();
            glg.cellSize = new Vector2(325f, 66f);
            glg.spacing = new Vector2(18f, 10f);
            glg.startCorner = GridLayoutGroup.Corner.UpperLeft;
            glg.startAxis = GridLayoutGroup.Axis.Horizontal;
            glg.childAlignment = TextAnchor.MiddleCenter;

            // 3. Pagination Bar
            GameObject pageBar = new GameObject("PaginationBar");
            pageBar.transform.SetParent(p, false);
            RectTransform pbRT = pageBar.AddComponent<RectTransform>();
            pbRT.anchorMin = new Vector2(0.06f, 0.10f);
            pbRT.anchorMax = new Vector2(0.94f, 0.19f);
            pbRT.offsetMin = Vector2.zero;
            pbRT.offsetMax = Vector2.zero;

            CreatePaginationButton(pageBar.transform, "< Prev Page", new Vector2(0f, 0f), new Vector2(0.26f, 1f), () => ChangeBgPage(-1));

            GameObject piObj = new GameObject("PageIndicator");
            piObj.transform.SetParent(pageBar.transform, false);
            RectTransform piRT = piObj.AddComponent<RectTransform>();
            piRT.anchorMin = new Vector2(0.28f, 0f);
            piRT.anchorMax = new Vector2(0.72f, 1f);
            piRT.offsetMin = Vector2.zero;
            piRT.offsetMax = Vector2.zero;

            bgPageIndicatorText = piObj.AddComponent<Text>();
            bgPageIndicatorText.material = null;
            bgPageIndicatorText.font = uiFont;
            bgPageIndicatorText.fontSize = 15;
            bgPageIndicatorText.fontStyle = FontStyle.Bold;
            bgPageIndicatorText.alignment = TextAnchor.MiddleCenter;
            bgPageIndicatorText.color = new Color(0.85f, 0.95f, 1.0f, 0.95f);

            CreatePaginationButton(pageBar.transform, "Next Page >", new Vector2(0.74f, 0f), new Vector2(1.0f, 1f), () => ChangeBgPage(1));

            // 4. Bottom Bar (Back to Main Menu)
            GameObject bottomBar = new GameObject("BottomBar");
            bottomBar.transform.SetParent(p, false);
            RectTransform bbRT = bottomBar.AddComponent<RectTransform>();
            bbRT.anchorMin = new Vector2(0.06f, 0.015f);
            bbRT.anchorMax = new Vector2(0.94f, 0.085f);
            bbRT.offsetMin = Vector2.zero;
            bbRT.offsetMax = Vector2.zero;

            CreateSimpleButton(bottomBar.transform, "< Back to Main Menu", 36,
                new Color(0.18f, 0.22f, 0.32f, 0.95f), Color.white, ActionBackToMain);

            RefreshBgPageButtons();
        }

        private GameObject CreatePaginationButton(Transform parent, string label, Vector2 min, Vector2 max, Action onClick)
        {
            GameObject btnObj = new GameObject("Btn_" + label);
            btnObj.transform.SetParent(parent, false);

            RectTransform rt = btnObj.AddComponent<RectTransform>();
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            Image img = btnObj.AddComponent<Image>();
            img.material = null;
            img.color = isDarkMode ? new Color(0.16f, 0.22f, 0.32f, 0.95f) : new Color(0.76f, 0.82f, 0.92f, 0.98f);

            Button btn = btnObj.AddComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.highlightedColor = new Color(0.25f, 0.38f, 0.52f, 1f);
            cb.pressedColor = new Color(0.10f, 0.15f, 0.24f, 1f);
            btn.colors = cb;
            btn.onClick.AddListener(() => onClick());
            allMenuButtons.Add(btn);

            Outline ol = btnObj.AddComponent<Outline>();
            ol.effectColor = isDarkMode ? new Color(0.0f, 0.85f, 1.0f, 0.35f) : new Color(0.20f, 0.40f, 0.70f, 0.40f);
            ol.effectDistance = new Vector2(1f, -1f);

            Text txt = CreateText(btnObj.transform, label, 14, isDarkMode ? Color.white : new Color(0.08f, 0.12f, 0.22f), TextAnchor.MiddleCenter);
            txt.fontStyle = FontStyle.Bold;

            return btnObj;
        }

        public void ChangeBgPage(int delta)
        {
            int totalPages = Mathf.CeilToInt((float)skyboxOptions.Count / 6);
            if (totalPages == 0) totalPages = 1;
            currentBgPage = Mathf.Clamp(currentBgPage + delta, 0, totalPages - 1);
            RefreshBgPageButtons();

            OVRInput.SetControllerVibration(0.3f, 0.3f, OVRInput.Controller.RTouch);
            StartCoroutine(StopHapticsAfter(0.04f));
        }

        public void RefreshBgPageButtons()
        {
            if (bgGridContainer == null) return;

            foreach (var b in activePageButtons)
            {
                if (b != null)
                {
                    var btnComp = b.GetComponent<Button>();
                    if (btnComp != null) allMenuButtons.Remove(btnComp);
                    Destroy(b);
                }
            }
            activePageButtons.Clear();

            int totalPages = Mathf.CeilToInt((float)skyboxOptions.Count / 6);
            if (totalPages == 0) totalPages = 1;
            currentBgPage = Mathf.Clamp(currentBgPage, 0, totalPages - 1);

            int startIdx = currentBgPage * 6;
            int count = Mathf.Min(6, skyboxOptions.Count - startIdx);

            string categoryHeader = "";

            for (int i = 0; i < count; i++)
            {
                int optIndex = startIdx + i;
                var opt = skyboxOptions[optIndex];
                if (string.IsNullOrEmpty(categoryHeader) && !string.IsNullOrEmpty(opt.category))
                {
                    categoryHeader = opt.category;
                }

                bool isActive = opt.displayName == currentActiveSkyboxName;

                GameObject btnObj = new GameObject("BgBtn_" + optIndex);
                btnObj.transform.SetParent(bgGridContainer.transform, false);

                Image img = btnObj.AddComponent<Image>();
                img.material = null; // stereo-safe
                img.color = isActive 
                    ? (isDarkMode ? new Color(0.06f, 0.30f, 0.36f, 0.98f) : new Color(0.70f, 0.88f, 0.98f, 0.98f))
                    : (isDarkMode ? new Color(0.10f, 0.14f, 0.22f, 0.95f) : new Color(0.82f, 0.88f, 0.96f, 0.98f));

                Outline ol = btnObj.AddComponent<Outline>();
                ol.effectColor = isActive 
                    ? (isDarkMode ? new Color(0.0f, 1.0f, 0.85f, 0.85f) : new Color(0.10f, 0.50f, 0.85f, 0.85f))
                    : (isDarkMode ? new Color(1.0f, 1.0f, 1.0f, 0.18f) : new Color(0.20f, 0.30f, 0.45f, 0.25f));
                ol.effectDistance = isActive ? new Vector2(2f, -2f) : new Vector2(1f, -1f);

                Button btn = btnObj.AddComponent<Button>();
                ColorBlock cb = btn.colors;
                cb.highlightedColor = isActive ? new Color(0.10f, 0.42f, 0.50f, 1f) : new Color(0.18f, 0.26f, 0.38f, 1f);
                cb.pressedColor = new Color(0.04f, 0.08f, 0.16f, 1f);
                btn.colors = cb;

                var capturedOpt = opt;
                btn.onClick.AddListener(() => ApplySkybox(capturedOpt));
                allMenuButtons.Add(btn);

                string labelText;
                if (isDarkMode)
                {
                    labelText = isActive 
                        ? $"<b><color=#00FFAA>{opt.displayName}</color></b>\n<size=12><color=#80E0FF>[Active Environment]</color></size>" 
                        : $"<b>{opt.displayName}</b>\n<size=12><color=#90CAF9>{opt.category}</color></size>";
                }
                else
                {
                    labelText = isActive 
                        ? $"<b><color=#005544>{opt.displayName}</color></b>\n<size=12><color=#006699>[Active Environment]</color></size>" 
                        : $"<b>{opt.displayName}</b>\n<size=12><color=#335577>{opt.category}</color></size>";
                }

                Text txt = CreateText(btnObj.transform, labelText, 14, isDarkMode ? Color.white : new Color(0.08f, 0.12f, 0.22f), TextAnchor.MiddleCenter);
                txt.lineSpacing = 1.1f;

                activePageButtons.Add(btnObj);
            }

            if (bgPageIndicatorText != null)
            {
                bgPageIndicatorText.text = $"Page {currentBgPage + 1} of {totalPages}   |   {categoryHeader}";
            }

            UpdateActiveBgCard();
        }

        private void UpdateActiveBgCard()
        {
            if (activeBgStatusText != null)
            {
                activeBgStatusText.text = $"Active Skybox: <b><color=#00E5FF>{currentActiveSkyboxName}</color></b>   <size=13><color=#00FF88>[Applied to all VR Eyes]</color></size>";
            }
        }

        private IEnumerator StopHapticsAfter(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch);
        }

        public void ApplySkybox(SkyboxOption opt)
        {
            if (opt == null) return;
            currentActiveSkyboxName = opt.displayName;

            var cams = FindObjectsByType<Camera>(FindObjectsSortMode.None);
            if (opt.skyboxMaterial == null)
            {
                foreach (var c in cams)
                {
                    if (c != null)
                    {
                        c.clearFlags = CameraClearFlags.SolidColor;
                        c.backgroundColor = Color.black;
                    }
                }
                RenderSettings.skybox = null;
            }
            else
            {
                foreach (var c in cams)
                {
                    if (c != null)
                    {
                        c.clearFlags = CameraClearFlags.Skybox;
                    }
                }
                RenderSettings.skybox = opt.skyboxMaterial;
            }

            DynamicGI.UpdateEnvironment();

            PlayerPrefs.SetString("UR3eVR_SelectedSkybox", opt.displayName);
            PlayerPrefs.Save();

            AddLog("SYSTEM", $"Skybox Environment set to: {opt.displayName}");
            RefreshBgPageButtons();

            OVRInput.SetControllerVibration(0.5f, 0.4f, OVRInput.Controller.RTouch);
            StartCoroutine(StopHapticsAfter(0.06f));

            var glove = FindAnyObjectByType<BhapticsGloveFeedback>();
            if (glove != null)
            {
                glove.TriggerClickPulse(60, 60);
            }
        }

        // =========================================================================
        // UI Helpers
        // =========================================================================

        private void CreateHeader(Transform parent, string title, Action onClose, Action onBack = null)
        {
            GameObject header = new GameObject("Header");
            header.transform.SetParent(parent, false);

            RectTransform hRT = header.AddComponent<RectTransform>();
            hRT.anchorMin = new Vector2(0.02f, 0.86f);
            hRT.anchorMax = new Vector2(0.98f, 0.98f);
            hRT.offsetMin = Vector2.zero;
            hRT.offsetMax = Vector2.zero;

            Image hImg = header.AddComponent<Image>();
            if (roundedRectSprite != null)
            {
                hImg.sprite = roundedRectSprite;
                hImg.type = Image.Type.Sliced;
            }
            hImg.material = null; // Stereo-safe UI/Default shader
            hImg.color = isDarkMode ? new Color(0.10f, 0.14f, 0.22f, 0.95f) : new Color(0.84f, 0.88f, 0.95f, 0.98f);

            Outline hOl = header.AddComponent<Outline>();
            hOl.effectColor = isDarkMode ? new Color(0.0f, 0.75f, 1.0f, 0.30f) : new Color(0.15f, 0.35f, 0.65f, 0.40f);
            hOl.effectDistance = new Vector2(1f, -1f);

            if (onBack != null)
            {
                GameObject backObj = new GameObject("Btn_Back");
                backObj.transform.SetParent(header.transform, false);
                RectTransform bRT = backObj.AddComponent<RectTransform>();
                bRT.anchorMin = new Vector2(0.02f, 0.15f);
                bRT.anchorMax = new Vector2(0.16f, 0.85f);
                bRT.offsetMin = Vector2.zero;
                bRT.offsetMax = Vector2.zero;

                Image bImg = backObj.AddComponent<Image>();
                if (roundedRectSprite != null)
                {
                    bImg.sprite = roundedRectSprite;
                    bImg.type = Image.Type.Sliced;
                }
                bImg.material = null;
                bImg.color = isDarkMode ? new Color(0.18f, 0.24f, 0.34f, 0.95f) : new Color(0.74f, 0.80f, 0.90f, 0.98f);

                Button bBtn = backObj.AddComponent<Button>();
                bBtn.onClick.AddListener(() => onBack());
                allMenuButtons.Add(bBtn);

                CreateText(backObj.transform, "◀ Back", 14, isDarkMode ? Color.white : new Color(0.06f, 0.12f, 0.24f), TextAnchor.MiddleCenter);
            }

            GameObject titleObj = new GameObject("TitleText");
            titleObj.transform.SetParent(header.transform, false);
            RectTransform tRT = titleObj.AddComponent<RectTransform>();
            tRT.anchorMin = new Vector2(0.17f, 0f);
            tRT.anchorMax = new Vector2(0.75f, 1f);
            tRT.offsetMin = Vector2.zero;
            tRT.offsetMax = Vector2.zero;

            Text titleText = titleObj.AddComponent<Text>();
            titleText.material = null;
            titleText.font = uiFont;
            titleText.fontSize = 20;
            titleText.fontStyle = FontStyle.Bold;
            titleText.alignment = TextAnchor.MiddleCenter;
            titleText.color = isDarkMode ? new Color(0.92f, 0.96f, 1.0f, 1.0f) : new Color(0.06f, 0.10f, 0.22f, 1.0f);
            titleText.text = title;

            // Quick Theme Toggle Button (Visible on every Header!)
            GameObject themeObj = new GameObject("Btn_QuickTheme");
            themeObj.transform.SetParent(header.transform, false);
            RectTransform thRT = themeObj.AddComponent<RectTransform>();
            thRT.anchorMin = new Vector2(0.76f, 0.15f);
            thRT.anchorMax = new Vector2(0.87f, 0.85f);
            thRT.offsetMin = Vector2.zero;
            thRT.offsetMax = Vector2.zero;

            Image thImg = themeObj.AddComponent<Image>();
            if (roundedRectSprite != null)
            {
                thImg.sprite = roundedRectSprite;
                thImg.type = Image.Type.Sliced;
            }
            thImg.material = null;
            thImg.color = isDarkMode ? new Color(0.18f, 0.24f, 0.34f, 0.95f) : new Color(0.74f, 0.80f, 0.90f, 0.98f);

            Button thBtn = themeObj.AddComponent<Button>();
            thBtn.onClick.AddListener(ToggleDarkLightMode);
            allMenuButtons.Add(thBtn);

            Text thText = CreateText(themeObj.transform, isDarkMode ? "🌓 Dark" : "☀️ Light", 13, isDarkMode ? Color.white : new Color(0.06f, 0.12f, 0.24f), TextAnchor.MiddleCenter);
            thText.fontStyle = FontStyle.Bold;

            // Close button [X] at top-right (Item 5)
            GameObject closeObj = new GameObject("Btn_Close");
            closeObj.transform.SetParent(header.transform, false);
            RectTransform cRT = closeObj.AddComponent<RectTransform>();
            cRT.anchorMin = new Vector2(0.89f, 0.15f);
            cRT.anchorMax = new Vector2(0.98f, 0.85f);
            cRT.offsetMin = Vector2.zero;
            cRT.offsetMax = Vector2.zero;

            Image cImg = closeObj.AddComponent<Image>();
            if (roundedRectSprite != null)
            {
                cImg.sprite = roundedRectSprite;
                cImg.type = Image.Type.Sliced;
            }
            cImg.material = null;
            cImg.color = isDarkMode ? new Color(0.80f, 0.18f, 0.22f, 0.95f) : new Color(0.88f, 0.24f, 0.28f, 0.95f);

            Button cBtn = closeObj.AddComponent<Button>();
            cBtn.onClick.AddListener(() => onClose());
            allMenuButtons.Add(cBtn);

            if (crossIconSprite != null)
            {
                GameObject iconObj = new GameObject("CrossIcon");
                iconObj.transform.SetParent(closeObj.transform, false);
                RectTransform iRT = iconObj.AddComponent<RectTransform>();
                iRT.anchorMin = new Vector2(0.2f, 0.2f);
                iRT.anchorMax = new Vector2(0.8f, 0.8f);
                iRT.offsetMin = Vector2.zero;
                iRT.offsetMax = Vector2.zero;
                Image iImg = iconObj.AddComponent<Image>();
                iImg.sprite = crossIconSprite;
                iImg.color = Color.white;
            }
            else
            {
                Text xText = CreateText(closeObj.transform, "✕", 18, Color.white, TextAnchor.MiddleCenter);
                xText.fontStyle = FontStyle.Bold;
            }
        }

        // =========================================================================
        // Settings Panel (Item 4b)
        // =========================================================================

        private bool isDarkMode = true;
        private GameObject colCtrlObj;
        private GameObject colAppObj;
        private GameObject speedCardObj;
        private GameObject sliderCardObj;
        private GameObject grabBarObj;
        private GameObject resizeHandleObj;
        private GameObject themeToggleBtnObj;
        private Text themeToggleBtnText;
        private Image themeToggleBtnImg;
        private GameObject skyboxSelectBtnObj;
        private GameObject resetKeybindsBtnObj;
        private GameObject btnMainSetOrigin;
        private GameObject btnMainAddCam;
        private GameObject btnMainOpenLog;
        private GameObject btnMainSettings;
        private GameObject btnMainSpeed;

        private readonly Dictionary<VRAction, Text> keybindBtnTexts = new Dictionary<VRAction, Text>();
        private readonly Dictionary<VRAction, Image> keybindBtnImages = new Dictionary<VRAction, Image>();
        private readonly Dictionary<VRAction, Outline> keybindBtnOutlines = new Dictionary<VRAction, Outline>();

        private void BuildSettingsPanelContent(Transform p)
        {
            CreateHeader(p, "System Settings", () => ActionCloseMenu(), () => ActionBackToMain());

            // 2-Column Split: Left = Controls & Keybinds, Right = Appearance & Environment
            GameObject splitObj = new GameObject("SettingsSplit");
            splitObj.transform.SetParent(p, false);
            RectTransform sRT = splitObj.AddComponent<RectTransform>();
            sRT.anchorMin = new Vector2(0.04f, 0.17f);
            sRT.anchorMax = new Vector2(0.96f, 0.85f);
            sRT.offsetMin = Vector2.zero;
            sRT.offsetMax = Vector2.zero;

            var hlg = splitObj.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 16;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;

            // Column 1: Controls Card
            GameObject colCtrl = new GameObject("Card_Controls");
            colCtrl.transform.SetParent(splitObj.transform, false);
            colCtrlObj = colCtrl;
            Image ccImg = colCtrl.AddComponent<Image>();
            if (roundedRectSprite != null) { ccImg.sprite = roundedRectSprite; ccImg.type = Image.Type.Sliced; }
            ccImg.material = null;
            ccImg.color = new Color(0.06f, 0.10f, 0.16f, 0.95f);
            Outline ccOl = colCtrl.AddComponent<Outline>();
            ccOl.effectColor = new Color(0.0f, 0.80f, 1.0f, 0.35f);

            var vlgCtrl = colCtrl.AddComponent<VerticalLayoutGroup>();
            vlgCtrl.padding = new RectOffset(12, 12, 10, 10);
            vlgCtrl.spacing = 5;
            vlgCtrl.childForceExpandWidth = true;
            vlgCtrl.childForceExpandHeight = false;

            CreateText(colCtrl.transform, "🎮 <b>Touch Controller Keybinds</b>", 15, new Color(0.0f, 0.9f, 1.0f), TextAnchor.MiddleLeft);
            CreateText(colCtrl.transform, "<i>Click any key below to rebind, then press desired button:</i>", 11, new Color(0.75f, 0.85f, 0.95f), TextAnchor.MiddleLeft);

            keybindBtnTexts.Clear();
            keybindBtnImages.Clear();
            keybindBtnOutlines.Clear();

            CreateKeybindRow(colCtrl.transform, "Set Origin / Home:", VRAction.SetOrigin);
            CreateKeybindRow(colCtrl.transform, "Pause / Resume:", VRAction.PauseResume);
            CreateKeybindRow(colCtrl.transform, "Shake Test Tube:", VRAction.ShakeTestTube);
            CreateKeybindRow(colCtrl.transform, "Gripper Lock:", VRAction.ToggleGripperLock);
            CreateKeybindRow(colCtrl.transform, "Open / Close Menu:", VRAction.OpenMenu);

            resetKeybindsBtnObj = CreateSimpleButton(colCtrl.transform, "↺ Reset Default Keybinds", 28,
                isDarkMode ? new Color(0.16f, 0.22f, 0.30f, 0.95f) : new Color(0.82f, 0.86f, 0.94f, 0.98f),
                isDarkMode ? new Color(0.85f, 0.92f, 1.0f) : new Color(0.08f, 0.16f, 0.30f), () =>
            {
                VRKeybindManager.Instance?.ResetDefaults();
                RefreshKeybindUI();
                HapticPulse(0.4f, 0.08f);
            });

            CreateText(colCtrl.transform, "• <b>[Left Stick Y]</b> : Window Push/Pull (Analog)", 11, isDarkMode ? new Color(0.6f, 0.7f, 0.8f) : new Color(0.25f, 0.35f, 0.45f), TextAnchor.MiddleLeft);
            CreateText(colCtrl.transform, "• <b>[Right Trigger]</b> : Gripper Pinch 0-255 (Analog)", 11, isDarkMode ? new Color(0.6f, 0.7f, 0.8f) : new Color(0.25f, 0.35f, 0.45f), TextAnchor.MiddleLeft);

            // Column 2: Appearance Card
            GameObject colApp = new GameObject("Card_Appearance");
            colApp.transform.SetParent(splitObj.transform, false);
            colAppObj = colApp;
            Image caImg = colApp.AddComponent<Image>();
            if (roundedRectSprite != null) { caImg.sprite = roundedRectSprite; caImg.type = Image.Type.Sliced; }
            caImg.material = null;
            caImg.color = isDarkMode ? new Color(0.06f, 0.10f, 0.16f, 0.95f) : new Color(0.88f, 0.92f, 0.97f, 0.98f);
            Outline caOl = colApp.AddComponent<Outline>();
            caOl.effectColor = isDarkMode ? new Color(0.7f, 0.3f, 1.0f, 0.35f) : new Color(0.50f, 0.20f, 0.75f, 0.45f);

            var vlgApp = colApp.AddComponent<VerticalLayoutGroup>();
            vlgApp.padding = new RectOffset(16, 16, 14, 14);
            vlgApp.spacing = 14;
            vlgApp.childForceExpandWidth = true;
            vlgApp.childForceExpandHeight = false;

            CreateText(colApp.transform, "🎨 <b>Appearance & Environment</b>", 17, isDarkMode ? new Color(0.85f, 0.55f, 1.0f) : new Color(0.45f, 0.15f, 0.70f), TextAnchor.MiddleLeft);

            skyboxSelectBtnObj = CreateSimpleButton(colApp.transform, "🌌 Skybox Background Selector >", 48,
                isDarkMode ? new Color(0.18f, 0.16f, 0.32f, 0.95f) : new Color(0.84f, 0.82f, 0.94f, 0.98f),
                isDarkMode ? Color.white : new Color(0.25f, 0.15f, 0.45f), ActionBackground);

            themeToggleBtnObj = CreateSimpleButton(colApp.transform, isDarkMode ? "🌓 Mode: Dark (Switch to Light)" : "☀️ Mode: Light (Switch to Dark)", 48,
                isDarkMode ? new Color(0.14f, 0.22f, 0.28f, 0.95f) : new Color(0.78f, 0.84f, 0.94f, 0.98f),
                isDarkMode ? Color.white : new Color(0.08f, 0.14f, 0.26f), ToggleDarkLightMode);
            if (themeToggleBtnObj != null)
            {
                themeToggleBtnImg = themeToggleBtnObj.GetComponent<Image>();
                themeToggleBtnText = themeToggleBtnObj.GetComponentInChildren<Text>();
            }

            // Bottom Navigation Bar
            GameObject botBar = new GameObject("SettingsBottomBar");
            botBar.transform.SetParent(p, false);
            RectTransform bbRT = botBar.AddComponent<RectTransform>();
            bbRT.anchorMin = new Vector2(0.06f, 0.04f);
            bbRT.anchorMax = new Vector2(0.94f, 0.14f);
            bbRT.offsetMin = Vector2.zero;
            bbRT.offsetMax = Vector2.zero;

            CreateSimpleButton(botBar.transform, "◀ Back to Main Menu", 40,
                isDarkMode ? new Color(0.18f, 0.22f, 0.30f, 0.95f) : new Color(0.76f, 0.82f, 0.92f, 0.98f),
                isDarkMode ? Color.white : new Color(0.06f, 0.12f, 0.24f), ActionBackToMain);
        }

        private void CreateKeybindRow(Transform parent, string actionLabel, VRAction action)
        {
            GameObject row = new GameObject("Row_" + action);
            row.transform.SetParent(parent, false);
            var le = row.AddComponent<LayoutElement>();
            le.minHeight = 26;
            le.preferredHeight = 28;

            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 6;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;

            // Action Label
            GameObject labelObj = new GameObject("Label");
            labelObj.transform.SetParent(row.transform, false);
            var labelLE = labelObj.AddComponent<LayoutElement>();
            labelLE.minWidth = 130;
            labelLE.preferredWidth = 140;
            Text lText = labelObj.AddComponent<Text>();
            lText.font = uiFont;
            lText.fontSize = 12;
            lText.alignment = TextAnchor.MiddleLeft;
            lText.color = Color.white;
            lText.text = actionLabel;

            // Rebind Button
            GameObject btnObj = new GameObject("Btn_" + action);
            btnObj.transform.SetParent(row.transform, false);
            var btnLE = btnObj.AddComponent<LayoutElement>();
            btnLE.minWidth = 140;
            btnLE.preferredWidth = 150;

            Image btnImg = btnObj.AddComponent<Image>();
            if (roundedRectSprite != null) { btnImg.sprite = roundedRectSprite; btnImg.type = Image.Type.Sliced; }
            btnImg.color = new Color(0.12f, 0.20f, 0.32f, 0.95f);

            Outline btnOl = btnObj.AddComponent<Outline>();
            btnOl.effectColor = new Color(0.0f, 0.80f, 1.0f, 0.40f);

            Button btn = btnObj.AddComponent<Button>();
            var cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(0.2f, 0.8f, 1.0f, 1f);
            cb.pressedColor = new Color(0.0f, 0.5f, 0.8f, 1f);
            btn.colors = cb;

            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(btnObj.transform, false);
            RectTransform trt = textObj.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;
            Text bText = textObj.AddComponent<Text>();
            bText.font = uiFont;
            bText.fontSize = 11;
            bText.fontStyle = FontStyle.Bold;
            bText.alignment = TextAnchor.MiddleCenter;
            bText.color = Color.white;

            VRButtonId bound = VRKeybindManager.Instance != null ? VRKeybindManager.Instance.GetBoundButton(action) : VRKeybindManager.GetDefaultButton(action);
            bText.text = VRKeybindManager.GetButtonDisplayName(bound);

            keybindBtnTexts[action] = bText;
            keybindBtnImages[action] = btnImg;
            keybindBtnOutlines[action] = btnOl;

            btn.onClick.AddListener(() =>
            {
                var km = VRKeybindManager.Instance;
                if (km == null) return;

                if (km.isListening && km.listeningAction == action)
                {
                    km.StopListening();
                }
                else
                {
                    km.StartListening(action);
                    ShowToast($"🟡 Press button to bind [{actionLabel}]...");
                    HapticPulse(0.3f, 0.05f);
                }
                RefreshKeybindUI();
            });
        }

        private void RefreshKeybindUI()
        {
            var km = VRKeybindManager.Instance;
            if (km == null) return;

            foreach (VRAction action in Enum.GetValues(typeof(VRAction)))
            {
                if (!keybindBtnTexts.ContainsKey(action)) continue;

                Text txt = keybindBtnTexts[action];
                Image img = keybindBtnImages.ContainsKey(action) ? keybindBtnImages[action] : null;
                Outline ol = keybindBtnOutlines.ContainsKey(action) ? keybindBtnOutlines[action] : null;

                if (km.isListening && km.listeningAction == action)
                {
                    if (txt != null)
                    {
                        txt.text = "🟡 <b>Press Button...</b>";
                        txt.color = Color.yellow;
                    }
                    if (img != null) img.color = new Color(0.48f, 0.32f, 0.05f, 0.95f);
                    if (ol != null) ol.effectColor = new Color(1f, 0.85f, 0.1f, 0.9f);
                }
                else
                {
                    VRButtonId bound = km.GetBoundButton(action);
                    if (txt != null)
                    {
                        txt.text = VRKeybindManager.GetButtonDisplayName(bound);
                        txt.color = isDarkMode ? Color.white : new Color(0.08f, 0.12f, 0.20f);
                    }
                    if (img != null) img.color = isDarkMode ? new Color(0.12f, 0.20f, 0.32f, 0.95f) : new Color(0.72f, 0.78f, 0.88f, 0.98f);
                    if (ol != null) ol.effectColor = isDarkMode ? new Color(0.0f, 0.80f, 1.0f, 0.40f) : new Color(0.15f, 0.45f, 0.75f, 0.50f);
                }
            }
        }

        public void ToggleDarkLightMode()
        {
            isDarkMode = !isDarkMode;
            PlayerPrefs.SetInt("VR_UI_DARK_MODE", isDarkMode ? 1 : 0);
            PlayerPrefs.Save();
            ApplyTheme(isDarkMode);
            ShowToast(isDarkMode ? "🌙 Dark Theme Active" : "☀️ Light Theme Active");
            HapticPulse(0.3f, 0.08f);
        }

        public void ApplyTheme(bool dark)
        {
            isDarkMode = dark;
            GameObject[] panels = { panelMain, panelCamera, panelLog, panelBg, panelSettings, panelSpeed };
            foreach (var p in panels)
            {
                if (p == null) continue;
                var img = p.GetComponent<Image>();
                if (img != null)
                {
                    img.material = null; // Stereo-safe UI/Default shader
                    img.color = dark ? new Color(0.07f, 0.09f, 0.14f, 0.96f) : new Color(0.93f, 0.95f, 0.98f, 0.98f);
                }
                var ol = p.GetComponent<Outline>();
                if (ol != null)
                {
                    ol.effectColor = dark ? new Color(0.0f, 0.80f, 1.0f, 0.35f) : new Color(0.20f, 0.30f, 0.45f, 0.45f);
                }

                Transform hTr = p.transform.Find("Header");
                if (hTr != null)
                {
                    var hImg = hTr.GetComponent<Image>();
                    if (hImg != null)
                    {
                        hImg.material = null;
                        hImg.color = dark ? new Color(0.10f, 0.14f, 0.22f, 0.95f) : new Color(0.84f, 0.88f, 0.95f, 0.98f);
                    }
                    var hOl = hTr.GetComponent<Outline>();
                    if (hOl != null) hOl.effectColor = dark ? new Color(0.0f, 0.75f, 1.0f, 0.30f) : new Color(0.15f, 0.35f, 0.65f, 0.40f);

                    Transform tTr = hTr.Find("TitleText");
                    if (tTr != null)
                    {
                        var tTxt = tTr.GetComponent<Text>();
                        if (tTxt != null) tTxt.color = dark ? new Color(0.92f, 0.96f, 1.0f, 1.0f) : new Color(0.06f, 0.10f, 0.22f, 1.0f);
                    }

                    Transform bTr = hTr.Find("Btn_Back");
                    if (bTr != null)
                    {
                        var bImg = bTr.GetComponent<Image>();
                        if (bImg != null)
                        {
                            bImg.material = null;
                            bImg.color = dark ? new Color(0.18f, 0.24f, 0.34f, 0.95f) : new Color(0.74f, 0.80f, 0.90f, 0.98f);
                        }
                        var bTxt = bTr.GetComponentInChildren<Text>();
                        if (bTxt != null) bTxt.color = dark ? Color.white : new Color(0.06f, 0.12f, 0.24f);
                    }

                    Transform cTr = hTr.Find("Btn_Close");
                    if (cTr != null)
                    {
                        var cImg = cTr.GetComponent<Image>();
                        if (cImg != null)
                        {
                            cImg.material = null;
                            cImg.color = dark ? new Color(0.80f, 0.18f, 0.22f, 0.95f) : new Color(0.88f, 0.24f, 0.28f, 0.95f);
                        }
                    }

                    Transform qTr = hTr.Find("Btn_QuickTheme");
                    if (qTr != null)
                    {
                        var qImg = qTr.GetComponent<Image>();
                        if (qImg != null)
                        {
                            qImg.material = null;
                            qImg.color = dark ? new Color(0.18f, 0.24f, 0.34f, 0.95f) : new Color(0.74f, 0.80f, 0.90f, 0.98f);
                        }
                        var qTxt = qTr.GetComponentInChildren<Text>();
                        if (qTxt != null)
                        {
                            qTxt.text = dark ? "🌓 Dark" : "☀️ Light";
                            qTxt.color = dark ? Color.white : new Color(0.06f, 0.12f, 0.24f);
                        }
                    }
                }
            }

            // Update panelMain's 5 primary buttons
            UpdateMainButtonTheme(btnMainSetOrigin,
                dark ? new Color(0.0f, 0.38f, 0.55f, 0.95f) : new Color(0.80f, 0.90f, 0.98f, 0.98f),
                dark ? Color.white : new Color(0.04f, 0.24f, 0.44f));
            UpdateMainButtonTheme(btnMainAddCam,
                dark ? new Color(0.12f, 0.22f, 0.40f, 0.95f) : new Color(0.85f, 0.88f, 0.98f, 0.98f),
                dark ? Color.white : new Color(0.10f, 0.16f, 0.46f));
            UpdateMainButtonTheme(btnMainOpenLog,
                dark ? new Color(0.24f, 0.18f, 0.38f, 0.95f) : new Color(0.92f, 0.86f, 0.98f, 0.98f),
                dark ? Color.white : new Color(0.26f, 0.10f, 0.46f));
            UpdateMainButtonTheme(btnMainSettings,
                dark ? new Color(0.14f, 0.26f, 0.32f, 0.95f) : new Color(0.84f, 0.91f, 0.94f, 0.98f),
                dark ? Color.white : new Color(0.08f, 0.24f, 0.32f));
            UpdateMainButtonTheme(btnMainSpeed,
                dark ? new Color(0.10f, 0.34f, 0.28f, 0.95f) : new Color(0.82f, 0.94f, 0.90f, 0.98f),
                dark ? Color.white : new Color(0.06f, 0.30f, 0.22f));

            if (colCtrlObj != null)
            {
                var img = colCtrlObj.GetComponent<Image>();
                if (img != null)
                {
                    img.material = null;
                    img.color = dark ? new Color(0.06f, 0.10f, 0.16f, 0.95f) : new Color(0.88f, 0.92f, 0.97f, 0.98f);
                }
                var ol = colCtrlObj.GetComponent<Outline>();
                if (ol != null) ol.effectColor = dark ? new Color(0.0f, 0.80f, 1.0f, 0.35f) : new Color(0.15f, 0.40f, 0.70f, 0.45f);

                var texts = colCtrlObj.GetComponentsInChildren<Text>(true);
                foreach (var t in texts)
                {
                    if (t.transform.parent != null && t.transform.parent.name.StartsWith("Btn_")) continue;
                    if (t.transform.parent != null && t.transform.parent.name.StartsWith("Row_"))
                    {
                        t.color = dark ? Color.white : new Color(0.08f, 0.12f, 0.22f);
                        continue;
                    }
                    if (t.text.Contains("Touch Controller Keybinds"))
                        t.color = dark ? new Color(0.0f, 0.90f, 1.0f) : new Color(0.05f, 0.35f, 0.70f);
                    else
                        t.color = dark ? new Color(0.75f, 0.85f, 0.95f) : new Color(0.20f, 0.30f, 0.45f);
                }

                if (resetKeybindsBtnObj != null)
                {
                    var rImg = resetKeybindsBtnObj.GetComponent<Image>();
                    if (rImg != null)
                    {
                        rImg.material = null;
                        rImg.color = dark ? new Color(0.16f, 0.22f, 0.30f, 0.95f) : new Color(0.82f, 0.86f, 0.94f, 0.98f);
                    }
                    var rTxt = resetKeybindsBtnObj.GetComponentInChildren<Text>();
                    if (rTxt != null) rTxt.color = dark ? new Color(0.85f, 0.92f, 1.0f) : new Color(0.08f, 0.16f, 0.30f);
                }
            }

            if (colAppObj != null)
            {
                var img = colAppObj.GetComponent<Image>();
                if (img != null)
                {
                    img.material = null;
                    img.color = dark ? new Color(0.06f, 0.10f, 0.16f, 0.95f) : new Color(0.88f, 0.92f, 0.97f, 0.98f);
                }
                var ol = colAppObj.GetComponent<Outline>();
                if (ol != null) ol.effectColor = dark ? new Color(0.7f, 0.3f, 1.0f, 0.35f) : new Color(0.50f, 0.20f, 0.75f, 0.45f);

                var texts = colAppObj.GetComponentsInChildren<Text>(true);
                foreach (var t in texts)
                {
                    if (t.transform.parent != null && t.transform.parent.name.StartsWith("Btn_")) continue;
                    if (t.text.Contains("Appearance & Environment"))
                        t.color = dark ? new Color(0.85f, 0.55f, 1.0f) : new Color(0.45f, 0.15f, 0.70f);
                    else
                        t.color = dark ? new Color(0.75f, 0.85f, 0.95f) : new Color(0.20f, 0.30f, 0.45f);
                }

                if (skyboxSelectBtnObj != null)
                {
                    var sImg = skyboxSelectBtnObj.GetComponent<Image>();
                    if (sImg != null)
                    {
                        sImg.material = null;
                        sImg.color = dark ? new Color(0.18f, 0.16f, 0.32f, 0.95f) : new Color(0.84f, 0.82f, 0.94f, 0.98f);
                    }
                    var sTxt = skyboxSelectBtnObj.GetComponentInChildren<Text>();
                    if (sTxt != null) sTxt.color = dark ? Color.white : new Color(0.25f, 0.15f, 0.45f);
                }
            }

            if (themeToggleBtnText != null)
            {
                themeToggleBtnText.text = dark ? "🌓 Mode: Dark (Switch to Light)" : "☀️ Mode: Light (Switch to Dark)";
                themeToggleBtnText.color = dark ? Color.white : new Color(0.08f, 0.14f, 0.26f);
            }
            if (themeToggleBtnImg != null)
            {
                themeToggleBtnImg.material = null;
                themeToggleBtnImg.color = dark ? new Color(0.14f, 0.22f, 0.28f, 0.95f) : new Color(0.78f, 0.84f, 0.94f, 0.98f);
            }

            if (speedCardObj != null)
            {
                var img = speedCardObj.GetComponent<Image>();
                if (img != null)
                {
                    img.material = null;
                    img.color = dark ? new Color(0.06f, 0.10f, 0.16f, 0.95f) : new Color(0.88f, 0.92f, 0.97f, 0.98f);
                }
                var ol = speedCardObj.GetComponent<Outline>();
                if (ol != null) ol.effectColor = dark ? new Color(0.0f, 0.85f, 1.0f, 0.40f) : new Color(0.15f, 0.45f, 0.75f, 0.45f);
            }

            if (sliderCardObj != null)
            {
                Transform sBgTr = sliderCardObj.transform.Find("SliderBackground");
                if (sBgTr != null)
                {
                    var sBgImg = sBgTr.GetComponent<Image>();
                    if (sBgImg != null)
                    {
                        sBgImg.material = null;
                        sBgImg.color = dark ? new Color(0.12f, 0.16f, 0.22f, 0.95f) : new Color(0.78f, 0.82f, 0.90f, 0.98f);
                    }
                }
            }

            if (panelSpeed != null)
            {
                Transform prTr = panelSpeed.transform.Find("PresetRow");
                if (prTr != null)
                {
                    var pBtns = prTr.GetComponentsInChildren<Button>(true);
                    foreach (var b in pBtns)
                    {
                        var bImg = b.GetComponent<Image>();
                        if (bImg != null)
                        {
                            bImg.material = null;
                            bImg.color = dark ? new Color(0.14f, 0.20f, 0.28f, 0.95f) : new Color(0.78f, 0.84f, 0.94f, 0.98f);
                        }
                        var bTxt = b.GetComponentInChildren<Text>();
                        if (bTxt != null) bTxt.color = dark ? Color.white : new Color(0.06f, 0.14f, 0.26f);
                    }
                }
            }

            if (panelLog != null)
            {
                Transform lsTr = panelLog.transform.Find("LogScroll");
                if (lsTr != null)
                {
                    var scBg = lsTr.GetComponent<Image>();
                    if (scBg != null)
                    {
                        scBg.material = null;
                        scBg.color = dark ? new Color(0.03f, 0.04f, 0.06f, 0.95f) : new Color(0.92f, 0.94f, 0.98f, 0.98f);
                    }
                }
            }

            if (panelBg != null)
            {
                Transform aBgTr = panelBg.transform.Find("ActiveBgCard");
                if (aBgTr != null)
                {
                    var aBgImg = aBgTr.GetComponent<Image>();
                    if (aBgImg != null)
                    {
                        aBgImg.material = null;
                        aBgImg.color = dark ? new Color(0.06f, 0.12f, 0.20f, 0.95f) : new Color(0.88f, 0.92f, 0.97f, 0.98f);
                    }
                }
            }

            if (grabBarObj != null)
            {
                var img = grabBarObj.GetComponent<Image>();
                if (img != null)
                {
                    img.material = null;
                    img.color = dark ? new Color(0.18f, 0.22f, 0.32f, 0.90f) : new Color(0.78f, 0.84f, 0.92f, 0.95f);
                }
                var ol = grabBarObj.GetComponent<Outline>();
                if (ol != null) ol.effectColor = dark ? new Color(1f, 1f, 1f, 0.25f) : new Color(0.2f, 0.3f, 0.45f, 0.45f);
                var lbl = grabBarObj.GetComponentInChildren<Text>();
                if (lbl != null) lbl.color = dark ? new Color(0.8f, 0.9f, 1.0f, 0.85f) : new Color(0.10f, 0.15f, 0.25f, 0.90f);
            }

            if (resizeHandleObj != null)
            {
                var img = resizeHandleObj.GetComponent<Image>();
                if (img != null)
                {
                    img.material = null;
                    img.color = dark ? new Color(0.15f, 0.75f, 0.95f, 0.50f) : new Color(0.10f, 0.45f, 0.80f, 0.65f);
                }
            }

            if (statusText != null)
            {
                statusText.color = dark ? new Color(0.85f, 0.85f, 0.85f, 1f) : new Color(0.10f, 0.15f, 0.25f, 1f);
            }

            string[] bottomBarNames = { "SettingsBottomBar", "SpeedBottomBar", "LogBottomBar", "BottomBar" };
            foreach (var p in panels)
            {
                if (p == null) continue;
                foreach (var bName in bottomBarNames)
                {
                    Transform bbTr = p.transform.Find(bName);
                    if (bbTr != null)
                    {
                        var btns = bbTr.GetComponentsInChildren<Button>(true);
                        foreach (var b in btns)
                        {
                            var bImg = b.GetComponent<Image>();
                            if (bImg != null)
                            {
                                bImg.material = null;
                                bImg.color = dark ? new Color(0.18f, 0.22f, 0.30f, 0.95f) : new Color(0.76f, 0.82f, 0.92f, 0.98f);
                            }
                            var bTxt = b.GetComponentInChildren<Text>();
                            if (bTxt != null) bTxt.color = dark ? Color.white : new Color(0.06f, 0.12f, 0.24f);
                        }
                    }
                }
            }

            RefreshKeybindUI();
            UpdateSpeedVisuals(currentSpeedPercent);
            RefreshBgPageButtons();
            RefreshLogText();

            if (VRWristTelemetry.Instance != null) VRWristTelemetry.Instance.SetTheme(dark);
            if (VRCameraWindow.Instance != null) VRCameraWindow.Instance.SetTheme(dark);
        }

        private void UpdateMainButtonTheme(GameObject btnObj, Color bg, Color txtCol)
        {
            if (btnObj == null) return;
            var img = btnObj.GetComponent<Image>();
            if (img != null)
            {
                img.material = null;
                img.color = bg;
            }
            var txt = btnObj.GetComponentInChildren<Text>();
            if (txt != null) txt.color = txtCol;

            var btn = btnObj.GetComponent<Button>();
            if (btn != null)
            {
                ColorBlock cb = btn.colors;
                cb.normalColor = Color.white;
                cb.highlightedColor = new Color(1.2f, 1.2f, 1.2f, 1f);
                cb.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
                btn.colors = cb;
            }
        }

        // =========================================================================
        // Speed Panel (Item 4c)
        // =========================================================================

        public int currentSpeedPercent = 100;
        private Text speedDisplayLabel;
        private Slider speedSliderComponent;
        private Image speedSliderFillImg;

        private void BuildSpeedPanelContent(Transform p)
        {
            CreateHeader(p, "Robot Motion Speed", () => ActionCloseMenu(), () => ActionBackToMain());

            // Card 1: Current Speed Display Card
            GameObject cardObj = new GameObject("SpeedCard");
            cardObj.transform.SetParent(p, false);
            speedCardObj = cardObj;
            RectTransform cRT = cardObj.AddComponent<RectTransform>();
            cRT.anchorMin = new Vector2(0.08f, 0.65f);
            cRT.anchorMax = new Vector2(0.92f, 0.83f);
            cRT.offsetMin = Vector2.zero;
            cRT.offsetMax = Vector2.zero;

            Image cardImg = cardObj.AddComponent<Image>();
            if (roundedRectSprite != null)
            {
                cardImg.sprite = roundedRectSprite;
                cardImg.type = Image.Type.Sliced;
            }
            cardImg.material = null;
            cardImg.color = new Color(0.06f, 0.10f, 0.16f, 0.95f);

            Outline cardOl = cardObj.AddComponent<Outline>();
            cardOl.effectColor = new Color(0.0f, 0.85f, 1.0f, 0.40f);
            cardOl.effectDistance = new Vector2(1.5f, -1.5f);

            speedDisplayLabel = CreateText(cardObj.transform, "Robot Speed: 100% [MAX]", 24, Color.white, TextAnchor.MiddleCenter);
            speedDisplayLabel.fontStyle = FontStyle.Bold;

            // Card 2: Interactive Slider Container
            GameObject sliderCard = new GameObject("SliderContainer");
            sliderCard.transform.SetParent(p, false);
            sliderCardObj = sliderCard;
            RectTransform sRT = sliderCard.AddComponent<RectTransform>();
            sRT.anchorMin = new Vector2(0.08f, 0.44f);
            sRT.anchorMax = new Vector2(0.92f, 0.60f);
            sRT.offsetMin = Vector2.zero;
            sRT.offsetMax = Vector2.zero;

            // Slider Background Track
            GameObject sBg = new GameObject("SliderBackground");
            sBg.transform.SetParent(sliderCard.transform, false);
            RectTransform sbgRT = sBg.AddComponent<RectTransform>();
            sbgRT.anchorMin = new Vector2(0f, 0.35f);
            sbgRT.anchorMax = new Vector2(1f, 0.65f);
            sbgRT.offsetMin = Vector2.zero;
            sbgRT.offsetMax = Vector2.zero;
            Image sbgImg = sBg.AddComponent<Image>();
            if (roundedRectSprite != null) { sbgImg.sprite = roundedRectSprite; sbgImg.type = Image.Type.Sliced; }
            sbgImg.material = null;
            sbgImg.color = new Color(0.12f, 0.16f, 0.22f, 0.95f);

            // Slider Fill Area
            GameObject fillArea = new GameObject("FillArea");
            fillArea.transform.SetParent(sliderCard.transform, false);
            RectTransform faRT = fillArea.AddComponent<RectTransform>();
            faRT.anchorMin = new Vector2(0f, 0.35f);
            faRT.anchorMax = new Vector2(1f, 0.65f);
            faRT.offsetMin = new Vector2(6f, 0f);
            faRT.offsetMax = new Vector2(-6f, 0f);

            GameObject fillObj = new GameObject("Fill");
            fillObj.transform.SetParent(fillArea.transform, false);
            RectTransform fRT = fillObj.AddComponent<RectTransform>();
            fRT.sizeDelta = Vector2.zero;
            speedSliderFillImg = fillObj.AddComponent<Image>();
            if (roundedRectSprite != null) { speedSliderFillImg.sprite = roundedRectSprite; speedSliderFillImg.type = Image.Type.Sliced; }
            speedSliderFillImg.material = null;
            speedSliderFillImg.color = new Color(1.0f, 0.25f, 0.25f, 0.95f);

            // Slider Handle Area
            GameObject handleArea = new GameObject("HandleSlideArea");
            handleArea.transform.SetParent(sliderCard.transform, false);
            RectTransform haRT = handleArea.AddComponent<RectTransform>();
            haRT.anchorMin = Vector2.zero;
            haRT.anchorMax = Vector2.one;
            haRT.offsetMin = new Vector2(15f, 0f);
            haRT.offsetMax = new Vector2(-15f, 0f);

            GameObject handleObj = new GameObject("Handle");
            handleObj.transform.SetParent(handleArea.transform, false);
            RectTransform hRT = handleObj.AddComponent<RectTransform>();
            hRT.sizeDelta = new Vector2(34f, 44f);
            Image hImg = handleObj.AddComponent<Image>();
            if (roundedRectSprite != null) { hImg.sprite = roundedRectSprite; hImg.type = Image.Type.Sliced; }
            hImg.material = null;
            hImg.color = Color.white;

            speedSliderComponent = sliderCard.AddComponent<Slider>();
            speedSliderComponent.fillRect = fRT;
            speedSliderComponent.handleRect = hRT;
            speedSliderComponent.targetGraphic = hImg;
            speedSliderComponent.direction = Slider.Direction.LeftToRight;
            speedSliderComponent.minValue = 0f;
            speedSliderComponent.maxValue = 100f;
            speedSliderComponent.wholeNumbers = true;
            speedSliderComponent.value = currentSpeedPercent;
            speedSliderComponent.onValueChanged.AddListener((v) => SetSpeedPercent(Mathf.RoundToInt(v)));

            // Card 3: Quick Snap Presets Row (0%, 25%, 50%, 75%, 100%)
            GameObject presetRow = new GameObject("PresetRow");
            presetRow.transform.SetParent(p, false);
            RectTransform prRT = presetRow.AddComponent<RectTransform>();
            prRT.anchorMin = new Vector2(0.08f, 0.25f);
            prRT.anchorMax = new Vector2(0.92f, 0.38f);
            prRT.offsetMin = Vector2.zero;
            prRT.offsetMax = Vector2.zero;

            var hlg = presetRow.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 10;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;

            CreateSimpleButton(presetRow.transform, "0% Stop", 42, new Color(0.20f, 0.20f, 0.25f, 0.95f), Color.white, () => SetSpeedPercent(0));
            CreateSimpleButton(presetRow.transform, "25% Slow", 42, new Color(0.05f, 0.35f, 0.20f, 0.95f), Color.white, () => SetSpeedPercent(25));
            CreateSimpleButton(presetRow.transform, "50% Med", 42, new Color(0.10f, 0.40f, 0.35f, 0.95f), Color.white, () => SetSpeedPercent(50));
            CreateSimpleButton(presetRow.transform, "75% Fast ⚠️", 42, new Color(0.45f, 0.35f, 0.05f, 0.95f), Color.white, () => SetSpeedPercent(75));
            CreateSimpleButton(presetRow.transform, "100% Max 🔴", 42, new Color(0.55f, 0.15f, 0.18f, 0.95f), Color.white, () => SetSpeedPercent(100));

            // Bottom Back Button
            GameObject botBar = new GameObject("SpeedBottomBar");
            botBar.transform.SetParent(p, false);
            RectTransform bbRT = botBar.AddComponent<RectTransform>();
            bbRT.anchorMin = new Vector2(0.08f, 0.06f);
            bbRT.anchorMax = new Vector2(0.92f, 0.18f);
            bbRT.offsetMin = Vector2.zero;
            bbRT.offsetMax = Vector2.zero;

            CreateSimpleButton(botBar.transform, "◀ Back to Main Menu", 44, new Color(0.18f, 0.22f, 0.30f, 0.95f), Color.white, ActionBackToMain);

            UpdateSpeedVisuals(currentSpeedPercent);
        }

        public void SetSpeedPercent(int pct)
        {
            currentSpeedPercent = Mathf.Clamp(pct, 0, 100);
            PlayerPrefs.SetInt("UR3eVR_MotionSpeed", currentSpeedPercent);
            PlayerPrefs.Save();

            if (speedSliderComponent != null && Mathf.RoundToInt(speedSliderComponent.value) != currentSpeedPercent)
            {
                speedSliderComponent.value = currentSpeedPercent;
            }

            if (gripperBridge != null)
            {
                gripperBridge.SendSpeed(currentSpeedPercent);
            }

            UpdateSpeedVisuals(currentSpeedPercent);
            HapticPulse(0.35f, 0.06f);
        }

        private void UpdateSpeedVisuals(int pct)
        {
            Color trackCol;
            string statusSuffix;
            if (pct < 50)
            {
                trackCol = new Color(0.0f, 1.0f, 0.55f, 0.95f); // Neon Green (Safe)
                statusSuffix = "<color=#00FF88>[SAFE]</color>";
            }
            else if (pct < 75)
            {
                trackCol = new Color(1.0f, 0.80f, 0.0f, 0.95f);  // Amber Yellow (Caution ⚠️)
                statusSuffix = "<color=#FFC800>[CAUTION ⚠️]</color>";
            }
            else
            {
                trackCol = new Color(1.0f, 0.22f, 0.25f, 0.95f); // Vivid Red (Danger 🔴)
                statusSuffix = "<color=#FF3344>[MAX VELOCITY 🔴]</color>";
            }

            if (speedSliderFillImg != null) speedSliderFillImg.color = trackCol;
            if (speedDisplayLabel != null)
            {
                speedDisplayLabel.text = $"Robot Speed: <b><color=#{ColorUtility.ToHtmlStringRGB(trackCol)}>{pct}%</color></b>  {statusSuffix}";
            }
        }

        private void RefreshSpeedUI()
        {
            if (gripperBridge != null)
            {
                currentSpeedPercent = Mathf.Clamp(gripperBridge.currentSpeedPct, 0, 100);
            }
            else
            {
                currentSpeedPercent = Mathf.Clamp(PlayerPrefs.GetInt("UR3eVR_MotionSpeed", 100), 0, 100);
            }
            if (speedSliderComponent != null) speedSliderComponent.value = currentSpeedPercent;
            UpdateSpeedVisuals(currentSpeedPercent);
        }

        private GameObject CreateSimpleButton(Transform parent, string label, float height, Color bgCol, Color textCol, Action onClick)
        {
            GameObject btnObj = new GameObject("Btn_" + label);
            btnObj.transform.SetParent(parent, false);

            RectTransform rt = btnObj.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0f, height);
            var le = btnObj.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;
            le.flexibleWidth = 1f;

            Image img = btnObj.AddComponent<Image>();
            if (roundedRectSprite != null)
            {
                img.sprite = roundedRectSprite;
                img.type = Image.Type.Sliced;
            }
            img.material = null; // Stereo-safe UI/Default shader (no additive wash)
            img.color = bgCol;

            Button btn = btnObj.AddComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.2f, 1.2f, 1.2f, 1f);
            cb.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
            btn.colors = cb;
            btn.onClick.AddListener(() => onClick());
            allMenuButtons.Add(btn);

            Outline ol = btnObj.AddComponent<Outline>();
            ol.effectColor = new Color(1f, 1f, 1f, 0.15f);
            ol.effectDistance = new Vector2(1f, -1f);

            Text txt = CreateText(btnObj.transform, label, 16, textCol, TextAnchor.MiddleCenter);
            txt.fontStyle = FontStyle.Bold;

            return btnObj;
        }

        private Text CreateText(Transform parent, string content, int fontSize, Color col, TextAnchor anchor)
        {
            GameObject txtObj = new GameObject("Text");
            txtObj.transform.SetParent(parent, false);

            RectTransform rt = txtObj.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            Text txt = txtObj.AddComponent<Text>();
            txt.material = null; // UI/Default renders in both eyes
            txt.font = uiFont;
            txt.fontSize = fontSize;
            txt.alignment = anchor;
            txt.color = col;
            txt.text = content;
            txt.raycastTarget = false;
            return txt;
        }

        private Text CreateTextItem(Transform parent, string content, int fontSize, Color col)
        {
            GameObject txtObj = new GameObject("TextItem");
            txtObj.transform.SetParent(parent, false);

            RectTransform rt = txtObj.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0f, 60f);

            Text txt = txtObj.AddComponent<Text>();
            txt.material = null;
            txt.font = uiFont;
            txt.fontSize = fontSize;
            txt.alignment = TextAnchor.MiddleLeft;
            txt.color = col;
            txt.text = content;
            txt.raycastTarget = false;
            return txt;
        }
    }
}
