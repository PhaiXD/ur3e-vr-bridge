using System;
using UnityEngine;
using Bhaptics.SDK2;

namespace UR3eVR
{
    /// <summary>
    /// BhapticsGloveFeedback:
    /// 1. Gripper pinch haptic feedback (Finger motors)
    ///    - Unconditional full pinch: 100% intensity when index touches thumb
    ///    - Relaxed hand gating: 0% intensity when hand is open/relaxed to prevent ghost vibrations
    ///    - Grip contact latch & floor: maintains steady haptic floor while gripping
    /// 2. Safety boundary wrist haptic alerts (Motor #5)
    ///    - Caution zone: 4 Hz rhythmic pulse
    ///    - Danger zone: 20 Hz urgent buzz
    /// </summary>
    public class BhapticsGloveFeedback : MonoBehaviour
    {
        [Header("Target Device")]
        [Tooltip("Target glove device (default: GloveR)")]
        public PositionType glovePosition = PositionType.GloveR;

        [Header("Component References")]
        [Tooltip("GripperBridge reference (auto-detected if null)")]
        public GripperBridge gripperBridge;

        [Tooltip("SafetyBoundaryVisualizer reference (auto-detected if null)")]
        public SafetyBoundaryVisualizer safetyBoundary;

        [Tooltip("VRHandMover reference (auto-detected if null)")]
        public VRHandMover vrHandMover;

        [Tooltip("PinchDetector reference (auto-detected if null)")]
        public PinchDetector pinchDetector;

        [Tooltip("OVRHand reference (auto-detected if null)")]
        public OVRHand ovrHand;

        [Header("Unconditional Full Pinch (100% Intensity)")]
        [Tooltip("Enable unconditional 100% haptic on full pinch")]
        public bool enableUnconditionalFullPinch = true;

        [Tooltip("Pinch contact distance threshold (cm)")]
        [Range(1.5f, 4.0f)]
        public float fullPinchDistanceCmThreshold = 2.6f;

        [Tooltip("Normalized pinch threshold (0.0 - 1.0)")]
        [Range(0.70f, 0.98f)]
        public float fullNormalizedPinchThreshold = 0.85f;

        [Tooltip("Controller trigger threshold for full press")]
        [Range(0.70f, 1.0f)]
        public float fullTriggerThreshold = 0.88f;

        [Tooltip("Haptic intensity on full pinch (%)")]
        [Range(50, 100)]
        public int fullPinchIntensity = 100;

        [Header("Hand State & Anti-Ghost Gating")]
        [Tooltip("Pinch threshold below which hand is considered relaxed (cuts haptic to 0%)")]
        [Range(0.15f, 0.45f)]
        public float releasePinchThreshold = 0.32f;

        [Tooltip("Fingertip distance (cm) exceeding this indicates open hand")]
        [Range(3.5f, 7.0f)]
        public float openDistanceCmThreshold = 4.5f;

        [Header("Grip Stability & Latch")]
        [Tooltip("Minimum hold floor intensity (%) while gripping to prevent dropouts")]
        [Range(20, 60)]
        public int gripHoldFloorIntensity = 35;

        [Header("Hysteresis Thresholds (Robot Force Sensor)")]
        [Tooltip("Minimum grip force (N) to trigger contact haptic")]
        [Range(0.2f, 4.0f)]
        public float startForceN = 0.8f;

        [Tooltip("Release force threshold (N) for hysteresis")]
        [Range(0.05f, 2.0f)]
        public float stopForceN = 0.3f;

        [Tooltip("Maximum grip force (N) for 100% intensity")]
        [Range(5.0f, 50.0f)]
        public float maxForceN = 25.0f;

        [Tooltip("Minimum contact intensity (%)")]
        [Range(5, 50)]
        public int minIntensity = 15;

        [Tooltip("Maximum contact intensity (%)")]
        [Range(50, 100)]
        public int maxIntensity = 100;

        [Header("Smoothing Envelope")]
        [Tooltip("Attack speed for grip haptic onset")]
        [Range(100f, 800f)]
        public float attackSpeed = 450f;

        [Tooltip("Release speed for grip haptic decay")]
        [Range(50f, 400f)]
        public float releaseSpeed = 180f;

