import os
import re

print("Starting implementation of 5 subagent topics...")

# -------------------------------------------------------------
# Topic 1: Fix Left Hand Grip Pose in VRControllerGripPoseHandler.cs
# -------------------------------------------------------------
grip_handler_path = r"C:\Users\dhamm\VR\Assets\Scripts\VRControllerGripPoseHandler.cs"

grip_code = """using System;
using System.Collections.Generic;
using UnityEngine;

namespace UR3eVR
{
    /// <summary>
    /// VRControllerGripPoseHandler: Controls hand mesh finger curling when holding Meta Quest Touch Controller.
    /// - Only curls fingers when the specific controller (Left/Right) is actually held/connected.
    /// - Middle, Ring, Pinky wrap around controller handle, flexing along local X-axis.
    /// - Index finger rests on the front index trigger, curling dynamically when trigger is pulled.
    /// - Thumb rests near thumbstick and face buttons.
    /// - Fully compatible with Meta Quest Natural controller-driven poses and OpenXR skeletal hands.
    /// </summary>
    public class VRControllerGripPoseHandler : MonoBehaviour
    {
        [Header("Hand & Controller Configuration")]
        public bool isRightHand = false;
        public OVRInput.Controller controller = OVRInput.Controller.LTouch;

        [Header("Pose Blending")]
        [Range(0f, 1f)]
        public float gripWeight = 0f;
        public float blendSpeed = 14f;

        [Header("References")]
        public OVRSkeleton ovrSkeleton;
        public OVRHand ovrHand;

        private class FingerJoints
        {
            public Transform proximal;
            public Transform intermediate;
            public Transform distal;
            public Quaternion defaultProximalRot;
            public Quaternion defaultIntermediateRot;
            public Quaternion defaultDistalRot;
            public bool isCaptured;
        }

        private FingerJoints indexFinger;
        private FingerJoints middleFinger;
        private FingerJoints ringFinger;
        private FingerJoints pinkyFinger;
        private FingerJoints thumbFinger;

        private bool bonesCached = false;
        private bool hasValidBones = false;

        private void Awake()
        {
            if (ovrSkeleton == null) ovrSkeleton = GetComponent<OVRSkeleton>();
            if (ovrHand == null) ovrHand = GetComponent<OVRHand>();

            DetectHandSide();
        }

        private void Start()
        {
            DetectHandSide();
            TryCacheBones();
        }

        private void DetectHandSide()
        {
            if (ovrSkeleton != null)
            {
                var st = ovrSkeleton.GetSkeletonType();
                if (st == OVRSkeleton.SkeletonType.XRHandRight || st == OVRSkeleton.SkeletonType.HandRight)
                {
                    isRightHand = true;
                }
                else if (st == OVRSkeleton.SkeletonType.XRHandLeft || st == OVRSkeleton.SkeletonType.HandLeft)
                {
                    isRightHand = false;
                }
                else
                {
                    isRightHand = name.ToLower().Contains("right");
                }
            }
            else
            {
                isRightHand = name.ToLower().Contains("right");
            }

            controller = isRightHand ? OVRInput.Controller.RTouch : OVRInput.Controller.LTouch;
        }

        private void Update()
        {
            if (!bonesCached || !hasValidBones)
            {
                TryCacheBones();
            }
        }

        private void TryCacheBones()
        {
            if (ovrSkeleton == null) ovrSkeleton = GetComponent<OVRSkeleton>();

            if (ovrSkeleton != null && ovrSkeleton.IsInitialized && ovrSkeleton.Bones != null && ovrSkeleton.Bones.Count > 0)
            {
                var boneMap = new Dictionary<OVRSkeleton.BoneId, Transform>();
                foreach (var b in ovrSkeleton.Bones)
                {
                    if (b != null && b.Transform != null)
                    {
                        boneMap[b.Id] = b.Transform;
                    }
                }

                // Index (Hand_Index1, Hand_Index2, Hand_Index3)
                Transform idx1 = GetBone(boneMap, OVRSkeleton.BoneId.Hand_Index1) ?? GetBone(boneMap, OVRSkeleton.BoneId.XRHand_IndexProximal);
                Transform idx2 = GetBone(boneMap, OVRSkeleton.BoneId.Hand_Index2) ?? GetBone(boneMap, OVRSkeleton.BoneId.XRHand_IndexIntermediate);
                Transform idx3 = GetBone(boneMap, OVRSkeleton.BoneId.Hand_Index3) ?? GetBone(boneMap, OVRSkeleton.BoneId.XRHand_IndexDistal);
                if (idx1 != null)
                {
                    indexFinger = new FingerJoints { proximal = idx1, intermediate = idx2, distal = idx3 };
                }

                // Middle
                Transform mid1 = GetBone(boneMap, OVRSkeleton.BoneId.Hand_Middle1) ?? GetBone(boneMap, OVRSkeleton.BoneId.XRHand_MiddleProximal);
                Transform mid2 = GetBone(boneMap, OVRSkeleton.BoneId.Hand_Middle2) ?? GetBone(boneMap, OVRSkeleton.BoneId.XRHand_MiddleIntermediate);
                Transform mid3 = GetBone(boneMap, OVRSkeleton.BoneId.Hand_Middle3) ?? GetBone(boneMap, OVRSkeleton.BoneId.XRHand_MiddleDistal);
                if (mid1 != null)
                {
                    middleFinger = new FingerJoints { proximal = mid1, intermediate = mid2, distal = mid3 };
                }

                // Ring
                Transform rng1 = GetBone(boneMap, OVRSkeleton.BoneId.Hand_Ring1) ?? GetBone(boneMap, OVRSkeleton.BoneId.XRHand_RingProximal);
                Transform rng2 = GetBone(boneMap, OVRSkeleton.BoneId.Hand_Ring2) ?? GetBone(boneMap, OVRSkeleton.BoneId.XRHand_RingIntermediate);
                Transform rng3 = GetBone(boneMap, OVRSkeleton.BoneId.Hand_Ring3) ?? GetBone(boneMap, OVRSkeleton.BoneId.XRHand_RingDistal);
                if (rng1 != null)
                {
                    ringFinger = new FingerJoints { proximal = rng1, intermediate = rng2, distal = rng3 };
                }

                // Pinky
                Transform pnk1 = GetBone(boneMap, OVRSkeleton.BoneId.Hand_Pinky1) ?? GetBone(boneMap, OVRSkeleton.BoneId.XRHand_LittleProximal);
                Transform pnk2 = GetBone(boneMap, OVRSkeleton.BoneId.Hand_Pinky2) ?? GetBone(boneMap, OVRSkeleton.BoneId.XRHand_LittleIntermediate);
                Transform pnk3 = GetBone(boneMap, OVRSkeleton.BoneId.Hand_Pinky3) ?? GetBone(boneMap, OVRSkeleton.BoneId.XRHand_LittleDistal);
                if (pnk1 != null)
                {
                    pinkyFinger = new FingerJoints { proximal = pnk1, intermediate = pnk2, distal = pnk3 };
                }

                // Thumb
                Transform thm1 = GetBone(boneMap, OVRSkeleton.BoneId.Hand_Thumb1) ?? GetBone(boneMap, OVRSkeleton.BoneId.XRHand_ThumbMetacarpal);
                Transform thm2 = GetBone(boneMap, OVRSkeleton.BoneId.Hand_Thumb2) ?? GetBone(boneMap, OVRSkeleton.BoneId.XRHand_ThumbProximal);
                Transform thm3 = GetBone(boneMap, OVRSkeleton.BoneId.Hand_Thumb3) ?? GetBone(boneMap, OVRSkeleton.BoneId.XRHand_ThumbDistal);
                if (thm1 != null)
                {
                    thumbFinger = new FingerJoints { proximal = thm1, intermediate = thm2, distal = thm3 };
                }

                if (indexFinger != null && middleFinger != null)
                {
                    bonesCached = true;
                    hasValidBones = true;
                }
            }
        }

        private Transform GetBone(Dictionary<OVRSkeleton.BoneId, Transform> map, OVRSkeleton.BoneId id)
        {
            Transform t;
            map.TryGetValue(id, out t);
            return t;
        }

        private void LateUpdate()
        {
            if (!bonesCached || !hasValidBones)
            {
                TryCacheBones();
                if (!bonesCached) return;
            }

            // Check if THIS specific controller is held:
            bool isConnected = (OVRInput.GetConnectedControllers() & controller) != 0;
            bool isTracked = OVRInput.GetControllerPositionTracked(controller);
            bool hasInput = OVRInput.Get(OVRInput.Button.Any, controller)
                         || OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, controller) > 0.01f
                         || OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, controller) > 0.01f
                         || OVRInput.Get(OVRInput.Touch.Any, controller);

            bool isHoldingRemote = isConnected && (isTracked || hasInput);

            // In Editor testing: left hand defaults to holding remote if left controller is assigned
            if (Application.isEditor && !isRightHand)
            {
                if (isTracked || hasInput || Input.GetKey(KeyCode.LeftShift) || Input.GetMouseButton(0))
                {
                    isHoldingRemote = true;
                }
            }

            // Smooth blend
            gripWeight = Mathf.MoveTowards(gripWeight, isHoldingRemote ? 1.0f : 0.0f, Time.deltaTime * blendSpeed);
            if (gripWeight <= 0.001f) return;

            float indexTrigger = Mathf.Clamp01(OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, controller));
            float handTrigger = Mathf.Clamp01(OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, controller));
            Vector2 thumbstick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, controller);

            if (Application.isEditor && !isConnected)
            {
                if (Input.GetMouseButton(0)) indexTrigger = 0.9f;
                if (Input.GetMouseButton(1)) handTrigger = 0.9f;
            }

            // Flexion curl angle on anatomical local X-axis:
            // Middle, Ring, Pinky wrap handle tightly
            if (middleFinger != null)
            {
                float midProx = Mathf.Lerp(45f, 75f, handTrigger);
                float midInter = Mathf.Lerp(55f, 85f, handTrigger);
                float midDist = Mathf.Lerp(30f, 60f, handTrigger);
                CurlJoint(middleFinger.proximal, midProx);
                CurlJoint(middleFinger.intermediate, midInter);
                CurlJoint(middleFinger.distal, midDist);
            }

            if (ringFinger != null)
            {
                float rngProx = Mathf.Lerp(50f, 78f, handTrigger);
                float rngInter = Mathf.Lerp(60f, 88f, handTrigger);
                float rngDist = Mathf.Lerp(35f, 65f, handTrigger);
                CurlJoint(ringFinger.proximal, rngProx);
                CurlJoint(ringFinger.intermediate, rngInter);
                CurlJoint(ringFinger.distal, rngDist);
            }

            if (pinkyFinger != null)
            {
                float pnkProx = Mathf.Lerp(52f, 80f, handTrigger);
                float pnkInter = Mathf.Lerp(62f, 90f, handTrigger);
                float pnkDist = Mathf.Lerp(38f, 68f, handTrigger);
                CurlJoint(pinkyFinger.proximal, pnkProx);
                CurlJoint(pinkyFinger.intermediate, pnkInter);
                CurlJoint(pinkyFinger.distal, pnkDist);
            }

            // Index finger rests on trigger, curling when pulled
            if (indexFinger != null)
            {
                float idxProx = Mathf.Lerp(22f, 60f, indexTrigger);
                float idxInter = Mathf.Lerp(28f, 72f, indexTrigger);
                float idxDist = Mathf.Lerp(16f, 48f, indexTrigger);
                CurlJoint(indexFinger.proximal, idxProx);
                CurlJoint(indexFinger.intermediate, idxInter);
                CurlJoint(indexFinger.distal, idxDist);
            }

            // Thumb rests on faceplate / thumbstick
            if (thumbFinger != null)
            {
                float thmPitch = 15f + thumbstick.y * 8f;
                float thmYaw = 18f + thumbstick.x * 10f;
                float thmRoll = -22f;
                if (thumbFinger.proximal != null)
                {
                    Quaternion targetRot = thumbFinger.proximal.localRotation * Quaternion.Euler(thmPitch, thmYaw, thmRoll);
                    thumbFinger.proximal.localRotation = Quaternion.Slerp(thumbFinger.proximal.localRotation, targetRot, gripWeight * 0.7f);
                }
                if (thumbFinger.distal != null)
                {
                    CurlJoint(thumbFinger.distal, 20f);
                }
            }
        }

        private void CurlJoint(Transform joint, float angleDegrees)
        {
            if (joint == null) return;
            // Flexion around local X axis (anatomical finger curling)
            Quaternion curlOffset = Quaternion.Euler(angleDegrees, 0f, 0f);
            joint.localRotation = Quaternion.Slerp(joint.localRotation, joint.localRotation * curlOffset, gripWeight);
        }
    }
}
"""

