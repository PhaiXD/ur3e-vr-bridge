using UnityEngine.UI;
using System.Collections;
using UnityEngine;

namespace UR3eVR
{
    /// <summary>
    /// SafetyBoundaryVisualizer: สร้างและควบคุมตาข่ายทรงกลม Holographic Safety Barrier ใน Unity
    /// - ย้ายจุดศูนย์กลางของ Barrier ทั้งหมดไปอยู่ที่จุด (0, 0, 0) ของหุ่นยนต์ (Robot Base Origin)
    /// - เพิ่ม Blue Marker สีฟ้าเรืองแสงที่จุด (0, 0, 0) ของหุ่นยนต์ พร้อมเส้นบอกทิศทางเชื่อมไปยัง Home Point
    /// - Outer Boundary (สีแดง): รัศมี 0.62m จาก Robot Base (0, 0, 0) เตือนเมื่อเอื้อมแขนออกไปไกลเกินระยะปลอดภัย
    /// - Inner Boundary (สีส้ม/แอมเบอร์): รัศมี 0.18m จาก Robot Base (0, 0, 0) เตือนเมื่อดึงมือเข้าใกล้ฐานหุ่นยนต์หรือลำตัวเกินไป
    /// </summary>
    public class SafetyBoundaryVisualizer : MonoBehaviour
    {
        [Header("References")]
        public UR3eStarterPoint starterPoint;
        public VRHandMover vrHandMover;
        public GripperBridge gripperBridge;
        public Transform handTransform;
        public Transform headTransform;

        [Header("Robot Base Marker (0, 0, 0)")]
        [Tooltip("เปิด/ปิด การแสดงผลจุด Base (0, 0, 0) สีฟ้า")]
        public bool showBaseMarker = true;
        [Tooltip("แสดงเส้น Laser เชื่อมระหว่าง Base (0, 0, 0) กับ Home Point")]
        public bool showBaseToHomeLine = true;
        public Color baseMarkerColor = new Color(0.0f, 0.75f, 1.0f, 0.95f); // Electric Cyan / Blue

        [Header("Outer Boundary (Too Far from Base - Red)")]
        [Tooltip("รัศมีทรงกลมขอบเขตภายนอกจาก Robot Base (0, 0, 0) (เมตร) ตามคู่มือ UR3e Robotiq 2F Reach ~700mm")]
        [Range(0.40f, 0.85f)]
        public float outerRadius = 0.72f;

        [Tooltip("ระยะห่างจากขอบเขตภายนอกที่เริ่มสว่างเตือน (เมตร) (เริ่มสว่างที่ 58 cm)")]
        [Range(0.05f, 0.25f)]
        public float outerWarnDistance = 0.10f;

        public Color outerColor = new Color(1.0f, 0.15f, 0.15f, 0.95f); // Red

        [Header("Inner Boundary (Too Close to Base - Orange)")]
        [Tooltip("รัศมีทรงกลมขอบเขตภายในจาก Robot Base (0, 0, 0) (เมตร) ตามคู่มือ UR3e TCP Self-Collision 210mm")]
        [Range(0.10f, 0.35f)]
        public float innerRadius = 0.14f;

        [Tooltip("ระยะห่างจากขอบเขตภายในที่เริ่มสว่างเตือน (เมตร) (เริ่มสว่างที่ 30 cm)")]
        [Range(0.04f, 0.20f)]
        public float innerWarnDistance = 0.06f;

        public Color innerColor = new Color(1.0f, 0.55f, 0.05f, 0.95f); // Amber / Orange

        [Header("Singularity Warning (Z-axis Magenta)")]
        public Color singularityColor = new Color(1.0f, 0.05f, 0.95f, 0.95f);