        [Header("Active Motors on Glove")]
        [Tooltip("Thumb Tip")]
        public bool vibrateThumb = true;

        [Tooltip("Index Tip")]
        public bool vibrateIndex = true;

        [Tooltip("Middle Tip")]
        public bool vibrateMiddle = false;

        [Tooltip("Ring Tip")]
        public bool vibrateRing = false;

        [Tooltip("Pinky Tip")]
        public bool vibratePinky = false;

        [Tooltip("Palm")]
        public bool vibratePalm = false;

        [Header("Safety Boundary Warning (Wrist Motor #5)")]
        [Tooltip("Enable wrist haptic warning near caution/danger boundaries")]
        public bool enableBoundaryWristWarning = true;

        [Tooltip("Caution threshold for rhythmic pulse warning")]
        [Range(0.10f, 0.50f)]
        public float yellowCautionThreshold = 0.18f;

        [Tooltip("Danger threshold for urgent continuous buzz")]
        [Range(0.50f, 0.90f)]
        public float redDangerThreshold = 0.65f;

        [Tooltip("Minimum wrist alert intensity (%)")]
        [Range(10, 50)]
        public int minWristWarningIntensity = 30;

        [Tooltip("Maximum wrist alert intensity (%)")]
        [Range(60, 100)]
        public int maxWristWarningIntensity = 100;

        [Tooltip("Enable 4 Hz rhythmic pulse in caution zone")]
        public bool enableYellowPulsing = true;

        [Header("Joint Rotation Limit Warning (Wrist Motor #5)")]
        [Tooltip("Enable wrist alert near joint rotation limits")]
        public bool enableRotationLimitWristWarning = true;
        public bool isNearRotationLimitStatus = false;

        [Header("Haptic Timing")]
        [Tooltip("Haptic frame duration (ms)")]
        [Range(50, 250)]
        public int pulseDurationMs = 120;

        [Tooltip("Haptic update rate (Hz)")]
        [Range(15, 50)]
        public int updateRateHz = 25;

        [Header("Telemetry (Read Only)")]
        public float currentGripForceN = 0f;
        public int currentMotorIntensity = 0;
        public float currentBoundaryRatio = 0f;
        public int currentWristIntensity = 0;
        public bool isNearYellowZone = false;
        public bool isNearRedZone = false;
        public bool isVibrating = false;
        public bool isHapticActive = false;
        public bool isFullPinchActive = false;
        public bool isGripLatchedStatus = false;
        public bool isHandRelaxedOpenStatus = false;
        public float userPinchPercent = 0f;
        public float userTipDistanceCm = 0f;

        [Header("Testing")]
        [Tooltip("Test grip haptic in Editor")]
        public bool testVibration = false;
        [Range(0, 100)]
        public int testIntensity = 50;

        [Tooltip("Test wrist alert in Editor")]
        public bool testWristWarning = false;
        [Range(0, 100)]
        public int testWristIntensity = 80;

        private float nextSendTime = 0f;
        private bool wasVibrating = false;
        private bool isGripLatched = false;
        private float currentSmoothedIntensity = 0f;
        private float currentSmoothedWristIntensity = 0f;
        private readonly int[] motorValues = new int[8];

        private void Awake()
        {
            FindReferences();
            EnsureBhapticsInitialized();
        }

        private void OnEnable()
        {
            FindReferences();
        }

        private void FindReferences()
        {
            if (gripperBridge == null)
            {
                gripperBridge = GetComponent<GripperBridge>();
                if (gripperBridge == null) gripperBridge = FindAnyObjectByType<GripperBridge>();
            }

            if (safetyBoundary == null)
            {
                safetyBoundary = GetComponent<SafetyBoundaryVisualizer>();
                if (safetyBoundary == null) safetyBoundary = FindAnyObjectByType<SafetyBoundaryVisualizer>();
            }

            if (vrHandMover == null)
            {
                vrHandMover = GetComponent<VRHandMover>();
                if (vrHandMover == null) vrHandMover = FindAnyObjectByType<VRHandMover>();
            }

            if (pinchDetector == null)
            {
                pinchDetector = GetComponent<PinchDetector>();
                if (pinchDetector == null && gripperBridge != null) pinchDetector = gripperBridge.pinchDetector;
                if (pinchDetector == null) pinchDetector = FindAnyObjectByType<PinchDetector>();
            }

            if (ovrHand == null)
            {
                ovrHand = GetComponent<OVRHand>();
                if (ovrHand == null && pinchDetector != null) ovrHand = pinchDetector.ovrHand;
                if (ovrHand == null) ovrHand = FindAnyObjectByType<OVRHand>();
            }
        }

