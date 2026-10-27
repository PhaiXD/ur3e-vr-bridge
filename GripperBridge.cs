using System.Collections.Generic;
using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace UR3eVR
{
    /// <summary>
    /// GripperBridge: à¸ªà¹ˆà¸‡à¸„à¸³à¸ªà¸±à¹ˆà¸‡ GRIP à¹à¸¥à¸° MOVE (6-DOF) à¹„à¸›à¸¢à¸±à¸‡ Python vr_bridge_server.py à¸œà¹ˆà¸²à¸™ TCP
    /// </summary>
        [System.Serializable]
    public struct SystemAlert
    {
        public string Level;
        public string Message;
        public DateTime Timestamp;

        public SystemAlert(string level, string message)
        {
            Level = level;
            Message = message;
            Timestamp = DateTime.Now;
        }
    }

    public class GripperBridge : MonoBehaviour
    {
                public static event Action<string, string> OnAlertReceived;
        public static event Action<string> OnLogReceived;
        [System.NonSerialized] public List<SystemAlert> recentAlerts = new List<SystemAlert>();
        private readonly Queue<SystemAlert> pendingAlertsQueue = new Queue<SystemAlert>();
        private readonly object alertLock = new object();
        [Header("VRHandMover Reference (Auto-detected)")]
        public VRHandMover vrHandMover;

        [Header("Network Settings")]
        [Tooltip("IP Address of machine running test_move.py or vr_bridge_server.py")]
        public string serverIp = "127.0.0.1";

        [Tooltip("Port of VR Bridge Server (default: 5555)")]
        public int serverPort = 5555;

        [Tooltip("Max send rate (Hz) to minimize latency")]
        [Range(5, 30)]
        public int sendRateHz = 50;

        [Header("Significant Change Thresholds")]
        [Tooltip("Minimum significant translation change (meters) to avoid redundant sends")]
        [Range(0.0005f, 0.01f)]
        public float posChangeThreshold = 0.0006f; // 1.5 mm

        [Tooltip("Minimum significant wrist rotation change (degrees)")]
        [Range(0.2f, 3.0f)]
        public float rotChangeThreshold = 0.35f; // 0.8 degrees

        [Tooltip("Heartbeat send interval when stationary (seconds)")]
        [Range(0.5f, 3.0f)]
        public float heartbeatIntervalSec = 0.12f;

        [Header("Reference")]
        [Tooltip("PinchDetector component (auto-detected if null)")]
        public PinchDetector pinchDetector;

        [Header("Gripper Settings")]
        [Tooltip("Deadband to suppress noisy grip values (default: 5)")]
        [Range(1, 15)]
        public int deadband = 5;

        [Tooltip("Hold duration (sec) before Precision Mode (reduces deadband to 2)")]
        public float precisionHoldThreshold = 1.0f;

        private float gripperHoldTime = 0f;
        private int lastObservedPos = -1;
        private float lastGripSendTime = 0f;
        private const float GRIP_MIN_SEND_INTERVAL = 0.10f; // 10Hz max rate limit

        [Header("Live Status (Read Only)")]
        public bool isConnected = false;
        public int lastSentPos = -1;
        public int actualGripperPos = 0;
        public float actualGripForce = 0.0f;
        public string lastStatus = "Disconnected";
        public string lastReceivedMessage = "";
        public int currentSpeedPct = 100;

        [Header("Gripper Lock & Shake State")]
        [Tooltip("Whether gripper position is locked")]
        public bool isGripperLocked = false;
        public int lockedGripPos = 0;
        public volatile bool isShaking = false;
        private volatile int pendingShakeLockAction = 0; // 0=none, 1=lock, 2=unlock

        [Header("Left Controller Grip Lock Interaction (Middle Finger Hold)")]
        [Tooltip("Hold duration to toggle grip lock (seconds)")]
        public float gripLockHoldDuration = 0.85f;
        public float gripHoldTimer = 0f;
        public bool isGripLockArmed = true;
        private float gripSuccessTimer = 0f;
        private GameObject gripCircleRoot;
        private LineRenderer gripBgCircle;
        private LineRenderer gripFillCircle;
        private TextMesh gripTextMesh;
        private Transform leftAnchorCache;
        private Transform headTransformCache;
        private const int CIRCLE_SEGMENTS = 40;
        private const float CIRCLE_RADIUS = 0.045f;

        [Header("Robot Pose Feedback (Read Only)")]
        public Vector3 actualRobotPos = Vector3.zero;
        public Vector3 actualRobotRot = Vector3.zero; // Axis-Angle (rad)
        public bool hasReceivedRobotPose = false;

        [Header("Safety Proximity Ratios (0.0=safe, 1.0=at boundary)")]
        [Tooltip("Outer sphere proximity (0=center, 1=at max reach boundary)")]
        [Range(0f, 1f)] public float safetyOuterRatio = 0f;
        [Tooltip("Inner sphere proximity (0=far, 1=at self-collision boundary)")]
        [Range(0f, 1f)] public float safetyInnerRatio = 0f;
        [Tooltip("Cylinder proximity (0=far from Z-axis, 1=at singularity zone)")]
        [Range(0f, 1f)] public float safetyCylinderRatio = 0f;
        [Tooltip("Speed proximity (0=slow, 1=at max joint speed)")]
        [Range(0f, 1f)] public float safetySpeedRatio = 0f;

        [Header("Joint Rotation Limit Proximity (Near Full Turn / Max Limit)")]
        [Tooltip("Joint rotation limit proximity (0=safe, 1=at rotation limit)")]
        [Range(0f, 1f)] public float safetyRotLimitRatio = 0f;
        public int safetyRotLimitJointIdx = 5;
        public float safetyRotLimitDegLeft = 999f;

        public static readonly string[] JointNames = new string[] {
            "Base (J1)", "Shoulder (J2)", "Elbow (J3)", "Wrist 1 (J4)", "Wrist 2 (J5)", "Wrist 3 (J6)"
        };

        public string safetyRotLimitJointName
        {
            get
            {
                if (safetyRotLimitJointIdx >= 0 && safetyRotLimitJointIdx < JointNames.Length)
                    return JointNames[safetyRotLimitJointIdx];
                return "Wrist 3 (J6)";
            }
        }

        // Robot Arm Target Pose (6-DOF: Absolute / Anchor-based)
        private float targetPx = 0f;
        private float targetPy = 0f;
        private float targetPz = 0f;
        private float targetPrx = 0f;
        private float targetPry = 0f;
        private float targetPrz = 0f;
        private bool newPoseAvailable = false;
        private Vector3 lastSentPosePos = new Vector3(-999f, -999f, -999f);
        private Vector3 lastSentPoseRot = new Vector3(-999f, -999f, -999f);

        public void ResetPoseCache()
        {
            lastSentPosePos = new Vector3(-999f, -999f, -999f);
            lastSentPoseRot = new Vector3(-999f, -999f, -999f);
        }

        [Header("Auto-Tare (Calibrate Fz when opening fingers)")]
        [Tooltip("Gripper open threshold for auto-tare Fz (default: 35)")]
        [Range(10, 80)]
        public int autoTareThreshold = 35;
        public bool isCalibratedAtOpen = false;
        private bool rearmedForTare = false;
        private bool tareRequested = false;
        private bool homeRequested = false;
        private bool shakeRequested = false;
        private bool pauseRequested = false;
        private bool pauseTargetState = false;
        private bool speedRequested = false;
        private int pendingSpeedPct = 100;

        // Internal
        private Thread networkThread;
        private bool isRunning = false;
        private readonly object lockObj = new object();
        private int targetGripPos = 0;
        private bool newPosAvailable = false;

        // Robot Arm Velocity (6-DOF: Linear + Angular)
        private float targetVx = 0f;
        private float targetVy = 0f;
        private float targetVz = 0f;
        private float targetWx = 0f;
        private float targetWy = 0f;
        private float targetWz = 0f;
        private bool newVelAvailable = false;

        private void Awake()
        {
            currentSpeedPct = Mathf.Clamp(PlayerPrefs.GetInt("UR3eVR_MotionSpeed", 100), 0, 100);
            pendingSpeedPct = currentSpeedPct;
            if (pinchDetector == null)
            {
                pinchDetector = GetComponent<PinchDetector>();
                if (pinchDetector == null) pinchDetector = FindAnyObjectByType<PinchDetector>();
            }
        }

        private void OnEnable()
        {
            StartNetworkThread();
        }

        private void OnDisable()
        {
            StopNetworkThread();
            try { OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.LTouch); } catch { }
            HideGripCircle();
        }

        private void OnDestroy()
        {
            StopNetworkThread();
            try { OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.LTouch); } catch { }
            if (gripCircleRoot != null) Destroy(gripCircleRoot);
        }

        public bool isUsingController
        {
            get
            {
                if (vrHandMover == null)
                    vrHandMover = VRHandMover.Instance != null ? VRHandMover.Instance : FindAnyObjectByType<VRHandMover>();

                if (vrHandMover != null)
                    return vrHandMover.isUsingController;

                return VRHandStereoFixer.IsRightControllerInHand;
            }
        }

        private void Update()
        {
            // 1. Dispatch pending alerts received from network thread to main thread
            lock (alertLock)
            {
                while (pendingAlertsQueue.Count > 0)
                {
                    var alert = pendingAlertsQueue.Dequeue();
                    recentAlerts.Add(alert);
                    if (recentAlerts.Count > 100) recentAlerts.RemoveAt(0);
                    OnAlertReceived?.Invoke(alert.Level, alert.Message);
                }
            }

            // 2. Read Gripper input: Touch Controller Index Trigger or Bare Hand Pinch
            // Determine whether user is holding Touch Controller or using Bare Hand Tracking
            bool usingController = isUsingController;

            if (pendingShakeLockAction == 1)
            {
                pendingShakeLockAction = 0;
                if (!isGripperLocked) ToggleGripperLock();
            }
            else if (pendingShakeLockAction == 2)
            {
                pendingShakeLockAction = 0;
                if (isGripperLocked) ToggleGripperLock();
            }

            if (usingController)
            {
                float triggerVal = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, OVRInput.Controller.RTouch);
                int rawCurrentPos = Mathf.RoundToInt(triggerVal * 255f);

                // Temporal hold filter: If held steady for > precisionHoldThreshold, drop deadband to 2
                if (Mathf.Abs(rawCurrentPos - lastObservedPos) <= 2)
                {
                    gripperHoldTime += Time.deltaTime;
                }
                else
                {
                    gripperHoldTime = 0f;
                    lastObservedPos = rawCurrentPos;
                }
                int activeDeadband = (gripperHoldTime >= precisionHoldThreshold) ? 2 : deadband;

                if (!isGripperLocked)
                {
                    bool isExtreme = (rawCurrentPos == 0 && lastSentPos != 0) || (rawCurrentPos == 255 && lastSentPos != 255);
                    bool deadbandExceeded = Mathf.Abs(rawCurrentPos - lastSentPos) >= activeDeadband;

                    if (isExtreme || deadbandExceeded)
                    {
                        if (isExtreme || (Time.time - lastGripSendTime >= GRIP_MIN_SEND_INTERVAL))
                        {
                            SetTargetPos(rawCurrentPos);
                            lastGripSendTime = Time.time;
                        }
                    }

                    // Auto-Tare when opening trigger fully (<= autoTareThreshold)
                    if (rawCurrentPos >= 42)
                    {
                        rearmedForTare = true;
                        isCalibratedAtOpen = false;
                    }
                    else if (rearmedForTare && rawCurrentPos <= autoTareThreshold)
                    {
                        rearmedForTare = false;
                        isCalibratedAtOpen = true;
                        RequestTare();
                    }
                }
                else
                {
                    if (lastSentPos != lockedGripPos)
                    {
                        SetTargetPos(lockedGripPos);
                    }
                }
            }
            else if (pinchDetector != null && pinchDetector.isTracked)
            {
                int rawCurrentPos = pinchDetector.gripperPos;

                if (Mathf.Abs(rawCurrentPos - lastObservedPos) <= 2)
                {
                    gripperHoldTime += Time.deltaTime;
                }
                else
                {
                    gripperHoldTime = 0f;
                    lastObservedPos = rawCurrentPos;
                }
                int activeDeadband = (gripperHoldTime >= precisionHoldThreshold) ? 2 : deadband;

                if (!isGripperLocked)
                {
                    bool isExtreme = (rawCurrentPos == 0 && lastSentPos != 0) || (rawCurrentPos == 255 && lastSentPos != 255);
                    bool deadbandExceeded = Mathf.Abs(rawCurrentPos - lastSentPos) >= activeDeadband;

                    if (isExtreme || deadbandExceeded)
                    {
                        if (isExtreme || (Time.time - lastGripSendTime >= GRIP_MIN_SEND_INTERVAL))
                        {
                            SetTargetPos(rawCurrentPos);
                            lastGripSendTime = Time.time;
                        }
                    }

                    if (rawCurrentPos >= 42)
                    {
                        rearmedForTare = true;
                        isCalibratedAtOpen = false;
                    }
                    else if (rearmedForTare && rawCurrentPos <= autoTareThreshold)
                    {
                        rearmedForTare = false;
                        isCalibratedAtOpen = true;
                        RequestTare();
                    }
                }
                else
                {
                    if (lastSentPos != lockedGripPos)
                    {
                        SetTargetPos(lockedGripPos);
                    }
                }
            }

            // Rebindable Gripper Lock action check
            bool actionLockDown = false;
            try { actionLockDown = VRKeybindManager.IsActionDown(VRAction.ToggleGripperLock); } catch { }
            if (actionLockDown)
            {
                bool nowLocked = ToggleGripperLock();
                int gripPct = Mathf.RoundToInt((lockedGripPos / 255f) * 100f);
                string msg = nowLocked ? $"🔒 Gripper Locked ({gripPct}%)" : "🔓 Gripper Unlocked (Live)";
                VRMenuManager.Instance?.ShowToast(msg);
                try
                {
                    OVRInput.SetControllerVibration(0.6f, 0.6f, OVRInput.Controller.RTouch);
                    OVRInput.SetControllerVibration(0.6f, 0.6f, OVRInput.Controller.LTouch);
                }
                catch { }
            }

            // Left Controller Grip button hold with circular progress visual
            UpdateLeftGripLockInteraction();
        }

        public void SetTargetPos(int pos)
        {
            lock (lockObj)
            {
                targetGripPos = Mathf.Clamp(pos, 0, 255);
                newPosAvailable = true;
            }
        }
        public void RequestTare()
        {
            lock (lockObj)
            {
                tareRequested = true;
            }
            Debug.Log(string.Format("[GripperBridge] Auto-Tare Requested: Fingers opened to <= {0}/255", autoTareThreshold));
        }

        /// <summary>
        /// à¸ªà¸±à¹ˆà¸‡à¸«à¸¸à¹ˆà¸™à¸¢à¸™à¸•à¹Œ UR3e à¸à¸¥à¸±à¸šà¸ªà¸¹à¹ˆ Safe Home à¹€à¸¡à¸·à¹ˆà¸­à¸ˆà¸µà¸šà¸¡à¸·à¸­à¸‹à¹‰à¸²à¸¢à¸„à¹‰à¸²à¸‡ 3 à¸§à¸´à¸™à¸²à¸—à¸µ
        /// </summary>
        public bool ToggleGripperLock()
        {
            isGripperLocked = !isGripperLocked;
            if (isGripperLocked)
            {
                if (isUsingController)
                {
                    float triggerVal = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, OVRInput.Controller.RTouch);
                    lockedGripPos = Mathf.RoundToInt(triggerVal * 255f);
                }
                else if (pinchDetector != null && pinchDetector.isTracked)
                {
                    lockedGripPos = pinchDetector.gripperPos;
                }
                else
                {
                    lockedGripPos = lastSentPos >= 0 ? lastSentPos : actualGripperPos;
                }

                SetTargetPos(lockedGripPos);
                Debug.Log(string.Format("[GripperBridge] Gripper LOCKED at {0}/255", lockedGripPos));
            }
            else
            {
                Debug.Log("[GripperBridge] Gripper UNLOCKED");
            }
            return isGripperLocked;
        }

        public void SetGripperLock(bool locked)
        {
            if (isGripperLocked == locked) return;
            ToggleGripperLock();
        }

        #region Left Controller Grip Lock Interaction (Circle Bar)
        private void UpdateLeftGripLockInteraction()
        {
            // Find references if null
            if (leftAnchorCache == null)
            {
                var rig = FindAnyObjectByType<OVRCameraRig>();
                if (rig != null) leftAnchorCache = rig.leftControllerInHandAnchor ?? rig.leftControllerAnchor ?? rig.leftHandAnchor;
                if (leftAnchorCache == null)
                {
                    string[] possible = { "LeftControllerInHandAnchor", "LeftControllerAnchor", "LeftHandAnchor" };
                    foreach (var n in possible)
                    {
                        var found = GameObject.Find(n);
                        if (found != null) { leftAnchorCache = found.transform; break; }
                    }
                }
            }

            if (headTransformCache == null)
            {
                if (Camera.main != null) headTransformCache = Camera.main.transform;
                else
                {
                    var ce = GameObject.Find("CenterEyeAnchor");
                    if (ce != null) headTransformCache = ce.transform;
                }
            }

            // Check Left Grip Button input (Middle finger / Hand Trigger on Left Controller)
            float leftGrip = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, OVRInput.Controller.LTouch);
            bool isGripHeld = (leftGrip >= 0.55f) || OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.LTouch);
            try { isGripHeld |= Input.GetKey(KeyCode.G) || Input.GetKey(KeyCode.L); } catch { }

            // Do not trigger lock if actively dragging a window
            if (VRWindowDragger.ActiveDragger != null) isGripHeld = false;

            if (isGripHeld)
            {
                if (isGripLockArmed)
                {
                    gripHoldTimer += Time.deltaTime;
                    float progress = Mathf.Clamp01(gripHoldTimer / gripLockHoldDuration);

                    // Light haptic tick while holding
                    try { OVRInput.SetControllerVibration(0.12f, 0.12f, OVRInput.Controller.LTouch); } catch { }

                    UpdateGripCircleVisual(progress, false);

                    if (gripHoldTimer >= gripLockHoldDuration)
                    {
                        isGripLockArmed = false;
                        bool nowLocked = ToggleGripperLock();

                        // Strong haptic pulse on success
                        try { OVRInput.SetControllerVibration(0.85f, 0.85f, OVRInput.Controller.LTouch); } catch { }

                        gripSuccessTimer = 1.2f;
                        UpdateGripCircleVisual(1.0f, true);

                        int gripPct = Mathf.RoundToInt((lockedGripPos / 255f) * 100f);
                        string msg = nowLocked ? $"🔒 Gripper Locked ({gripPct}%)" : "🔓 Gripper Unlocked (Live)";
                        VRMenuManager.Instance?.ShowToast(msg);
                    }
                }
                else
                {
                    // Keep success feedback alive while user still holds
                    if (gripSuccessTimer > 0f)
                    {
                        gripSuccessTimer -= Time.deltaTime;
                        UpdateGripCircleVisual(1.0f, true);
                    }
                    else
                    {
                        HideGripCircle();
                    }
                }
            }
            else
            {
                // Released grip trigger
                isGripLockArmed = true;
                try { OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.LTouch); } catch { }

                if (gripSuccessTimer > 0f)
                {
                    gripSuccessTimer -= Time.deltaTime;
                    UpdateGripCircleVisual(1.0f, true);
                }
                else
                {
                    gripHoldTimer = 0f;
                    HideGripCircle();
                }
            }
        }

        private void EnsureGripCircleVisuals()
        {
            if (gripCircleRoot != null) return;

            gripCircleRoot = new GameObject("LeftGrip_LockCircle");
            gripCircleRoot.transform.SetParent(transform);

            Shader unlitShader = Shader.Find("UR3eVR/StereoUnlitColor")
                               ?? Shader.Find("Universal Render Pipeline/Unlit")
                               ?? Shader.Find("Sprites/Default");

            // Background circle
            GameObject bgObj = new GameObject("BG_Circle");
            bgObj.transform.SetParent(gripCircleRoot.transform);
            gripBgCircle = bgObj.AddComponent<LineRenderer>();
            gripBgCircle.useWorldSpace = false;
            gripBgCircle.loop = true;
            gripBgCircle.positionCount = CIRCLE_SEGMENTS;
            gripBgCircle.startWidth = 0.003f;
            gripBgCircle.endWidth = 0.003f;
            gripBgCircle.material = new Material(unlitShader) { enableInstancing = true };
            gripBgCircle.startColor = new Color(0.1f, 0.5f, 0.7f, 0.4f);
            gripBgCircle.endColor = new Color(0.1f, 0.5f, 0.7f, 0.4f);
            for (int i = 0; i < CIRCLE_SEGMENTS; i++)
            {
                float rad = (i / (float)CIRCLE_SEGMENTS) * Mathf.PI * 2f;
                gripBgCircle.SetPosition(i, new Vector3(Mathf.Cos(rad) * CIRCLE_RADIUS, Mathf.Sin(rad) * CIRCLE_RADIUS, 0f));
            }

            // Fill arc circle
            GameObject fillObj = new GameObject("Fill_Circle");
            fillObj.transform.SetParent(gripCircleRoot.transform);
            gripFillCircle = fillObj.AddComponent<LineRenderer>();
            gripFillCircle.useWorldSpace = false;
            gripFillCircle.loop = false;
            gripFillCircle.positionCount = 0;
            gripFillCircle.startWidth = 0.005f;
            gripFillCircle.endWidth = 0.005f;
            gripFillCircle.material = new Material(unlitShader) { enableInstancing = true };

            // Text
            GameObject textObj = new GameObject("Text_Lock");
            textObj.transform.SetParent(gripCircleRoot.transform);
            textObj.transform.localPosition = new Vector3(0f, 0f, -0.002f);
            textObj.transform.localScale = Vector3.one * 0.0012f;
            gripTextMesh = textObj.AddComponent<TextMesh>();
            gripTextMesh.alignment = TextAlignment.Center;
            gripTextMesh.anchor = TextAnchor.MiddleCenter;
            gripTextMesh.fontSize = 28;
            gripTextMesh.fontStyle = FontStyle.Bold;
            gripTextMesh.color = Color.cyan;

            var mr = textObj.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                Shader s = Shader.Find("UR3eVR/StereoTextShader");
                if (s != null)
                {
                    Texture fTex = (gripTextMesh.font != null && gripTextMesh.font.material != null) ? gripTextMesh.font.material.mainTexture : null;
                    Material mat = new Material(s);
                    if (fTex != null) mat.mainTexture = fTex;
                    mat.enableInstancing = true;
                    mr.material = mat;
                }
            }

            gripCircleRoot.SetActive(false);
        }

        private void UpdateGripCircleVisual(float progress, bool isSuccess)
        {
            EnsureGripCircleVisuals();
            if (gripCircleRoot == null) return;

            // Position ~9cm above left controller/wrist, slightly forward
            Vector3 targetPos;
            if (leftAnchorCache != null)
            {
                targetPos = leftAnchorCache.position + leftAnchorCache.up * 0.09f + leftAnchorCache.forward * 0.03f;
            }
            else if (headTransformCache != null)
            {
                targetPos = headTransformCache.position + headTransformCache.forward * 0.5f - headTransformCache.right * 0.15f;
            }
            else
            {
                targetPos = transform.position;
            }

            gripCircleRoot.transform.position = targetPos;

            // Billboard towards head
            if (headTransformCache != null)
            {
                Vector3 toHead = targetPos - headTransformCache.position;
                if (toHead.sqrMagnitude > 1e-4f)
                {
                    gripCircleRoot.transform.rotation = Quaternion.LookRotation(toHead);
                }
            }

            if (!gripCircleRoot.activeSelf) gripCircleRoot.SetActive(true);

            int currentPct = isGripperLocked ? Mathf.RoundToInt((lockedGripPos / 255f) * 100f) : Mathf.RoundToInt((actualGripperPos / 255f) * 100f);

            if (isSuccess)
            {
                if (isGripperLocked)
                {
                    Color gold = new Color(1f, 0.82f, 0.1f);
                    gripTextMesh.text = $"LOCKED!\n({currentPct}%)";
                    gripTextMesh.color = gold;
                    gripBgCircle.startColor = gold;
                    gripBgCircle.endColor = gold;
                    UpdateGripArcFill(1f, gold);
                }
                else
                {
                    Color green = Color.green;
                    gripTextMesh.text = "UNLOCKED!\n(Live Tracking)";
                    gripTextMesh.color = green;
                    gripBgCircle.startColor = green;
                    gripBgCircle.endColor = green;
                    UpdateGripArcFill(1f, green);
                }
            }
            else
            {
                int fillPct = Mathf.RoundToInt(progress * 100f);
                Color activeCol;
                if (isGripperLocked)
                {
                    activeCol = Color.Lerp(new Color(1f, 0.75f, 0.1f), Color.yellow, progress);
                    gripTextMesh.text = $"UNLOCK?\n[{fillPct}%]";
                }
                else
                {
                    activeCol = Color.Lerp(new Color(0.0f, 0.85f, 1.0f), Color.yellow, progress);
                    gripTextMesh.text = $"LOCK GRIP\n[{fillPct}%]";
                }

                gripTextMesh.color = activeCol;
                gripBgCircle.startColor = new Color(activeCol.r, activeCol.g, activeCol.b, 0.45f);
                gripBgCircle.endColor = new Color(activeCol.r, activeCol.g, activeCol.b, 0.45f);
                UpdateGripArcFill(progress, activeCol);
            }
        }

        private void UpdateGripArcFill(float pct, Color col)
        {
            if (gripFillCircle == null) return;
            int activePoints = Mathf.Clamp(Mathf.RoundToInt(pct * CIRCLE_SEGMENTS), 0, CIRCLE_SEGMENTS);
            if (activePoints < 2)
            {
                gripFillCircle.positionCount = 0;
            }
            else
            {
                gripFillCircle.positionCount = activePoints + 1;
                for (int i = 0; i <= activePoints; i++)
                {
                    float rad = (Mathf.PI * 0.5f) - (i / (float)CIRCLE_SEGMENTS) * Mathf.PI * 2f;
                    gripFillCircle.SetPosition(i, new Vector3(Mathf.Cos(rad) * CIRCLE_RADIUS, Mathf.Sin(rad) * CIRCLE_RADIUS, 0f));
                }
            }
            gripFillCircle.startColor = col;
            gripFillCircle.endColor = col;
        }

        private void HideGripCircle()
        {
            if (gripCircleRoot != null && gripCircleRoot.activeSelf)
            {
                gripCircleRoot.SetActive(false);
            }
        }
        #endregion

        public void RequestHome()
        {
            lock (lockObj)
            {
                homeRequested = true;
            }
            Debug.Log("[GripperBridge] ðŸ  RequestHome: Sending HOME command to UR3e robot...");
        }

        public void RequestShake()
        {
            lock (lockObj)
            {
                shakeRequested = true;
            }
            isShaking = true;
            ResetPoseCache();
            if (!isGripperLocked)
            {
                ToggleGripperLock();
            }
            Debug.Log("[GripperBridge] 🧪 RequestShake: Sending SHAKE command to UR3e robot & auto-locking gripper...");
        }

        public void RequestPause(bool pause)
        {
            lock (lockObj)
            {
                pauseTargetState = pause;
                pauseRequested = true;
            }
            Debug.Log($"[GripperBridge] ⏸️ RequestPause: Sending PAUSE {pause} to UR3e robot...");
        }

        public void SendSpeed(int speedPct)
        {
            speedPct = Mathf.Clamp(speedPct, 0, 100);
            currentSpeedPct = speedPct;
            PlayerPrefs.SetInt("UR3eVR_MotionSpeed", speedPct);
            PlayerPrefs.Save();
            lock (lockObj)
            {
                pendingSpeedPct = speedPct;
                speedRequested = true;
            }
            Debug.Log($"[GripperBridge] ⚡ SendSpeed: {speedPct}%");
        }

        /// <summary>
        /// à¸£à¸±à¸šà¸„à¸§à¸²à¸¡à¹€à¸£à¹‡à¸§ 6-DOF à¸ˆà¸²à¸ VRHandMover à¹à¸¥à¹‰à¸§à¸ªà¹ˆà¸‡à¹„à¸› Python à¹€à¸›à¹‡à¸™à¸„à¸³à¸ªà¸±à¹ˆà¸‡ MOVE
        /// </summary>
        /// <summary>
        /// à¸•à¸±à¹‰à¸‡à¸„à¹ˆà¸² Target Pose 6-DOF (Cartesian X, Y, Z à¹à¸¥à¸° Orientation Rx, Ry, Rz) à¸ªà¸³à¸«à¸£à¸±à¸šà¸à¸²à¸£à¸•à¸´à¸”à¸•à¸²à¸¡à¸žà¸´à¸à¸±à¸”à¸ˆà¸£à¸´à¸‡
        /// </summary>
        public void SetTargetPose(float x, float y, float z, float rx, float ry, float rz)
        {
            lock (lockObj)
            {
                targetPx = x;
                targetPy = y;
                targetPz = z;
                targetPrx = rx;
                targetPry = ry;
                targetPrz = rz;
                newPoseAvailable = true;
            }
        }

        public void SetTargetVelocity(float vx, float vy, float vz, float wx = 0f, float wy = 0f, float wz = 0f)
        {
            lock (lockObj)
            {
                targetVx = vx;
                targetVy = vy;
                targetVz = vz;
                targetWx = wx;
                targetWy = wy;
                targetWz = wz;
                newVelAvailable = true;
            }
        }

        /// <summary>
        /// Sends PAUSE command to Python UR3e bridge to freeze/resume robot teleoperation
        /// </summary>
        public void SendPause(bool isPaused)
        {
            lock (lockObj)
            {
                pauseTargetState = isPaused;
                pauseRequested = true;
            }
            Debug.Log($"[GripperBridge] SendPause queued: {isPaused}");
        }

        private void StartNetworkThread()
        {
            if (networkThread != null && networkThread.IsAlive) return;

            isRunning = true;
            networkThread = new Thread(NetworkLoop)
            {
                IsBackground = true,
                Name = "GripperBridge_TCP"
            };
            networkThread.Start();
        }

        private void StopNetworkThread()
        {
            isRunning = false;
            if (networkThread != null && networkThread.IsAlive)
            {
                networkThread.Join(500);
                networkThread = null;
            }
            isConnected = false;
        }

        private void NetworkLoop()
        {
            byte[] recvBuffer = new byte[1024];

            while (isRunning)
            {
                TcpClient client = null;
                NetworkStream stream = null;

                try
                {
                    lastStatus = $"Connecting to {serverIp}:{serverPort}...";
                    client = new TcpClient();
                    client.NoDelay = true; // à¸›à¸´à¸” Nagle's Algorithm à¹€à¸žà¸·à¹ˆà¸­à¸ªà¹ˆà¸‡à¹à¸žà¹‡à¸à¹€à¸à¹‡à¸•à¸—à¸±à¸™à¸—à¸µ à¹„à¸£à¹‰ Buffer Delay!
                    try { client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true); } catch { }
                    var connectTask = client.ConnectAsync(serverIp, serverPort);
                    
                    // Wait up to 2 seconds for connection
                    if (!connectTask.Wait(2000))
                    {
                        client.Close();
                        Thread.Sleep(1000);
                        continue;
                    }

                    stream = client.GetStream();
                    stream.ReadTimeout = 50;
                    isConnected = true;
                    lastStatus = "Connected";

                    int lastSentLocal = -1;
                    DateTime lastSendTime = DateTime.UtcNow;

                    ResetPoseCache();
                    DateTime lastPoseSendTime = DateTime.MinValue;
                    bool lastMoveWasZero = false;

                    while (isRunning && client.Connected)
                    {
                        int minIntervalMs = Mathf.RoundToInt(1000f / sendRateHz);
                        int posToSend = -1;
                        bool shouldSend = false;

                        lock (lockObj)
                        {
                            if (newPosAvailable || (DateTime.UtcNow - lastSendTime).TotalMilliseconds >= (heartbeatIntervalSec * 1000))
                            {
                                posToSend = targetGripPos;
                                shouldSend = true;
                                newPosAvailable = false;
                            }
                        }

                        if (shouldSend && (posToSend != lastSentLocal || (DateTime.UtcNow - lastSendTime).TotalMilliseconds >= (heartbeatIntervalSec * 1000)))
                        {
                            string cmd = $"GRIP {posToSend}\n";
                            byte[] bytes = Encoding.ASCII.GetBytes(cmd);
                            stream.Write(bytes, 0, bytes.Length);
                            stream.Flush();

                            lastSentLocal = posToSend;
                            lastSentPos = posToSend;
                            lastSendTime = DateTime.UtcNow;
                        }

                        // à¸ªà¹ˆà¸‡à¸„à¸³à¸ªà¸±à¹ˆà¸‡ TARE à¹„à¸›à¸¢à¸±à¸‡ Python à¹€à¸¡à¸·à¹ˆà¸­à¸­à¹‰à¸²à¸™à¸´à¹‰à¸§ <= 35 (Auto-Tare Fz à¹€à¸«à¸¡à¸·à¸­à¸™à¸à¸” T)
                        bool shouldTare = false;
                        lock (lockObj)
                        {
                            if (tareRequested)
                            {
                                shouldTare = true;
                                tareRequested = false;
                            }
                        }
                        if (shouldTare)
                        {
                            byte[] tareBytes = Encoding.ASCII.GetBytes("TARE\n");
                            stream.Write(tareBytes, 0, tareBytes.Length);
                            stream.Flush();
                        }

                        // à¸ªà¹ˆà¸‡à¸„à¸³à¸ªà¸±à¹ˆà¸‡ HOME à¹„à¸›à¸¢à¸±à¸‡ Python à¹€à¸¡à¸·à¹ˆà¸­à¸ˆà¸µà¸šà¸¡à¸·à¸­à¸‹à¹‰à¸²à¸¢à¸„à¹‰à¸²à¸‡ 3 à¸§à¸´à¸™à¸²à¸—à¸µ
                        bool shouldHome = false;
                        lock (lockObj)
                        {
                            if (homeRequested)
                            {
                                shouldHome = true;
                                homeRequested = false;
                            }
                        }
                        if (shouldHome)
                        {
                            byte[] homeBytes = Encoding.ASCII.GetBytes("HOME\n");
                            stream.Write(homeBytes, 0, homeBytes.Length);
                            stream.Flush();
                        }

                        // Send SHAKE command to UR3e robot for automatic tube mixing
                        bool shouldShake = false;
                        lock (lockObj)
                        {
                            if (shakeRequested)
                            {
                                shouldShake = true;
                                shakeRequested = false;
                            }
                        }
                        if (shouldShake)
                        {
                            byte[] shakeBytes = Encoding.ASCII.GetBytes("SHAKE\n");
                            stream.Write(shakeBytes, 0, shakeBytes.Length);
                            stream.Flush();
                        }

                        // Send PAUSE command to UR3e robot
                        bool shouldPause = false;
                        bool sendPauseVal = false;
                        lock (lockObj)
                        {
                            if (pauseRequested)
                            {
                                shouldPause = true;
                                sendPauseVal = pauseTargetState;
                                pauseRequested = false;
                            }
                        }
                        if (shouldPause)
                        {
                            byte[] pauseBytes = Encoding.ASCII.GetBytes(sendPauseVal ? "PAUSE 1\n" : "PAUSE 0\n");
                            stream.Write(pauseBytes, 0, pauseBytes.Length);
                            stream.Flush();
                        }

                        // Send SPEED command to UR3e robot (Item 4)
                        bool shouldSendSpeed = false;
                        int sendSpeedVal = 100;
                        lock (lockObj)
                        {
                            if (speedRequested)
                            {
                                shouldSendSpeed = true;
                                sendSpeedVal = pendingSpeedPct;
                                speedRequested = false;
                            }
                        }
                        if (shouldSendSpeed)
                        {
                            byte[] speedBytes = Encoding.ASCII.GetBytes($"SPEED {sendSpeedVal}\n");
                            stream.Write(speedBytes, 0, speedBytes.Length);
                            stream.Flush();
                        }

                        // à¸ªà¹ˆà¸‡ POSE 6-DOF Target Pose à¸ˆà¸²à¸ VR Hand â†’ Python à¹€à¸‰à¸žà¸²à¸°à¹€à¸¡à¸·à¹ˆà¸­à¸¡à¸µ "à¸à¸²à¸£à¹€à¸„à¸¥à¸·à¹ˆà¸­à¸™à¸—à¸µà¹ˆà¸—à¸µà¹ˆà¸¡à¸µà¸™à¸±à¸¢à¸ªà¸³à¸„à¸±à¸" (Significant Change)
                        float sendPx = 0f, sendPy = 0f, sendPz = 0f;
                        float sendPrx = 0f, sendPry = 0f, sendPrz = 0f;
                        bool hasPoseCandidate = false;
                        lock (lockObj)
                        {
                            if (newPoseAvailable)
                            {
                                sendPx = targetPx;
                                sendPy = targetPy;
                                sendPz = targetPz;
                                sendPrx = targetPrx;
                                sendPry = targetPry;
                                sendPrz = targetPrz;
                                hasPoseCandidate = true;
                                newPoseAvailable = false;
                            }
                        }
                        if (hasPoseCandidate)
                        {
                            Vector3 curP = new Vector3(sendPx, sendPy, sendPz);
                            Vector3 curR = new Vector3(sendPrx, sendPry, sendPrz);

                            float dPos = Vector3.Distance(curP, lastSentPosePos);
                            float dRotDeg = Vector3.Distance(curR, lastSentPoseRot) * Mathf.Rad2Deg;
                            double elapsedSec = (DateTime.UtcNow - lastPoseSendTime).TotalSeconds;

                            // à¸ªà¹ˆà¸‡à¹€à¸‰à¸žà¸²à¸°à¹€à¸¡à¸·à¹ˆà¸­:
                            // 1. à¸•à¸³à¹à¸«à¸™à¹ˆà¸‡à¸‚à¸¢à¸±à¸šà¹€à¸à¸´à¸™ posChangeThreshold (à¹€à¸Šà¹ˆà¸™ 1.5 mm)
                            // 2. à¸«à¸£à¸·à¸­ à¸‚à¹‰à¸­à¸¡à¸·à¸­à¸«à¸¡à¸¸à¸™à¹€à¸à¸´à¸™ rotChangeThreshold (à¹€à¸Šà¹ˆà¸™ 0.8 à¸­à¸‡à¸¨à¸²)
                            // 3. à¸«à¸£à¸·à¸­ à¸­à¸¢à¸¹à¹ˆà¸™à¸´à¹ˆà¸‡à¸™à¸²à¸™à¹€à¸à¸´à¸™ heartbeatIntervalSec (à¹€à¸Šà¹ˆà¸™ 1.0 à¸§à¸´à¸™à¸²à¸—à¸µ à¸ªà¹ˆà¸‡ Keep-alive 1 à¸„à¸£à¸±à¹‰à¸‡)
                            bool isSignificant = (dPos >= posChangeThreshold) || (dRotDeg >= rotChangeThreshold);
                            bool isHeartbeat = (elapsedSec >= heartbeatIntervalSec);

                            if (isSignificant || isHeartbeat)
                            {
                                string poseCmd = string.Format(
                                    System.Globalization.CultureInfo.InvariantCulture,
                                    "POSE {0:F4} {1:F4} {2:F4} {3:F4} {4:F4} {5:F4}\n",
                                    sendPx, sendPy, sendPz, sendPrx, sendPry, sendPrz);
                                byte[] poseBytes = Encoding.ASCII.GetBytes(poseCmd);
                                stream.Write(poseBytes, 0, poseBytes.Length);
                                stream.Flush();

                                lastSentPosePos = curP;
                                lastSentPoseRot = curR;
                                lastPoseSendTime = DateTime.UtcNow;
                            }
                        }

                        // à¸ªà¹ˆà¸‡ MOVE 6-DOF velocity à¸ˆà¸²à¸ VR Hand â†’ Python (à¸„à¸§à¸šà¸„à¸¸à¸¡à¹à¸‚à¸™à¸à¸¥)
                        float sendVx = 0f, sendVy = 0f, sendVz = 0f;
                        float sendWx = 0f, sendWy = 0f, sendWz = 0f;
                        bool shouldSendVel = false;
                        lock (lockObj)
                        {
                            if (newVelAvailable)
                            {
                                sendVx = targetVx;
                                sendVy = targetVy;
                                sendVz = targetVz;
                                sendWx = targetWx;
                                sendWy = targetWy;
                                sendWz = targetWz;
                                shouldSendVel = true;
                                newVelAvailable = false;
                            }
                        }
                        if (shouldSendVel)
                        {
                            bool isCurrentZero = (Mathf.Abs(sendVx) < 1e-4f && Mathf.Abs(sendVy) < 1e-4f && Mathf.Abs(sendVz) < 1e-4f &&
                                                  Mathf.Abs(sendWx) < 1e-4f && Mathf.Abs(sendWy) < 1e-4f && Mathf.Abs(sendWz) < 1e-4f);
                            // à¸–à¹‰à¸²à¸«à¸¢à¸¸à¸”à¸™à¸´à¹ˆà¸‡à¹à¸¥à¹‰à¸§ (à¹€à¸›à¹‡à¸™ 0 à¸‹à¹‰à¸³) à¹ƒà¸«à¹‰à¸ªà¹ˆà¸‡à¸«à¸¢à¸¸à¸” 1 à¸„à¸£à¸±à¹‰à¸‡à¹à¸¥à¹‰à¸§à¹„à¸¡à¹ˆà¸•à¹‰à¸­à¸‡à¸ªà¹ˆà¸‡à¸‹à¹‰à¸³à¸‹à¹‰à¸­à¸™
                            if (!isCurrentZero || !lastMoveWasZero)
                            {
                                string moveCmd = string.Format(
                                    System.Globalization.CultureInfo.InvariantCulture,
                                    "MOVE {0:F4} {1:F4} {2:F4} {3:F4} {4:F4} {5:F4}\n",
                                    sendVx, sendVy, sendVz, sendWx, sendWy, sendWz);
                                byte[] moveBytes = Encoding.ASCII.GetBytes(moveCmd);
                                stream.Write(moveBytes, 0, moveBytes.Length);
                                stream.Flush();
                                lastMoveWasZero = isCurrentZero;
                            }
                        }

                        // Check incoming data from Python (non-blocking)
                        if (stream.DataAvailable)
                        {
                            int bytesRead = stream.Read(recvBuffer, 0, recvBuffer.Length);
                            if (bytesRead > 0)
                            {
                                string msg = Encoding.UTF8.GetString(recvBuffer, 0, bytesRead).Trim();
                                lastReceivedMessage = msg;

                                // Parse STATUS <pos> <force>
                                string[] lines = msg.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                                foreach (var line in lines)
                                {
                                    if (line.StartsWith("ALERT"))
                                    {
                                        var alertParts = line.Split(new char[] { ' ' }, 3);
                                        string level = alertParts.Length > 1 ? alertParts[1] : "WARNING";
                                        string alertMsg = alertParts.Length > 2 ? alertParts[2] : "";
                                        lock (alertLock)
                                        {
                                            pendingAlertsQueue.Enqueue(new SystemAlert(level, alertMsg));
                                        }
                                    }
                                    else if (line.StartsWith("SHAKE START"))
                                    {
                                        isShaking = true;
                                        pendingShakeLockAction = 1;
                                    }
                                    else if (line.StartsWith("SHAKE DONE"))
                                    {
                                        isShaking = false;
                                        pendingShakeLockAction = 2;
                                        ResetPoseCache();
                                    }
                                    else if (line.StartsWith("STATUS"))
                                    {
                                        var parts = line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                                        if (parts.Length >= 3)
                                        {
                                            int.TryParse(parts[1], out actualGripperPos);
                                            float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out actualGripForce);
                                            if (parts.Length >= 4 && int.TryParse(parts[3], out int parsedSpd))
                                            {
                                                currentSpeedPct = Mathf.Clamp(parsedSpd, 0, 100);
                                            }
                                            if (parts.Length >= 10)
                                            {
                                                float.TryParse(parts[4], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out actualRobotPos.x);
                                                float.TryParse(parts[5], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out actualRobotPos.y);
                                                float.TryParse(parts[6], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out actualRobotPos.z);
                                                float.TryParse(parts[7], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out actualRobotRot.x);
                                                float.TryParse(parts[8], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out actualRobotRot.y);
                                                float.TryParse(parts[9], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out actualRobotRot.z);
                                                hasReceivedRobotPose = true;
                                            // Parse safety proximity ratios (additional floats appended by Python)
                                            if (parts.Length >= 14)
                                            {
                                                float.TryParse(parts[10], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out safetyOuterRatio);
                                                float.TryParse(parts[11], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out safetyInnerRatio);
                                                float.TryParse(parts[12], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out safetyCylinderRatio);
                                                float.TryParse(parts[13], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out safetySpeedRatio);
                                            }
                                            if (parts.Length >= 15)
                                            {
                                                float.TryParse(parts[14], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out safetyRotLimitRatio);
                                            }
                                            if (parts.Length >= 16)
                                            {
                                                int.TryParse(parts[15], out safetyRotLimitJointIdx);
                                            }
                                            if (parts.Length >= 17)
                                            {
                                                float.TryParse(parts[16], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out safetyRotLimitDegLeft);
                                            }
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        Thread.Sleep(minIntervalMs);
                    }
                }
                catch (Exception ex)
                {
                    lastStatus = $"Error: {ex.Message}";
                }
                finally
                {
                    isConnected = false;
                    try { stream?.Close(); } catch { }
                    try { client?.Close(); } catch { }
                }

                if (isRunning)
                {
                    Thread.Sleep(1000); // Wait before reconnecting
                }
            }
        }
    }
}

