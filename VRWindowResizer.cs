using System;
using UnityEngine;
using UnityEngine.UI;

namespace UR3eVR
{
    /// <summary>
    /// VRWindowResizer: Attached to resize handles at corners/edges of UI windows.
    /// Allows scaling World Space UI Canvases (min 0.55x to max 2.2x) using Left Remote Laser Pointer or Hand Grab.
    /// Follows VisionOS glass design with smooth hover/grab visuals.
    /// </summary>
    public class VRWindowResizer : MonoBehaviour
    {
        public static VRWindowResizer ActiveResizer = null;

        [Header("Target Window")]
        public RectTransform targetCanvasRT;

        [Header("Resize Limits")]
        public float minScaleFactor = 0.55f;
        public float maxScaleFactor = 2.20f;

        [Header("Visuals")]
        public Image handleImage;
        public Color normalColor = new Color(0.15f, 0.75f, 0.95f, 0.40f);
        public Color hoverColor = new Color(0.10f, 1.0f, 0.80f, 0.90f);
        public Color activeColor = new Color(1.0f, 0.85f, 0.20f, 0.98f);

        public bool isResizing { get; private set; } = false;
        public bool isHovered { get; set; } = false;

        private Vector3 initialBaseScale = Vector3.one;
        private Vector3 initialPointerWorldPos;
        private float initialDistanceToCenter;
        private Vector3 windowCenterWorld;

        private void Start()
        {
            if (targetCanvasRT == null)
            {
                Canvas c = GetComponentInParent<Canvas>();
                if (c != null) targetCanvasRT = c.GetComponent<RectTransform>();
            }

            if (targetCanvasRT != null)
            {
                initialBaseScale = targetCanvasRT.localScale;
            }

            if (handleImage == null) handleImage = GetComponent<Image>();
            UpdateVisual();
        }

        public void StartResize(Vector3 pointerWorldPos)
        {
            if (targetCanvasRT == null) return;

            ActiveResizer = this;
            isResizing = true;
            initialPointerWorldPos = pointerWorldPos;
            windowCenterWorld = targetCanvasRT.position;
            initialDistanceToCenter = Vector3.Distance(initialPointerWorldPos, windowCenterWorld);
            if (initialDistanceToCenter < 0.05f) initialDistanceToCenter = 0.05f;

            initialBaseScale = targetCanvasRT.localScale;
            UpdateVisual();
        }

        public void UpdateResize(Vector3 currentPointerWorldPos)
        {
            if (!isResizing || targetCanvasRT == null) return;

            float currentDistance = Vector3.Distance(currentPointerWorldPos, windowCenterWorld);
            float ratio = currentDistance / initialDistanceToCenter;

            // Smooth scale application clamped to min/max
            float baseFactor = initialBaseScale.x;
            float newFactor = Mathf.Clamp(baseFactor * ratio, minScaleFactor * baseFactor, maxScaleFactor * baseFactor);

            targetCanvasRT.localScale = new Vector3(newFactor, newFactor, newFactor);
            UpdateVisual();
        }

        public void EndResize()
        {
            isResizing = false;
            if (ActiveResizer == this) ActiveResizer = null;
            UpdateVisual();
        }

        public void UpdateVisual()
        {
            if (handleImage == null) return;
            if (isResizing) handleImage.color = activeColor;
            else if (isHovered) handleImage.color = hoverColor;
            else handleImage.color = normalColor;
        }

        private void OnDisable()
        {
            if (isResizing) EndResize();
        }

        private void OnDestroy()
        {
            if (isResizing) EndResize();
        }
    }
}