        private void EnsureBhapticsInitialized()
        {
            try
            {
                if (!BhapticsLibrary.IsBhapticsAvailable(true))
                {
                    BhapticsLibrary.Initialize("ur3e_vr_bridge", "ur3e_vr_bridge", "{}");
                    BhapticsLibrary.IsBhapticsAvailableForce(true);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[BhapticsGloveFeedback] Init note: " + ex.Message);
            }
        }

        private void Update()
        {
            if (gripperBridge == null || safetyBoundary == null || vrHandMover == null || pinchDetector == null)
            {
                FindReferences();
            }

            EnsureBhapticsInitialized();

            // =========================================================================
            // PART 1: GRIPPER FORCE HAPTIC & UNCONDITIONAL FULL PINCH
            // =========================================================================
            if (gripperBridge != null)
            {
                currentGripForceN = gripperBridge.actualGripForce;
            }

            // Read live finger distance and pinch from VR Hand Tracking
            float pinchNormalized = 0f;
            float tipDistCm = 999f;
            bool isHandTracked = false;

            if (pinchDetector != null && pinchDetector.isTracked)
            {
                isHandTracked = true;
                pinchNormalized = pinchDetector.normalizedPinch;
                tipDistCm = pinchDetector.rawDistanceCm;
            }
            else if (ovrHand != null && ovrHand.IsTracked)
            {
                isHandTracked = true;
                pinchNormalized = ovrHand.GetFingerPinchStrength(OVRHand.HandFinger.Index);
                tipDistCm = (ovrHand.GetFingerIsPinching(OVRHand.HandFinger.Index)) ? 1.8f : 8.0f;
            }
            else
            {
                // Fallback for Meta Touch Controller
                float trig = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, OVRInput.Controller.RTouch);
                pinchNormalized = trig;
                tipDistCm = Mathf.Lerp(10.0f, 1.8f, trig);
                isHandTracked = (trig > 0.05f || OVRInput.Get(OVRInput.Button.Any, OVRInput.Controller.RTouch));
            }

            userPinchPercent = pinchNormalized * 100f;
            userTipDistanceCm = tipDistCm;

            // 1. Unconditional Full Pinch check
            bool isFullPinch = false;
            if (enableUnconditionalFullPinch)
            {
                if (pinchDetector != null && pinchDetector.isTracked)
                {
                    if (tipDistCm > 0f && tipDistCm <= fullPinchDistanceCmThreshold)
                    {
                        isFullPinch = true;
                    }
                    else if (pinchNormalized >= fullNormalizedPinchThreshold)
                    {
                        isFullPinch = true;
                    }
                }

                if (!isFullPinch && ovrHand != null && ovrHand.IsTracked)
                {
                    if (ovrHand.GetFingerIsPinching(OVRHand.HandFinger.Index) ||
                        ovrHand.GetFingerPinchStrength(OVRHand.HandFinger.Index) >= fullNormalizedPinchThreshold)
                    {
                        isFullPinch = true;
                    }
                }

                if (!isFullPinch)
                {
                    float trig = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, OVRInput.Controller.RTouch);
                    if (trig >= fullTriggerThreshold)
                    {
                        isFullPinch = true;
                    }
                }

                if (!isFullPinch && gripperBridge != null && gripperBridge.isGripperLocked)
                {
                    isFullPinch = true;
                }
            }
            isFullPinchActive = isFullPinch;

