using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Advanced Webcam controller for Unity VR (URP compatible, Single Pass Instanced VR support,
/// multi-camera selection, auto-flip, aspect ratio correction, and New Input System support).
/// </summary>
public class script : MonoBehaviour
{
    [Header("Material & Target")]
    [Tooltip("Target material where webcam texture will be assigned (optional, defaults to MeshRenderer's material)")]
    [SerializeField] private Material WCmat;

    [Header("Camera Selection")]
    [Tooltip("Preferred camera name filter (e.g. 'OKER', 'USB', 'iVCam', 'Cam'). Case-insensitive search.")]
    [SerializeField] private string WCname = "OKER";

    [Tooltip("Specific camera index to open (used if useDeviceIndex is true)")]
    [SerializeField] private int deviceIndex = 0;

    [Tooltip("If true, selects camera by deviceIndex instead of WCname filter")]
    [SerializeField] private bool useDeviceIndex = false;

    [Header("Resolution & FPS")]
    [SerializeField] private int requestedWidth = 1920;
    [SerializeField] private int requestedHeight = 1080;
    [SerializeField] private int requestedFPS = 30;

    [Header("VR & Display Corrections")]
    [Tooltip("Force Two-Sided and GPU Instancing for VR Single Pass Instanced (stereo in both eyes)")]
    [SerializeField] private bool optimizeForVR = true;

    [Tooltip("Use URP Unlit shader for consistent brightness in both eyes (avoids PBR specular discrepancies in VR)")]
    [SerializeField] private bool useUnlitShader = true;

    [Tooltip("Automatically correct vertically mirrored webcam feeds on Quad mesh")]
    [SerializeField] private bool autoFlipUV = true;

    [Tooltip("Automatically adjust Quad local scale to preserve camera aspect ratio (standard 16:9)")]
    [SerializeField] private bool autoAdjustAspectRatio = true;

    [Tooltip("Target width of Quad screen in meters (default 1.6m)")]
    [SerializeField] private float targetScreenWidth = 1.6f;

    [Tooltip("Allow pressing 'C' (Next) or 'V' (Prev) key to cycle cameras")]
    [SerializeField] private bool allowKeyCycle = true;

    // Public Telemetry & Status for HUD / UI
    public bool IsStreaming => _webcamTexture != null && _webcamTexture.isPlaying;
    public int CurrentDeviceIndex => _currentDeviceIndex;
    public int DeviceCount => _cachedDevices != null ? _cachedDevices.Length : 0;
    public string CurrentDeviceName => (_cachedDevices != null && _currentDeviceIndex >= 0 && _currentDeviceIndex < _cachedDevices.Length)
        ? _cachedDevices[_currentDeviceIndex].name : "None";
    public string CurrentResolution => (_webcamTexture != null && _webcamTexture.width > 16)
        ? $"{_webcamTexture.width}x{_webcamTexture.height} @ {_webcamTexture.requestedFPS}fps" : "Initializing...";
    public string StatusDescription { get; private set; } = "Not Started";

    // Runtime state
    private WebCamTexture _webcamTexture;
    private Material _targetMaterial;
    private Renderer _meshRenderer;
    private RawImage _rawImage;
    private WebCamDevice[] _cachedDevices;
    private bool _isInitialized = false;
    private int _currentDeviceIndex = -1;
    private float _retryTimer = 0f;

    void Start()
    {
        InitializeMaterialTarget();
        StartWebcam();
    }

    private void InitializeMaterialTarget()
    {
        _meshRenderer = GetComponent<Renderer>();

        // 1. If MeshRenderer is present, prioritize instanced material on this renderer
        if (_meshRenderer != null)
        {
            _targetMaterial = _meshRenderer.material;
        }
        else if (WCmat != null)
        {
            _targetMaterial = WCmat;
        }

        // 2. VR & Stereo Optimization: Ensure both eyes render properly
        if (_targetMaterial != null && optimizeForVR)
        {
            // Switch to URP Unlit shader if requested to prevent eye-lighting mismatch
            if (useUnlitShader)
            {
                Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
                if (unlit != null)
                {
                    _targetMaterial.shader = unlit;
                }
            }

            // CRITICAL FOR VR STEREO (BOTH EYES): Enable GPU Instancing & Disable Backface Culling
            _targetMaterial.enableInstancing = true;
            if (_targetMaterial.HasProperty("_Cull"))
            {
                _targetMaterial.SetFloat("_Cull", 0f); // 0 = Cull Off (Two-sided)
            }
        }

        // 3. UI RawImage if attached to a Canvas
        _rawImage = GetComponent<RawImage>();
    }

    /// <summary>
    /// Finds available cameras and starts the most appropriate stream.
    /// </summary>
    public void StartWebcam()
    {
        _cachedDevices = WebCamTexture.devices;
        if (_cachedDevices == null || _cachedDevices.Length == 0)
        {
            StatusDescription = "No Webcams Found";
            Debug.LogError("❌ [Webcam] ไม่พบอุปกรณ์ Webcam เชื่อมต่อกับคอมพิวเตอร์!");
            return;
        }

        Debug.Log($"📹 [Webcam] พบกล้องทั้งหมด {_cachedDevices.Length} ตัว:");
        for (int i = 0; i < _cachedDevices.Length; i++)
        {
            Debug.Log($"   [{i}] {_cachedDevices[i].name} (FrontFacing: {_cachedDevices[i].isFrontFacing})");
        }

        // Determine target device index
        int selectedIndex = -1;

        if (useDeviceIndex && deviceIndex >= 0 && deviceIndex < _cachedDevices.Length)
        {
            selectedIndex = deviceIndex;
        }
        else if (!string.IsNullOrEmpty(WCname))
        {
            // Substring search (case-insensitive)
            for (int i = 0; i < _cachedDevices.Length; i++)
            {
                if (_cachedDevices[i].name.IndexOf(WCname, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    selectedIndex = i;
                    break;
                }
            }
        }

        // Fallback: If not found, try to avoid virtual Quest cameras and pick first real webcam
        if (selectedIndex < 0)
        {
            for (int i = 0; i < _cachedDevices.Length; i++)
            {
                string n = _cachedDevices[i].name.ToLower();
                if (!n.Contains("quest") && !n.Contains("virtual"))
                {
                    selectedIndex = i;
                    break;
                }
            }
            if (selectedIndex < 0) selectedIndex = 0;
        }

        OpenDeviceByIndex(selectedIndex);
    }

    public void OpenDeviceByIndex(int index)
    {
        if (_cachedDevices == null || _cachedDevices.Length == 0)
            _cachedDevices = WebCamTexture.devices;

        if (_cachedDevices == null || _cachedDevices.Length == 0 || index < 0 || index >= _cachedDevices.Length)
            return;

        // Stop existing camera if running
        StopWebcam();

        _currentDeviceIndex = index;
        string deviceName = _cachedDevices[index].name;
        StatusDescription = $"Starting '{deviceName}'...";
        Debug.Log($"✅ [Webcam] กำลังเปิดกล้อง [{index}]: '{deviceName}' ({requestedWidth}x{requestedHeight} @ {requestedFPS}fps)...");

        try
        {
            // Create WebCamTexture with requested parameters
            _webcamTexture = new WebCamTexture(deviceName, requestedWidth, requestedHeight, requestedFPS);

            ApplyTextureToTarget();

            _webcamTexture.Play();
            _isInitialized = false;
            _retryTimer = 0f;
            StatusDescription = $"Streaming '{deviceName}'";
            Debug.Log($"🎬 [Webcam] กล้อง '{deviceName}' เริ่มสตรีมแล้ว!");
        }
        catch (Exception ex)
        {
            StatusDescription = $"Error: {ex.Message}";
            Debug.LogError($"❌ [Webcam] เกิดข้อผิดพลาดในการเปิดกล้อง '{deviceName}': {ex.Message}");
        }
    }

    private void ApplyTextureToTarget()
    {
        if (_webcamTexture == null) return;

        // Assign to material: Support mainTexture, _BaseMap (URP), and _MainTex (Legacy/Unlit)
        if (_targetMaterial != null)
        {
            _targetMaterial.mainTexture = _webcamTexture;

            if (_targetMaterial.HasProperty("_BaseMap"))
            {
                _targetMaterial.SetTexture("_BaseMap", _webcamTexture);
            }
            if (_targetMaterial.HasProperty("_MainTex"))
            {
                _targetMaterial.SetTexture("_MainTex", _webcamTexture);
            }

            // CRITICAL FOR VR STEREO: Ensure GPU Instancing is active
            _targetMaterial.enableInstancing = true;
        }

        // Assign to UI RawImage if present
        if (_rawImage != null)
        {
            _rawImage.texture = _webcamTexture;
        }
    }

    void Update()
    {
        // 1. Keyboard shortcuts: Next Camera (C) / Prev Camera (V)
        if (allowKeyCycle)
        {
            CheckKeyboardInput();
        }

        // 2. Wait until webcam texture initializes with valid dimensions to correct UV and Aspect Ratio
        if (_webcamTexture != null && _webcamTexture.isPlaying && !_isInitialized)
        {
            _retryTimer += Time.deltaTime;

            if (_webcamTexture.width > 16 && _webcamTexture.height > 16)
            {
                ApplyCorrections();
                _isInitialized = true;
                StatusDescription = $"Active: {_webcamTexture.width}x{_webcamTexture.height}";
            }
            else if (_retryTimer > 3.0f)
            {
                // Fallback: If 1080p failed to produce frames after 3s, retry with standard 1280x720 or native default
                Debug.LogWarning($"⚠️ [Webcam] กล้องตอบสนองช้าที่ {requestedWidth}x{requestedHeight} -> ลองเปิดด้วยโหมด Auto-Resolution...");
                _retryTimer = 0f;
                _isInitialized = true; // prevent infinite loops
                string devName = _cachedDevices[_currentDeviceIndex].name;
                StopWebcam();
                _webcamTexture = new WebCamTexture(devName, 1280, 720, 30);
                ApplyTextureToTarget();
                _webcamTexture.Play();
            }
        }
    }

    private void CheckKeyboardInput()
    {
        bool nextPressed = false;
        bool prevPressed = false;

        // Method A: New Input System (preferred in Unity 2022/2023+ with InputSystem package)
#if ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null)
        {
            if (kb.cKey.wasPressedThisFrame) nextPressed = true;
            if (kb.vKey.wasPressedThisFrame) prevPressed = true;
        }
#endif

        // Method B: Legacy Input fallback (wrapped in try/catch to never throw InvalidOperationException)
        if (!nextPressed && !prevPressed)
        {
            try
            {
                if (Input.GetKeyDown(KeyCode.C)) nextPressed = true;
                if (Input.GetKeyDown(KeyCode.V)) prevPressed = true;
            }
            catch
            {
                // Legacy input disabled in Player Settings; handled safely
            }
        }

        if (nextPressed) NextCamera();
        if (prevPressed) PrevCamera();
    }

    private void ApplyCorrections()
    {
        if (_webcamTexture == null) return;

        // 1. Correct vertical flip on Quad mesh (Both mainTextureScale and _BaseMap / _MainTex)
        if (autoFlipUV && _targetMaterial != null)
        {
            float scaleY = _webcamTexture.videoVerticallyMirrored ? -1.0f : 1.0f;
            float offsetY = _webcamTexture.videoVerticallyMirrored ? 1.0f : 0.0f;

            Vector2 scaleVec = new Vector2(1.0f, scaleY);
            Vector2 offsetVec = new Vector2(0.0f, offsetY);

            _targetMaterial.mainTextureScale = scaleVec;
            _targetMaterial.mainTextureOffset = offsetVec;

            if (_targetMaterial.HasProperty("_BaseMap"))
            {
                _targetMaterial.SetTextureScale("_BaseMap", scaleVec);
                _targetMaterial.SetTextureOffset("_BaseMap", offsetVec);
            }
            if (_targetMaterial.HasProperty("_MainTex"))
            {
                _targetMaterial.SetTextureScale("_MainTex", scaleVec);
                _targetMaterial.SetTextureOffset("_MainTex", offsetVec);
            }
        }

        // 2. Adjust Quad Scale to reasonable VR monitor proportions (e.g. 1.6m x 0.9m)
        if (autoAdjustAspectRatio && _webcamTexture.height > 0)
        {
            float aspect = (float)_webcamTexture.width / _webcamTexture.height;
            float height = targetScreenWidth / aspect;

            // Preserve current Z scale
            transform.localScale = new Vector3(targetScreenWidth, height, transform.localScale.z);
        }

        Debug.Log($"🖥️ [Webcam] สตรีมพร้อมใช้งาน! ความละเอียด: {_webcamTexture.width}x{_webcamTexture.height} (Mirrored: {_webcamTexture.videoVerticallyMirrored}, Angle: {_webcamTexture.videoRotationAngle}°)");
    }

    /// <summary>
    /// Switches to the next available camera.
    /// </summary>
    public void NextCamera()
    {
        if (_cachedDevices == null || _cachedDevices.Length <= 1) return;
        int nextIndex = (_currentDeviceIndex + 1) % _cachedDevices.Length;
        OpenDeviceByIndex(nextIndex);
    }

    /// <summary>
    /// Switches to the previous available camera.
    /// </summary>
    public void PrevCamera()
    {
        if (_cachedDevices == null || _cachedDevices.Length <= 1) return;
        int prevIndex = (_currentDeviceIndex - 1 + _cachedDevices.Length) % _cachedDevices.Length;
        OpenDeviceByIndex(prevIndex);
    }

    /// <summary>
    /// Stops the webcam and frees hardware resources.
    /// </summary>
    public void StopWebcam()
    {
        if (_webcamTexture != null)
        {
            if (_webcamTexture.isPlaying)
            {
                _webcamTexture.Stop();
            }
            Destroy(_webcamTexture);
            _webcamTexture = null;
        }
    }

    void OnEnable()
    {
        if (_webcamTexture != null && !_webcamTexture.isPlaying)
        {
            _webcamTexture.Play();
        }
    }

    void OnDisable()
    {
        if (_webcamTexture != null && _webcamTexture.isPlaying)
        {
            _webcamTexture.Stop();
        }
    }

    void OnDestroy()
    {
        StopWebcam();
    }

    void OnApplicationPause(bool pauseStatus)
    {
        if (_webcamTexture != null)
        {
            if (pauseStatus && _webcamTexture.isPlaying)
            {
                _webcamTexture.Pause();
            }
            else if (!pauseStatus && !_webcamTexture.isPlaying)
            {
                _webcamTexture.Play();
            }
        }
    }
}


/// <summary>
/// Alias class for WebcamFeed to maintain semantic naming across scripts.
/// </summary>
public class WebcamFeed : script { }