with open(grip_handler_path, "w", encoding="utf-8") as f:
    f.write(grip_code)
print("Updated VRControllerGripPoseHandler.cs successfully.")

# -------------------------------------------------------------
# Topic 2: Fix Left Remote Laser Pointer in VRRemoteLaserPointer.cs
# -------------------------------------------------------------
laser_pointer_path = r"C:\Users\dhamm\VR\Assets\Scripts\VRRemoteLaserPointer.cs"

laser_code = """using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace UR3eVR
{
    /// <summary>
    /// VRRemoteLaserPointer: Laser pointer continuously emanating from Meta Quest Touch Remote (Left Hand LTouch).
    /// - Always active and vividly visible at all times
    /// - Robust anchor tracking from LeftControllerAnchor / LeftHandAnchor
    /// - Magnetic Curved Snap (suction curve): curves toward UI buttons and grab bars
    /// - Front Index Trigger selects/clicks targeted UI buttons
    /// - Stereo Single Pass Instanced compatible
    /// </summary>
    public class VRRemoteLaserPointer : MonoBehaviour
    {
        public static VRRemoteLaserPointer Instance { get; private set; }

        [Header("Controller & Ray Settings")]
        [Tooltip("Controller to bind laser pointer to (Default: Left Hand LTouch)")]
        public OVRInput.Controller controller = OVRInput.Controller.LTouch;
        public float maxRayLength = 4.0f;
        public float tipForwardOffset = 0.065f;
        public float tipUpOffset = 0.018f;

        [Header("Magnetic Suction Snap Settings")]
        [Tooltip("Radius around UI buttons where laser curves and snaps to target (meters)")]
        public float magneticSnapRadius = 0.28f;
        [Tooltip("Max angle between controller forward and button to engage suction (degrees)")]
        public float magneticMaxAngle = 22.0f;
        [Tooltip("Speed of curve smoothing")]
        public float snapSmoothSpeed = 20.0f;

        [Header("Colors & Styling")]
        public Color normalLaserColor = new Color(0.0f, 1.0f, 1.0f, 0.95f);     // Vivid Electric Cyan (always visible)
        public Color magneticLaserColor = new Color(0.0f, 1.0f, 0.75f, 1.0f);   // Neon Aqua Snap
        public Color pressLaserColor = new Color(1.0f, 0.95f, 0.25f, 1.0f);     // Trigger Click Yellow

        [Header("References")]
        public Transform leftControllerAnchor;
        public Transform headTransform;
        public BhapticsGloveFeedback bhapticsFeedback;

        // Visual Elements
        private LineRenderer lineRenderer;
        private GameObject reticleObj;
        private Material lineMaterial;
        private Material reticleMaterial;

        // Curve Sampling Resolution
        private const int CURVE_POINTS = 28;
        private Vector3[] curvePoints = new Vector3[CURVE_POINTS];

        // State
        public bool isLaserActive = true;
        public bool isSnapped = false;
        public Selectable hoveredSelectable = null;
        public VRWindowDragger hoveredDragger = null;
        private Vector3 smoothedEndpoint;
        private bool wasTriggerDown = false;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            BuildVisuals();
        }

        private void Start()
        {
            FindReferences();
        }

        private void FindReferences()
        {
            if (leftControllerAnchor == null)
            {
                // Search across root and nested objects
                string[] possibleNames = new string[] {
                    "LeftControllerAnchor", "LeftControllerInHandAnchor", "LeftHandAnchor", "LeftHandAnchorDetached"
                };

                foreach (var name in possibleNames)
                {
                    var found = GameObject.Find(name);
                    if (found != null)
                    {
                        leftControllerAnchor = found.transform;
                        break;
                    }
                }

                if (leftControllerAnchor == null)
                {
                    var allTransforms = Resources.FindObjectsOfTypeAll<Transform>();
                    foreach (var t in allTransforms)
                    {
                        if (t.name == "LeftControllerAnchor" || t.name == "LeftHandAnchor")
                        {
                            leftControllerAnchor = t;
                            break;
                        }
                    }
                }
            }

            if (headTransform == null)
            {
                var cec = GameObject.Find("CenterEyeAnchor");
                if (cec != null) headTransform = cec.transform;
                if (headTransform == null && Camera.main != null) headTransform = Camera.main.transform;
            }

            if (bhapticsFeedback == null) bhapticsFeedback = FindAnyObjectByType<BhapticsGloveFeedback>();
        }

        private void BuildVisuals()
        {
            lineRenderer = GetComponent<LineRenderer>();
            if (lineRenderer == null) lineRenderer = gameObject.AddComponent<LineRenderer>();

            Shader stereoShader = Shader.Find("UR3eVR/StereoUnlitColor") 
                               ?? Shader.Find("Universal Render Pipeline/Unlit")
                               ?? Shader.Find("Unlit/Color");

            lineMaterial = new Material(stereoShader);
            lineMaterial.name = "VR_Laser_LineMaterial";
            lineMaterial.enableInstancing = true;
            if (lineMaterial.HasProperty("_BaseColor")) lineMaterial.SetColor("_BaseColor", normalLaserColor);
            if (lineMaterial.HasProperty("_Color")) lineMaterial.SetColor("_Color", normalLaserColor);

            lineRenderer.material = lineMaterial;
            lineRenderer.startWidth = 0.0075f; // 7.5mm start (crisp and clearly visible in VR)
            lineRenderer.endWidth = 0.0035f;   // 3.5mm end
            lineRenderer.positionCount = CURVE_POINTS;
            lineRenderer.useWorldSpace = true;
            lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lineRenderer.receiveShadows = false;

            // Reticle Disc
            reticleObj = new GameObject("VR_Laser_Reticle");
            reticleObj.transform.SetParent(transform, false);

            var mf = reticleObj.AddComponent<MeshFilter>();
            var mr = reticleObj.AddComponent<MeshRenderer>();
            mf.mesh = CreateDiscMesh(0.018f, 32);

            reticleMaterial = new Material(stereoShader);
            reticleMaterial.name = "VR_Laser_ReticleMaterial";
            reticleMaterial.enableInstancing = true;
            if (reticleMaterial.HasProperty("_BaseColor")) reticleMaterial.SetColor("_BaseColor", normalLaserColor);
            if (reticleMaterial.HasProperty("_Color")) reticleMaterial.SetColor("_Color", normalLaserColor);
            mr.material = reticleMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            reticleObj.SetActive(true);
        }

        private Mesh CreateDiscMesh(float radius, int segments)
        {
            Mesh mesh = new Mesh { name = "ReticleDisc" };
            Vector3[] verts = new Vector3[segments + 1];
            int[] tris = new int[segments * 3];

            verts[0] = Vector3.zero;
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2.0f / segments;
                verts[i + 1] = new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f);
            }

            for (int i = 0; i < segments; i++)
            {
                tris[i * 3] = 0;
                tris[i * 3 + 1] = i + 1;
                tris[i * 3 + 2] = (i + 1) % segments + 1;
            }

            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            return mesh;
        }

        private void Update()
        {
            // Laser pointer is ALWAYS ACTIVE continuously on Left Hand
            isLaserActive = true;

            if (leftControllerAnchor == null || headTransform == null)
            {
                FindReferences();
            }

            Vector3 rayOrigin;
            Vector3 rayDir;

            if (leftControllerAnchor != null)
            {
                rayOrigin = leftControllerAnchor.position + leftControllerAnchor.forward * tipForwardOffset + leftControllerAnchor.up * tipUpOffset;
                rayDir = leftControllerAnchor.forward;
            }
            else if (headTransform != null)
            {
                rayOrigin = headTransform.position + headTransform.forward * 0.15f + Vector3.down * 0.08f;
                rayDir = headTransform.forward;
            }
            else
            {
                return;
            }

            lineRenderer.enabled = true;

            // 1. Magnetic Suction Snap Check
            Vector3 snapTargetPos = Vector3.zero;
            Vector3 snapTargetNormal = -rayDir;
            Selectable bestSelectable = null;
            VRWindowDragger bestDragger = null;

            bool foundMagneticTarget = FindBestMagneticTarget(rayOrigin, rayDir, out snapTargetPos, out snapTargetNormal, out bestSelectable, out bestDragger);

            // 2. Trigger Input (Left Index Trigger)
            bool isTriggerDownNow = OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, controller)
                                 || OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, controller) > 0.40f
                                 || Input.GetMouseButton(0);

            bool isTriggerJustPressed = OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, controller)
                                     || (isTriggerDownNow && !wasTriggerDown)
                                     || Input.GetMouseButtonDown(0);

            // 3. Render Laser (Suction Bezier Curve vs Straight Ray)
            if (foundMagneticTarget)
            {
                isSnapped = true;
                hoveredSelectable = bestSelectable;
                hoveredDragger = bestDragger;

                smoothedEndpoint = Vector3.Lerp(smoothedEndpoint, snapTargetPos, Time.deltaTime * snapSmoothSpeed);

                float totalDist = Vector3.Distance(rayOrigin, snapTargetPos);
                Vector3 p0 = rayOrigin;
                Vector3 p1 = rayOrigin + rayDir * (totalDist * 0.40f);
                Vector3 p2 = snapTargetPos - snapTargetNormal * (totalDist * 0.25f);
                Vector3 p3 = snapTargetPos;

                for (int i = 0; i < CURVE_POINTS; i++)
                {
                    float t = (float)i / (CURVE_POINTS - 1);
                    curvePoints[i] = EvaluateCubicBezier(p0, p1, p2, p3, t);
                }
                lineRenderer.SetPositions(curvePoints);

                Color activeCol = isTriggerDownNow ? pressLaserColor : magneticLaserColor;
                SetLaserColor(activeCol, 0.008f, 0.004f);

                if (reticleObj != null)
                {
                    reticleObj.SetActive(true);
                    reticleObj.transform.position = snapTargetPos + snapTargetNormal * 0.003f;
                    reticleObj.transform.rotation = Quaternion.LookRotation(snapTargetNormal, Vector3.up);
                    float pulseScale = 1.0f + 0.15f * Mathf.Sin(Time.time * 8.0f);
                    reticleObj.transform.localScale = Vector3.one * (pulseScale * (isTriggerDownNow ? 1.4f : 1.2f));
                    SetReticleColor(activeCol);
                }

                if (isTriggerJustPressed)
                {
                    ExecuteSelectionClick(bestSelectable, bestDragger);
                }
            }
            else
            {
                isSnapped = false;
                hoveredSelectable = null;
                hoveredDragger = null;

                RaycastHit hit;
                Vector3 endPoint;
                Vector3 hitNormal = -rayDir;
                bool hasHit = Physics.Raycast(rayOrigin, rayDir, out hit, maxRayLength);

                if (hasHit)
                {
                    endPoint = hit.point;
                    hitNormal = hit.normal;

                    var sel = hit.collider.GetComponentInParent<Selectable>();
                    var drag = hit.collider.GetComponentInParent<VRWindowDragger>();
                    if (sel != null || drag != null)
                    {
                        hoveredSelectable = sel;
                        hoveredDragger = drag;
                        if (isTriggerJustPressed) ExecuteSelectionClick(sel, drag);
                    }
                }
                else
                {
                    endPoint = rayOrigin + rayDir * maxRayLength;
                }

                smoothedEndpoint = endPoint;

                for (int i = 0; i < CURVE_POINTS; i++)
                {
                    float t = (float)i / (CURVE_POINTS - 1);
                    curvePoints[i] = Vector3.Lerp(rayOrigin, endPoint, t);
                }
                lineRenderer.SetPositions(curvePoints);

                // Always vivid and thick enough to be seen in VR against skybox
                Color defaultCol = isTriggerDownNow ? pressLaserColor : normalLaserColor;
                SetLaserColor(defaultCol, 0.0075f, 0.0035f);

                if (reticleObj != null)
                {
                    reticleObj.SetActive(true);
                    reticleObj.transform.position = endPoint + hitNormal * 0.002f;
                    reticleObj.transform.rotation = Quaternion.LookRotation(hitNormal, Vector3.up);
                    reticleObj.transform.localScale = Vector3.one * (isTriggerDownNow ? 1.3f : 1.0f);
                    SetReticleColor(defaultCol);
                }
            }

            wasTriggerDown = isTriggerDownNow;
        }

        private bool FindBestMagneticTarget(Vector3 origin, Vector3 dir, out Vector3 targetPos, out Vector3 targetNormal, out Selectable bestSel, out VRWindowDragger bestDrag)
        {
            targetPos = Vector3.zero;
            targetNormal = -dir;
            bestSel = null;
            bestDrag = null;

            float bestScore = float.MaxValue;
            bool found = false;

            // 1. UI Selectables
            var selectables = FindObjectsByType<Selectable>(FindObjectsSortMode.None);
            foreach (var sel in selectables)
            {
                if (sel == null || !sel.gameObject.activeInHierarchy || !sel.interactable) continue;

                Vector3 bPos = sel.transform.position;
                Vector3 toButton = bPos - origin;
                float forwardProj = Vector3.Dot(toButton, dir);
                if (forwardProj < 0.10f || forwardProj > maxRayLength) continue;

                Vector3 rayPoint = origin + dir * forwardProj;
                float perpDist = Vector3.Distance(rayPoint, bPos);
                float angle = Vector3.Angle(dir, toButton);

                if (perpDist <= magneticSnapRadius && angle <= magneticMaxAngle)
                {
                    float score = perpDist + angle * 0.01f;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        targetPos = bPos;
                        targetNormal = -sel.transform.forward;
                        bestSel = sel;
                        bestDrag = null;
                        found = true;
                    }
                }
            }

            // 2. VR Window Draggers
            var draggers = FindObjectsByType<VRWindowDragger>(FindObjectsSortMode.None);
            foreach (var drag in draggers)
            {
                if (drag == null || !drag.gameObject.activeInHierarchy) continue;

                Vector3 dPos = drag.transform.position;
                Vector3 toDrag = dPos - origin;
                float forwardProj = Vector3.Dot(toDrag, dir);
                if (forwardProj < 0.10f || forwardProj > maxRayLength) continue;

                Vector3 rayPoint = origin + dir * forwardProj;
                float perpDist = Vector3.Distance(rayPoint, dPos);
                float angle = Vector3.Angle(dir, toDrag);

                if (perpDist <= magneticSnapRadius * 1.3f && angle <= magneticMaxAngle * 1.3f)
                {
                    float score = perpDist + angle * 0.01f;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        targetPos = dPos;
                        targetNormal = -drag.transform.forward;
                        bestSel = null;
                        bestDrag = drag;
                        found = true;
                    }
                }
            }

            return found;
        }

        private void ExecuteSelectionClick(Selectable sel, VRWindowDragger drag)
        {
            if (sel != null)
            {
                var btn = sel as Button;
                if (btn != null)
                {
                    btn.onClick.Invoke();
                    StartCoroutine(AnimateButtonClick(btn));
                }
                else
                {
                    ExecuteEvents.Execute(sel.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerClickHandler);
                }

                HapticPulse(0.6f, 0.12f);
            }
            else if (drag != null)
            {
                drag.ToggleDirectGrab();
                HapticPulse(0.4f, 0.08f);
            }
        }

        private IEnumerator AnimateButtonClick(Button btn)
        {
            if (btn == null) yield break;
            var rt = btn.GetComponent<RectTransform>();
            if (rt == null) yield break;

            Vector3 origScale = rt.localScale;
            rt.localScale = origScale * 0.88f;
            yield return new WaitForSeconds(0.08f);
            if (rt != null) rt.localScale = origScale;
        }

        private void SetLaserColor(Color col, float startW, float endW)
        {
            if (lineRenderer == null) return;
            lineRenderer.startWidth = startW;
            lineRenderer.endWidth = endW;
            if (lineMaterial != null)
            {
                if (lineMaterial.HasProperty("_BaseColor")) lineMaterial.SetColor("_BaseColor", col);
                if (lineMaterial.HasProperty("_Color")) lineMaterial.SetColor("_Color", col);
            }
        }

        private void SetReticleColor(Color col)
        {
            if (reticleMaterial != null)
            {
                if (reticleMaterial.HasProperty("_BaseColor")) reticleMaterial.SetColor("_BaseColor", col);
                if (reticleMaterial.HasProperty("_Color")) reticleMaterial.SetColor("_Color", col);
            }
        }

        private Vector3 EvaluateCubicBezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float u = 1f - t;
            float tt = t * t;
            float uu = u * u;
            float uuu = uu * u;
            float ttt = tt * t;

            return uuu * p0 + 3f * uu * t * p1 + 3f * u * tt * p2 + ttt * p3;
        }

        public void HapticPulse(float amplitude, float duration)
        {
            try
            {
                OVRInput.SetControllerVibration(amplitude * 0.5f, amplitude, controller);
                StartCoroutine(StopHapticAfter(duration));
            }
            catch { }

            if (bhapticsFeedback != null)
            {
                bhapticsFeedback.TriggerClickPulse((int)(amplitude * 100), (int)(duration * 1000));
            }
        }

        private IEnumerator StopHapticAfter(float duration)
        {
            yield return new WaitForSeconds(duration);
            try
            {
                OVRInput.SetControllerVibration(0f, 0f, controller);
            }
            catch { }
        }
    }
}
"""

