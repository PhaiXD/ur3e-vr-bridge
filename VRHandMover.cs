// Recompile trigger: 1792728559.5519507
using System;
using UnityEngine;

namespace UR3eVR
{
    public enum VRTrackingMode
    {
        TargetPose,
        Velocity
    }

    public class VRHandMover : MonoBehaviour
    {
        public static VRHandMover Instance { get; private set; }
        [Header("Tracking Mode")]
        [Tooltip("TargetPose: Closed-loop | Velocity: Open-loop")]
        public VRTrackingMode trackingMode = VRTrackingMode.TargetPose;

        [Header("Hand Tracking Reference")]
        public Transform handTransform;
        public OVRHand ovrHand;
        public Transform headTransform;

        [Header("Orientation Settings")]
        public bool useHeadsetOrientation = true;

        [Header("One Euro Filter Settings")]
        public bool enableOneEuroFilter = true;
        [Range(0.1f, 5.0f)] public float posMinCutoff = 0.85f;
        [Range(0.0f, 50.0f)] public float posBeta = 1.5f;
        [Range(0.1f, 5.0f)] public float rotMinCutoff = 0.80f;
        [Range(0.0f, 20.0f)] public float rotBeta = 1.2f;
        public float dCutoff = 1.5f;

        [Header("Starter Point (UR3e Home Visualizer)")]
        public bool showStarterPoint = true;
        public UR3eStarterPoint starterPoint;
        public LeftHandHomeTrigger leftHandHomeTrigger;

        [Header("Touch Controller / Remote Support")]
        [Tooltip("Enable Quest Touch Controller teleoperation when user picks up remote")]
        public bool allowControllerTeleoperation = true;
        [Tooltip("Right controller transform (RightControllerAnchor)")]
        public Transform rightControllerAnchor;
        [Tooltip("Whether Touch Controller is currently in use")]
        public bool isUsingController = false;

        [Header("Left Touch Controller Actions")]
        [Tooltip("Enable left controller buttons (X: Set Origin/Home, Y: Pause/Resume, Thumbstick: Shake)")]
        public bool enableLeftControllerActions = true;

        [Header("Teleoperation Pause State")]
        [Tooltip("Pause teleoperation (Y button on left controller to toggle)")]
        public bool isTeleoperationPaused = false;

        [Header("Auto Shake Test Tube State")]
        [Tooltip("Auto shake test tube active (left thumbstick click)")]
        public bool isShakingActive = false;
        private bool wasShaking = false;
        private float shakeStartTime = 0f;

        [Header("Target Pose and Anchor Settings")]
        [Range(0.2f, 3.0f)] public float posSensitivity = 0.50f;
        public Vector3 homeRobotPos = new Vector3(0.3053f, -0.1313f, 0.3108f);
        public Vector3 homeRobotRotAxisAngle = new Vector3(0.0f, 0.0f, -1.5708f);
        public KeyCode recenterKey = KeyCode.R;

        [Header("Cartesian Target Bounds (m)")]
        public Vector3 minTargetPos = new Vector3(0.08f, -0.45f, -0.05f);
        public Vector3 maxTargetPos = new Vector3(0.58f, 0.35f, 0.65f);

        [Header("Physical Reach Clamping & Soft Deceleration (Robot Base Limits)")]
        [Tooltip("Max reach from robot base, prevents unreachable targets (meters)")]
        public float maxBaseReach = 0.58f;
        [Tooltip("Deceleration zone before outer red boundary (meters)")]
        public float outerSlowZone = 0.04f;

        [Tooltip("Min reach from robot base, prevents tool-base collision (meters)")]
        public float minBaseReach = 0.28f;
        [Tooltip("Deceleration zone before inner yellow boundary (meters)")]
        public float innerSlowZone = 0.06f;

        [Tooltip("Max target position speed to prevent jerking and inertia (m/s)")]
        public float maxTargetLinearSpeed = 1.20f;

        [Header("Linear Axis Inversions")]
        public bool invertForwardBackward = false;
        public bool invertLeftRight = false;
        public bool invertUpDown = false;

        [Header("Rotation Settings")]
        public bool enableRotation = true;
        [Range(0.5f, 4.0f)] public float rotSensitivity = 1.0f;