        [Header("Shader & Ambient Visibility (Build-Safe)")]
        [Tooltip("Shader สำหรับแสดงตาข่าย Barrier (หากเว้นว่างจะค้นหาอัตโนมัติหรือดึงจาก Resources)")]
        public Shader barrierShader;
        [Tooltip("ความโปร่งแสงต่ำสุดของโครงข่ายแม้ยังไม่เข้าใกล้ (0.05 = เห็นโครงข่ายจางๆ สวยงามตลอดเวลา, 0 = ซ่อนสนิท)")]
        [Range(0.0f, 0.30f)]
        public float minAmbientVisibility = 0.05f;

        [Header("Proximity Patch Settings")]
        [Tooltip("รัศมีวงสว่างเฉพาะจุดบนผิว Barrier รอบมือ (เมตร)")]
        [Range(0.15f, 0.60f)]
        public float focusSpotRadius = 0.38f;

        [Header("Live Status (Read Only)")]
        public Vector3 centerPointWorldPos = Vector3.zero;
        public Vector3 robotBaseWorldPos = Vector3.zero;
        public float distToBase = 0.0f;
        public float distToCenter = 0.0f;
        [Range(0f, 1f)] public float outerApproachAlpha = 0.0f;
        [Range(0f, 1f)] public float innerApproachAlpha = 0.0f;

        // Visual Objects for Safety Barriers
        private GameObject outerSphereObj;
        private GameObject innerSphereObj;
        private Material outerMat;
        private Material innerMat;
        private MeshRenderer outerRenderer;
        private MeshRenderer innerRenderer;

        // Visual Objects for Robot Base (0, 0, 0) Blue Marker
        private GameObject baseMarkerRoot;
        private GameObject baseCylinder;
        private GameObject baseCoreSphere;
        private GameObject baseRingObj;
        private GameObject baseLabelObj;
        private Canvas baseCanvas;
        private Text baseLabelText;
        private Image baseLabelBg;
        private Camera centerEyeCamera;
        private LineRenderer baseRingLine;
        private LineRenderer baseToHomeLine;
        private Material baseMat;
        private Material baseLineMat;

        private void Start()
        {
            FindReferences();
            CreateBaseMarker();
            CreateBarrierSpheres();
        }

        private void FindReferences()
        {
            if (starterPoint == null) starterPoint = FindAnyObjectByType<UR3eStarterPoint>();
            if (vrHandMover == null) vrHandMover = FindAnyObjectByType<VRHandMover>();
            if (gripperBridge == null) gripperBridge = FindAnyObjectByType<GripperBridge>();
            if (handTransform == null && vrHandMover != null) handTransform = vrHandMover.handTransform;
            if (headTransform == null && vrHandMover != null) headTransform = vrHandMover.headTransform;
            if (centerEyeCamera == null || !centerEyeCamera.gameObject.activeInHierarchy)
            {
                var cec = GameObject.Find("CenterEyeAnchor");
                if (cec != null) centerEyeCamera = cec.GetComponent<Camera>();
                if (centerEyeCamera == null && Camera.main != null) centerEyeCamera = Camera.main;
            }
            if (headTransform == null && centerEyeCamera != null) headTransform = centerEyeCamera.transform;
            if (headTransform == null && Camera.main != null) headTransform = Camera.main.transform;
        }

        /// <summary>
        /// สร้าง Blue Marker สีฟ้าที่จุดพิกัด (0, 0, 0) ของหุ่นยนต์ UR3e
        /// </summary>
        private void CreateBaseMarker()
        {
            Shader unlitShader = Shader.Find("UR3eVR/StereoUnlitColor");
            if (unlitShader == null) unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlitShader == null) unlitShader = Shader.Find("Sprites/Default");

            baseMat = new Material(unlitShader) { color = baseMarkerColor };
            baseMat.enableInstancing = true;

            baseLineMat = new Material(unlitShader) { color = new Color(0.0f, 0.85f, 1.0f, 0.80f) };
            baseLineMat.enableInstancing = true;

            baseMarkerRoot = new GameObject("UR3e_RobotBase_Marker_0_0");
            baseMarkerRoot.transform.position = Vector3.zero;
            baseMarkerRoot.transform.rotation = Quaternion.identity;