with open(laser_pointer_path, "w", encoding="utf-8") as f:
    f.write(laser_code)
print("Updated VRRemoteLaserPointer.cs successfully.")

# -------------------------------------------------------------
# Topic 3: Button Y Teleoperation Pause (GripperBridge.cs, VRHandMover.cs, vr_bridge_server.py, test_move.py)
# -------------------------------------------------------------
# 1. Update GripperBridge.cs
gripper_bridge_path = r"C:\Users\dhamm\VR\Assets\Scripts\GripperBridge.cs"
with open(gripper_bridge_path, "r", encoding="utf-8") as f:
    gb_content = f.read()

# Add SendPause method if not exists
if "public void SendPause(" not in gb_content:
    pause_method = """
        /// <summary>
        /// Sends PAUSE command to Python UR3e bridge to freeze/resume robot teleoperation
        /// </summary>
        public void SendPause(bool isPaused)
        {
            try
            {
                string cmd = isPaused ? "PAUSE 1\\n" : "PAUSE 0\\n";
                if (client != null && client.Connected && stream != null)
                {
                    byte[] bytes = Encoding.ASCII.GetBytes(cmd);
                    stream.Write(bytes, 0, bytes.Length);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[GripperBridge] SendPause error: " + ex.Message);
            }
        }
"""
    # Insert right before SendCustomCommand
    gb_content = gb_content.replace("public void SendCustomCommand(", pause_method + "\n        public void SendCustomCommand(")
    with open(gripper_bridge_path, "w", encoding="utf-8") as f:
        f.write(gb_content)
    print("Updated GripperBridge.cs with SendPause.")

