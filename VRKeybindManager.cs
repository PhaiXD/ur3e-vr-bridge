using System;
using System.Collections.Generic;
using UnityEngine;

namespace UR3eVR
{
    /// <summary>
    /// VR Actions that can be rebound to different controller buttons or keys.
    /// </summary>
    public enum VRAction
    {
        SetOrigin,        // Set Origin Point & Robot Safe Home (Default: Left X)
        PauseResume,      // Pause / Resume Teleoperation (Default: Left Y)
        ShakeTestTube,    // Auto Shake Test Tube 3 Times (Default: Left Stick Click)
        ToggleGripperLock,// Toggle Gripper Hold / Lock (Default: Right Grip Trigger)
        OpenMenu          // Open / Close VR Main Menu (Default: Left Menu Button)
    }

    /// <summary>
    /// Rebindable controller buttons and keyboard fallbacks.
    /// </summary>
    public enum VRButtonId
    {
        // Left Touch Controller
        LeftX,                // Button Three / RawButton.X / KeyCode.X
        LeftY,                // Button Four / RawButton.Y / KeyCode.Y
        LeftStickClick,       // Button PrimaryThumbstick / RawButton.LThumbstick / KeyCode.K
        LeftGrip,             // PrimaryHandTrigger (Axis1D or Button) / KeyCode.G
        LeftMenu,             // Button Start / RawButton.Start / KeyCode.M

        // Right Touch Controller
        RightA,               // Button One / RawButton.A / KeyCode.A
        RightB,               // Button Two / RawButton.B / KeyCode.B
        RightStickClick,      // Button SecondaryThumbstick / RawButton.RThumbstick / KeyCode.J
        RightGrip,            // SecondaryHandTrigger (Axis1D or Button) / KeyCode.L

        // Common Keyboard Fallbacks
        KeySpace,             // KeyCode.Space
        KeyH,                 // KeyCode.H
        KeyP                  // KeyCode.P
    }

