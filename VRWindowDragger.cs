using System;
using UnityEngine;
using UnityEngine.UI;

namespace UR3eVR
{
    /// <summary>
    /// VRWindowDragger: Allows repositioning World Space UI Windows ONLY with the Left Remote Controller.
    /// Right Hand is 100% reserved for UR3e robot teleoperation and will NEVER drag or interfere with UI!
    /// Supports direct proximity hand grab AND remote laser pointer trigger grab.
    /// Guaranteed single-window grab lock so only one UI can be dragged at a time.
    /// </summary>
    public class VRWindowDragger : MonoBehaviour
    {
        public static VRWindowDragger ActiveDragger = null;

        [Header("Target Window")]
        public Transform windowTransform;

        [Header("Grabbing Config")]
        public float grabDistance = 0.35f;
        public Image handleImage;
        public Color normalColor = new Color(0.0f, 0.70f, 0.85f, 0.75f);
        public Color hoverColor = new Color(0.0f, 1.0f, 0.75f, 0.95f);
        public Color grabbedColor = new Color(1.0f, 0.90f, 0.20f, 0.95f);

        [Header("References")]
        public Transform leftControllerAnchor;
        public Transform headTransform;

        public bool isGrabbed { get; private set; } = false;
        public bool isHovered { get; private set; } = false;
        public bool isLaserGrabbed = false;

        private Transform grabberTransform;
        private Vector3 grabOffset;
        private Quaternion grabRotOffset;

        private void Start()
        {
            if (windowTransform == null)
            {
                Canvas c = GetComponentInParent<Canvas>();
                if (c != null) windowTransform = c.transform;
                else windowTransform = transform.parent != null ? transform.parent : transform;
            }

            FindReferences();
        }

        private void FindReferences()
        {
            if (leftControllerAnchor == null)
            {
                string[] possibleNames = { "LeftControllerAnchor", "LeftControllerInHandAnchor", "LeftHandAnchor" };
                foreach (var n in possibleNames)
                {
                    var found = GameObject.Find(n);
                    if (found != null) { leftControllerAnchor = found.transform; break; }
                }
            }

            if (headTransform == null)
            {
                var cec = GameObject.Find("CenterEyeAnchor");
                if (cec != null) headTransform = cec.transform;
                else if (Camera.main != null) headTransform = Camera.main.transform;
            }
        }

        private void Update()
        {
            if (leftControllerAnchor == null) FindReferences();

            if (isGrabbed)
            {
                if (!isLaserGrabbed)
                {
                    UpdateDirectDragging();
                }
            }
            else
            {
                CheckLeftRemoteNear();
            }

            UpdateHandleVisual();
        }

        private void CheckLeftRemoteNear()
        {
            // Only allow grab if no other window is currently being dragged
            if (ActiveDragger != null && ActiveDragger != this)
            {
                isHovered = false;
                return;
            }

            if (leftControllerAnchor == null)
            {
                isHovered = false;
                return;
            }

            float dist = Vector3.Distance(transform.position, leftControllerAnchor.position);
            isHovered = (dist <= grabDistance);

            if (isHovered)
            {
                bool leftGrip = OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.LTouch)
                             || OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, OVRInput.Controller.LTouch) > 0.4f;

                if (leftGrip)
                {
                    StartGrab(leftControllerAnchor, false);
                }
            }
        }

        public void StartGrab(Transform grabber, bool viaLaser = false)
        {
            if (ActiveDragger != null && ActiveDragger != this) return;
            ActiveDragger = this;

            if (grabber == null) grabber = leftControllerAnchor;
            if (grabber == null) return;

            isGrabbed = true;
            isLaserGrabbed = viaLaser;
            grabberTransform = grabber;
            if (windowTransform != null)
            {
                grabOffset = windowTransform.position - grabber.position;
                grabRotOffset = Quaternion.Inverse(grabber.rotation) * windowTransform.rotation;
            }
        }

        private void UpdateDirectDragging()
        {
            if (ActiveDragger != this || grabberTransform == null || windowTransform == null)
            {
                EndGrab();
                return;
            }

            bool stillHolding = OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.LTouch)
                             || OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, OVRInput.Controller.LTouch) > 0.25f;
            try { stillHolding |= Input.GetMouseButton(0); } catch { }

            if (!stillHolding)
            {
                EndGrab();
                return;
            }

            Vector3 targetPos = grabberTransform.position + grabOffset;

            if (headTransform != null)
            {
                Vector3 toHead = headTransform.position - windowTransform.position;
                toHead.y = 0;
                if (toHead.sqrMagnitude > 0.01f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(-toHead.normalized, Vector3.up);
                    windowTransform.rotation = Quaternion.Slerp(windowTransform.rotation, targetRot, Time.deltaTime * 20f);
                }
            }

            windowTransform.position = Vector3.Lerp(windowTransform.position, targetPos, Time.deltaTime * 20f);
        }

        public void EndGrab()
        {
            if (ActiveDragger == this)
            {
                ActiveDragger = null;
            }
            isGrabbed = false;
            isLaserGrabbed = false;
            grabberTransform = null;
        }

        public void SetHovered(bool hovered)
        {
            isHovered = hovered;
            UpdateHandleVisual();
        }

        private void UpdateHandleVisual()
        {
            if (handleImage == null) handleImage = GetComponent<Image>();
            if (handleImage == null) return;

            Color targetColor = isGrabbed ? grabbedColor : (isHovered ? hoverColor : normalColor);
            handleImage.color = Color.Lerp(handleImage.color, targetColor, Time.deltaTime * 15f);
        }
    }
}