# 2. Update VRHandMover.cs to call SendPause
vrhandmover_path = r"C:\Users\dhamm\VR\Assets\Scripts\VRHandMover.cs"
with open(vrhandmover_path, "r", encoding="utf-8") as f:
    vhm_content = f.read()

if "gripperBridge.SendPause(isTeleoperationPaused);" not in vhm_content:
    vhm_content = vhm_content.replace(
        "isTeleoperationPaused = !isTeleoperationPaused;",
        "isTeleoperationPaused = !isTeleoperationPaused;\n            if (gripperBridge != null) gripperBridge.SendPause(isTeleoperationPaused);"
    )
    with open(vrhandmover_path, "w", encoding="utf-8") as f:
        f.write(vhm_content)
    print("Updated VRHandMover.cs to call gripperBridge.SendPause.")

# 3. Update vr_bridge_server.py
bridge_server_path = r"c:\Users\dhamm\Documents\GitHub\ur3e-vr-bridge\vr_bridge_server.py"
with open(bridge_server_path, "r", encoding="utf-8") as f:
    bs_content = f.read()

if "is_teleop_paused" not in bs_content:
    # Add attribute in __init__
    bs_content = bs_content.replace(
        "self.on_shake_callback = None",
        "self.on_shake_callback = None\n        self.is_teleop_paused = False\n        self.on_pause_callback = None"
    )
    # Add PAUSE parsing in _client_read_loop
    pause_parse = """                elif cmd.startswith("PAUSE"):
                    parts = cmd.split()
                    if len(parts) >= 2:
                        self.is_teleop_paused = (parts[1].strip() == "1")
                    else:
                        self.is_teleop_paused = not self.is_teleop_paused
                    print(f"⏸️ [VR Bridge] Teleoperation Pause state: {self.is_teleop_paused}")
                    if self.on_pause_callback:
                        try:
                            self.on_pause_callback(self.is_teleop_paused)
                        except Exception as e:
                            print(f"⚠️ [VR Bridge] Error in pause callback: {e}")
"""
    bs_content = bs_content.replace(
        'elif cmd.startswith("SHAKE"):',
        pause_parse + '                elif cmd.startswith("SHAKE"):'
    )
    with open(bridge_server_path, "w", encoding="utf-8") as f:
        f.write(bs_content)
    print("Updated vr_bridge_server.py with PAUSE support.")

