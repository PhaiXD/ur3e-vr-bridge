using System;
using UnityEngine;

namespace UR3eVR
{
    /// <summary>
    /// VRControllerGripPoseHandler: Ensures Meta Quest Natural Controller-Driven Hand Poses
    /// are active for the left remote hand, and prevents interference with bare-hand robot teleoperation.
    /// - Right hand is strictly bare-hand tracking for UR3e robot teleoperation.
    /// - Left hand utilizes Meta Quest native Natural Controller-driven hand poses when holding controller.
    /// </summary>
    public class VRControllerGripPoseHandler : MonoBehaviour
    {
        public OVRHand ovrHand;
        public OVRSkeleton ovrSkeleton;
        public OVRInput.Controller controller = OVRInput.Controller.LTouch;
        public bool isRightHand = false;

        private void Awake()
        {
            DetectHandSide();
            if (isRightHand)
            {
                // Right hand is dedicated purely to UR3e robot control with bare hand tracking!
                enabled = false;
                return;
            }

            EnableNativeControllerHandPoses();
        }

        private void Start()
        {
            DetectHandSide();
            if (isRightHand)
            {
                enabled = false;
                return;
            }

            EnableNativeControllerHandPoses();
        }

        private void DetectHandSide()
        {
            if (ovrHand != null)
            {
                var ht = ovrHand.GetHand();
                if (ht == OVRPlugin.Hand.HandRight)
                {
                    isRightHand = true;
                    return;
                }
                else if (ht == OVRPlugin.Hand.HandLeft)
                {
                    isRightHand = false;
                    return;
                }
            }

            var skel = ovrSkeleton != null ? ovrSkeleton : GetComponent<OVRSkeleton>();
            if (skel != null)
            {
                var st = skel.GetSkeletonType();
                if (st == OVRSkeleton.SkeletonType.XRHandRight || st == OVRSkeleton.SkeletonType.HandRight)
                {
                    isRightHand = true;
                    return;
                }
                else if (st == OVRSkeleton.SkeletonType.XRHandLeft || st == OVRSkeleton.SkeletonType.HandLeft)
                {
                    isRightHand = false;
                    return;
                }
            }

            isRightHand = gameObject.name.ToLower().Contains("right");
        }

        private void EnableNativeControllerHandPoses()
        {
            if (OVRManager.instance != null)
            {
                OVRManager.instance.controllerDrivenHandPosesType = OVRManager.ControllerDrivenHandPosesType.Natural;
            }
        }
    }
}