            // 1. ฐานทรงกระบอก Pedestal (จำลองแป้นยึดฐาน UR3e เส้นผ่านศูนย์กลาง 128 มม.)
            baseCylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            baseCylinder.name = "Base_Pedestal";
            baseCylinder.transform.SetParent(baseMarkerRoot.transform, false);
            baseCylinder.transform.localPosition = new Vector3(0f, -0.008f, 0f);
            baseCylinder.transform.localScale = new Vector3(0.128f, 0.008f, 0.128f);
            DestroyCollider(baseCylinder);
            baseCylinder.GetComponent<Renderer>().material = baseMat;

            // 2. วงแหวนเลเซอร์สีฟ้า 64 จุดรอบฐาน (Smooth circular ring)
            baseRingObj = new GameObject("Base_GuideRing");
            baseRingObj.transform.SetParent(baseMarkerRoot.transform, false);
            baseRingObj.transform.localPosition = new Vector3(0f, 0.001f, 0f);
            baseRingLine = baseRingObj.AddComponent<LineRenderer>();
            baseRingLine.useWorldSpace = false;
            baseRingLine.loop = true;
            baseRingLine.startWidth = 0.003f;
            baseRingLine.endWidth = 0.003f;
            int segs = 64;
            baseRingLine.positionCount = segs;
            float rBase = 0.068f; // รัศมี 6.8 ซม.
            for (int i = 0; i < segs; i++)
            {
                float ang = i * Mathf.PI * 2.0f / segs;
                baseRingLine.SetPosition(i, new Vector3(Mathf.Cos(ang) * rBase, 0f, Mathf.Sin(ang) * rBase));
            }
            baseRingLine.material = baseLineMat;

            // 3. Central Beacon Sphere (จุดกึ่งกลาง 0, 0, 0)
            baseCoreSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            baseCoreSphere.name = "Base_OriginBeacon";
            baseCoreSphere.transform.SetParent(baseMarkerRoot.transform, false);
            baseCoreSphere.transform.localPosition = Vector3.zero;
            baseCoreSphere.transform.localScale = Vector3.one * 0.035f;
            DestroyCollider(baseCoreSphere);
            baseCoreSphere.GetComponent<Renderer>().material = baseMat;

            // 4. เส้น Tether เชื่อมระหว่าง Base (0, 0, 0) กับ Home Point
            GameObject tetherObj = new GameObject("Base_To_Home_Laser");
            tetherObj.transform.SetParent(baseMarkerRoot.transform, false);
            baseToHomeLine = tetherObj.AddComponent<LineRenderer>();
            baseToHomeLine.useWorldSpace = true;
            baseToHomeLine.startWidth = 0.0025f;
            baseToHomeLine.endWidth = 0.0015f;
            baseToHomeLine.material = baseLineMat;
            baseToHomeLine.positionCount = 2;
            baseToHomeLine.startColor = new Color(0f, 0.8f, 1f, 0.75f);
            baseToHomeLine.endColor = new Color(0f, 1f, 0.5f, 0.75f);

            // 5. ป้ายข้อความ World Space Canvas ลอยเหนือฐาน (Stereo Single Pass Instanced - เห็นทั้ง 2 ตา)
            baseLabelObj = new GameObject("Base_Label_Canvas");
            baseLabelObj.transform.SetParent(baseMarkerRoot.transform, false);
            baseLabelObj.transform.localPosition = new Vector3(0f, 0.085f, 0f);

            baseCanvas = baseLabelObj.AddComponent<Canvas>();
            baseCanvas.renderMode = RenderMode.WorldSpace;
            if (centerEyeCamera != null) baseCanvas.worldCamera = centerEyeCamera;

            var scaler = baseLabelObj.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 10f;

            RectTransform canvasRT = baseLabelObj.GetComponent<RectTransform>();
            canvasRT.sizeDelta = new Vector2(360f, 130f);
            canvasRT.localScale = Vector3.one * 0.0008f;