# 4. Update test_move.py
test_move_path = r"c:\Users\dhamm\Documents\GitHub\ur3e-vr-bridge\test_move.py"
with open(test_move_path, "r", encoding="utf-8") as f:
    tm_content = f.read()

if "vr_server.is_teleop_paused" not in tm_content:
    pause_handling = """
        # --- Teleoperation Pause Check (Button Y on Quest Touch Remote) ---
        if vr_server.is_teleop_paused:
            cmd_vel = [0.0, 0.0, 0.0, 0.0, 0.0, 0.0]
            vr_pose_state["target_pose"] = None
            if rtde_c is not None:
                try:
                    rtde_c.speedStop(STOP_ACCEL)
                except Exception:
                    pass
            time.sleep(dt)
            continue
"""
    # Insert at the beginning of the teleoperation loop
    tm_content = tm_content.replace(
        '# -------------------------------------------------------------',
        pause_handling + '\n        # -------------------------------------------------------------',
        1
    )
    with open(test_move_path, "w", encoding="utf-8") as f:
        f.write(tm_content)
    print("Updated test_move.py with pause freeze logic.")

# -------------------------------------------------------------
# Topic 4: Fix Camera Feed Selector in VRMenuManager.cs
# -------------------------------------------------------------
menu_manager_path = r"C:\Users\dhamm\VR\Assets\Scripts\VRMenuManager.cs"
with open(menu_manager_path, "r", encoding="utf-8") as f:
    mm_content = f.read()

