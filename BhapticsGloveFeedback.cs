using System;
using UnityEngine;
using Bhaptics.SDK2;

namespace UR3eVR
{
    /// <summary>
    /// BhapticsGloveFeedback:
    /// 1. ระบบสั่นตอบสนองแรงบีบ Gripper (Finger Haptic Feedback):
    ///    - โหมดไม่มีเงื่อนไข (Unconditional Full Pinch): เมื่อจีบนิ้วสุดหรือปลายนิ้วชี้ชนกับนิ้วโป้งใน VR สั่งสั่น 100% เต็มพิกัดทันที
    ///    - โหมดตัดการสั่นหลอน (Relaxed Hand Gating): เมื่อมืออยู่ในท่าปล่อยสบาย/อ้านิ้ว จะตัดการสั่นเป็น 0 ทันที ป้องกันแรงสั่นหลอนจากการขยับแขนกลในอากาศ
    ///    - โหมดล็อกความเสถียร (Grip Contact Latch & Floor): เมื่อจับวัตถุ ระบบจะรักษาระดับการสั่นคงที่ ไม่ขาดๆ หายๆ แม้เซนเซอร์แรงจะกระเพื่อม
    /// 2. ระบบสั่นเตือนขอบเขตความปลอดภัยที่ข้อมือ (Wrist Motor #5):
    ///    - โซนเหลือง (Caution): สั่นเตือนเป็นจังหวะชีพจร 4 Hz
    ///    - โซนแดง (Danger Limit): สั่นเตือนถี่และแรง 20 Hz (Urgent Buzz)
    /// </summary>
    public class BhapticsGloveFeedback : MonoBehaviour
    {
        [Header("Target Device")]
        [Tooltip("เลือกถุงมือข้างที่จะให้สั่น (ค่าเริ่มต้นคือมือขวา GloveR)")]
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

        [Header("Unconditional Full Pinch (จีบนิ้วสุด / นิ้วชนกัน สั่น 100% ทันที)")]
        [Tooltip("เปิดใช้งานการสั่นเต็มที่ 100% ไม่มีเงื่อนไขเมื่อนิ้วชี้ชนกับนิ้วโป้ง")]
        public bool enableUnconditionalFullPinch = true;

        [Tooltip("ระยะห่างระหว่างปลายนิ้วชี้และนิ้วโป้ง (cm) ที่ถือว่านิ้วชนกันสนิท")]
        [Range(1.5f, 4.0f)]
        public float fullPinchDistanceCmThreshold = 2.6f;

        [Tooltip("ระดับการจีบนิ้ว Normalized Pinch (0.0 - 1.0) ที่ถือว่าจีบสุด")]
        [Range(0.70f, 0.98f)]
        public float fullNormalizedPinchThreshold = 0.85f;

        [Tooltip("ระดับ Trigger ของ Controller ที่ถือว่ากดสุด")]
        [Range(0.70f, 1.0f)]
        public float fullTriggerThreshold = 0.88f;

        [Tooltip("ความแรงการสั่นเมื่อจีบนิ้วสุด (%)")]
        [Range(50, 100)]
        public int fullPinchIntensity = 100;

        [Header("Hand State & Anti-Ghost Gating (ตัดการสั่นหลอนเมื่อไม่อยู่ในท่าจับ)")]
        [Tooltip("ระดับการจีบต่ำกว่าค่านี้ถือว่ามืออ้า/ปล่อยสบาย (ตัดแรงสั่นจากแขนกลขยับในอากาศ 100%)")]
        [Range(0.15f, 0.45f)]
        public float releasePinchThreshold = 0.32f;

        [Tooltip("ระยะห่างระหว่างนิ้ว (cm) เกินค่านี้ถือว่าอ้านิ้วชัดเจน")]
        [Range(3.5f, 7.0f)]
        public float openDistanceCmThreshold = 4.5f;

        [Header("Grip Stability & Latch (แก้ปัญหาสั่นขาดๆ หายๆ ขณะจับวัตถุ)")]
        [Tooltip("ระดับแรงสั่นพยุงขั้นต่ำ (%) ขณะกำลังหนีบจับวัตถุ ป้องกันแรงสั่นวูบหายเมื่อเซนเซอร์ตก")]
        [Range(20, 60)]
        public int gripHoldFloorIntensity = 35;