            baseLabelBg = baseLabelObj.AddComponent<Image>();
            baseLabelBg.color = new Color(0.06f, 0.08f, 0.12f, 0.88f);

            var outline = baseLabelObj.AddComponent<Outline>();
            outline.effectColor = new Color(0.0f, 0.85f, 1.0f, 0.45f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);

            GameObject textObj = new GameObject("BaseLabelText");
            textObj.transform.SetParent(baseLabelObj.transform, false);
            RectTransform textRT = textObj.AddComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = new Vector2(8f, 6f);
            textRT.offsetMax = new Vector2(-8f, -6f);

            baseLabelText = textObj.AddComponent<Text>();
            baseLabelText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (baseLabelText.font == null) baseLabelText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            baseLabelText.fontSize = 20;
            baseLabelText.alignment = TextAnchor.MiddleCenter;
            baseLabelText.color = Color.white;
            baseLabelText.supportRichText = true;
            baseLabelText.text = "<b><color=#00E5FF>UR3e Base (0, 0, 0)</color></b>\n<size=15><color=#90CAF9>Robot Origin Frame</color></size>";
        }

        private void CreateBarrierSpheres()
        {
            if (barrierShader == null)
            {
                // 1. Try loading from bundled Resources Material
                Material resOuter = Resources.Load<Material>("Materials/SafetyBarrierOuter");
                if (resOuter != null && resOuter.shader != null)
                {
                    barrierShader = resOuter.shader;
                }
            }

            if (barrierShader == null)
            {
                // 2. Try Shader.Find custom shader
                barrierShader = Shader.Find("UR3eVR/SafetyGridSphere");
            }

            // 3. Fallbacks guaranteed to be compiled in every Unity Standalone build
            bool isFallback = false;
            if (barrierShader == null)
            {
                barrierShader = Shader.Find("Universal Render Pipeline/Unlit");
                if (barrierShader == null) barrierShader = Shader.Find("Sprites/Default");
                if (barrierShader == null) barrierShader = Shader.Find("Unlit/Color");
                isFallback = true;
                Debug.LogWarning("[SafetyBoundaryVisualizer] UR3eVR/SafetyGridSphere not found in build! Falling back to: " + (barrierShader != null ? barrierShader.name : "null"));
            }

            if (barrierShader == null)
            {
                Debug.LogError("[SafetyBoundaryVisualizer] Could not resolve any shader for safety barriers!");
                return;
            }

            // 1. สร้าง Outer Boundary Sphere (ขอบเขตภายนอก - สีแดง)
            outerMat = new Material(barrierShader);
            outerMat.name = "Mat_OuterSafetyBarrier";
            outerMat.SetColor("_BaseColor", outerColor);
            if (!isFallback)
            {
                outerMat.SetFloat("_GridDensity", 24.0f);
                outerMat.SetFloat("_LineWidth", 0.08f);
                outerMat.SetFloat("_FocusRadius", focusSpotRadius);
                outerMat.SetFloat("_ApproachAlpha", minAmbientVisibility);
            }
            else
            {
                outerMat.SetFloat("_Surface", 1);
                outerMat.SetFloat("_Blend", 0);
                outerMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                outerMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                outerMat.SetInt("_ZWrite", 0);
                Color c = outerColor;
                c.a = minAmbientVisibility;
                outerMat.SetColor("_BaseColor", c);
            }
            outerMat.renderQueue = 2980;
            outerMat.enableInstancing = true;

            outerSphereObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            outerSphereObj.name = "UR3e_OuterSafetyBarrier_Grid";
            outerSphereObj.transform.SetParent(null, false);
            outerSphereObj.transform.localScale = Vector3.one * (outerRadius * 2.0f);
            outerSphereObj.transform.rotation = Quaternion.identity;
            DestroyCollider(outerSphereObj);

            outerRenderer = outerSphereObj.GetComponent<MeshRenderer>();
            outerRenderer.material = outerMat;
            outerRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            outerRenderer.receiveShadows = false;

            // 2. สร้าง Inner Boundary Sphere (ขอบเขตจุดบอดใกล้ Base - สีส้ม/เหลือง)
            innerMat = new Material(barrierShader);
            innerMat.name = "Mat_InnerSafetyBarrier";
            innerMat.SetColor("_BaseColor", innerColor);
            if (!isFallback)
            {
                innerMat.SetFloat("_GridDensity", 20.0f);
                innerMat.SetFloat("_LineWidth", 0.08f);
                innerMat.SetFloat("_FocusRadius", focusSpotRadius);
                innerMat.SetFloat("_ApproachAlpha", minAmbientVisibility);
            }
            else
            {
                innerMat.SetFloat("_Surface", 1);
                innerMat.SetFloat("_Blend", 0);
                innerMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                innerMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                innerMat.SetInt("_ZWrite", 0);
                Color c = innerColor;
                c.a = minAmbientVisibility;
                innerMat.SetColor("_BaseColor", c);
            }
            innerMat.renderQueue = 2980;
            innerMat.enableInstancing = true;

            innerSphereObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            innerSphereObj.name = "UR3e_InnerSafetyBarrier_Grid";
            innerSphereObj.transform.SetParent(null, false);
            innerSphereObj.transform.localScale = Vector3.one * (innerRadius * 2.0f);
            innerSphereObj.transform.rotation = Quaternion.identity;
            DestroyCollider(innerSphereObj);

            innerRenderer = innerSphereObj.GetComponent<MeshRenderer>();
            innerRenderer.material = innerMat;
            innerRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            innerRenderer.receiveShadows = false;
        }