            // 2. Relaxed hand gating (cuts ghost haptic to 0%)
            bool isHandRelaxedOpen = false;
            if (!isFullPinch)
            {
                if (pinchDetector != null && pinchDetector.isTracked)
                {
                    if (pinchNormalized < releasePinchThreshold)
                    {
                        isHandRelaxedOpen = true;
                    }
                    else if (tipDistCm > openDistanceCmThreshold)
                    {
                        isHandRelaxedOpen = true;
                    }
                }
                else if (ovrHand != null && ovrHand.IsTracked)
                {
                    if (!ovrHand.GetFingerIsPinching(OVRHand.HandFinger.Index) &&
                        ovrHand.GetFingerPinchStrength(OVRHand.HandFinger.Index) < 0.22f)
                    {
                        isHandRelaxedOpen = true;
                    }
                }
                else
                {
                    float trig = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, OVRInput.Controller.RTouch);
                    if (trig < 0.20f)
                    {
                        if (gripperBridge != null && gripperBridge.actualGripperPos <= 35)
                        {
                            isHandRelaxedOpen = true;
                        }
                    }
                }
            }
            isHandRelaxedOpenStatus = isHandRelaxedOpen;

            // 3. Target grip intensity calculation
            int targetGripIntensity = 0;

            if (testVibration)
            {
                targetGripIntensity = testIntensity;
                isHapticActive = true;
            }
            else if (isFullPinch)
            {
                // Full pinch: 100% unconditional intensity
                targetGripIntensity = fullPinchIntensity;
                isHapticActive = true;
                isGripLatched = true;
            }
            else if (isHandRelaxedOpen)
            {
                // Relaxed hand: cut haptic to 0 immediately
                targetGripIntensity = 0;
                isHapticActive = false;
                isGripLatched = false;
                currentSmoothedIntensity = 0f; // Cut haptic immediately
            }
            else
            {
                // Gripping state: Grip latch & hold floor
                if (!isGripLatched)
                {
                    // Contact engagement
                    if (currentGripForceN >= startForceN)
                    {
                        isGripLatched = true;
                    }
                    else if (pinchNormalized >= 0.55f && currentGripForceN >= stopForceN)
                    {
                        isGripLatched = true;
                    }
                }

                if (isGripLatched)
                {
                    isHapticActive = true;
                    float t = Mathf.Clamp01((currentGripForceN - stopForceN) / Mathf.Max(0.1f, maxForceN - stopForceN));
                    int calculatedIntensity = Mathf.RoundToInt(Mathf.Lerp(minIntensity, maxIntensity, t));

                    // Grip hold floor: maintain minimum vibration while holding
                    targetGripIntensity = Mathf.Max(gripHoldFloorIntensity, calculatedIntensity);
                }
                else
                {
                    isHapticActive = false;
                    targetGripIntensity = 0;
                }
            }
            isGripLatchedStatus = isGripLatched;

            // 4. Smooth envelope for grip haptic
            if (targetGripIntensity > currentSmoothedIntensity)
            {
                currentSmoothedIntensity = Mathf.MoveTowards(currentSmoothedIntensity, targetGripIntensity, attackSpeed * Time.deltaTime);
            }
            else
            {
                currentSmoothedIntensity = Mathf.MoveTowards(currentSmoothedIntensity, targetGripIntensity, releaseSpeed * Time.deltaTime);
            }

            int finalGripIntensity = Mathf.RoundToInt(currentSmoothedIntensity);
            currentMotorIntensity = finalGripIntensity;

            // =========================================================================
            // PART 2: SAFETY BOUNDARY WARNING HAPTIC (Wrist Motor #5)
            // =========================================================================
            float maxRatio = 0f;

            // 1. Safety boundary approach alpha check
            if (safetyBoundary != null)
            {
                maxRatio = Mathf.Max(maxRatio, safetyBoundary.outerApproachAlpha);
                maxRatio = Mathf.Max(maxRatio, safetyBoundary.innerApproachAlpha);
            }

            // 2. Python safety ratios check
            if (gripperBridge != null)
            {
                if (gripperBridge.safetyOuterRatio > 0.72f)
                {
                    float r = Mathf.InverseLerp(0.72f, 0.98f, gripperBridge.safetyOuterRatio);
                    maxRatio = Mathf.Max(maxRatio, r);
                }
                if (gripperBridge.safetyInnerRatio > 0.72f)
                {
                    float r = Mathf.InverseLerp(0.72f, 0.98f, gripperBridge.safetyInnerRatio);
                    maxRatio = Mathf.Max(maxRatio, r);
                }
                if (gripperBridge.safetyCylinderRatio > 0.50f)
                {
                    float r = Mathf.InverseLerp(0.50f, 0.90f, gripperBridge.safetyCylinderRatio);
                    maxRatio = Mathf.Max(maxRatio, r);
                }
            }