        [Header("Hysteresis Thresholds (Robot Force Sensor)")]
        [Tooltip("แรงบีบขั้นต่ำ (N) ที่จะเริ่มเปิดระบบสั่นจากการสัมผัสวัตถุจริง")]
        [Range(0.2f, 4.0f)]
        public float startForceN = 0.8f;

        [Tooltip("แรงบีบขั้นต่ำ (N) ที่จะคลายระบบสั่น (Hysteresis)")]
        [Range(0.05f, 2.0f)]
        public float stopForceN = 0.3f;

        [Tooltip("แรงบีบสูงสุด (N) ที่จะให้ถุงมือสั่นเต็ม 100%")]
        [Range(5.0f, 50.0f)]
        public float maxForceN = 25.0f;

        [Tooltip("ความแรงการสั่นต่ำสุด (%) เมื่อเริ่มแตะสัมผัสวัตถุ")]
        [Range(5, 50)]
        public int minIntensity = 15;

        [Tooltip("ความแรงการสั่นสูงสุด (%)")]
        [Range(50, 100)]
        public int maxIntensity = 100;

        [Header("Smoothing Envelope (ความต่อเนื่องและนุ่มนวล)")]
        [Tooltip("ความเร็วในการพุ่งของแรงสั่นเมื่อบีบ (Attack speed - ฉับพลัน คมชัด)")]
        [Range(100f, 800f)]
        public float attackSpeed = 450f;

        [Tooltip("ความเร็วในการลดแรงสั่นเมื่อคลายมือ (Release speed)")]
        [Range(50f, 400f)]
        public float releaseSpeed = 180f;

        [Header("Active Motors on Glove (เลือกนิ้วที่ต้องการให้สั่น)")]
        [Tooltip("นิ้วโป้ง (Thumb Tip)")]
        public bool vibrateThumb = true;

        [Tooltip("นิ้วชี้ (Index Tip)")]
        public bool vibrateIndex = true;

        [Tooltip("นิ้วกลาง (Middle Tip)")]
        public bool vibrateMiddle = false;

        [Tooltip("นิ้วนาง (Ring Tip)")]
        public bool vibrateRing = false;

        [Tooltip("นิ้วก้อย (Pinky Tip)")]
        public bool vibratePinky = false;

        [Tooltip("ฝ่ามือ (Palm)")]
        public bool vibratePalm = false;

        [Header("Safety Boundary Warning (Wrist Motor #5)")]
        [Tooltip("เปิดใช้งานการสั่นเตือนที่ข้อมือเมื่อเข้าใกล้วงเหลืองหรือวงแดง")]
        public bool enableBoundaryWristWarning = true;

        [Tooltip("ระดับเตือนสีเหลือง (Caution Threshold): เริ่มสั่นเตือนเป็นจังหวะเมื่อเข้าใกล้ขอบเขต")]
        [Range(0.10f, 0.50f)]
        public float yellowCautionThreshold = 0.18f;

        [Tooltip("ระดับเตือนสีแดง (Danger Threshold): สั่นเตือนแรงและถี่เมื่อใกล้ชนขอบเขต")]
        [Range(0.50f, 0.90f)]
        public float redDangerThreshold = 0.65f;

        [Tooltip("ความแรงการสั่นต่ำสุดที่ข้อมือ (%) เมื่อเริ่มเข้าโซนเหลือง")]
        [Range(10, 50)]
        public int minWristWarningIntensity = 30;

        [Tooltip("ความแรงการสั่นสูงสุดที่ข้อมือ (%) เมื่อเข้าใกล้วงแดง")]
        [Range(60, 100)]
        public int maxWristWarningIntensity = 100;

        [Tooltip("เปิดระบบสั่นเป็นจังหวะชีพจร (Rhythmic Pulse 4 Hz) ในโซนเหลือง")]
        public bool enableYellowPulsing = true;

        [Header("Joint Rotation Limit Warning (Wrist Motor #5)")]
        [Tooltip("เปิดใช้งานการสั่นเตือนที่ข้อมือเมื่อหมุนใกล้ครบรอบ/สุดขีดจำกัด (Rotation Limit Warning)")]
        public bool enableRotationLimitWristWarning = true;
        public bool isNearRotationLimitStatus = false;

