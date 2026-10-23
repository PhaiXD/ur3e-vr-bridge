using System;
using UnityEngine;

namespace UR3eVR
{
    public class PinchDetector : MonoBehaviour
    {
        public enum PinchMode
        {
            Distance,       // Measures physical distance between thumb and index tips
            PinchStrength,  // Uses Meta's built-in ML pinch strength (0.0 to 1.0)
            Hybrid          // Uses physical distance + boosts with ML pinch on contact
        }

        [Header("Mode & Tracking References")]
        [Tooltip("Tracking mode to determine pinch amount")]
        public PinchMode mode = PinchMode.Hybrid;

        [Tooltip("Reference to OVRHand (auto-detected if null)")]
        public OVRHand ovrHand;

        [Tooltip("Reference to OVRSkeleton (auto-detected if null)")]
        public OVRSkeleton ovrSkeleton;

        [Header("Distance Calibration (Meters)")]
        [Tooltip("Distance when fingers are fully open (corresponds to Gripper POS = 0)")]
        public float maxOpenDistance = 0.105f; // ~10.5 cm open (allows relaxed hand to be ~35-50% open)

        [Tooltip("Distance when fingers are touching (corresponds to Gripper POS = 255)")]
        public float fullPinchDistance = 0.020f; // ~2.0 cm closed (bone centers when skin touches)

        [Header("Smoothing & Filter")]
        [Range(0.01f, 1f)]
        [Tooltip("Response speed when closing/pinching (Higher = more responsive)")]
        public float smoothing = 0.35f;

        [Range(0.01f, 1f)]
        [Tooltip("Response speed when opening/releasing (Lower = gentler, gradual release)")]
        public float releaseSmoothing = 0.10f;

        [Tooltip("Maximum rate of pinch decrease per second (prevents snapping wide open)")]
        public float maxReleaseSpeed = 1.25f; // ~0.8s from full pinch to fully open

        [Header("Live Outputs (Read Only)")]
        public bool isTracked = false;
        public float rawDistanceMeters = 0f;
        public float rawDistanceCm = 0f;
        [Range(0f, 1f)]
        public float normalizedPinch = 0f;     // 0.0 = fully open, 1.0 = fully closed
        [Range(0, 255)]
        public int gripperPos = 0;              // 0 = open, 255 = fully closed
        public string trackingSource = "Initializing...";

        public Transform thumbTipTransform;
        public Transform indexTipTransform;
        private float smoothedPinch = 0f;

        private void Awake()
        {
            FindReferences();
        }

        private void OnEnable()
        {
            FindReferences();
        }

        private void FindReferences()
        {
            if (ovrHand == null)
            {
                ovrHand = GetComponent<OVRHand>();
                if (ovrHand == null) ovrHand = GetComponentInParent<OVRHand>();
            }

            if (ovrSkeleton == null)
            {
                ovrSkeleton = GetComponent<OVRSkeleton>();
                if (ovrSkeleton == null) ovrSkeleton = GetComponentInParent<OVRSkeleton>();
            }
        }

        private void Update()
        {
            if (ovrHand == null)
            {
                FindReferences();
                if (ovrHand == null)
                {
                    isTracked = false;
                    trackingSource = "OVRHand Not Found";
                    return;
                }
            }

            // Ensure bone references are cached as soon as skeleton is ready
            if (thumbTipTransform == null || indexTipTransform == null)
            {
                FindBoneTransforms();
            }

            isTracked = ovrHand.IsTracked;
            if (!isTracked)
            {
                trackingSource = "Hand Not In Camera View";
                return;
            }

            float targetPinch = 0f;
            float mlStrength = ovrHand.GetFingerPinchStrength(OVRHand.HandFinger.Index);

            if (mode == PinchMode.PinchStrength)
            {
                targetPinch = mlStrength;
                trackingSource = "ML Pinch Strength";
            }
            else
            {
                if (thumbTipTransform != null && indexTipTransform != null)
                {
                    rawDistanceMeters = Vector3.Distance(thumbTipTransform.position, indexTipTransform.position);
                    rawDistanceCm = rawDistanceMeters * 100f;

                    // InverseLerp: maxOpenDistance (open) -> 0.0, fullPinchDistance (pinch) -> 1.0
                    float distPinch = Mathf.InverseLerp(maxOpenDistance, fullPinchDistance, rawDistanceMeters);
                    distPinch = Mathf.Clamp01(distPinch);

                    if (mode == PinchMode.Hybrid)
                    {
                        // Proportional physical distance across the 2-9cm range + ML seal on pinch contact
                        targetPinch = Mathf.Max(distPinch, mlStrength);
                        trackingSource = string.Format("Hybrid ({0} <-> {1})", thumbTipTransform.name, indexTipTransform.name);
                    }
                    else
                    {
                        targetPinch = distPinch;
                        trackingSource = string.Format("Distance ({0} <-> {1})", thumbTipTransform.name, indexTipTransform.name);
                    }
                }
                else
                {
                    // Fallback to ML pinch strength while bones are initializing
                    targetPinch = mlStrength;
                    trackingSource = "Fallback: ML Pinch";
                }
            }

            // Asymmetric smoothing: responsive closing, gradual smooth release
            if (targetPinch >= smoothedPinch)
            {
                // Closing: responsive tracking
                smoothedPinch = Mathf.Lerp(smoothedPinch, targetPinch, smoothing);
            }
            else
            {
                // Releasing / Opening: smooth gradual release, prevents violent snapping wide open
                float desired = Mathf.Lerp(smoothedPinch, targetPinch, releaseSmoothing);
                float maxDelta = maxReleaseSpeed * Mathf.Max(Time.deltaTime, 0.005f);
                smoothedPinch = Mathf.MoveTowards(smoothedPinch, desired, maxDelta);
            }
            normalizedPinch = smoothedPinch;

            // Map 0.0..1.0 to Robotiq Gripper range 0..255
            gripperPos = Mathf.Clamp(Mathf.RoundToInt(normalizedPinch * 255f), 0, 255);
        }