# Make sure dynamic camera buttons get LayoutElement minHeight = 44
old_cam_btn = """                var btnObj = CreateSimpleButton(camButtonContainer, label, 42, bgCol, textCol, () =>"""
new_cam_btn = """                var btnObj = CreateSimpleButton(camButtonContainer, label, 46, bgCol, textCol, () =>"""

if old_cam_btn in mm_content:
    mm_content = mm_content.replace(old_cam_btn, new_cam_btn)

# In CreateSimpleButton: add LayoutElement to ensure buttons don't collapse
old_csb = """            RectTransform rt = btnObj.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0f, height);"""

new_csb = """            RectTransform rt = btnObj.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0f, height);
            var le = btnObj.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;
            le.flexibleWidth = 1f;"""

if old_csb in mm_content:
    mm_content = mm_content.replace(old_csb, new_csb)
    print("Added LayoutElement in CreateSimpleButton.")

with open(menu_manager_path, "w", encoding="utf-8") as f:
    f.write(mm_content)
print("Updated VRMenuManager.cs camera feed layout.")

# -------------------------------------------------------------
# Topic 5: Stereo Dual-Eye HUD in HandDebugHUD.cs
# -------------------------------------------------------------
hud_path = r"C:\Users\dhamm\VR\Assets\Scripts\HandDebugHUD.cs"