            // 2b. Joint rotation limit check
            if (gripperBridge != null && gripperBridge.safetyRotLimitRatio > 0.0f)
            {
                float rotRatio = Mathf.Clamp01(gripperBridge.safetyRotLimitRatio);
                maxRatio = Mathf.Max(maxRatio, rotRatio);
            }

            // 3. VRHandMover slow zone check
            if (vrHandMover != null && vrHandMover.isAnchorSet)
            {
                float d = vrHandMover.currentTargetPos.magnitude;
                float outerSlowStart = vrHandMover.maxBaseReach - vrHandMover.outerSlowZone;
                if (d > outerSlowStart)
                {
                    float r = Mathf.Clamp01((d - outerSlowStart) / Mathf.Max(0.005f, vrHandMover.outerSlowZone));
                    maxRatio = Mathf.Max(maxRatio, r);
                }

                float innerSlowStart = vrHandMover.minBaseReach + vrHandMover.innerSlowZone;
                if (d < innerSlowStart)
                {
                    float r = Mathf.Clamp01((innerSlowStart - d) / Mathf.Max(0.005f, vrHandMover.innerSlowZone));
                    maxRatio = Mathf.Max(maxRatio, r);
                }
            }

            currentBoundaryRatio = maxRatio;
            isNearYellowZone = maxRatio >= yellowCautionThreshold && maxRatio < redDangerThreshold;
            isNearRedZone = maxRatio >= redDangerThreshold;

            int targetWristIntensity = 0;

            if (testWristWarning)
            {
                targetWristIntensity = testWristIntensity;
            }
            else if (enableBoundaryWristWarning && maxRatio >= yellowCautionThreshold)
            {
                float t = Mathf.InverseLerp(yellowCautionThreshold, 1.0f, maxRatio);
                int baseIntensity = Mathf.RoundToInt(Mathf.Lerp(minWristWarningIntensity, maxWristWarningIntensity, t));

                if (isNearRedZone)
                {
                    // Danger zone: urgent buzz (20 Hz)
                    float rapidPulse = (Mathf.Sin(Time.time * 20f * Mathf.PI) > -0.2f) ? 1.0f : 0.65f;
                    targetWristIntensity = Mathf.RoundToInt(baseIntensity * rapidPulse);
                }
                else
                {
                    // Caution zone: rhythmic pulse (4 Hz)
                    if (enableYellowPulsing)
                    {
                        bool pulseOn = Mathf.Sin(Time.time * 8f * Mathf.PI) > 0.05f;
                        targetWristIntensity = pulseOn ? baseIntensity : 0;
                    }
                    else
                    {
                        targetWristIntensity = baseIntensity;
                    }
                }
            }

            // Joint rotation limit wrist alert
            int rotWristIntensity = 0;
            if (enableRotationLimitWristWarning && enableBoundaryWristWarning && gripperBridge != null && gripperBridge.safetyRotLimitRatio > 0.05f)
            {
                isNearRotationLimitStatus = true;
                float tRot = Mathf.Clamp01((gripperBridge.safetyRotLimitRatio - 0.05f) / 0.95f);
                int baseRotInt = Mathf.RoundToInt(Mathf.Lerp(minWristWarningIntensity, maxWristWarningIntensity, tRot));
                if (gripperBridge.safetyRotLimitRatio >= redDangerThreshold)
                {
                    // Rotation limit danger (< 10 deg): urgent buzz (22 Hz)
                    float rapidPulse = (Mathf.Sin(Time.time * 22f * Mathf.PI) > -0.2f) ? 1.0f : 0.70f;
                    rotWristIntensity = Mathf.RoundToInt(baseRotInt * rapidPulse);
                }
                else
                {
                    // Rotation limit caution (10-30 deg): rhythmic pulse (5 Hz)
                    if (enableYellowPulsing)
                    {
                        bool pulseOn = Mathf.Sin(Time.time * 10f * Mathf.PI) > 0.05f;
                        rotWristIntensity = pulseOn ? baseRotInt : 0;
                    }
                    else
                    {
                        rotWristIntensity = baseRotInt;
                    }
                }
            }
            else
            {
                isNearRotationLimitStatus = false;
            }