        private void Update()
        {
            FindReferences();

            // 1. คำนวณพิกัด Home Point (Starter Point)
            Vector3 homePos = Vector3.zero;
            if (starterPoint != null)
            {
                homePos = starterPoint.transform.position;
            }
            else if (vrHandMover != null && vrHandMover.isAnchorSet)
            {
                homePos = vrHandMover.anchorHandWorldPos;
            }
            else if (handTransform != null)
            {
                homePos = handTransform.position;
            }
            centerPointWorldPos = homePos;

            // 2. คำนวณพิกัด Robot Base Origin (0, 0, 0)
            // ในพิกัดหุ่นยนต์: Base = [0, 0, 0], Home = [0.3053, -0.1313, 0.3108]
            // แปลงเวกเตอร์จาก Home ไปหา Base ใน VR Headset Frame:
            //   Robot +X (Forward)  -> Headset +Z  =>  deltaZ = 0 - 0.3053 = -0.3053m (ถอยหลัง)
            //   Robot +Y (Left)     -> Headset -X  =>  deltaX = -(0 - (-0.1313)) = -0.1313m (ไปทางซ้าย)
            //   Robot +Z (Up)       -> Headset +Y  =>  deltaY = 0 - 0.3108 = -0.3108m (ลงล่าง)
            Quaternion headYaw = (vrHandMover != null && vrHandMover.isAnchorSet) ? vrHandMover.anchorHeadYaw : Quaternion.identity;
            Vector3 baseOffset = headYaw * new Vector3(-0.1313f, -0.3108f, -0.3053f);
            robotBaseWorldPos = centerPointWorldPos + baseOffset;

            // 3. อัปเดตตำแหน่ง Blue Marker ที่จุด (0, 0, 0)
            if (baseMarkerRoot != null)
            {
                baseMarkerRoot.SetActive(showBaseMarker);
                if (showBaseMarker)
                {
                    baseMarkerRoot.transform.position = robotBaseWorldPos;
                    baseMarkerRoot.transform.rotation = headYaw;

                    // ตรวจสอบและผูก CenterEyeCamera กับ World Space Canvas ต่อเนื่อง (แก้ไขภาพไม่ขึ้นตาขวา)
                    if (baseCanvas != null && (baseCanvas.worldCamera == null || !baseCanvas.worldCamera.gameObject.activeInHierarchy))
                    {
                        FindReferences();
                        if (centerEyeCamera != null) baseCanvas.worldCamera = centerEyeCamera;
                    }

                    // ป้ายข้อความหันหน้าหาผู้ใช้เสมอ (Billboard)
                    if (baseLabelObj != null && headTransform != null)
                    {
                        Vector3 lookDir = baseLabelObj.transform.position - headTransform.position;
                        if (lookDir.sqrMagnitude > 0.001f)
                        {
                            baseLabelObj.transform.rotation = Quaternion.LookRotation(lookDir);
                        }
                    }

                    // แสงกระพริบ Beacon นุ่มนวล
                    float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * 3f);
                    if (baseMat != null)
                    {
                        Color c = baseMarkerColor;
                        c.a = pulse;
                        baseMat.color = c;
                    }

                    // เส้นเชื่อมโยง Base (0, 0, 0) -> Home Point
                    if (baseToHomeLine != null)
                    {
                        baseToHomeLine.enabled = showBaseToHomeLine;
                        if (showBaseToHomeLine)
                        {
                            baseToHomeLine.SetPosition(0, robotBaseWorldPos);
                            baseToHomeLine.SetPosition(1, centerPointWorldPos);
                        }
                    }
                }
            }

