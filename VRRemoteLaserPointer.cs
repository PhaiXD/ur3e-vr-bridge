using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace UR3eVR
{
    /// <summary>
    /// VRRemoteLaserPointer: Clean, straight, non-magnetic laser pointer for Meta Quest Touch Controller.
    /// Provides 100% reliable UI raycasting, hover highlights via EventSystem, and click/drag execution.
    /// Follows Meta Quest OS style interaction (as seen in standard VR interfaces).
    /// Supports distance grabbing of UI windows via laser trigger or grip, with single-dragger locking.
    /// </summary>
    public class VRRemoteLaserPointer : MonoBehaviour
    {
        public static VRRemoteLaserPointer Instance { get; private set; }

        [Header("Controller Settings")]
        public OVRInput.Controller controller = OVRInput.Controller.LTouch;
        public float maxRayLength = 6.0f;
        public float tipForwardOffset = 0.055f;
        public float tipDownOffset = -0.015f;
        public float pointerPitchAngle = 10.0f; // Natural pointing tilt down in degrees

        [Header("Laser Colors")]
        public Color normalLaserColor = new Color(0.0f, 0.90f, 1.0f, 0.90f);    // Neon Cyan
        public Color hoverLaserColor = new Color(0.1f, 1.0f, 0.75f, 1.0f);      // Bright Aqua Hover
        public Color clickLaserColor = new Color(1.0f, 0.90f, 0.15f, 1.0f);     // Bright Gold Click

        [Header("References")]
        public Transform leftControllerAnchor;
        public Transform headTransform;
        public Transform trackingSpace;
        public BhapticsGloveFeedback bhapticsFeedback;

        private LineRenderer lineRenderer;
        private GameObject reticleObj;
        private Material lineMaterial;
        private Material reticleMaterial;

        // Current Hover & Drag States
        private Selectable currentHoverSelectable = null;
        private VRWindowDragger currentHoverDragger = null;
        private VRWindowResizer currentHoverResizer = null;
        private VRWindowDragger activeDraggingWindow = null;
        private VRWindowResizer activeResizingWindow = null;
        private Slider activeDraggingSlider = null;
        private float activeDragDistance = 1.0f;
        private Vector3 activeDragLocalOffset = Vector3.zero;

        private bool wasTriggerDown = false;
        private PointerEventData pointerEventData;
        private Vector3 snappedEndPoint = Vector3.zero;
        private float currentReticleScale = 1.0f;

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
            EnsureEventSystem();
        }

        private void EnsureEventSystem()
        {
            if (EventSystem.current == null)
            {
                var esObj = new GameObject("EventSystem");
                esObj.AddComponent<EventSystem>();
                esObj.AddComponent<StandaloneInputModule>();
            }
            pointerEventData = new PointerEventData(EventSystem.current);
        }

        private void FindReferences()
        {
            var rig = FindAnyObjectByType<OVRCameraRig>();
            if (rig != null)
            {
                if (trackingSpace == null) trackingSpace = rig.trackingSpace;
                if (headTransform == null) headTransform = rig.centerEyeAnchor;
                if (leftControllerAnchor == null)
                {
                    leftControllerAnchor = rig.leftControllerInHandAnchor ?? rig.leftControllerAnchor ?? rig.leftHandAnchor;
                }
            }

            if (leftControllerAnchor == null)
            {
                string[] possible = { "LeftControllerInHandAnchor", "LeftControllerAnchor", "LeftHandAnchor", "OVRControllerPrefab_Left" };
                foreach (var p in possible)
                {
                    var found = GameObject.Find(p);
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

        private void BuildVisuals()
        {
            lineRenderer = GetComponent<LineRenderer>();
            if (lineRenderer == null) lineRenderer = gameObject.AddComponent<LineRenderer>();

            lineRenderer.useWorldSpace = true;
            lineRenderer.positionCount = 2;
            lineRenderer.startWidth = 0.0035f;
            lineRenderer.endWidth = 0.0015f;

            Shader sh = Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Sprites/Default")
                     ?? Shader.Find("Unlit/Color");

            lineMaterial = new Material(sh);
            lineMaterial.color = normalLaserColor;
            if (lineMaterial.HasProperty("_BaseColor")) lineMaterial.SetColor("_BaseColor", normalLaserColor);
            lineRenderer.material = lineMaterial;

            // Reticle Dot
            reticleObj = new GameObject("LaserReticle");
            reticleObj.transform.SetParent(transform, false);
            var mf = reticleObj.AddComponent<MeshFilter>();
            var mr = reticleObj.AddComponent<MeshRenderer>();
            mf.mesh = CreateDiscMesh(0.012f, 24);

            reticleMaterial = new Material(sh);
            reticleMaterial.color = normalLaserColor;
            if (reticleMaterial.HasProperty("_BaseColor")) reticleMaterial.SetColor("_BaseColor", normalLaserColor);
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

        private void LateUpdate()
        {
            if (leftControllerAnchor == null || headTransform == null) FindReferences();

            Vector3 rayOrigin;
            Vector3 rayDir;

            bool isControllerTracked = VRHandStereoFixer.IsLeftControllerInHand && OVRInput.GetControllerPositionTracked(controller);
            if (!isControllerTracked)
            {
                if (lineRenderer != null && lineRenderer.enabled) lineRenderer.enabled = false;
                if (reticleObj != null && reticleObj.activeSelf) reticleObj.SetActive(false);
                return;
            }
            if (isControllerTracked && trackingSpace != null)
            {
                Vector3 localPos = OVRInput.GetLocalControllerPosition(controller);
                Quaternion localRot = OVRInput.GetLocalControllerRotation(controller);
                Vector3 worldPos = trackingSpace.TransformPoint(localPos);
                Quaternion worldRot = trackingSpace.rotation * localRot;

                // Aim pointer forward with standard Quest ergonomic pitch angle
                Quaternion aimRot = worldRot * Quaternion.Euler(pointerPitchAngle, 0f, 0f);
                rayOrigin = worldPos + worldRot * new Vector3(0f, tipDownOffset, tipForwardOffset);
                rayDir = aimRot * Vector3.forward;
            }
            else if (leftControllerAnchor != null)
            {
                Quaternion aimRot = leftControllerAnchor.rotation * Quaternion.Euler(pointerPitchAngle, 0f, 0f);
                rayOrigin = leftControllerAnchor.position + leftControllerAnchor.forward * tipForwardOffset + leftControllerAnchor.up * tipDownOffset;
                rayDir = aimRot * Vector3.forward;
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

            // Safe Trigger Input check (Index Trigger on Left Remote)
            bool isTriggerDown = OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, controller)
                              || OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, controller) > 0.45f;

            try
            {
                isTriggerDown |= Input.GetMouseButton(0);
            }
            catch { }

            bool isTriggerJustPressed = isTriggerDown && !wasTriggerDown;
            bool isTriggerJustReleased = !isTriggerDown && wasTriggerDown;

            // Handle active window dragging in 3D space via Laser Pointer
            if (activeDraggingWindow != null)
            {
                bool isStillHeld = OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, controller)
                                || OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, controller) > 0.35f;
                try { isStillHeld |= Input.GetMouseButton(0); } catch { }

                if (isStillHeld && activeDraggingWindow.windowTransform != null)
                {
                    // Item 10: Joystick Push/Pull for UI Window Distance
                    Vector2 thumbstick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, controller);
                    try
                    {
                        if (Mathf.Abs(Input.GetAxis("Vertical")) > 0.1f) thumbstick.y = Input.GetAxis("Vertical");
                    }
                    catch { }

                    if (Mathf.Abs(thumbstick.y) > 0.25f)
                    {
                        float pushPullSpeed = 1.6f;
                        activeDragDistance += thumbstick.y * pushPullSpeed * Time.deltaTime;
                        activeDragDistance = Mathf.Clamp(activeDragDistance, 0.35f, 4.5f);
                    }

                    Vector3 targetWindowPos = rayOrigin + rayDir * activeDragDistance - activeDragLocalOffset;
                    activeDraggingWindow.windowTransform.position = Vector3.Lerp(activeDraggingWindow.windowTransform.position, targetWindowPos, Time.deltaTime * 25f);

                    if (headTransform != null)
                    {
                        Vector3 toHead = headTransform.position - activeDraggingWindow.windowTransform.position;
                        toHead.y = 0;
                        if (toHead.sqrMagnitude > 0.01f)
                        {
                            Quaternion targetRot = Quaternion.LookRotation(-toHead.normalized, Vector3.up);
                            activeDraggingWindow.windowTransform.rotation = Quaternion.Slerp(activeDraggingWindow.windowTransform.rotation, targetRot, Time.deltaTime * 20f);
                        }
                    }
                }
                else
                {
                    if (VRWindowDragger.ActiveDragger == activeDraggingWindow)
                    {
                        VRWindowDragger.ActiveDragger = null;
                    }
                    if (activeDraggingWindow != null)
                    {
                        activeDraggingWindow.EndGrab();
                        activeDraggingWindow = null;
                    }
                }
            }

            // Handle active window resizing in 3D space via Laser Pointer (Item 2)
            if (activeResizingWindow != null)
            {
                bool isStillHeld = OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, controller)
                                || OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, controller) > 0.35f;
                try { isStillHeld |= Input.GetMouseButton(0); } catch { }

                if (isStillHeld)
                {
                    Vector3 currentPointerHit = rayOrigin + rayDir * activeDragDistance;
                    activeResizingWindow.UpdateResize(currentPointerHit);
                }
                else
                {
                    activeResizingWindow.EndResize();
                    activeResizingWindow = null;
                }
            }

            // Handle active Slider dragging via Laser Pointer
            if (activeDraggingSlider != null)
            {
                bool isStillHeld = OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, controller)
                                || OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, controller) > 0.35f;
                try { isStillHeld |= Input.GetMouseButton(0); } catch { }

                if (isStillHeld && activeDraggingSlider != null && activeDraggingSlider.gameObject.activeInHierarchy && activeDraggingSlider.interactable)
                {
                    UpdateSliderDrag(rayOrigin, rayDir, activeDraggingSlider);

                    Vector3 handlePos = activeDraggingSlider.handleRect != null
                        ? activeDraggingSlider.handleRect.position
                        : activeDraggingSlider.transform.position;

                    lineRenderer.enabled = true;
                    lineRenderer.SetPosition(0, rayOrigin);
                    lineRenderer.SetPosition(1, handlePos);
                    SetVisualColors(clickLaserColor);

                    if (reticleObj != null)
                    {
                        reticleObj.SetActive(true);
                        Vector3 norm = activeDraggingSlider.transform.forward;
                        if (Vector3.Dot(norm, rayDir) > 0f) norm = -norm;
                        reticleObj.transform.position = handlePos + norm * 0.003f;
                        reticleObj.transform.rotation = Quaternion.LookRotation(norm, Vector3.up);
                        currentReticleScale = Mathf.Lerp(currentReticleScale, 1.35f, Time.deltaTime * 18f);
                        reticleObj.transform.localScale = Vector3.one * currentReticleScale;
                    }

                    wasTriggerDown = isTriggerDown;
                    return;
                }
                else
                {
                    activeDraggingSlider = null;
                }
            }

            Ray ray = new Ray(rayOrigin, rayDir);
            Vector3 endPoint = rayOrigin + rayDir * maxRayLength;
            Vector3 hitNormal = -rayDir;
            bool hasHit = false;

            Selectable hitSelectable = null;
            VRWindowDragger hitDragger = null;
            VRWindowResizer hitResizer = null;

            // Raycast against World Space UI Canvases, Resizers, and Draggers
            hasHit = RaycastUIAndDraggers(ray, ref endPoint, ref hitNormal, ref hitSelectable, ref hitDragger, ref hitResizer);

            // Handle Hover State Transitions
            UpdateHoverStates(hitSelectable, hitDragger, hitResizer);

            // Trigger Pressed: Click UI Button, Resize Window, Drag Slider, or Begin Window Drag
            if (isTriggerJustPressed)
            {
                if (hitSelectable != null)
                {
                    Slider hitSlider = hitSelectable as Slider ?? hitSelectable.GetComponentInParent<Slider>();
                    if (hitSlider != null && hitSlider.interactable)
                    {
                        activeDraggingSlider = hitSlider;
                        UpdateSliderDrag(rayOrigin, rayDir, hitSlider);
                        HapticPulse(0.35f, 0.05f);
                    }
                    else
                    {
                        ExecuteButtonClick(hitSelectable);
                    }
                }
                else if (hitResizer != null)
                {
                    activeResizingWindow = hitResizer;
                    activeDragDistance = Vector3.Distance(rayOrigin, endPoint);
                    hitResizer.StartResize(endPoint);
                    HapticPulse(0.5f, 0.08f);
                }
                else if (hitDragger != null)
                {
                    // Only one UI window can be grabbed at any time
                    if (VRWindowDragger.ActiveDragger == null || VRWindowDragger.ActiveDragger == hitDragger)
                    {
                        activeDraggingWindow = hitDragger;
                        hitDragger.StartGrab(leftControllerAnchor ?? transform, true);
                        if (hitDragger.windowTransform != null)
                        {
                            activeDragDistance = Vector3.Distance(rayOrigin, hitDragger.windowTransform.position);
                            activeDragLocalOffset = (rayOrigin + rayDir * activeDragDistance) - hitDragger.windowTransform.position;
                        }
                        else
                        {
                            activeDragDistance = Vector3.Distance(rayOrigin, hitDragger.transform.position);
                            activeDragLocalOffset = (rayOrigin + rayDir * activeDragDistance) - hitDragger.transform.position;
                        }
                        HapticPulse(0.5f, 0.08f);
                    }
                }
            }

            // Update Laser Beam Visuals
            lineRenderer.enabled = true;
            lineRenderer.SetPosition(0, rayOrigin);
            lineRenderer.SetPosition(1, endPoint);

            // Visual Colors
            Color activeColor = normalLaserColor;
            if (isTriggerDown) activeColor = clickLaserColor;
            else if (currentHoverSelectable != null || currentHoverDragger != null || currentHoverResizer != null || activeDraggingWindow != null || activeResizingWindow != null) activeColor = hoverLaserColor;

            SetVisualColors(activeColor);

            // Update Reticle Disc & Scale Pulse (Item 1)
            if (reticleObj != null)
            {
                reticleObj.SetActive(hasHit);
                if (hasHit)
                {
                    reticleObj.transform.position = endPoint + hitNormal * 0.003f;
                    reticleObj.transform.rotation = Quaternion.LookRotation(hitNormal, Vector3.up);

                    float targetScale = (hitSelectable != null) ? 1.35f : 1.0f;
                    currentReticleScale = Mathf.Lerp(currentReticleScale, targetScale, Time.deltaTime * 18f);
                    reticleObj.transform.localScale = Vector3.one * currentReticleScale;
                }
                else
                {
                    currentReticleScale = 1.0f;
                    reticleObj.transform.localScale = Vector3.one;
                }
            }

            wasTriggerDown = isTriggerDown;
        }

        public static bool RaycastRectTransformQuad(Ray ray, RectTransform rt, float marginRatio, out float distance, out Vector3 hitPoint, out Vector3 hitNormal)
        {
            distance = 0f;
            hitPoint = Vector3.zero;
            hitNormal = Vector3.forward;

            if (rt == null || !rt.gameObject.activeInHierarchy) return false;

            Vector3[] c = new Vector3[4];
            rt.GetWorldCorners(c);
            // c[0] = bottom-left, c[1] = top-left, c[2] = top-right, c[3] = bottom-right

            Vector3 right = c[3] - c[0];
            Vector3 up = c[1] - c[0];
            float rSq = right.sqrMagnitude;
            float uSq = up.sqrMagnitude;
            if (rSq < 1e-7f || uSq < 1e-7f) return false;

            Vector3 normal = Vector3.Cross(right, up).normalized;
            if (normal.sqrMagnitude < 0.001f) return false;

            // Two-sided plane intersection so pointing angle never fails
            if (Vector3.Dot(normal, ray.direction) > 0f)
            {
                normal = -normal;
            }

            Plane plane = new Plane(normal, c[0]);
            if (!plane.Raycast(ray, out float enter)) return false;
            if (enter <= 0.01f) return false;

            Vector3 p = ray.GetPoint(enter);
            Vector3 v = p - c[0];

            float u = Vector3.Dot(v, right) / rSq;
            float w = Vector3.Dot(v, up) / uSq;

            float minBound = -marginRatio;
            float maxBound = 1.0f + marginRatio;

            if (u >= minBound && u <= maxBound && w >= minBound && w <= maxBound)
            {
                distance = enter;
                hitPoint = p;
                hitNormal = normal;
                return true;
            }

            return false;
        }

        private bool RaycastUIAndDraggers(Ray ray, ref Vector3 endPoint, ref Vector3 hitNormal, ref Selectable hitSelectable, ref VRWindowDragger hitDragger, ref VRWindowResizer hitResizer)
        {
            float closestInteractableDist = maxRayLength;
            Selectable bestSel = null;
            VRWindowDragger bestDragger = null;
            VRWindowResizer bestResizer = null;
            Vector3 bestInteractableHit = Vector3.zero;
            Vector3 bestInteractableNormal = -ray.direction;

            // 1. Raycast all active Selectables (Buttons, Toggles, etc.) FIRST with priority
            var selectables = FindObjectsByType<Selectable>();
            foreach (var sel in selectables)
            {
                if (sel == null || !sel.gameObject.activeInHierarchy || !sel.interactable) continue;
                RectTransform srt = sel.GetComponent<RectTransform>();
                if (srt == null) continue;

                // Item 1: Magnetic Snap Margin (15% boundary margin around selectable buttons)
                if (RaycastRectTransformQuad(ray, srt, 0.15f, out float dist, out Vector3 pt, out Vector3 norm))
                {
                    if (dist < closestInteractableDist)
                    {
                        closestInteractableDist = dist;
                        bestSel = sel;
                        bestDragger = null;
                        bestResizer = null;
                        bestInteractableHit = pt;
                        bestInteractableNormal = norm;
                    }
                }
            }

            // 2. Raycast VRWindowResizers if no button was hit (Corner resize handles)
            if (bestSel == null)
            {
                var resizers = FindObjectsByType<VRWindowResizer>();
                foreach (var r in resizers)
                {
                    if (r == null || !r.gameObject.activeInHierarchy) continue;
                    RectTransform rrt = r.GetComponent<RectTransform>();
                    if (rrt != null)
                    {
                        if (RaycastRectTransformQuad(ray, rrt, 0.15f, out float dist, out Vector3 pt, out Vector3 norm))
                        {
                            if (dist < closestInteractableDist)
                            {
                                closestInteractableDist = dist;
                                bestResizer = r;
                                bestDragger = null;
                                bestSel = null;
                                bestInteractableHit = pt;
                                bestInteractableNormal = norm;
                            }
                        }
                    }
                }
            }

            // 3. Raycast VRWindowDraggers ONLY if no button or resizer was hit
            if (bestSel == null && bestResizer == null)
            {
                var draggers = FindObjectsByType<VRWindowDragger>();
                foreach (var d in draggers)
                {
                    if (d == null || !d.gameObject.activeInHierarchy) continue;
                    RectTransform drt = d.GetComponent<RectTransform>();
                    if (drt != null)
                    {
                        // Exact quad bounds so dragger never overlaps adjacent buttons
                        if (RaycastRectTransformQuad(ray, drt, 0.0f, out float dist, out Vector3 pt, out Vector3 norm))
                        {
                            if (dist < closestInteractableDist)
                            {
                                closestInteractableDist = dist;
                                bestDragger = d;
                                bestSel = null;
                                bestInteractableHit = pt;
                                bestInteractableNormal = norm;
                            }
                        }
                    }
                }
            }

            // If we hit an interactable (button, resizer, or dragger), prioritize it!
            if (bestSel != null || bestResizer != null || bestDragger != null)
            {
                endPoint = bestInteractableHit;
                hitNormal = bestInteractableNormal;
                hitSelectable = bestSel;
                hitDragger = bestDragger;
                hitResizer = bestResizer;

                // Item 1: Magnetic Snap to Button Center or Slider Handle
                if (bestSel != null)
                {
                    Slider selSlider = bestSel as Slider ?? bestSel.GetComponentInParent<Slider>();
                    if (selSlider != null)
                    {
                        RectTransform handleRT = selSlider.handleRect;
                        RectTransform trackRT = handleRT != null && handleRT.parent is RectTransform parentRT
                            ? parentRT
                            : selSlider.GetComponent<RectTransform>();

                        if (handleRT != null && trackRT != null)
                        {
                            Vector3 handlePos = handleRT.position;
                            Vector3[] hCorners = new Vector3[4];
                            handleRT.GetWorldCorners(hCorners);
                            float handleWidth = (hCorners[3] - hCorners[0]).magnitude;
                            float handleHeight = (hCorners[1] - hCorners[0]).magnitude;

                            // Snap radius around the white handle (generous for effortless VR grabbing)
                            float snapRadius = Mathf.Max(handleWidth * 2.5f, handleHeight * 1.5f);
                            float distToHandle = Vector3.Distance(bestInteractableHit, handlePos);

                            if (distToHandle <= snapRadius)
                            {
                                // Magnetically snap directly to the white handle!
                                Vector3 snapTarget = handlePos;
                                if (snappedEndPoint == Vector3.zero) snappedEndPoint = bestInteractableHit;
                                snappedEndPoint = Vector3.Lerp(snappedEndPoint, snapTarget, Time.deltaTime * 24f);
                                endPoint = snappedEndPoint;
                            }
                            else
                            {
                                // Outside handle proximity: Snap smoothly along the track center line
                                Vector3[] tCorners = new Vector3[4];
                                trackRT.GetWorldCorners(tCorners);
                                Vector3 leftPt = (tCorners[0] + tCorners[1]) * 0.5f;
                                Vector3 rightPt = (tCorners[2] + tCorners[3]) * 0.5f;
                                Vector3 trackVec = rightPt - leftPt;
                                float trackLen = trackVec.magnitude;

                                if (trackLen > 0.001f)
                                {
                                    Vector3 trackDirNorm = trackVec / trackLen;
                                    float proj = Vector3.Dot(bestInteractableHit - leftPt, trackDirNorm);
                                    proj = Mathf.Clamp(proj, 0f, trackLen);
                                    Vector3 projectedOnTrack = leftPt + trackDirNorm * proj;

                                    if (snappedEndPoint == Vector3.zero) snappedEndPoint = bestInteractableHit;
                                    snappedEndPoint = Vector3.Lerp(snappedEndPoint, projectedOnTrack, Time.deltaTime * 24f);
                                    endPoint = snappedEndPoint;
                                }
                                else
                                {
                                    endPoint = bestInteractableHit;
                                }
                            }
                        }
                        else
                        {
                            endPoint = bestInteractableHit;
                        }
                    }
                    else
                    {
                        // Standard Buttons, Toggles: Magnetic Snap to Button Center
                        RectTransform srt = bestSel.GetComponent<RectTransform>();
                        if (srt != null)
                        {
                            Vector3 buttonCenter = srt.position;
                            if (snappedEndPoint == Vector3.zero) snappedEndPoint = bestInteractableHit;
                            snappedEndPoint = Vector3.Lerp(snappedEndPoint, buttonCenter, Time.deltaTime * 20f);
                            endPoint = snappedEndPoint;
                        }
                    }
                }
                else
                {
                    snappedEndPoint = endPoint;
                }

                Camera eventCam = headTransform != null ? headTransform.GetComponent<Camera>() : Camera.main;
                if (eventCam != null && pointerEventData != null)
                {
                    pointerEventData.position = eventCam.WorldToScreenPoint(bestInteractableHit);
                }
                return true;
            }

            // 3. Fallback: Raycast WorldSpace Canvases so laser beam lands smoothly on the window glass
            float closestCanvasDist = maxRayLength;
            Vector3 bestCanvasHit = Vector3.zero;
            Vector3 bestCanvasNormal = -ray.direction;
            bool hitCanvas = false;

            var canvases = FindObjectsByType<Canvas>();
            foreach (var c in canvases)
            {
                if (c == null || !c.gameObject.activeInHierarchy || c.renderMode != RenderMode.WorldSpace) continue;
                RectTransform crt = c.GetComponent<RectTransform>();
                if (crt == null) continue;

                if (RaycastRectTransformQuad(ray, crt, 0.02f, out float dist, out Vector3 pt, out Vector3 norm))
                {
                    if (dist < closestCanvasDist)
                    {
                        closestCanvasDist = dist;
                        bestCanvasHit = pt;
                        bestCanvasNormal = norm;
                        hitCanvas = true;
                    }
                }
            }

            if (hitCanvas)
            {
                endPoint = bestCanvasHit;
                hitNormal = bestCanvasNormal;
                return true;
            }

            // 4. Physics Raycast fallback for 3D Draggers / Colliders
            RaycastHit hit;
            if (Physics.Raycast(ray, out hit, maxRayLength))
            {
                endPoint = hit.point;
                hitNormal = hit.normal;
                var drg = hit.collider.GetComponentInParent<VRWindowDragger>();
                if (drg != null) hitDragger = drg;
                var btn = hit.collider.GetComponentInParent<Selectable>();
                if (btn != null && btn.interactable) hitSelectable = btn;
                return true;
            }

            return false;
        }

        private void UpdateHoverStates(Selectable newSelectable, VRWindowDragger newDragger, VRWindowResizer newResizer)
        {
            // Transition Selectable Hover
            if (newSelectable != currentHoverSelectable)
            {
                snappedEndPoint = Vector3.zero;

                if (currentHoverSelectable != null && EventSystem.current != null)
                {
                    ExecuteEvents.Execute(currentHoverSelectable.gameObject, pointerEventData, ExecuteEvents.pointerExitHandler);
                }

                currentHoverSelectable = newSelectable;

                if (currentHoverSelectable != null && EventSystem.current != null)
                {
                    ExecuteEvents.Execute(currentHoverSelectable.gameObject, pointerEventData, ExecuteEvents.pointerEnterHandler);
                    HapticPulse(0.18f, 0.04f);
                }
            }

            // Transition Resizer Hover (Item 2)
            if (newResizer != currentHoverResizer)
            {
                if (currentHoverResizer != null) currentHoverResizer.isHovered = false;
                currentHoverResizer = newResizer;
                if (currentHoverResizer != null)
                {
                    currentHoverResizer.isHovered = true;
                    HapticPulse(0.20f, 0.04f);
                }
            }

            // Transition Dragger Hover
            if (newDragger != currentHoverDragger)
            {
                if (currentHoverDragger != null) currentHoverDragger.SetHovered(false);
                currentHoverDragger = newDragger;
                if (currentHoverDragger != null)
                {
                    currentHoverDragger.SetHovered(true);
                    HapticPulse(0.20f, 0.04f);
                }
            }
        }

        private float lastButtonClickTime = 0f;

        private void ExecuteButtonClick(Selectable sel)
        {
            // Click debounce (0.28s) to strictly ensure 1 trigger press = exactly 1 click action
            if (Time.unscaledTime - lastButtonClickTime < 0.28f) return;
            lastButtonClickTime = Time.unscaledTime;

            HapticPulse(0.6f, 0.10f);

            if (bhapticsFeedback != null)
            {
                bhapticsFeedback.TriggerClickPulse(70, 60);
            }

            var btn = sel as Button;
            if (btn != null && btn.onClick != null)
            {
                // Directly invoke button click handler once
                btn.onClick.Invoke();
            }
            else if (EventSystem.current != null)
            {
                ExecuteEvents.Execute(sel.gameObject, pointerEventData, ExecuteEvents.pointerClickHandler);
            }

            StartCoroutine(PunchScale(sel.transform));
        }

        private IEnumerator PunchScale(Transform t)
        {
            if (t == null) yield break;
            Vector3 orig = t.localScale;
            t.localScale = orig * 0.92f;
            yield return new WaitForSeconds(0.06f);
            if (t != null) t.localScale = orig * 1.05f;
            yield return new WaitForSeconds(0.06f);
            if (t != null) t.localScale = orig;
        }

        private void SetVisualColors(Color c)
        {
            if (lineMaterial != null)
            {
                lineMaterial.color = c;
                if (lineMaterial.HasProperty("_BaseColor")) lineMaterial.SetColor("_BaseColor", c);
            }
            if (reticleMaterial != null)
            {
                reticleMaterial.color = c;
                if (reticleMaterial.HasProperty("_BaseColor")) reticleMaterial.SetColor("_BaseColor", c);
            }
        }

        private void HapticPulse(float amplitude, float duration)
        {
            try
            {
                OVRInput.SetControllerVibration(amplitude, amplitude, controller);
                StartCoroutine(StopHapticsAfter(duration));
            }
            catch { }
        }

        private IEnumerator StopHapticsAfter(float delay)
        {
            yield return new WaitForSeconds(delay);
            try
            {
                OVRInput.SetControllerVibration(0, 0, controller);
            }
            catch { }
        }

        private void UpdateSliderDrag(Vector3 rayOrigin, Vector3 rayDir, Slider slider)
        {
            if (slider == null || !slider.interactable) return;

            RectTransform handleRT = slider.handleRect;
            RectTransform trackRT = handleRT != null && handleRT.parent is RectTransform parentRT
                ? parentRT
                : slider.GetComponent<RectTransform>();

            if (trackRT == null) return;

            Vector3[] corners = new Vector3[4];
            trackRT.GetWorldCorners(corners);
            // corners: 0=bottom-left, 1=top-left, 2=top-right, 3=bottom-right

            Vector3 leftPt = (corners[0] + corners[1]) * 0.5f;
            Vector3 rightPt = (corners[2] + corners[3]) * 0.5f;
            Vector3 trackVec = rightPt - leftPt;
            float trackLen = trackVec.magnitude;
            if (trackLen < 0.0001f) return;

            Vector3 trackDir = trackVec / trackLen;

            Vector3 upVec = corners[1] - corners[0];
            Vector3 normal = Vector3.Cross(trackDir, upVec.normalized).normalized;
            if (normal.sqrMagnitude < 0.001f) normal = trackRT.forward;
            if (Vector3.Dot(normal, rayDir) > 0f) normal = -normal;

            Plane plane = new Plane(normal, leftPt);
            Ray ray = new Ray(rayOrigin, rayDir);

            if (plane.Raycast(ray, out float enter))
            {
                Vector3 hitPoint = ray.GetPoint(enter);
                float proj = Vector3.Dot(hitPoint - leftPt, trackDir);
                float t = Mathf.Clamp01(proj / trackLen);

                if (slider.direction == Slider.Direction.RightToLeft)
                {
                    t = 1.0f - t;
                }

                float targetVal = Mathf.Lerp(slider.minValue, slider.maxValue, t);
                if (slider.wholeNumbers)
                {
                    targetVal = Mathf.Round(targetVal);
                }

                if (Mathf.Abs(slider.value - targetVal) > 0.001f)
                {
                    slider.value = targetVal;
                }
            }
        }
    }
}