        [Header("Rotation Axis Inversions (Custom Overrides)")]
        public bool invertRoll = false;
        public bool invertPitch = false;
        public bool invertYaw = false;

        [Header("Velocity Mode Settings")]
        [Range(0.01f, 0.6f)] public float maxSpeed = 0.30f;
        [Range(0.2f, 3.0f)] public float maxRotSpeed = 1.60f;
        [Range(0f, 0.95f)] public float smoothing = 0.4f;
        [Range(0f, 0.95f)] public float rotSmoothing = 0.2f;

        [Header("Home Docking Engagement (Safety Interlock)")]
        [Tooltip("Require hand placement at Home before robot starts tracking")]
        public bool requireDockAtHomeToStart = false;
        [Tooltip("Whether tracking is engaged (requires Home docking first)")]
        public bool isTrackingEngaged = false;
        [Tooltip("Required dwell time at Home to confirm engagement (seconds)")]
        public float dockDwellTimeRequired = 0.35f;
        public float currentDockTime = 0.0f;

        [Header("Live Output (Read Only)")]
        public bool isHandTracked = false;
        public bool isAnchorSet = false;
        public Vector3 anchorHandWorldPos;
        public Quaternion anchorHeadYaw = Quaternion.identity;
        public Vector3 anchorHandPos;
        public Quaternion anchorHandRot = Quaternion.identity;
        public Vector3 anchorRobotPos;
        public Vector3 anchorRobotRot;
        public Vector3 currentTargetPos;
        public Vector3 currentTargetRotAxisAngle;
        public Vector3 rawVelocity;
        public Vector3 mappedVelocity;
        public Vector3 mappedAngularVelocity;

        [Header("Robot Pose Feedback (Read Only)")]
        public Vector3 actualRobotPos = Vector3.zero;
        public Vector3 actualRobotRot = Vector3.zero;
        public bool hasReceivedRobotPose = false;

        private Vector3 prevHandPos;
        private Quaternion prevHandRot;
        private bool hasPrevState = false;
        private Vector3 smoothedVel = Vector3.zero;
        private Vector3 smoothedRotVel = Vector3.zero;
        private GripperBridge gripperBridge;
        private bool hasSyncedInitialRobotPose = false;
        private OneEuroFilterVector3 posFilter = new OneEuroFilterVector3();
        private OneEuroFilterQuaternion rotFilter = new OneEuroFilterQuaternion();

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else if (Instance != this)
            {
                Debug.LogWarning("[VRHandMover] Multiple VRHandMover instances detected! Destroying duplicate.");
                Destroy(this);
                return;
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            gripperBridge = FindAnyObjectByType<GripperBridge>();
            if (gripperBridge == null)
                Debug.LogWarning("[VRHandMover] GripperBridge not found!");

            if (handTransform == null) handTransform = transform;
            if (rightControllerAnchor == null)
            {
                GameObject rca = GameObject.Find("RightControllerAnchor");
                if (rca != null) rightControllerAnchor = rca.transform;
            }

            if (ovrHand == null && handTransform != null)
            {
                ovrHand = handTransform.GetComponent<OVRHand>();
                if (ovrHand == null) ovrHand = handTransform.GetComponentInParent<OVRHand>();
            }

            if (headTransform == null)
            {
                if (Camera.main != null) headTransform = Camera.main.transform;
                else
                {
                    GameObject centerEye = GameObject.Find("CenterEyeAnchor");
                    if (centerEye != null) headTransform = centerEye.transform;
                }
            }

            anchorRobotPos = homeRobotPos;
            anchorRobotRot = homeRobotRotAxisAngle;
            currentTargetPos = homeRobotPos;
            currentTargetRotAxisAngle = homeRobotRotAxisAngle;

            Transform initialTracking = GetActiveTrackingTransform();
            if (initialTracking != null)
            {
                anchorHandWorldPos = initialTracking.position;
            }

            if (showStarterPoint && starterPoint == null)
            {
                starterPoint = FindAnyObjectByType<UR3eStarterPoint>();
                if (starterPoint == null)
                {
                    GameObject spObj = new GameObject("VRHaptiLab_StarterPoint");
                    starterPoint = spObj.AddComponent<UR3eStarterPoint>();
                    starterPoint.vrHandMover = this;
                    starterPoint.handTransform = handTransform;
                    starterPoint.headTransform = headTransform;
                }
            }

            if (leftHandHomeTrigger == null)
            {
                leftHandHomeTrigger = FindAnyObjectByType<LeftHandHomeTrigger>();
                if (leftHandHomeTrigger == null)
                {
                    GameObject lhObj = new GameObject("LeftHand_HomeTrigger");
                    leftHandHomeTrigger = lhObj.AddComponent<LeftHandHomeTrigger>();
                    leftHandHomeTrigger.vrHandMover = this;
                    leftHandHomeTrigger.gripperBridge = gripperBridge;
                    leftHandHomeTrigger.headTransform = headTransform;
                }
            }

            // Ensure VRHandStereoFixer is running to guarantee hand visibility and stereo rendering in builds
            if (FindAnyObjectByType<VRHandStereoFixer>() == null)
            {
                gameObject.AddComponent<VRHandStereoFixer>();
            }

            // Ensure SafetyBoundaryVisualizer is running to display boundary mesh and warning spheres
            if (FindAnyObjectByType<SafetyBoundaryVisualizer>() == null)
            {
                var sb = gameObject.AddComponent<SafetyBoundaryVisualizer>();
                sb.vrHandMover = this;
                sb.gripperBridge = gripperBridge;
                sb.handTransform = handTransform;
            }

            isTrackingEngaged = !requireDockAtHomeToStart;
            currentDockTime = 0.0f;

            if (FindAnyObjectByType<VRWristTelemetry>() == null)
            {
                GameObject wristObj = new GameObject("VR_WristTelemetry");
                wristObj.AddComponent<VRWristTelemetry>();
            }
        }