            // 4. อัปเดตตำแหน่ง Barrier Spheres ให้ตั้งอยู่ที่ Robot Base (0, 0, 0) ของหุ่นยนต์
            if (outerSphereObj != null)
            {
                outerSphereObj.transform.position = robotBaseWorldPos;
                outerSphereObj.transform.rotation = Quaternion.identity;
                outerSphereObj.transform.localScale = Vector3.one * (outerRadius * 2.0f);
            }

            if (innerSphereObj != null)
            {
                innerSphereObj.transform.position = robotBaseWorldPos;
                innerSphereObj.transform.rotation = Quaternion.identity;
                innerSphereObj.transform.localScale = Vector3.one * (innerRadius * 2.0f);
            }

            // 5. ตรวจสอบตำแหน่งมือเทียบกับ Robot Base (0, 0, 0)
            Vector3 handPos = centerPointWorldPos;
            if (vrHandMover != null && vrHandMover.isUsingController && vrHandMover.rightControllerAnchor != null)
            {
                handPos = vrHandMover.rightControllerAnchor.position;
            }
            else if (handTransform != null)
            {
                handPos = handTransform.position;
            }
            distToBase = Vector3.Distance(handPos, robotBaseWorldPos);
            distToCenter = Vector3.Distance(handPos, centerPointWorldPos);

            // คำนวณ Approach Alpha สำหรับ Outer Barrier (สว่างเมื่อเข้าใกล้ขอบเขตนอก 0.62m จาก Base)
            float distToOuterWall = outerRadius - distToBase;
            float targetOuterAlpha = 0f;
            if (distToOuterWall <= outerWarnDistance)
            {
                float t = Mathf.Clamp01(1.0f - (distToOuterWall / outerWarnDistance));
                targetOuterAlpha = Mathf.SmoothStep(0f, 1f, t);
            }

            // คำนวณ Approach Alpha สำหรับ Inner Barrier (สว่างเมื่อดึงมือเข้าใกล้ Base ต่ำกว่า 0.18m)
            float distFromInnerWall = distToBase - innerRadius;
            float targetInnerAlpha = 0f;
            if (distFromInnerWall <= innerWarnDistance)
            {
                float t = Mathf.Clamp01(1.0f - (distFromInnerWall / innerWarnDistance));
                targetInnerAlpha = Mathf.SmoothStep(0f, 1f, t);
            }