    /// <summary>
    /// VRKeybindManager: Centralized manager for Touch Controller and Keyboard keybindings.
    /// Allows in-VR rebinding, automatic swap on conflicts, persistence via PlayerPrefs,
    /// and clean separation of input polling.
    /// </summary>
    public class VRKeybindManager : MonoBehaviour
    {
        private static VRKeybindManager instance;
        public static VRKeybindManager Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindAnyObjectByType<VRKeybindManager>();
                    if (instance == null)
                    {
                        GameObject go = new GameObject("VRKeybindManager");
                        instance = go.AddComponent<VRKeybindManager>();
                        DontDestroyOnLoad(go);
                    }
                }
                return instance;
            }
        }

        private const string PREF_PREFIX = "UR3eVR_Keybind_";

        // Active keybindings
        private readonly Dictionary<VRAction, VRButtonId> bindings = new Dictionary<VRAction, VRButtonId>();

        // Rebinding / Listening State
        public bool isListening { get; private set; } = false;
        public VRAction listeningAction { get; private set; } = VRAction.SetOrigin;
        private float listeningTimer = 0f;
        private float listeningCooldown = 0f;

        // Events
        public Action onKeybindsChanged;
        public Action<bool, VRAction> onListeningStateChanged;

        // Axis triggers rising-edge detection
        private float prevLeftGrip = 0f;
        private float prevRightGrip = 0f;

        // Candidate buttons available for rebinding
        private static readonly VRButtonId[] candidateButtons = new VRButtonId[]
        {
            VRButtonId.LeftX,
            VRButtonId.LeftY,
            VRButtonId.LeftStickClick,
            VRButtonId.LeftGrip,
            VRButtonId.LeftMenu,
            VRButtonId.RightA,
            VRButtonId.RightB,
            VRButtonId.RightStickClick,
            VRButtonId.RightGrip,
            VRButtonId.KeySpace,
            VRButtonId.KeyH,
            VRButtonId.KeyP
        };

        private void Awake()
        {
            if (instance == null)
            {
                instance = this;
                DontDestroyOnLoad(gameObject);
                LoadKeybinds();
            }
            else if (instance != this)
            {
                Destroy(gameObject);
            }
        }

        private void Start()
        {
            if (bindings.Count == 0)
            {
                LoadKeybinds();
            }
        }

        /// <summary>
        /// Check if an action's assigned button was pressed down in this frame.
        /// Suppressed while rebind listening is active to prevent unwanted execution.
        /// </summary>
        public static bool IsActionDown(VRAction action)
        {
            var mgr = Instance;
            if (mgr == null) return false;
            if (mgr.isListening) return false;

            if (!mgr.bindings.TryGetValue(action, out var btnId))
            {
                btnId = GetDefaultButton(action);
            }

            return mgr.CheckButtonDown(btnId);
        }

        /// <summary>
        /// Start listening for any controller button or key to bind to the specified action.
        /// </summary>
        public void StartListening(VRAction action)
        {
            isListening = true;
            listeningAction = action;
            listeningTimer = 0f;
            listeningCooldown = 0.25f; // Small cooldown so the click that triggered rebinding is ignored

            onListeningStateChanged?.Invoke(true, action);
            Debug.Log($"[VRKeybindManager] Started listening for new keybind for: {action}");
        }

        /// <summary>
        /// Cancel active listening state without modifying keybinds.
        /// </summary>
        public void StopListening()
        {
            if (!isListening) return;
            isListening = false;
            onListeningStateChanged?.Invoke(false, listeningAction);
            Debug.Log("[VRKeybindManager] Stopped listening for keybinds.");
        }

        /// <summary>
        /// Assign a button to an action. If the button is already assigned to another action,
        /// automatically swaps them so no action is left unbound.
        /// </summary>
        public void SetKeybind(VRAction action, VRButtonId buttonId)
        {
            // Check for conflict
            VRAction conflictAction = (VRAction)(-1);
            foreach (var kvp in bindings)
            {
                if (kvp.Key != action && kvp.Value == buttonId)
                {
                    conflictAction = kvp.Key;
                    break;
                }
            }

            if ((int)conflictAction != -1)
            {
                // Auto-swap with existing action
                VRButtonId oldForAction = bindings.ContainsKey(action) ? bindings[action] : GetDefaultButton(action);
                bindings[conflictAction] = oldForAction;
                PlayerPrefs.SetString(PREF_PREFIX + conflictAction.ToString(), oldForAction.ToString());
                Debug.Log($"[VRKeybindManager] Swapped {conflictAction} to {oldForAction}");
            }

            bindings[action] = buttonId;
            PlayerPrefs.SetString(PREF_PREFIX + action.ToString(), buttonId.ToString());
            PlayerPrefs.Save();

            Debug.Log($"[VRKeybindManager] Successfully bound {action} to {buttonId}");
            onKeybindsChanged?.Invoke();
        }

        /// <summary>
        /// Get the currently bound button for an action.
        /// </summary>
        public VRButtonId GetBoundButton(VRAction action)
        {
            if (bindings.TryGetValue(action, out var btnId))
            {
                return btnId;
            }
            return GetDefaultButton(action);
        }

        /// <summary>
        /// Reset all keybindings to factory defaults.
        /// </summary>
        public void ResetDefaults()
        {
            bindings[VRAction.SetOrigin] = VRButtonId.LeftX;
            bindings[VRAction.PauseResume] = VRButtonId.LeftY;
            bindings[VRAction.ShakeTestTube] = VRButtonId.LeftStickClick;
            bindings[VRAction.ToggleGripperLock] = VRButtonId.RightGrip;
            bindings[VRAction.OpenMenu] = VRButtonId.LeftMenu;

            foreach (var kvp in bindings)
            {
                PlayerPrefs.SetString(PREF_PREFIX + kvp.Key.ToString(), kvp.Value.ToString());
            }
            PlayerPrefs.Save();

            Debug.Log("[VRKeybindManager] Reset all keybinds to defaults.");
            onKeybindsChanged?.Invoke();
        }

        private void LoadKeybinds()
        {
            bindings.Clear();
            bindings[VRAction.SetOrigin] = VRButtonId.LeftX;
            bindings[VRAction.PauseResume] = VRButtonId.LeftY;
            bindings[VRAction.ShakeTestTube] = VRButtonId.LeftStickClick;
            bindings[VRAction.ToggleGripperLock] = VRButtonId.RightGrip;
            bindings[VRAction.OpenMenu] = VRButtonId.LeftMenu;

            foreach (VRAction action in Enum.GetValues(typeof(VRAction)))
            {
                string key = PREF_PREFIX + action.ToString();
                if (PlayerPrefs.HasKey(key))
                {
                    string saved = PlayerPrefs.GetString(key);
                    if (Enum.TryParse<VRButtonId>(saved, out var btnId))
                    {
                        bindings[action] = btnId;
                    }
                }
            }
        }

        public static VRButtonId GetDefaultButton(VRAction action)
        {
            switch (action)
            {
                case VRAction.SetOrigin: return VRButtonId.LeftX;
                case VRAction.PauseResume: return VRButtonId.LeftY;
                case VRAction.ShakeTestTube: return VRButtonId.LeftStickClick;
                case VRAction.ToggleGripperLock: return VRButtonId.RightGrip;
                case VRAction.OpenMenu: return VRButtonId.LeftMenu;
                default: return VRButtonId.LeftX;
            }
        }

        public static string GetActionName(VRAction action)
        {
            switch (action)
            {
                case VRAction.SetOrigin: return "Set Origin / Home";
                case VRAction.PauseResume: return "Pause / Resume";
                case VRAction.ShakeTestTube: return "Shake Test Tube";
                case VRAction.ToggleGripperLock: return "Gripper Lock";
                case VRAction.OpenMenu: return "Open / Close Menu";
                default: return action.ToString();
            }
        }

        public static string GetButtonDisplayName(VRButtonId buttonId)
        {
            switch (buttonId)
            {
                case VRButtonId.LeftX: return "[ X Button ]";
                case VRButtonId.LeftY: return "[ Y Button ]";
                case VRButtonId.LeftStickClick: return "[ Left Stick ]";
                case VRButtonId.LeftGrip: return "[ Left Grip ]";
                case VRButtonId.LeftMenu: return "[ Menu Button ]";
                case VRButtonId.RightA: return "[ A Button ]";
                case VRButtonId.RightB: return "[ B Button ]";
                case VRButtonId.RightStickClick: return "[ Right Stick ]";
                case VRButtonId.RightGrip: return "[ Right Grip ]";
                case VRButtonId.KeySpace: return "[ Spacebar ]";
                case VRButtonId.KeyH: return "[ H Key ]";
                case VRButtonId.KeyP: return "[ P Key ]";
                default: return $"[ {buttonId} ]";
            }
        }

        private void Update()
        {
            // Sample analog grip levels for rising-edge detection
            float currLeftGrip = 0f;
            float currRightGrip = 0f;
            try
            {
                currLeftGrip = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, OVRInput.Controller.LTouch);
                currRightGrip = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, OVRInput.Controller.RTouch);
            }
            catch { }

            // Rebinding listening logic
            if (isListening)
            {
                listeningTimer += Time.unscaledDeltaTime;

                // Timeout or Escape cancellation
                if (listeningTimer > 8.0f || Input.GetKeyDown(KeyCode.Escape))
                {
                    StopListening();
                    prevLeftGrip = currLeftGrip;
                    prevRightGrip = currRightGrip;
                    return;
                }

                if (listeningCooldown > 0f)
                {
                    listeningCooldown -= Time.unscaledDeltaTime;
                    prevLeftGrip = currLeftGrip;
                    prevRightGrip = currRightGrip;
                    return;
                }

                // Scan all candidate buttons
                for (int i = 0; i < candidateButtons.Length; i++)
                {
                    VRButtonId candidate = candidateButtons[i];
                    if (CheckButtonDown(candidate))
                    {
                        VRAction act = listeningAction;
                        SetKeybind(act, candidate);
                        StopListening();

                        // Haptic feedback confirmation
                        try
                        {
                            OVRInput.SetControllerVibration(0.6f, 0.6f, OVRInput.Controller.LTouch);
                            OVRInput.SetControllerVibration(0.6f, 0.6f, OVRInput.Controller.RTouch);
                        }
                        catch { }

                        VRMenuManager.Instance?.ShowToast($"✅ {GetActionName(act)} bound to {GetButtonDisplayName(candidate)}");
                        break;
                    }
                }
            }

            prevLeftGrip = currLeftGrip;
            prevRightGrip = currRightGrip;
        }

        /// <summary>
        /// Check whether a specific button/key was pressed down this frame.
        /// </summary>
        public bool CheckButtonDown(VRButtonId buttonId)
        {
            try
            {
                switch (buttonId)
                {
                    case VRButtonId.LeftX:
                        return OVRInput.GetDown(OVRInput.Button.Three, OVRInput.Controller.LTouch) ||
                               OVRInput.GetDown(OVRInput.RawButton.X, OVRInput.Controller.LTouch) ||
                               Input.GetKeyDown(KeyCode.X);

                    case VRButtonId.LeftY:
                        return OVRInput.GetDown(OVRInput.Button.Four, OVRInput.Controller.LTouch) ||
                               OVRInput.GetDown(OVRInput.RawButton.Y, OVRInput.Controller.LTouch) ||
                               Input.GetKeyDown(KeyCode.Y);

                    case VRButtonId.LeftStickClick:
                        return OVRInput.GetDown(OVRInput.Button.PrimaryThumbstick, OVRInput.Controller.LTouch) ||
                               OVRInput.GetDown(OVRInput.RawButton.LThumbstick, OVRInput.Controller.LTouch) ||
                               Input.GetKeyDown(KeyCode.K);

                    case VRButtonId.LeftGrip:
                    {
                        float lg = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, OVRInput.Controller.LTouch);
                        bool rising = (lg >= 0.65f && prevLeftGrip < 0.65f);
                        return rising ||
                               OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.LTouch) ||
                               Input.GetKeyDown(KeyCode.G);
                    }

                    case VRButtonId.LeftMenu:
                        return OVRInput.GetDown(OVRInput.Button.Start, OVRInput.Controller.LTouch) ||
                               OVRInput.GetDown(OVRInput.RawButton.Start) ||
                               Input.GetKeyDown(KeyCode.M);

                    case VRButtonId.RightA:
                        return OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch) ||
                               OVRInput.GetDown(OVRInput.RawButton.A, OVRInput.Controller.RTouch) ||
                               Input.GetKeyDown(KeyCode.A);

                    case VRButtonId.RightB:
                        return OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch) ||
                               OVRInput.GetDown(OVRInput.RawButton.B, OVRInput.Controller.RTouch) ||
                               Input.GetKeyDown(KeyCode.B);

                    case VRButtonId.RightStickClick:
                        return OVRInput.GetDown(OVRInput.Button.SecondaryThumbstick, OVRInput.Controller.RTouch) ||
                               OVRInput.GetDown(OVRInput.RawButton.RThumbstick, OVRInput.Controller.RTouch) ||
                               Input.GetKeyDown(KeyCode.J);

                    case VRButtonId.RightGrip:
                    {
                        float rg = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, OVRInput.Controller.RTouch);
                        bool rising = (rg >= 0.65f && prevRightGrip < 0.65f);
                        return rising ||
                               OVRInput.GetDown(OVRInput.Button.SecondaryHandTrigger, OVRInput.Controller.RTouch) ||
                               Input.GetKeyDown(KeyCode.L);
                    }

                    case VRButtonId.KeySpace:
                        return Input.GetKeyDown(KeyCode.Space);

                    case VRButtonId.KeyH:
                        return Input.GetKeyDown(KeyCode.H);

                    case VRButtonId.KeyP:
                        return Input.GetKeyDown(KeyCode.P);
                }
            }
            catch { }

            return false;
        }
    }
}