        /// <summary>
        /// Recenter Anchor at current hand position (palm center of right hand).
        /// Starting pose reference: right hand tilted like holding a water bottle.
        /// Center Point in Unity will instantly snap to the hand's current position.
        /// </summary>
        public Transform GetActiveTrackingTransform()
        {
            bool controllerActive = allowControllerTeleoperation &&
                                    rightControllerAnchor != null &&
                                    VRHandStereoFixer.IsRightControllerInHand;
            if (controllerActive)
            {
                isUsingController = true;
                return rightControllerAnchor;
            }
            isUsingController = false;
            return handTransform;
        }

        public void RecenterAnchor()
        {
            Transform activeTransform = GetActiveTrackingTransform();
            if (activeTransform == null) return;

            // 1. Record right hand world position
            anchorHandWorldPos = activeTransform.position;

            // 2. Headset horizontal yaw direction
            Quaternion headYaw = Quaternion.identity;
            if (useHeadsetOrientation && headTransform != null)
            {
                headYaw = Quaternion.Euler(0f, headTransform.eulerAngles.y, 0f);
            }
            anchorHeadYaw = headYaw;

            // 3. Record hand rotation relative to head yaw
            anchorHandRot = Quaternion.Inverse(headYaw) * activeTransform.rotation;
            anchorHandPos = Quaternion.Inverse(headYaw) * (activeTransform.position - (headTransform != null ? headTransform.position : Vector3.zero));

            // 4. On recenter/home, reset robot reference to safe home
            anchorRobotPos = homeRobotPos;
            anchorRobotRot = homeRobotRotAxisAngle;

            currentTargetPos = anchorRobotPos;
            currentTargetRotAxisAngle = anchorRobotRot;
            isAnchorSet = true;
            isTrackingEngaged = true;
            posFilter.Reset();
            rotFilter.Reset();
            hasPrevState = false;

            // 5. Update StarterPoint marker to current hand position
            if (starterPoint != null)
            {
                starterPoint.UpdateAnchor(anchorHandWorldPos, headYaw, anchorRobotPos);
            }

            Debug.Log(string.Format("[VRHandMover] Anchor Recentered at hand palm! World: ({0:F3}, {1:F3}, {2:F3}), Robot Home: ({3:F3}, {4:F3}, {5:F3})",
                anchorHandWorldPos.x, anchorHandWorldPos.y, anchorHandWorldPos.z,
                anchorRobotPos.x, anchorRobotPos.y, anchorRobotPos.z));
        }