hud_code = """using System;
using UnityEngine;
using UnityEngine.UI;

namespace UR3eVR
{
    /// <summary>
    /// HandDebugHUD: Stereoscopic VR HUD displaying real-time UR3e robot teleoperation telemetry.
    /// - Replaces legacy OnGUI() with a World Space Canvas rendered in BOTH eyes (Single Pass Instanced).
    /// - Follows peripheral headset view or attaches to tracking space cleanly.
    /// - Displays XYZ target vs actual coordinates, bridge connection, grip force, and pause state.
    /// </summary>
    public class HandDebugHUD : MonoBehaviour
    {
        [Header("References")]
        public PinchDetector pinchDetector;
        public GripperBridge gripperBridge;
        public VRHandMover vrHandMover;
        public UR3eStarterPoint starterPoint;
        public SafetyBoundaryVisualizer safetyVisualizer;
        public script webcamScript;
        public Transform centerEyeAnchor;

        [Header("HUD Canvas Settings")]
        public float hudDistance = 1.35f;
        public float hudHeightOffset = -0.18f;
        public float hudSideOffset = -0.42f; // Left peripheral view
        public float followSpeed = 6.0f;

        // UI GameObjects
        private GameObject hudCanvasObj;
        private Canvas hudCanvas;
        private Text titleText;
        private Text telemetryText;
        private Text pauseBannerText;
        private Text helpText;
        private Image backgroundImage;
        private Material stereoUIMat;

        private void Start()
        {
            FindReferences();
            BuildStereoHUDCanvas();
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
            hudCanvasObj = new GameObject("StereoHandDebugHUD_Canvas");
            hudCanvasObj.transform.SetParent(transform, false);

            hudCanvas = hudCanvasObj.AddComponent<Canvas>();
            hudCanvas.renderMode = RenderMode.WorldSpace;
            hudCanvasObj.AddComponent<CanvasScaler>();

            var gr = hudCanvasObj.AddComponent<GraphicRaycaster>();

            RectTransform canvasRT = hudCanvasObj.GetComponent<RectTransform>();
            canvasRT.sizeDelta = new Vector2(480f, 320f);
            canvasRT.localScale = Vector3.one * 0.0016f; // ~0.75m wide in VR

            Shader stereoShader = Shader.Find("UR3eVR/StereoUI") 
                               ?? Shader.Find("UI/Default");
            stereoUIMat = new Material(stereoShader);

            // Background Panel
            GameObject bgObj = new GameObject("Background");
            bgObj.transform.SetParent(hudCanvasObj.transform, false);
            RectTransform bgRT = bgObj.AddComponent<RectTransform>();
            bgRT.anchorMin = Vector2.zero;
            bgRT.anchorMax = Vector2.one;
            bgRT.offsetMin = Vector2.zero;
            bgRT.offsetMax = Vector2.zero;

            backgroundImage = bgObj.AddComponent<Image>();
            backgroundImage.material = stereoUIMat;
            backgroundImage.color = new Color(0.04f, 0.07f, 0.12f, 0.88f);

            var ol = bgObj.AddComponent<Outline>();
            ol.effectColor = new Color(0.0f, 0.85f, 1.0f, 0.40f);
            ol.effectDistance = new Vector2(2, -2);

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Font.CreateDynamicFontFromOSFont("Arial", 16);

            // Title
            GameObject titleObj = new GameObject("Title");
            titleObj.transform.SetParent(bgObj.transform, false);
            RectTransform titleRT = titleObj.AddComponent<RectTransform>();
            titleRT.anchorMin = new Vector2(0.04f, 0.86f);
            titleRT.anchorMax = new Vector2(0.96f, 0.98f);
            titleRT.offsetMin = Vector2.zero;
            titleRT.offsetMax = Vector2.zero;

            titleText = titleObj.AddComponent<Text>();
            titleText.material = stereoUIMat;
            titleText.font = font;
            titleText.fontSize = 18;
            titleText.fontStyle = FontStyle.Bold;
            titleText.alignment = TextAnchor.MiddleLeft;
            titleText.color = new Color(0.0f, 1.0f, 0.85f, 1.0f);
            titleText.text = "UR3e TELEOPERATION TELEMETRY (STEREO)";

            // Pause Banner
            GameObject pauseObj = new GameObject("PauseBanner");
            pauseObj.transform.SetParent(bgObj.transform, false);
            RectTransform pauseRT = pauseObj.AddComponent<RectTransform>();
            pauseRT.anchorMin = new Vector2(0.55f, 0.86f);
            pauseRT.anchorMax = new Vector2(0.96f, 0.98f);
            pauseRT.offsetMin = Vector2.zero;
            pauseRT.offsetMax = Vector2.zero;

            pauseBannerText = pauseObj.AddComponent<Text>();
            pauseBannerText.material = stereoUIMat;
            pauseBannerText.font = font;
            pauseBannerText.fontSize = 16;
            pauseBannerText.fontStyle = FontStyle.Bold;
            pauseBannerText.alignment = TextAnchor.MiddleRight;
            pauseBannerText.color = Color.yellow;
            pauseBannerText.text = "";

            // Main Telemetry Text
            GameObject bodyObj = new GameObject("TelemetryText");
            bodyObj.transform.SetParent(bgObj.transform, false);
            RectTransform bodyRT = bodyObj.AddComponent<RectTransform>();
            bodyRT.anchorMin = new Vector2(0.04f, 0.16f);
            bodyRT.anchorMax = new Vector2(0.96f, 0.84f);
            bodyRT.offsetMin = Vector2.zero;
            bodyRT.offsetMax = Vector2.zero;

            telemetryText = bodyObj.AddComponent<Text>();
            telemetryText.material = stereoUIMat;
            telemetryText.font = font;
            telemetryText.fontSize = 13;
            telemetryText.lineSpacing = 1.15f;
            telemetryText.alignment = TextAnchor.UpperLeft;
            telemetryText.color = Color.white;

            // Help Footer
            GameObject helpObj = new GameObject("HelpFooter");
            helpObj.transform.SetParent(bgObj.transform, false);
            RectTransform helpRT = helpObj.AddComponent<RectTransform>();
            helpRT.anchorMin = new Vector2(0.04f, 0.02f);
            helpRT.anchorMax = new Vector2(0.96f, 0.14f);
            helpRT.offsetMin = Vector2.zero;
            helpRT.offsetMax = Vector2.zero;

            helpText = helpObj.AddComponent<Text>();
            helpText.material = stereoUIMat;
            helpText.font = font;
            helpText.fontSize = 12;
            helpText.alignment = TextAnchor.MiddleLeft;
            helpText.color = new Color(0.6f, 0.85f, 1.0f, 0.85f);
            helpText.text = "Left Remote: [X] Set Origin/Home | [Y] Pause/Resume | [L-Stick] Auto-Shake";
        }

        private void LateUpdate()
        {
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

            // Smooth Peripheral Billboard Placement
            if (centerEyeAnchor != null && hudCanvasObj != null)
            {
                Vector3 headForward = centerEyeAnchor.forward;
                headForward.y = 0f;
                if (headForward.sqrMagnitude > 0.001f) headForward.Normalize();
                else headForward = Vector3.forward;

                Vector3 headRight = Vector3.Cross(Vector3.up, headForward).normalized;

                Vector3 targetPos = centerEyeAnchor.position
                                  + headForward * hudDistance
                                  + headRight * hudSideOffset
                                  + Vector3.up * hudHeightOffset;

                hudCanvasObj.transform.position = Vector3.Lerp(hudCanvasObj.transform.position, targetPos, Time.deltaTime * followSpeed);
                hudCanvasObj.transform.rotation = Quaternion.LookRotation(hudCanvasObj.transform.position - centerEyeAnchor.position);
            }

            // Update Telemetry Display
            UpdateTelemetryContent();
        }

        private void UpdateTelemetryContent()
        {
            if (telemetryText == null) return;

            bool isPaused = vrHandMover != null && vrHandMover.isTeleoperationPaused;
            if (pauseBannerText != null)
            {
                pauseBannerText.text = isPaused ? "[PAUSED - FROZEN]" : "[TRACKING ACTIVE]";
                pauseBannerText.color = isPaused ? new Color(1f, 0.25f, 0.25f, 1f) : new Color(0.2f, 1f, 0.4f, 1f);
            }

            string bridgeStatus = (gripperBridge != null && gripperBridge.isConnected) ? "<color=#00FF88>CONNECTED</color>" : "<color=#FF4444>OFFLINE</color>";
            string modeStatus = "CLOSED-LOOP 6-DOF POSE";

            Vector3 tgtMm = (vrHandMover != null) ? vrHandMover.targetRobotPos * 1000f : Vector3.zero;
            Vector3 actMm = (gripperBridge != null) ? gripperBridge.actualRobotPos * 1000f : Vector3.zero;
            float posErrMm = Vector3.Distance(tgtMm, actMm);

            string homeStatus = (starterPoint != null && starterPoint.isAtHome) ? "<color=#00FF88>READY AT HOME</color>" : "<color=#00D4FF>OFF-HOME</color>";
            string pinchDesc = (pinchDetector != null) ? $"Pinch: {pinchDetector.normalizedPinch * 100f:F0}% ({pinchDetector.gripPositionMm:F1} mm)" : "Pinch: N/A";
            string barrierDesc = (safetyVisualizer != null) ? $"Dist to Base: {safetyVisualizer.distToBase * 100f:F1} cm | Limit: {safetyVisualizer.outerRadius * 100f:F0} cm" : "";

            telemetryText.text = 
                $"Bridge: {bridgeStatus} | Mode: {modeStatus}\\n" +
                $"Starter Point: {homeStatus} | {barrierDesc}\\n\\n" +
                $"<color=#00F0FF>Target XYZ:</color>  X:{tgtMm.x,5:F0}  Y:{tgtMm.y,5:F0}  Z:{tgtMm.z,5:F0} mm\\n" +
                $"<color=#00FF88>Actual XYZ:</color>  X:{actMm.x,5:F0}  Y:{actMm.y,5:F0}  Z:{actMm.z,5:F0} mm  (Err: {posErrMm:F1} mm)\\n\\n" +
                $"{pinchDesc}";
        }
    }
}
"""

with open(hud_path, "w", encoding="utf-8") as f:
    f.write(hud_code)
print("Updated HandDebugHUD.cs to Stereo World Space Canvas successfully.")

print("All 5 topics successfully generated and updated!")
