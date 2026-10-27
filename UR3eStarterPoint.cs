using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace UR3eVR
{
    /// <summary>
    /// UR3eStarterPoint: Creates and manages the UR3e Starter/Home Point in Unity VR Scene.
    /// - Safe Home: X=0.3053m, Y=-0.1313m, Z=0.3108m
    /// - Renders as holographic sphere with 64-point laser ring
    /// - World Space Canvas label with Meta Horizon OS style (Stereo SPI Support)
    /// - Floating UI prompt to place hand at Home before starting control
    /// - Turns emerald green when hand is docked at Home
    /// </summary>
    public class UR3eStarterPoint : MonoBehaviour
    {
        [Header("Target References")]
        public VRHandMover vrHandMover;
        public Transform handTransform;
        public Transform headTransform;

        [Header("Display Settings")]
        [Tooltip("Show/hide the Starter Point")]
        public bool isVisible = true;

        [Tooltip("Show laser tether line between hand and Starter Point")]
        public bool showTetherLine = true;

        [Tooltip("Show 3D coordinate axes (Robot X=red, Y=green, Z=blue)")]
        public bool showCoordinateAxes = true;

        [Tooltip("Show floating 3D text label")]
        public bool showLabel = false;

        [Tooltip("Starter Point marker size")]
        [Range(0.01f, 0.15f)]
        public float markerScale = 0.04f;

        [Header("Home Zone Threshold")]
        [Tooltip("Radius for detecting hand at Home position (meters)")]
        public float homeZoneRadius = 0.045f;

        [Header("Live Status (Read Only)")]
        public bool isAtHome = false;
        public float distanceToHome = 0f;
        public Vector3 homeRobotCoordinates = new Vector3(0.3053f, -0.1313f, 0.3108f);
        public float engagedCelebrationTimer = 0f;

        // Visual elements
        private GameObject coreSphere;
        private GameObject ringVisual;
        private GameObject axesRoot;
        private GameObject labelObj;
        private Canvas labelCanvas;
        private Text labelText;
        private Image labelBg;
        private Camera centerEyeCamera;
        private LineRenderer tetherLine;
        private LineRenderer ringLine;
        private Material sphereMat;
        private Material ringMat;
        private Material lineMat;

        private Color homeColor = new Color(0.0f, 1.0f, 0.55f, 0.95f);     // Vibrant Emerald Green
        private Color awayColor = new Color(0.0f, 0.85f, 1.0f, 0.75f);     // Holographic Cyan
        private Color pulseColor = new Color(1.0f, 0.85f, 0.1f, 0.90f);    // Prompt Yellow

        private void Awake()
        {
            CreateVisuals();
        }

        private void Start()
        {
            FindCameras();
            if (vrHandMover == null) vrHandMover = FindAnyObjectByType<VRHandMover>();
            if (vrHandMover != null)
            {
                if (handTransform == null) handTransform = vrHandMover.handTransform;
                if (headTransform == null) headTransform = vrHandMover.headTransform;
                homeRobotCoordinates = vrHandMover.homeRobotPos;
            }

            if (headTransform == null && Camera.main != null)
            {
                headTransform = Camera.main.transform;
            }

            // Place Home Point in front of user at comfortable arm's reach
            if (vrHandMover != null && vrHandMover.isAnchorSet)
            {
                transform.position = vrHandMover.anchorHandWorldPos;
                transform.rotation = vrHandMover.anchorHeadYaw;
            }
            else if (headTransform != null)
            {
                Vector3 fwd = Quaternion.Euler(0f, headTransform.eulerAngles.y, 0f) * Vector3.forward;
                Vector3 right = Quaternion.Euler(0f, headTransform.eulerAngles.y, 0f) * Vector3.right;
                transform.position = headTransform.position + fwd * 0.45f + right * 0.15f - Vector3.up * 0.18f;
                transform.rotation = Quaternion.Euler(0f, headTransform.eulerAngles.y, 0f);
            }
            else if (handTransform != null)
            {
                transform.position = handTransform.position;
            }
        }

        private void FindCameras()
        {
            if (centerEyeCamera == null || !centerEyeCamera.gameObject.activeInHierarchy)
            {
                var cec = GameObject.Find("CenterEyeAnchor");
                if (cec != null) centerEyeCamera = cec.GetComponent<Camera>();
                if (centerEyeCamera == null && Camera.main != null) centerEyeCamera = Camera.main;
            }

            if (headTransform == null && centerEyeCamera != null)
            {
                headTransform = centerEyeCamera.transform;
            }
        }

        /// <summary>
        /// Notify that hand was docked at Home and control is engaged
        /// </summary>
        public void NotifyEngaged()
        {
            engagedCelebrationTimer = 2.5f;
        }

        /// <summary>
        /// Update anchor to match right hand world position
        /// </summary>
        public void UpdateAnchor(Vector3 worldPos, Quaternion headingRot, Vector3 robotPos)
        {
            transform.position = worldPos;
            transform.rotation = headingRot;
            homeRobotCoordinates = robotPos;
        }

        private void Update()
        {
            if (!isVisible)
            {
                SetVisualsActive(false);
                return;
            }
            SetVisualsActive(true);

            if (engagedCelebrationTimer > 0f)
            {
                engagedCelebrationTimer -= Time.deltaTime;
            }

            bool isEngaged = (vrHandMover != null && vrHandMover.isTrackingEngaged);

            // Distance between current hand and Starter Point (world space)
            Vector3 currentHandPos = (handTransform != null) ? handTransform.position : transform.position;
            distanceToHome = Vector3.Distance(currentHandPos, transform.position);
            isAtHome = (distanceToHome <= homeZoneRadius);

            // Update color and effects (Home vs Not Engaged vs Moving)
            float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * (isEngaged ? 3.5f : 6.0f));
            Color activeColor;
            if (isAtHome)
            {
                activeColor = homeColor; // Green when docked at Home
            }
            else if (isEngaged)
            {
                activeColor = Color.Lerp(awayColor, pulseColor, Mathf.Clamp01(distanceToHome / 0.35f));
            }
            else
            {
                activeColor = pulseColor; // Yellow pulse: place hand before starting
            }
            activeColor.a *= pulse;

            if (sphereMat != null) SetMaterialColor(sphereMat, activeColor);
            Color ringC = isAtHome ? homeColor : (isEngaged ? awayColor : pulseColor);
            if (ringMat != null) SetMaterialColor(ringMat, ringC);
            if (ringLine != null)
            {
                Color rc = ringC;
                rc.a = 0.95f;
                ringLine.startColor = rc;
                ringLine.endColor = rc;
            }

            // Update tether laser line between hand and Home point
            if (tetherLine != null)
            {
                bool shouldShowLine = showTetherLine && (distanceToHome > 0.005f) && (handTransform != null);
                tetherLine.enabled = shouldShowLine;
                if (shouldShowLine)
                {
                    tetherLine.SetPosition(0, transform.position);
                    tetherLine.SetPosition(1, currentHandPos);
                    Color startC = isAtHome ? homeColor : (isEngaged ? awayColor : pulseColor);
                    Color endC = isAtHome ? homeColor : pulseColor;
                    tetherLine.startColor = startC;
                    tetherLine.endColor = endC;
                }
            }

            // Bind CenterEyeCamera to World Space Canvas (fix right-eye rendering)
            if (labelCanvas != null && (labelCanvas.worldCamera == null || !labelCanvas.worldCamera.gameObject.activeInHierarchy))
            {
                FindCameras();
                if (centerEyeCamera != null) labelCanvas.worldCamera = centerEyeCamera;
            }

            // Update World Space billboard label
            if (labelObj != null)
            {
                // Auto-hide label if hand is within 25cm to NEVER block the hand or teleoperation view
                bool shouldDisplayLabel = showLabel && (distanceToHome > 0.25f);
                labelObj.SetActive(shouldDisplayLabel);
                if (showLabel && headTransform != null)
                {
                    Vector3 lookDir = labelObj.transform.position - headTransform.position;
                    if (lookDir.sqrMagnitude > 0.001f)
                    {
                        labelObj.transform.rotation = Quaternion.LookRotation(lookDir);
                    }

                    if (labelText != null)
                    {
                        // Prompt to place hand at Home
                        if (!isEngaged)
                        {
                            if (isAtHome)
                            {
                                float pct = Mathf.Clamp01((vrHandMover != null ? vrHandMover.currentDockTime : 0f) / Mathf.Max(0.01f, (vrHandMover != null ? vrHandMover.dockDwellTimeRequired : 0.35f)));
                                labelText.text = string.Format(
                                    "<size=20><b><color=#00FF88>DOCKED AT HOME</color></b></size>\n" +
                                    "<size=16><color=#FFFFFF>Connecting control system... [{0:P0}]</color></size>\n" +
                                    "<size=14><color=#00E5FF>Hold hand still to engage</color></size>",
                                    pct
                                );
                            }
                            else
                            {
                                labelText.text = string.Format(
                                    "<size=18><b><color=#FFD54F>Home Point</color></b></size>\n" +
                                    "<size=16><color=#FFFFFF>Place Right Hand at Home Point to Engage</color></size>\n" +
                                    "<size=14><color=#00E5FF>Distance: {0:F1} cm  ▼  ▼  ▼</color></size>",
                                    distanceToHome * 100f
                                );
                            }
                        }
                        else if (engagedCelebrationTimer > 0f)
                        {
                            labelText.text = 
                                "<size=20><b><color=#00FF88>TELEOPERATION ACTIVE</color></b></size>\n" +
                                "<size=16><color=#FFFFFF>Robot is now tracking your hand!</color></size>\n" +
                                "<size=14><color=#00E5FF>Move your hand to control the robot arm</color></size>";
                        }
                        else
                        {
                            string statusStr = isAtHome 
                                ? "<color=#00FF88>★ AT HOME (Origin) ★</color>" 
                                : string.Format("<color=#00E5FF>Dist from Home: {0:F1} cm | Active</color>", distanceToHome * 100f);

                            labelText.text = string.Format(
                                "<size=18><b><color=#00E5FF>VR-HaptiLab (Home Point)</color></b></size>\n" +
                                "<size=16><color=#FFFFFF>Robot Home: [X:{0:F0}, Y:{1:F0}, Z:{2:F0}] mm</color></size>\n" +
                                "<size=14>{3}</size>",
                                homeRobotCoordinates.x * 1000f,
                                homeRobotCoordinates.y * 1000f,
                                homeRobotCoordinates.z * 1000f,
                                statusStr
                            );
                        }
                    }
                }
            }

            // Update 3D coordinate axes display
            if (axesRoot != null)
            {
                axesRoot.SetActive(showCoordinateAxes);
            }
        }

        private void SetVisualsActive(bool active)
        {
            if (coreSphere != null) coreSphere.SetActive(active);
            if (ringVisual != null) ringVisual.SetActive(active);
            if (axesRoot != null) axesRoot.SetActive(active && showCoordinateAxes);
            if (labelObj != null) labelObj.SetActive(active && showLabel);
            if (tetherLine != null) tetherLine.enabled = active && showTetherLine;
        }

        private void CreateVisuals()
        {
            Shader shader = Shader.Find("UR3eVR/StereoUnlitColor");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("UR3eVR/URPHandOutline");
            if (shader == null) shader = Shader.Find("Sprites/Default");

            // 1. Central Hologram Sphere
            coreSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            coreSphere.name = "Home_CoreSphere";
            coreSphere.transform.SetParent(transform, false);
            coreSphere.transform.localPosition = Vector3.zero;
            coreSphere.transform.localScale = Vector3.one * markerScale;
            DestroyCollider(coreSphere);

            sphereMat = new Material(shader);
            SetMaterialColor(sphereMat, awayColor);
            sphereMat.enableInstancing = true;
            coreSphere.GetComponent<Renderer>().material = sphereMat;

            // 2. Horizontal Home Zone Ring (Smooth 64-point circular laser ring)
            ringVisual = new GameObject("Home_ZoneRing");
            ringVisual.transform.SetParent(transform, false);
            ringVisual.transform.localPosition = new Vector3(0f, -0.003f, 0f);
            ringVisual.transform.localRotation = Quaternion.identity;

            ringLine = ringVisual.AddComponent<LineRenderer>();
            ringLine.useWorldSpace = false;
            ringLine.loop = true;
            ringLine.startWidth = 0.003f;
            ringLine.endWidth = 0.003f;
            int ringSegments = 64;
            ringLine.positionCount = ringSegments;
            for (int i = 0; i < ringSegments; i++)
            {
                float angle = i * Mathf.PI * 2.0f / ringSegments;
                ringLine.SetPosition(i, new Vector3(Mathf.Cos(angle) * homeZoneRadius, 0f, Mathf.Sin(angle) * homeZoneRadius));
            }

            ringMat = new Material(shader);
            SetMaterialColor(ringMat, new Color(0f, 0.85f, 1f, 0.85f));
            ringMat.enableInstancing = true;
            ringLine.material = ringMat;

            // 3. 3D Coordinate Frame (RGB Axes for Robot Motion)
            axesRoot = new GameObject("Home_CoordinateAxes");
            axesRoot.transform.SetParent(transform, false);
            axesRoot.transform.localPosition = Vector3.zero;
            axesRoot.transform.localRotation = Quaternion.identity;

            float axisLen = 0.055f;
            float axisThick = 0.0035f;

            CreateAxisBranch(axesRoot.transform, "Axis_X_Forward_Red", new Vector3(0f, 0f, axisLen / 2f), new Vector3(axisThick, axisLen / 2f, axisThick), Quaternion.Euler(90f, 0f, 0f), Color.red, shader);
            CreateAxisBranch(axesRoot.transform, "Axis_Y_Left_Green", new Vector3(-axisLen / 2f, 0f, 0f), new Vector3(axisThick, axisLen / 2f, axisThick), Quaternion.Euler(0f, 0f, 90f), Color.green, shader);
            CreateAxisBranch(axesRoot.transform, "Axis_Z_Up_Blue", new Vector3(0f, axisLen / 2f, 0f), new Vector3(axisThick, axisLen / 2f, axisThick), Quaternion.identity, Color.blue, shader);

            // 4. Tether LineRenderer
            tetherLine = gameObject.AddComponent<LineRenderer>();
            tetherLine.useWorldSpace = true;
            tetherLine.startWidth = 0.0035f;
            tetherLine.endWidth = 0.0015f;
            lineMat = new Material(shader);
            lineMat.enableInstancing = true;
            tetherLine.material = lineMat;
            tetherLine.positionCount = 2;
            tetherLine.enabled = false;

            // 5. World Space Canvas Label (Single Pass Instanced Stereo Ready - Both Eyes)
            CreateStereoLabelCanvas();
        }

        private void CreateStereoLabelCanvas()
        {
            FindCameras();

            labelObj = new GameObject("Home_Label_Canvas");
            labelObj.transform.SetParent(transform, false);
            labelObj.transform.localPosition = new Vector3(0f, markerScale * 0.75f + 0.12f, 0f);

            labelCanvas = labelObj.AddComponent<Canvas>();
            labelCanvas.renderMode = RenderMode.WorldSpace;
            if (centerEyeCamera != null) labelCanvas.worldCamera = centerEyeCamera;

            var scaler = labelObj.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 10f;

            RectTransform canvasRT = labelObj.GetComponent<RectTransform>();
            canvasRT.sizeDelta = new Vector2(460f, 150f);
            canvasRT.localScale = Vector3.one * 0.00035f;

            // Frosted Obsidian Background Card
            labelBg = labelObj.AddComponent<Image>();
            labelBg.color = new Color(0.06f, 0.08f, 0.12f, 0.88f);

            var outline = labelObj.AddComponent<Outline>();
            outline.effectColor = new Color(0.0f, 0.85f, 1.0f, 0.45f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);

            // Child Text Component
            GameObject textObj = new GameObject("LabelText");
            textObj.transform.SetParent(labelObj.transform, false);
            RectTransform textRT = textObj.AddComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = new Vector2(10f, 8f);
            textRT.offsetMax = new Vector2(-10f, -8f);

            labelText = textObj.AddComponent<Text>();
            labelText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (labelText.font == null) labelText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            labelText.fontSize = 18;
            labelText.alignment = TextAnchor.MiddleCenter;
            labelText.color = Color.white;
            labelText.supportRichText = true;
            labelText.text = "<b>VR-HaptiLab (Home Point)</b>";
        }

        private void CreateAxisBranch(Transform parent, string name, Vector3 pos, Vector3 scale, Quaternion rot, Color col, Shader shader)
        {
            GameObject cyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cyl.name = name;
            cyl.transform.SetParent(parent, false);
            cyl.transform.localPosition = pos;
            cyl.transform.localRotation = rot;
            cyl.transform.localScale = scale;
            DestroyCollider(cyl);

            Material mat = new Material(shader);
            SetMaterialColor(mat, col);
            mat.enableInstancing = true;
            cyl.GetComponent<Renderer>().material = mat;
        }

        private void SetMaterialColor(Material mat, Color col)
        {
            if (mat == null) return;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", col);
            mat.color = col;
        }

        private void DestroyCollider(GameObject go)
        {
            Collider col = go.GetComponent<Collider>();
            if (col != null) DestroyImmediate(col);
        }

        private void OnDestroy()
        {
            if (sphereMat != null) Destroy(sphereMat);
            if (ringMat != null) Destroy(ringMat);
            if (lineMat != null) Destroy(lineMat);
        }
    }
}