        private void Update()
        {
            Transform activeTransform = GetActiveTrackingTransform();

            // ========================================================
            // LEFT TOUCH CONTROLLER ACTIONS
            // ========================================================
            if (enableLeftControllerActions)
            {
                // 1. Set Origin Point & Robot Safe Home (Default: X)
                bool xPressed = false;
                try { xPressed = VRKeybindManager.IsActionDown(VRAction.SetOrigin); } catch { }
                if (!xPressed)
                {
                    try
                    {
                        xPressed = OVRInput.GetDown(OVRInput.Button.Three, OVRInput.Controller.LTouch) ||
                                   OVRInput.GetDown(OVRInput.RawButton.X, OVRInput.Controller.LTouch) ||
                                   Input.GetKeyDown(KeyCode.X);
                    }
                    catch { }
                }

                if (xPressed)
                {
                    ExecuteSetOriginAndHome();
                }

                // 2. Pause / Resume Teleoperation (Default: Y)
                bool yPressed = false;
                try { yPressed = VRKeybindManager.IsActionDown(VRAction.PauseResume); } catch { }
                if (!yPressed)
                {
                    try
                    {
                        yPressed = OVRInput.GetDown(OVRInput.Button.Four, OVRInput.Controller.LTouch) ||
                                   OVRInput.GetDown(OVRInput.RawButton.Y, OVRInput.Controller.LTouch) ||
                                   Input.GetKeyDown(KeyCode.Y);
                    }
                    catch { }
                }

                if (yPressed)
                {
                    ToggleTeleoperationPause();
                }

                // 3. Shake Test Tube: auto-shake 3 cycles (Default: Left Stick Click)
                bool thumbstickPressed = false;
                try { thumbstickPressed = VRKeybindManager.IsActionDown(VRAction.ShakeTestTube); } catch { }
                if (!thumbstickPressed)
                {
                    try
                    {
                        thumbstickPressed = OVRInput.GetDown(OVRInput.Button.PrimaryThumbstick, OVRInput.Controller.LTouch) ||
                                             OVRInput.GetDown(OVRInput.RawButton.LThumbstick, OVRInput.Controller.LTouch) ||
                                             Input.GetKeyDown(KeyCode.K);
                    }
                    catch { }
                }

                if (thumbstickPressed)
                {
                    ExecuteAutoShake();
                }
            }

            // Auto Shake state: block hand tracking while shaking, SyncAnchor on completion
            bool isShakingNow = (gripperBridge != null && gripperBridge.isShaking);

            // Safety timeout: if shaking exceeds 15s (e.g. server disconnect), force resume
            if (wasShaking && (Time.time - shakeStartTime > 15f))
            {
                if (gripperBridge != null) gripperBridge.isShaking = false;
                isShakingNow = false;
                Debug.LogWarning("[VRHandMover] ⚠️ Auto Shake timeout safety triggered (> 15s) - Resuming teleoperation!");
            }

            if (isShakingNow && !wasShaking)
            {
                wasShaking = true;
                isShakingActive = true;
                shakeStartTime = Time.time;
            }
            else if (!isShakingNow && wasShaking)
            {
                wasShaking = false;
                isShakingActive = false;
                SyncAnchorOnResume();
                Debug.Log("[VRHandMover] 🧪 Auto Shake complete! Teleoperation smoothly resumed.");
            }

            // If paused or shaking: stop sending pose to robot arm
            if (isTeleoperationPaused || isShakingNow)
            {
                if (gripperBridge != null)
                {
                    gripperBridge.SetTargetVelocity(0f, 0f, 0f, 0f, 0f, 0f);
                }
                return;
            }


            if (isUsingController)
            {
                isHandTracked = true;
            }
            else if (ovrHand != null)
            {
                isHandTracked = ovrHand.IsTracked;
            }
            else if (handTransform != null)
            {
                isHandTracked = true;
            }
            else
            {
                isHandTracked = false;
                ResetOutputs();
                return;
            }

            if (!isHandTracked || activeTransform == null)
            {
                ResetOutputs();
                return;
            }

#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.rKey.wasPressedThisFrame) RecenterAnchor();
            if (kb != null && kb.fKey.wasPressedThisFrame) ToggleFilter();
#endif
            try { if (Input.GetKeyDown(recenterKey)) RecenterAnchor(); } catch { }
            try { if (Input.GetKeyDown(KeyCode.F)) ToggleFilter(); } catch { }

            // Sync live robot pose on first connection
            if (!hasSyncedInitialRobotPose && gripperBridge != null && gripperBridge.hasReceivedRobotPose)
            {
                actualRobotPos = gripperBridge.actualRobotPos;
                actualRobotRot = gripperBridge.actualRobotRot;
                hasReceivedRobotPose = true;
                if (!isAnchorSet)
                {
                    RecenterAnchor();
                    hasSyncedInitialRobotPose = true;
                    Debug.Log("[VRHandMover] Initial Live Robot Pose Synced!");
                }
            }

            // Compute headset horizontal yaw
            Quaternion currentHeadYaw = Quaternion.identity;
            if (useHeadsetOrientation && headTransform != null)
            {
                currentHeadYaw = Quaternion.Euler(0f, headTransform.eulerAngles.y, 0f);
            }

            // Apply One Euro Filter
            if (enableOneEuroFilter)
            {
                posFilter.UpdateParams(posMinCutoff, posBeta, dCutoff);
                rotFilter.UpdateParams(rotMinCutoff, rotBeta, dCutoff);
            }
            Vector3 rawPos = activeTransform.position;
            Quaternion rawRot = activeTransform.rotation;
            Vector3 filteredWorldPos = enableOneEuroFilter ? posFilter.Filter(rawPos) : rawPos;
            Quaternion filteredWorldRot = enableOneEuroFilter ? rotFilter.Filter(rawRot) : rawRot;

            float dt = Time.deltaTime;
            if (dt <= 1e-6f) dt = 0.01f;

            // 1. Check hand placement at Home to unlock control (Safety Interlock)
            if (starterPoint == null) starterPoint = FindAnyObjectByType<UR3eStarterPoint>();
            Vector3 homeTargetPos = (starterPoint != null) ? starterPoint.transform.position : anchorHandWorldPos;
            float distToHome = (starterPoint != null) ? Vector3.Distance(filteredWorldPos, homeTargetPos) : 999f;
            bool isAtHomeZone = (starterPoint != null) && (distToHome <= starterPoint.homeZoneRadius);

            if (requireDockAtHomeToStart && !isTrackingEngaged)
            {
                if (isAtHomeZone)
                {
                    currentDockTime += dt;
                    if (currentDockTime >= dockDwellTimeRequired)
                    {
                        isTrackingEngaged = true;
                        anchorHandWorldPos = homeTargetPos;
                        anchorHeadYaw = currentHeadYaw;
                        anchorHandRot = Quaternion.Inverse(currentHeadYaw) * filteredWorldRot;
                        anchorRobotPos = homeRobotPos;
                        anchorRobotRot = homeRobotRotAxisAngle;
                        isAnchorSet = true;

                        if (starterPoint != null) starterPoint.NotifyEngaged();
                        Debug.Log("[VRHandMover] ★ HAND DOCKED AT HOME! ROBOT CONTROL ENGAGED ★");
                    }
                }
                else
                {
                    currentDockTime = 0.0f;
                }

                // Not docked at Home yet: lock robot at safe home position
                currentTargetPos = homeRobotPos;
                currentTargetRotAxisAngle = homeRobotRotAxisAngle;
                if (gripperBridge != null)
                {
                    gripperBridge.SetTargetPose(
                        homeRobotPos.x, homeRobotPos.y, homeRobotPos.z,
                        homeRobotRotAxisAngle.x, homeRobotRotAxisAngle.y, homeRobotRotAxisAngle.z
                    );
                }
                return; // Not yet tracking hand
            }

            if (!isAnchorSet)
            {
                anchorHandWorldPos = filteredWorldPos;
                anchorHeadYaw = currentHeadYaw;
                anchorHandRot = Quaternion.Inverse(currentHeadYaw) * filteredWorldRot;
                anchorRobotPos = homeRobotPos;
                anchorRobotRot = homeRobotRotAxisAngle;
                isAnchorSet = true;
                if (starterPoint != null)
                {
                    starterPoint.UpdateAnchor(anchorHandWorldPos, currentHeadYaw, anchorRobotPos);
                }
            }

            // ========================================================
            // 1. TARGET POSE TRACKING (Closed-loop mm-precision)
            // ========================================================
            if (trackingMode == VRTrackingMode.TargetPose)
            {
                // Movement vector from anchor in head-relative frame
                // Z = forward (from headset), X = right, Y = up
                Vector3 worldDelta = filteredWorldPos - anchorHandWorldPos;
                Quaternion referenceYaw = (useHeadsetOrientation && isAnchorSet) ? anchorHeadYaw : Quaternion.identity;
                Vector3 handDelta = Quaternion.Inverse(referenceYaw) * worldDelta;

                // Deadband to filter hand tremor when stationary
                if (Mathf.Abs(handDelta.x) < 0.0008f) handDelta.x = 0f;
                if (Mathf.Abs(handDelta.y) < 0.0008f) handDelta.y = 0f;
                if (Mathf.Abs(handDelta.z) < 0.0008f) handDelta.z = 0f;

                // Position Mapping (VR Head-Relative -> UR3e Base Frame)
                // handDelta.z (headset forward) -> Robot +X (Forward)
                // -handDelta.x (headset left) -> Robot +Y (Left)
                // handDelta.y (up) -> Robot +Z (Up)
                float dX = handDelta.z * posSensitivity * (invertForwardBackward ? -1f : 1f);
                float dY = -handDelta.x * posSensitivity * (invertLeftRight ? -1f : 1f);
                float dZ = handDelta.y * posSensitivity * (invertUpDown ? -1f : 1f);

                Vector3 targetP = anchorRobotPos + new Vector3(dX, dY, dZ);
                // 1. Box Bounds Clamping
                targetP.x = Mathf.Clamp(targetP.x, minTargetPos.x, maxTargetPos.x);
                targetP.y = Mathf.Clamp(targetP.y, minTargetPos.y, maxTargetPos.y);
                targetP.z = Mathf.Clamp(targetP.z, minTargetPos.z, maxTargetPos.z);

                // 2. Proximity soft deceleration & spherical boundary stopping from robot base
                // Smooth slowdown near red/yellow boundaries, eliminates inertia overshoot
                float distFromBase = targetP.magnitude;
                Vector3 dirFromBase = (distFromBase > 1e-4f) ? targetP.normalized : Vector3.forward;

                // Decelerate near outer red barrier
                float outerSlowStart = maxBaseReach - outerSlowZone;
                if (distFromBase > outerSlowStart)
                {
                    float excess = distFromBase - outerSlowStart;
                    float smoothDist = outerSlowStart + outerSlowZone * (float)System.Math.Tanh(excess / outerSlowZone);
                    distFromBase = Mathf.Min(smoothDist, maxBaseReach);
                }

                // Decelerate near inner yellow barrier
                float innerSlowStart = minBaseReach + innerSlowZone;
                if (distFromBase < innerSlowStart)
                {
                    float deficit = innerSlowStart - distFromBase;
                    float smoothDist = innerSlowStart - innerSlowZone * (float)System.Math.Tanh(deficit / innerSlowZone);
                    distFromBase = Mathf.Max(smoothDist, minBaseReach);
                }

                targetP = dirFromBase * distFromBase;

                // 3. Slew-rate limiter (cap target speed, prevent jerking & inertia)
                float frameDt = Time.deltaTime;
                if (frameDt > 0f)
                {
                    Vector3 step = targetP - currentTargetPos;
                    float maxStep = maxTargetLinearSpeed * frameDt;
                    if (step.magnitude > maxStep)
                    {
                        targetP = currentTargetPos + step.normalized * maxStep;
                    }
                }

                currentTargetPos = targetP;

                // Rotation Mapping (VR Headset Frame -> UR3e Base Frame)
                Vector3 targetRotVec = anchorRobotRot;
                if (enableRotation)
                {
                    Quaternion currentRotRel = Quaternion.Inverse(referenceYaw) * filteredWorldRot;
                    Quaternion deltaRot = currentRotRel * Quaternion.Inverse(anchorHandRot);
                    deltaRot.ToAngleAxis(out float angleDeg, out Vector3 axis);
                    if (angleDeg > 180f) angleDeg -= 360f;

                    if (Mathf.Abs(angleDeg) > 0.25f && !float.IsNaN(axis.x) && !float.IsInfinity(axis.x))
                    {
                        Vector3 vrRotVec = axis.normalized * (angleDeg * Mathf.Deg2Rad * rotSensitivity);

                        // Chirality correction: Unity left-hand -> UR3e right-hand frame
                        // VR Headset -> UR3e Base Frame mapping:
                        //   VR +Z (forward) -> Robot +X (Roll): negate
                        //   VR -X (left)    -> Robot +Y (Pitch): negate twice = +X
                        //   VR +Y (up)      -> Robot +Z (Yaw): negate
                        float d_wx = -vrRotVec.z * (invertRoll ? -1f : 1f);
                        float d_wy = vrRotVec.x * (invertPitch ? -1f : 1f);
                        float d_wz = -vrRotVec.y * (invertYaw ? -1f : 1f);

                        Vector3 d_rot = new Vector3(d_wx, d_wy, d_wz);
                        float d_mag = d_rot.magnitude;
                        if (d_mag > 1e-4f)
                        {
                            Vector3 d_axis = d_rot / d_mag;
                            Quaternion q_delta = Quaternion.AngleAxis(d_mag * Mathf.Rad2Deg, d_axis);

                            float anchorAngRad = anchorRobotRot.magnitude;
                            Vector3 anchorAxis = anchorAngRad > 1e-5f ? (anchorRobotRot / anchorAngRad) : Vector3.up;
                            Quaternion q_anchor = Quaternion.AngleAxis(anchorAngRad * Mathf.Rad2Deg, anchorAxis);

                            Quaternion q_target = q_delta * q_anchor;
                            q_target.ToAngleAxis(out float tgtAngleDeg, out Vector3 tgtAxis);
                            if (tgtAngleDeg > 180f) tgtAngleDeg -= 360f;
                            targetRotVec = tgtAxis.normalized * (tgtAngleDeg * Mathf.Deg2Rad);
                        }
                    }
                }
                currentTargetRotAxisAngle = targetRotVec;

                if (gripperBridge != null)
                {
                    gripperBridge.SetTargetPose(
                        currentTargetPos.x, currentTargetPos.y, currentTargetPos.z,
                        currentTargetRotAxisAngle.x, currentTargetRotAxisAngle.y, currentTargetRotAxisAngle.z
                    );
                }
            }
            // ========================================================
            // 2. VELOCITY TRACKING (Open-Loop backup)
            // ========================================================
            else
            {
                Vector3 deltaPos = filteredWorldPos - prevHandPos;
                Vector3 headRelDelta = Quaternion.Inverse(currentHeadYaw) * deltaPos;
                Vector3 linVel = (headRelDelta / dt) * posSensitivity;
                smoothedVel = Vector3.Lerp(linVel, smoothedVel, smoothing);

                float robotVx = smoothedVel.z * (invertForwardBackward ? -1f : 1f);
                float robotVy = -smoothedVel.x * (invertLeftRight ? -1f : 1f);
                float robotVz = smoothedVel.y * (invertUpDown ? -1f : 1f);

                Vector3 robotLinVel = new Vector3(robotVx, robotVy, robotVz);
                if (robotLinVel.magnitude > maxSpeed) robotLinVel = robotLinVel.normalized * maxSpeed;
                mappedVelocity = robotLinVel;

                Vector3 robotRotVel = Vector3.zero;
                if (enableRotation)
                {
                    Quaternion currentRotRel = Quaternion.Inverse(currentHeadYaw) * filteredWorldRot;
                    Quaternion deltaRot = currentRotRel * Quaternion.Inverse(prevHandRot);
                    deltaRot.ToAngleAxis(out float angleDeg, out Vector3 axis);
                    if (angleDeg > 180f) angleDeg -= 360f;

                    Vector3 angVel = Vector3.zero;
                    if (Mathf.Abs(angleDeg) > 0.02f && !float.IsNaN(axis.x) && !float.IsInfinity(axis.x))
                        angVel = axis.normalized * (angleDeg * Mathf.Deg2Rad / dt);

                    smoothedRotVel = Vector3.Lerp(angVel * rotSensitivity, smoothedRotVel, rotSmoothing);

                    // Chirality correction for velocity mode
                    float robotWx = -smoothedRotVel.z * (invertRoll ? -1f : 1f);
                    float robotWy = smoothedRotVel.x * (invertPitch ? -1f : 1f);
                    float robotWz = -smoothedRotVel.y * (invertYaw ? -1f : 1f);

                    robotRotVel = new Vector3(robotWx, robotWy, robotWz);
                    if (robotRotVel.magnitude > maxRotSpeed) robotRotVel = robotRotVel.normalized * maxRotSpeed;
                }
                mappedAngularVelocity = robotRotVel;

                if (gripperBridge != null)
                {
                    gripperBridge.SetTargetVelocity(
                        robotLinVel.x, robotLinVel.y, robotLinVel.z,
                        robotRotVel.x, robotRotVel.y, robotRotVel.z
                    );
                }
            }

            prevHandPos = filteredWorldPos;
            prevHandRot = Quaternion.Inverse(currentHeadYaw) * filteredWorldRot;
        }