            targetWristIntensity = Mathf.Max(targetWristIntensity, rotWristIntensity);

            // Smooth envelope for wrist motor
            if (targetWristIntensity > currentSmoothedWristIntensity)
            {
                currentSmoothedWristIntensity = Mathf.MoveTowards(currentSmoothedWristIntensity, targetWristIntensity, 450f * Time.deltaTime);
            }
            else
            {
                currentSmoothedWristIntensity = Mathf.MoveTowards(currentSmoothedWristIntensity, targetWristIntensity, 280f * Time.deltaTime);
            }

            int finalWristIntensity = Mathf.RoundToInt(currentSmoothedWristIntensity);
            currentWristIntensity = finalWristIntensity;

            // =========================================================================
            // PART 3: DISPATCH HAPTIC PACKET TO BHAPTICS
            // =========================================================================
            isVibrating = (finalGripIntensity > 0 || finalWristIntensity > 0);

            float interval = 1f / Mathf.Max(1, updateRateHz);
            if (Time.time >= nextSendTime)
            {
                nextSendTime = Time.time + interval;

                if (isVibrating)
                {
                    SendGloveHaptic(finalGripIntensity, finalWristIntensity);
                    wasVibrating = true;
                }
                else if (wasVibrating)
                {
                    StopGloveHaptic();
                    wasVibrating = false;
                }
            }
        }

        private void SendGloveHaptic(int gripIntensity, int wristIntensity)
        {
            // bHaptics Glove 8 motors:
            // 0: Thumb, 1: Index, 2: Middle, 3: Ring, 4: Pinky, 5: Wrist, 6: Palm Thumb, 7: Palm Pinky
            for (int i = 0; i < 8; i++) motorValues[i] = 0;

            // Finger motors (Gripper force)
            if (vibrateThumb) motorValues[0] = gripIntensity;
            if (vibrateIndex) motorValues[1] = gripIntensity;
            if (vibrateMiddle) motorValues[2] = gripIntensity;
            if (vibrateRing) motorValues[3] = gripIntensity;
            if (vibratePinky) motorValues[4] = gripIntensity;
            if (vibratePalm)
            {
                motorValues[6] = gripIntensity;
                motorValues[7] = gripIntensity;
            }

            // Motor 5: Wrist boundary alert
            if (enableBoundaryWristWarning)
            {
                motorValues[5] = wristIntensity;
            }

            try
            {
                BhapticsLibrary.PlayMotors((int)glovePosition, motorValues, pulseDurationMs);

                // Secondary forearm device sync (if connected)
                if (wristIntensity > 0)
                {
                    int forearmPos = (glovePosition == PositionType.GloveR) ? (int)PositionType.ForearmR : (int)PositionType.ForearmL;
                    int[] forearmMotors = new int[6];
                    for (int f = 0; f < 6; f++) forearmMotors[f] = wristIntensity;
                    BhapticsLibrary.PlayMotors(forearmPos, forearmMotors, pulseDurationMs);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[BhapticsGloveFeedback] PlayMotors error: " + ex.Message);
            }
        }

        /// <summary>
        /// Short haptic click pulse for UI button confirmation.
        /// </summary>
        public void TriggerClickPulse(int intensity = 60, int durationMs = 60)
        {
            try
            {
                int[] clickMotors = new int[8];
                clickMotors[1] = intensity; // Index finger
                clickMotors[5] = intensity; // Wrist
                BhapticsLibrary.PlayMotors((int)glovePosition, clickMotors, durationMs);
            }
            catch { }
        }

        private void StopGloveHaptic()
        {
            for (int i = 0; i < 8; i++) motorValues[i] = 0;
            try
            {
                BhapticsLibrary.PlayMotors((int)glovePosition, motorValues, 20);
                int forearmPos = (glovePosition == PositionType.GloveR) ? (int)PositionType.ForearmR : (int)PositionType.ForearmL;
                int[] forearmMotors = new int[6];
                BhapticsLibrary.PlayMotors(forearmPos, forearmMotors, 20);
            }
            catch { }
        }

        private void OnDisable()
        {
            StopGloveHaptic();
        }

        private void OnDestroy()
        {
            StopGloveHaptic();
        }
    }
}