        public void FindBoneTransforms()
        {
            if (ovrSkeleton == null || !ovrSkeleton.IsInitialized || ovrSkeleton.Bones == null)
            {
                return;
            }

            // Method 1: Check by GameObject Transform names (most reliable across OpenXR and Oculus rigs)
            foreach (var bone in ovrSkeleton.Bones)
            {
                if (bone == null || bone.Transform == null) continue;
                string boneName = bone.Transform.name.ToLowerInvariant();

                if (boneName.Contains("thumb") && (boneName.Contains("tip") || boneName.Contains("distal") || boneName.Contains("thumb3")))
                {
                    thumbTipTransform = bone.Transform;
                }
                else if (boneName.Contains("index") && (boneName.Contains("tip") || boneName.Contains("distal") || boneName.Contains("index3")))
                {
                    indexTipTransform = bone.Transform;
                }
            }

            // Method 2: Fallback to skeleton type specific bone indices
            if (thumbTipTransform == null || indexTipTransform == null)
            {
                var skelType = ovrSkeleton.GetSkeletonType();
                if (skelType == OVRSkeleton.SkeletonType.XRHandRight || skelType == OVRSkeleton.SkeletonType.XRHandLeft)
                {
                    // OpenXR Hand Skeleton layout: Index 5 = ThumbTip, Index 10 = IndexTip
                    if (ovrSkeleton.Bones.Count > 10)
                    {
                        if (thumbTipTransform == null && ovrSkeleton.Bones[5] != null)
                            thumbTipTransform = ovrSkeleton.Bones[5].Transform;
                        if (indexTipTransform == null && ovrSkeleton.Bones[10] != null)
                            indexTipTransform = ovrSkeleton.Bones[10].Transform;
                    }
                }
                else if (skelType == OVRSkeleton.SkeletonType.HandRight || skelType == OVRSkeleton.SkeletonType.HandLeft)
                {
                    // Oculus Native Hand Skeleton layout: Index 19 = Hand_ThumbTip, Index 20 = Hand_IndexTip
                    if (ovrSkeleton.Bones.Count > 20)
                    {
                        if (thumbTipTransform == null && ovrSkeleton.Bones[19] != null)
                            thumbTipTransform = ovrSkeleton.Bones[19].Transform;
                        if (indexTipTransform == null && ovrSkeleton.Bones[20] != null)
                            indexTipTransform = ovrSkeleton.Bones[20].Transform;
                    }
                }
            }

            // Method 3: Recursive search in child transforms
            if (thumbTipTransform == null || indexTipTransform == null)
            {
                SearchChildTransforms(ovrSkeleton.transform);
            }

            if (thumbTipTransform != null && indexTipTransform != null)
            {
                Debug.Log(string.Format("[PinchDetector] Successfully resolved finger tip bones: Thumb='{0}', Index='{1}'",
                    thumbTipTransform.name, indexTipTransform.name));
            }
        }

        private void SearchChildTransforms(Transform parent)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                string n = child.name.ToLowerInvariant();
                if (thumbTipTransform == null && n.Contains("thumb") && n.Contains("tip"))
                    thumbTipTransform = child;
                if (indexTipTransform == null && n.Contains("index") && n.Contains("tip"))
                    indexTipTransform = child;

                if (thumbTipTransform == null || indexTipTransform == null)
                    SearchChildTransforms(child);
            }
        }

        private void OnDrawGizmos()
        {
            if (thumbTipTransform != null && indexTipTransform != null)
            {
                Gizmos.color = Color.Lerp(Color.green, Color.red, normalizedPinch);
                Gizmos.DrawLine(thumbTipTransform.position, indexTipTransform.position);
                Gizmos.DrawWireSphere(thumbTipTransform.position, 0.008f);
                Gizmos.DrawWireSphere(indexTipTransform.position, 0.008f);
            }
        }
    }
}