        /// <summary>
        /// Toggle One Euro Filter on/off at runtime (press F)
        /// </summary>
        public void ToggleFilter()
        {
            enableOneEuroFilter = !enableOneEuroFilter;
            posFilter?.Reset();
            rotFilter?.Reset();
            Debug.Log(string.Format("[VRHandMover] One Euro Filter is now: {0} (Press 'F' to toggle)",
                enableOneEuroFilter ? "ENABLED" : "DISABLED"));
        }

        private void ResetOutputs()
        {
            hasPrevState = false;
            posFilter?.Reset();
            rotFilter?.Reset();
            rawVelocity = Vector3.zero;
            mappedVelocity = Vector3.zero;
            smoothedVel = Vector3.zero;
            mappedAngularVelocity = Vector3.zero;
            smoothedRotVel = Vector3.zero;
        }
    
        /// <summary>
        /// Button X: Set origin point (Unity center point + robot safe home)
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
            if (gripperBridge != null)
            {
                gripperBridge.SendPause(false);
            }
            TriggerLeftControllerHaptic(0.6f, 0.15f);
            Debug.Log("[VRHandMover] [Button X] Set Origin Point (Center Point & Safe Home) executed!");
        }

        /// <summary>
        /// Button Y: Pause/Resume robot arm teleoperation
        /// </summary>
        public void ToggleTeleoperationPause()
        {
            isTeleoperationPaused = !isTeleoperationPaused;
            if (gripperBridge != null) gripperBridge.SendPause(isTeleoperationPaused);
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
        /// Sync anchor on unpause for zero-jerk resume.
        /// Reads live robot pose from GripperBridge directly (avoids stale cache).
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

            // Read LIVE robot pose from GripperBridge (not stale cached value)
            if (gripperBridge != null && gripperBridge.hasReceivedRobotPose)
            {
                actualRobotPos = gripperBridge.actualRobotPos;
                actualRobotRot = gripperBridge.actualRobotRot;
                anchorRobotPos = actualRobotPos;
                anchorRobotRot = actualRobotRot;
                currentTargetPos = actualRobotPos;
                currentTargetRotAxisAngle = actualRobotRot;
            }
            else if (hasReceivedRobotPose)
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

            if (gripperBridge != null)
            {
                gripperBridge.ResetPoseCache();
                gripperBridge.SetTargetPose(
                    currentTargetPos.x, currentTargetPos.y, currentTargetPos.z,
                    currentTargetRotAxisAngle.x, currentTargetRotAxisAngle.y, currentTargetRotAxisAngle.z
                );
            }
        }

        /// <summary>
        /// Thumbstick click: auto-shake test tube 3 cycles (flip 180° and back)
        /// </summary>
        public void ExecuteAutoShake()
        {
            if (gripperBridge != null)
            {
                gripperBridge.RequestShake();
            }
            wasShaking = true;
            isShakingActive = true;
            shakeStartTime = Time.time;
            TriggerLeftControllerHaptic(0.7f, 0.25f);
            Debug.Log("[VRHandMover] [Thumbstick Click] 🧪 Automated Test Tube Shake (3 cycles) requested!");
        }

        /// <summary>
        /// Trigger haptic feedback on left controller
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