            // ซิงค์กับ Safety Ratios จาก Python
            if (gripperBridge != null)
            {
                // ซิงค์กับ Safety Ratios จาก Python: เตือนเฉพาะเมื่อเข้าใกล้ขอบเขตอันตรายจริง (> 0.85)
                if (gripperBridge.safetyOuterRatio > 0.85f)
                {
                    float rT = Mathf.InverseLerp(0.85f, 0.98f, gripperBridge.safetyOuterRatio);
                    targetOuterAlpha = Mathf.Max(targetOuterAlpha, rT);
                }
                if (gripperBridge.safetyInnerRatio > 0.85f)
                {
                    float rT = Mathf.InverseLerp(0.85f, 0.98f, gripperBridge.safetyInnerRatio);
                    targetInnerAlpha = Mathf.Max(targetInnerAlpha, rT);
                }
            }

            float dt = Time.deltaTime;
            float desiredOuter = Mathf.Max(targetOuterAlpha, minAmbientVisibility);
            float desiredInner = Mathf.Max(targetInnerAlpha, minAmbientVisibility);
            outerApproachAlpha = Mathf.Lerp(outerApproachAlpha, desiredOuter, Mathf.Clamp01(16.0f * dt));
            innerApproachAlpha = Mathf.Lerp(innerApproachAlpha, desiredInner, Mathf.Clamp01(16.0f * dt));

            // 6. อัปเดต Shader ของ Outer Barrier
            UpdateBarrierShader(outerMat, outerSphereObj, outerRenderer, outerColor, handPos, outerApproachAlpha, distToBase / outerRadius >= 0.90f);

            // 7. อัปเดต Shader ของ Inner Barrier
            bool isInnerDanger = (distToBase <= innerRadius * 1.15f);
            UpdateBarrierShader(innerMat, innerSphereObj, innerRenderer, innerColor, handPos, innerApproachAlpha, isInnerDanger);
        }

        private void UpdateBarrierShader(Material mat, GameObject obj, MeshRenderer rend, Color baseCol, Vector3 handPos, float approachAlpha, bool isDanger)
        {
            if (mat == null || obj == null || rend == null) return;

            if (approachAlpha <= 0.001f)
            {
                if (rend.enabled) rend.enabled = false;
                return;
            }

            if (!rend.enabled) rend.enabled = true;

            if (mat.HasProperty("_HandPosWS"))
            {
                mat.SetVector("_HandPosWS", new Vector4(handPos.x, handPos.y, handPos.z, 1.0f));
            }
            if (mat.HasProperty("_FocusRadius"))
            {
                mat.SetFloat("_FocusRadius", focusSpotRadius);
            }
            if (mat.HasProperty("_ApproachAlpha"))
            {
                mat.SetFloat("_ApproachAlpha", approachAlpha);
            }

            Color activeColor = baseCol;
            if (gripperBridge != null && gripperBridge.safetyCylinderRatio > 0.50f)
            {
                float blend = Mathf.InverseLerp(0.50f, 0.90f, gripperBridge.safetyCylinderRatio);
                activeColor = Color.Lerp(baseCol, singularityColor, blend);
            }

            if (!mat.HasProperty("_ApproachAlpha"))
            {
                activeColor.a = Mathf.Clamp01(baseCol.a * approachAlpha);
            }
            mat.SetColor("_BaseColor", activeColor);

            float pulse = 1.0f;
            if (isDanger)
            {
                pulse = 1.0f + 0.45f * Mathf.Sin(Time.time * 5.0f * Mathf.PI * 2.0f);
            }
            if (mat.HasProperty("_Pulse"))
            {
                mat.SetFloat("_Pulse", pulse);
            }
        }

        private void DestroyCollider(GameObject go)
        {
            Collider col = go.GetComponent<Collider>();
            if (col != null) DestroyImmediate(col);
        }

        private void OnDestroy()
        {
            if (baseMarkerRoot != null) Destroy(baseMarkerRoot);
            if (outerSphereObj != null) Destroy(outerSphereObj);
            if (innerSphereObj != null) Destroy(innerSphereObj);
            if (outerMat != null) Destroy(outerMat);
            if (innerMat != null) Destroy(innerMat);
            if (baseMat != null) Destroy(baseMat);
            if (baseLineMat != null) Destroy(baseLineMat);
        }
    }
}
