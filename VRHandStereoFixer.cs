using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace UR3eVR
{
    /// <summary>
    /// VRHandStereoFixer: Comprehensive Fix for Meta Quest Hand & Controller Tracking in Unity VR
    /// - True Independent Per-Hand Tracking: Each hand independently displays either as a Controller
    ///   (if physically held in hand) OR as a Bare Hand (if holding nothing), without cross-interference!
    /// - Guarantees hand mesh visibility in standalone builds (.exe & Link)
    /// - Prevents phantom controller models: Only shows controller 3D mesh when actually held
    /// - Fixes Single Pass Instanced stereo rendering: ensures World Space Canvases and Text render in BOTH eyes
    /// - Configures Left Hand Remote Laser Pointer and UI interaction
    /// </summary>
    public class VRHandStereoFixer : MonoBehaviour
    {
        [Tooltip("Custom URP Hand Material (optional, will auto-create or use URP fallback if null)")]
        public Material urpHandMaterial;

        // Static per-hand independent in-hand states accessible across all scripts
        public static bool IsLeftControllerInHand { get; private set; } = false;
        public static bool IsRightControllerInHand { get; private set; } = false;

        private static float lastLeftActiveTime = -10f;
        private static float lastRightActiveTime = -10f;
        private const float IN_HAND_TIMEOUT = 0.6f;

        private Camera centerEyeCamera;
        private Material stereoTextMaterial;

        // Tracking references cached for speed
        private GameObject leftHandObj;
        private GameObject rightHandObj;
        private SkinnedMeshRenderer leftHandSMR;
        private SkinnedMeshRenderer rightHandSMR;
        private OVRHand leftOVRHand;
        private OVRHand rightOVRHand;
        private GameObject leftControllerPrefab;
        private GameObject rightControllerPrefab;

        private void Awake()
        {
            EnsureMaterial();
            ConfigureOVRManagerSettings();
        }

        private void Start()
        {
            EnsureMaterial();
            ConfigureOVRManagerSettings();
            CacheTrackingReferences();
            UpdateHandAndControllerStates();
            FixOVRMeshRenderers();
            ApplyStereoFix();
            FixStereoWorldCanvasesAndText();
            EnsureGripPoseAndLaserPointer();
        }

        private void Update()
        {
            ConfigureOVRManagerSettings();
            UpdateHandAndControllerStates();
            FixOVRMeshRenderers();
            ApplyStereoFix();
            FixStereoWorldCanvasesAndText();
            EnsureGripPoseAndLaserPointer();
        }

        private void LateUpdate()
        {
            UpdateHandAndControllerStates();
            ApplyStereoFix();
            FixStereoWorldCanvasesAndText();
            EnsureGripPoseAndLaserPointer();
        }

        /// <summary>
        /// Multi-sensor independent detection for whether a controller is physically held in the user's hand.
        /// Combines Meta Quest in-hand state, capacitive touch, buttons, and physical IMU movement.
        /// </summary>
        public static bool CheckControllerInHand(OVRInput.Controller controller, OVRHand ovrHand = null)
        {
            if (!OVRInput.IsControllerConnected(controller)) return false;

            bool isLeft = (controller == OVRInput.Controller.LTouch);
            OVRInput.Hand handSide = isLeft ? OVRInput.Hand.HandLeft : OVRInput.Hand.HandRight;

            // 1. Direct System Recognition (Quest Multimodal)
            try
            {
                var inHandState = OVRInput.GetControllerIsInHandState(handSide);
                if (inHandState == OVRInput.ControllerInHandState.ControllerInHand)
                {
                    if (isLeft) lastLeftActiveTime = Time.time;
                    else lastRightActiveTime = Time.time;
                    return true;
                }
                else if (inHandState == OVRInput.ControllerInHandState.ControllerNotInHand)
                {
                    return false;
                }
            }
            catch { }

            // 2. Capacitive Touch Sensors & Physical Button / Trigger Inputs
            bool hasTouch = false;
            if (isLeft)
            {
                hasTouch = OVRInput.Get(OVRInput.Touch.PrimaryIndexTrigger, controller)
                        || OVRInput.Get(OVRInput.Touch.PrimaryThumbstick, controller)
                        || OVRInput.Get(OVRInput.Touch.Three, controller) // X
                        || OVRInput.Get(OVRInput.Touch.Four, controller)  // Y
                        || OVRInput.Get(OVRInput.Touch.PrimaryThumbRest, controller)
                        || OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, controller) > 0.05f
                        || OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, controller) > 0.05f
                        || OVRInput.Get(OVRInput.Button.Any, controller);
            }
            else
            {
                hasTouch = OVRInput.Get(OVRInput.Touch.PrimaryIndexTrigger, controller)
                        || OVRInput.Get(OVRInput.Touch.PrimaryThumbstick, controller)
                        || OVRInput.Get(OVRInput.Touch.One, controller) // A
                        || OVRInput.Get(OVRInput.Touch.Two, controller) // B
                        || OVRInput.Get(OVRInput.Touch.PrimaryThumbRest, controller)
                        || OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, controller) > 0.05f
                        || OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, controller) > 0.05f
                        || OVRInput.Get(OVRInput.Button.Any, controller);
            }

            // 3. Controller Physical Motion
            // A controller resting on a table has zero velocity and angular velocity.
            // A controller held in a human hand always exhibits micro-movements (hand tremor > 0.003).
            Vector3 vel = OVRInput.GetLocalControllerVelocity(controller);
            Vector3 angVel = OVRInput.GetLocalControllerAngularVelocity(controller);
            bool isMoving = (vel.sqrMagnitude > 0.005f) || (angVel.sqrMagnitude > 0.02f);

            bool isHandTracked = (ovrHand != null && ovrHand.IsTracked);

            if (hasTouch || (isMoving && !isHandTracked))
            {
                if (isLeft) lastLeftActiveTime = Time.time;
                else lastRightActiveTime = Time.time;
                return true;
            }

            // 4. Grace Period (Timeout)
            float lastTime = isLeft ? lastLeftActiveTime : lastRightActiveTime;
            if (Time.time - lastTime < IN_HAND_TIMEOUT)
            {
                if (isHandTracked && (Time.time - lastTime > 0.25f))
                {
                    return false;
                }
                return true;
            }

            return false;
        }

        private void CacheTrackingReferences()
        {
            if (leftHandObj == null) leftHandObj = GameObject.Find("[BuildingBlock] Hand Tracking left");
            if (rightHandObj == null) rightHandObj = GameObject.Find("[BuildingBlock] Hand Tracking right");

            if (leftHandObj != null)
            {
                if (leftHandSMR == null) leftHandSMR = leftHandObj.GetComponent<SkinnedMeshRenderer>();
                if (leftOVRHand == null) leftOVRHand = leftHandObj.GetComponent<OVRHand>();
            }

            if (rightHandObj != null)
            {
                if (rightHandSMR == null) rightHandSMR = rightHandObj.GetComponent<SkinnedMeshRenderer>();
                if (rightOVRHand == null) rightOVRHand = rightHandObj.GetComponent<OVRHand>();
            }

            if (leftControllerPrefab == null) leftControllerPrefab = GameObject.Find("OVRControllerPrefab_Left");
            if (rightControllerPrefab == null) rightControllerPrefab = GameObject.Find("OVRControllerPrefab_Right");
        }

        private void UpdateHandAndControllerStates()
        {
            if (leftHandObj == null || rightHandObj == null || leftControllerPrefab == null || rightControllerPrefab == null)
            {
                CacheTrackingReferences();
            }

            // 1. Evaluate independent states
            IsLeftControllerInHand = CheckControllerInHand(OVRInput.Controller.LTouch, leftOVRHand);
            IsRightControllerInHand = CheckControllerInHand(OVRInput.Controller.RTouch, rightOVRHand);

            // 2. Control Left Controller 3D Model
            if (leftControllerPrefab != null && leftControllerPrefab.activeSelf != IsLeftControllerInHand)
            {
                leftControllerPrefab.SetActive(IsLeftControllerInHand);
            }

            // 3. Control Right Controller 3D Model
            if (rightControllerPrefab != null && rightControllerPrefab.activeSelf != IsRightControllerInHand)
            {
                rightControllerPrefab.SetActive(IsRightControllerInHand);
            }

            // 4. Control Left Hand Mesh visibility
            if (leftHandSMR != null)
            {
                bool shouldShowLeftHand = !IsLeftControllerInHand;
                if (leftHandSMR.enabled != shouldShowLeftHand)
                {
                    leftHandSMR.enabled = shouldShowLeftHand;
                }
            }

            // 5. Control Right Hand Mesh visibility
            if (rightHandSMR != null)
            {
                bool shouldShowRightHand = !IsRightControllerInHand;
                if (rightHandSMR.enabled != shouldShowRightHand)
                {
                    rightHandSMR.enabled = shouldShowRightHand;
                }
            }
        }

        private void ConfigureOVRManagerSettings()
        {
            var mgr = OVRManager.instance ?? FindAnyObjectByType<OVRManager>();
            if (mgr != null)
            {
                // CRITICAL FIX: Disable global ControllerDrivenHandPoses so that bare hands
                // remain genuine optical tracking with natural fingers, rather than being forced
                // into conforming controller grip poses whenever ANY controller is picked up!
                if (mgr.controllerDrivenHandPosesType != OVRManager.ControllerDrivenHandPosesType.None)
                {
                    mgr.controllerDrivenHandPosesType = OVRManager.ControllerDrivenHandPosesType.None;
                }
                if (!mgr.SimultaneousHandsAndControllersEnabled)
                {
                    mgr.SimultaneousHandsAndControllersEnabled = true;
                }
            }

            // Ensure OVRControllerHelper displays models whenever their prefab is active
            var helpers = FindObjectsByType<OVRControllerHelper>(FindObjectsSortMode.None);
            foreach (var h in helpers)
            {
                if (h.m_showState != OVRInput.InputDeviceShowState.Always)
                {
                    h.m_showState = OVRInput.InputDeviceShowState.Always;
                }
            }
        }

        private void EnsureMaterial()
        {
            if (urpHandMaterial == null)
            {
                Shader urpShader = Shader.Find("UR3eVR/URPHandOutline")
                                ?? Shader.Find("UR3eVR/StereoUnlitColor")
                                ?? Shader.Find("Universal Render Pipeline/Lit")
                                ?? Shader.Find("Universal Render Pipeline/Unlit");

                if (urpShader != null)
                {
                    urpHandMaterial = new Material(urpShader);
                    urpHandMaterial.name = "RuntimeURPHandMaterial";
                    urpHandMaterial.enableInstancing = true;

                    if (urpHandMaterial.HasProperty("_Cull"))
                    {
                        urpHandMaterial.SetFloat("_Cull", 0f); // Two-sided
                    }

                    if (urpHandMaterial.HasProperty("_ColorPrimary"))
                    {
                        urpHandMaterial.SetColor("_ColorPrimary", new Color(0.95f, 0.98f, 1.0f, 1.0f));
                    }
                    if (urpHandMaterial.HasProperty("_ColorTop"))
                    {
                        urpHandMaterial.SetColor("_ColorTop", new Color(0.12f, 0.16f, 0.22f, 0.85f));
                    }
                    if (urpHandMaterial.HasProperty("_ColorBottom"))
                    {
                        urpHandMaterial.SetColor("_ColorBottom", new Color(0.04f, 0.06f, 0.10f, 0.85f));
                    }
                    if (urpHandMaterial.HasProperty("_RimFactor"))
                    {
                        urpHandMaterial.SetFloat("_RimFactor", 2.2f);
                    }
                    if (urpHandMaterial.HasProperty("_FresnelPower"))
                    {
                        urpHandMaterial.SetFloat("_FresnelPower", 1.5f);
                    }
                    if (urpHandMaterial.HasProperty("_HandAlpha"))
                    {
                        urpHandMaterial.SetFloat("_HandAlpha", 0.85f);
                    }
                    if (urpHandMaterial.HasProperty("_BaseAlpha"))
                    {
                        urpHandMaterial.SetFloat("_BaseAlpha", 0.35f);
                    }
                }
            }
        }

        public void FixOVRMeshRenderers()
        {
            var omrs = FindObjectsByType<OVRMeshRenderer>(FindObjectsSortMode.None);
            if (omrs == null || omrs.Length == 0) return;

            foreach (var omr in omrs)
            {
                if (omr == null) continue;

                var skel = omr.GetComponent<OVRSkeleton>();
                bool isRight = omr.gameObject.name.ToLower().Contains("right") ||
                    (skel != null && (skel.GetSkeletonType() == OVRSkeleton.SkeletonType.XRHandRight || skel.GetSkeletonType() == OVRSkeleton.SkeletonType.HandRight));

                bool shouldShow = isRight ? !IsRightControllerInHand : !IsLeftControllerInHand;
                var smr = omr.GetComponent<SkinnedMeshRenderer>();
                if (smr != null && smr.enabled != shouldShow)
                {
                    smr.enabled = shouldShow;
                }

                if (!shouldShow) continue;

                try
                {
                    var confField = typeof(OVRMeshRenderer).GetField("_confidenceBehavior", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (confField != null) confField.SetValue(omr, OVRMeshRenderer.ConfidenceBehavior.None);
                }
                catch { }

                try
                {
                    var gestField = typeof(OVRMeshRenderer).GetField("_systemGestureBehavior", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (gestField != null) gestField.SetValue(omr, OVRMeshRenderer.SystemGestureBehavior.None);
                }
                catch { }

                try
                {
                    var origMatField = typeof(OVRMeshRenderer).GetField("_originalMaterial", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (origMatField != null && urpHandMaterial != null) origMatField.SetValue(omr, urpHandMaterial);
                }
                catch { }

                var ovrMesh = omr.GetComponent<OVRMesh>();
                var ovrSkeleton = omr.GetComponent<OVRSkeleton>();
                if (!omr.IsInitialized && ovrMesh != null && ovrMesh.IsInitialized && ovrSkeleton != null && ovrSkeleton.IsInitialized)
                {
                    omr.ForceRebind();
                }
            }
        }

        public void ApplyStereoFix()
        {
            var smrs = FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None);
            if (smrs == null || smrs.Length == 0) return;

            foreach (var smr in smrs)
            {
                if (smr == null || smr.gameObject == null) continue;

                string goName = smr.gameObject.name.ToLower();
                bool isHand = goName.Contains("hand") || 
                              smr.GetComponent<OVRHand>() != null || 
                              smr.GetComponent<OVRSkeleton>() != null ||
                              smr.GetComponent<OVRMeshRenderer>() != null;

                if (isHand)
                {
                    // 1. Never cull hands offscreen in VR stereo
                    if (!smr.updateWhenOffscreen)
                    {
                        smr.updateWhenOffscreen = true;
                    }

                    // 2. Only show hand mesh if controller is NOT in hand on this side
                    bool isRight = goName.Contains("right") ||
                        (smr.GetComponent<OVRSkeleton>() != null && (smr.GetComponent<OVRSkeleton>().GetSkeletonType() == OVRSkeleton.SkeletonType.XRHandRight || smr.GetComponent<OVRSkeleton>().GetSkeletonType() == OVRSkeleton.SkeletonType.HandRight));
                    bool shouldShow = isRight ? !IsRightControllerInHand : !IsLeftControllerInHand;

                    if (smr.sharedMesh != null && smr.enabled != shouldShow)
                    {
                        smr.enabled = shouldShow;
                    }

                    // 3. Fix materials for VR Single Pass Instanced
                    var mats = smr.sharedMaterials;
                    if (mats == null || mats.Length == 0) continue;

                    bool needReassign = false;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        Material m = mats[i];
                        if (m == null)
                        {
                            if (urpHandMaterial != null)
                            {
                                mats[i] = urpHandMaterial;
                                needReassign = true;
                            }
                            continue;
                        }

                        bool isLegacy = m.shader != null && (m.shader.name.Contains("Oculus Sample") || m.shader.name.Contains("Alpha Hand Outline") || m.shader.name.Contains("Sprites/Default"));
                        if (isLegacy || !m.enableInstancing)
                        {
                            if (urpHandMaterial != null)
                            {
                                mats[i] = urpHandMaterial;
                                needReassign = true;
                            }
                        }
                    }

                    if (needReassign)
                    {
                        smr.sharedMaterials = mats;

                        var ovrRenderer = smr.GetComponent<OVRMeshRenderer>();
                        if (ovrRenderer != null && mats.Length > 0 && mats[0] != null)
                        {
                            ovrRenderer.SetMaterial(mats[0]);
                        }
                    }
                }
            }
        }

        public void FixStereoWorldCanvasesAndText()
        {
            if (centerEyeCamera == null || !centerEyeCamera.gameObject.activeInHierarchy)
            {
                var cec = GameObject.Find("CenterEyeAnchor");
                if (cec != null) centerEyeCamera = cec.GetComponent<Camera>();
                if (centerEyeCamera == null && Camera.main != null) centerEyeCamera = Camera.main;
                if (centerEyeCamera == null)
                {
                    var allCams = FindObjectsByType<Camera>(FindObjectsSortMode.None);
                    foreach (var cam in allCams)
                    {
                        if (cam.gameObject.name.Contains("CenterEye") || cam.stereoEnabled || cam.targetDisplay == 0)
                        {
                            centerEyeCamera = cam;
                            break;
                        }
                    }
                }
            }

            if (centerEyeCamera != null)
            {
                if (centerEyeCamera.tag != "MainCamera")
                {
                    try { centerEyeCamera.tag = "MainCamera"; } catch { }
                }

                // Continuously guarantee worldCamera on ALL WorldSpace Canvases in the scene
                // In XR Single Pass Instanced, null worldCamera causes single-eye (left only) rendering!
                var canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var c in canvases)
                {
                    if (c == null) continue;
                    if (c.renderMode == RenderMode.WorldSpace)
                    {
                        if (c.worldCamera != centerEyeCamera)
                        {
                            c.worldCamera = centerEyeCamera;
                        }

                        // Enable GPU instancing on all UI Image materials for SPI stereo
                        var images = c.GetComponentsInChildren<Image>(true);
                        foreach (var img in images)
                        {
                            if (img != null && img.material != null && !img.material.enableInstancing)
                            {
                                img.material.enableInstancing = true;
                            }
                        }
                    }
                }
            }

            if (stereoTextMaterial == null)
            {
                Shader s = Shader.Find("UR3eVR/StereoTextShader");
                if (s != null)
                {
                    stereoTextMaterial = new Material(s);
                    stereoTextMaterial.name = "VR_StereoText_Material";
                    stereoTextMaterial.enableInstancing = true;
                }
            }

            if (stereoTextMaterial != null)
            {
                var textMeshes = FindObjectsByType<TextMesh>(FindObjectsSortMode.None);
                foreach (var tm in textMeshes)
                {
                    if (tm == null) continue;
                    var mr = tm.GetComponent<MeshRenderer>();
                    if (mr != null)
                    {
                        var sm = mr.sharedMaterial;
                        if (sm == null || sm.shader == null || sm.shader.name != "UR3eVR/StereoTextShader")
                        {
                            Texture fTex = (tm.font != null && tm.font.material != null) ? tm.font.material.mainTexture : (sm != null ? sm.mainTexture : null);
                            Material m = new Material(stereoTextMaterial);
                            if (fTex != null) m.mainTexture = fTex;
                            m.enableInstancing = true;
                            mr.sharedMaterial = m;
                        }
                    }
                }
            }
        }

        public void EnsureGripPoseAndLaserPointer()
        {
            // Ensure VRRemoteLaserPointer exists and operates smoothly
            var laser = VRRemoteLaserPointer.Instance ?? FindAnyObjectByType<VRRemoteLaserPointer>();
            if (laser == null)
            {
                GameObject laserObj = new GameObject("VR_Remote_LaserPointer");
                laser = laserObj.AddComponent<VRRemoteLaserPointer>();
                laser.controller = OVRInput.Controller.LTouch;
            }
        }
    }
}