        [Header("Haptic Timing")]
        [Tooltip("ระยะเวลาการสั่นต่อรอบ (ms)")]
        [Range(50, 250)]
        public int pulseDurationMs = 120;

        [Tooltip("ความถี่ในการส่งสัญญาณ haptic (Hz)")]
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
        [Tooltip("ติ๊กถูกเพื่อทดสอบสั่นแรงบีบของถุงมือทันทีใน Editor")]
        public bool testVibration = false;
        [Range(0, 100)]
        public int testIntensity = 50;

        [Tooltip("ติ๊กถูกเพื่อทดสอบสั่นเตือนที่ข้อมือทันทีใน Editor")]
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

            // อ่านค่าระยะนิ้วและระดับการจีบสดจากเซนเซอร์ VR Hand Tracking
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
                // Fallback กรณีใช้ Meta Touch Controller
                float trig = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, OVRInput.Controller.RTouch);
                pinchNormalized = trig;
                tipDistCm = Mathf.Lerp(10.0f, 1.8f, trig);
                isHandTracked = (trig > 0.05f || OVRInput.Get(OVRInput.Button.Any, OVRInput.Controller.RTouch));
            }

            userPinchPercent = pinchNormalized * 100f;
            userTipDistanceCm = tipDistCm;

            // 1. ตรวจสอบเงื่อนไข "จีบนิ้วสุดหรือนิ้วชี้ชนกับนิ้วโป้ง" (Unconditional Full Pinch)
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

            // 2. ตรวจสอบเงื่อนไข "มืออ้า/ปล่อยสบาย" (Relaxed Hand Gating - ตัดการสั่นหลอน 100%)
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

            // 3. กำหนดความแรงของมอเตอร์นิ้วมือ (Target Grip Intensity)
            int targetGripIntensity = 0;

            if (testVibration)
            {
                targetGripIntensity = testIntensity;
                isHapticActive = true;
            }
            else if (isFullPinch)
            {
                // [คำสั่งพิเศษของผู้ใช้]: จีบนิ้วสุดหรือนิ้วชี้ชนกับนิ้วโป้ง ให้สั่นแบบไม่มีเงื่อนไข (100% เต็มที่ ทันที!)
                targetGripIntensity = fullPinchIntensity;
                isHapticActive = true;
                isGripLatched = true;
            }
            else if (isHandRelaxedOpen)
            {
                // [แก้ปัญหาสั่นหลอน]: เมื่อมืออ้า/ปล่อยสบาย จะตัดการสั่นเป็น 0 ทันที ไม่ว่าแขนกลจะขยับแรงแค่ไหนในอากาศ
                targetGripIntensity = 0;
                isHapticActive = false;
                isGripLatched = false;
                currentSmoothedIntensity = 0f; // ตัดการสั่นทันที ไร้เสียงฮัมค้าง
            }
            else
            {
                // [แก้ปัญหาสั่นขาดๆ หายๆ]: อยู่ในสถานะกำลังหนีบ/จับวัตถุ พร้อมระบบ Grip Latch & Hold Floor
                if (!isGripLatched)
                {
                    // เงื่อนไขเริ่มล็อกการสั่น (Contact Engagement)
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

                    // เสริมแรงสั่นพยุง (Grip Hold Floor): ตราบใดที่ยังไม่คลายนิ้ว สั่นต่อเนื่องไม่ต่ำกว่า floor intensity
                    targetGripIntensity = Mathf.Max(gripHoldFloorIntensity, calculatedIntensity);
                }
                else
                {
                    isHapticActive = false;
                    targetGripIntensity = 0;
                }
            }
            isGripLatchedStatus = isGripLatched;

            // 4. Smooth Envelope สำหรับแรงบีบนิ้ว (ตอบสนองฉับพลันเมื่อหนีบ และชะลอนุ่มนวลเมื่อปล่อย)
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

            // 1. ตรวจสอบค่า Approach Alpha จาก SafetyBoundaryVisualizer
            if (safetyBoundary != null)
            {
                maxRatio = Mathf.Max(maxRatio, safetyBoundary.outerApproachAlpha);
                maxRatio = Mathf.Max(maxRatio, safetyBoundary.innerApproachAlpha);
            }

            // 2. ตรวจสอบ Safety Ratios ที่ส่งมาจาก Python
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

            // 2b. ตรวจสอบ Joint Rotation Limit (เมื่อหมุนใกล้ครบรอบ จาก Python gripperBridge)
            if (gripperBridge != null && gripperBridge.safetyRotLimitRatio > 0.0f)
            {
                float rotRatio = Mathf.Clamp01(gripperBridge.safetyRotLimitRatio);
                maxRatio = Mathf.Max(maxRatio, rotRatio);
            }

            // 3. ตรวจสอบระยะ Slow Zone จาก VRHandMover
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
                    // 🔴 โซนใกล้วงแดง (Danger / Boundary Limit): สั่นแรงและถี่ต่อเนื่อง (Urgent Buzz 20 Hz)
                    float rapidPulse = (Mathf.Sin(Time.time * 20f * Mathf.PI) > -0.2f) ? 1.0f : 0.65f;
                    targetWristIntensity = Mathf.RoundToInt(baseIntensity * rapidPulse);
                }
                else
                {
                    // 🟡 โซนใกล้วงเหลือง (Caution / Warning): สั่นเป็นจังหวะชีพจรเตือน (Rhythmic Pulse 4 Hz)
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

            // ตรวจสอบแรงสั่นเตือนข้อมือจากการหมุนใกล้ครบรอบโดยเฉพาะ (ยิ่งหมุนต่อ ยิ่งสั่นเตือนที่ข้อมือแรงขึ้น)
            int rotWristIntensity = 0;
            if (enableRotationLimitWristWarning && enableBoundaryWristWarning && gripperBridge != null && gripperBridge.safetyRotLimitRatio > 0.05f)
            {
                isNearRotationLimitStatus = true;
                float tRot = Mathf.Clamp01((gripperBridge.safetyRotLimitRatio - 0.05f) / 0.95f);
                int baseRotInt = Mathf.RoundToInt(Mathf.Lerp(minWristWarningIntensity, maxWristWarningIntensity, tRot));
                if (gripperBridge.safetyRotLimitRatio >= redDangerThreshold)
                {
                    // 🔴 หมุนใกล้ครบรอบมาก (< 10° เหลือ): สั่นเตือนฉุกเฉินถี่และแรง (Urgent Buzz 22 Hz)
                    float rapidPulse = (Mathf.Sin(Time.time * 22f * Mathf.PI) > -0.2f) ? 1.0f : 0.70f;
                    rotWristIntensity = Mathf.RoundToInt(baseRotInt * rapidPulse);
                }
                else
                {
                    // 🟡 เริ่มเข้าสู่โซนเตือน 10°-30°: สั่นชีพจรเตือนเป็นจังหวะ (Rhythmic Pulse 5 Hz)
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

            // Smooth Envelope สำหรับมอเตอร์ข้อมือ
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
            // มอเตอร์ 8 ตัวของ bHaptics Glove:
            // 0: Thumb, 1: Index, 2: Middle, 3: Ring, 4: Pinky, 5: Wrist, 6: Palm Thumb, 7: Palm Pinky
            for (int i = 0; i < 8; i++) motorValues[i] = 0;

            // นิ้วมือ (แรงบีบ Gripper)
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

            // มอเตอร์ 5: ข้อมือ (Wrist) - สั่นเตือนเข้าใกล้วงเหลือง/แดง!
            if (enableBoundaryWristWarning)
            {
                motorValues[5] = wristIntensity;
            }

            try
            {
                BhapticsLibrary.PlayMotors((int)glovePosition, motorValues, pulseDurationMs);

                // หากมีปลอกแขน (bHaptics Tactosy for Forearm/Wrist) ให้สั่งสั่นพร้อมกันด้วย
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
        /// สั่นสะเทือนแบบจังหวะสั้น (Haptic Click Pulse) ที่นิ้วชี้และข้อมือเพื่อยืนยันการกดปุ่ม UI
        /// </summary>
        public void TriggerClickPulse(int intensity = 60, int durationMs = 60)
        {
            try
            {
                int[] clickMotors = new int[8];
                clickMotors[1] = intensity; // นิ้วชี้
                clickMotors[5] = intensity; // ข้อมือ
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