using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace UR3eVR
{
    /// <summary>
    /// VRPythonBridgeManager: Controls the Python Teleoperation Bridge process directly from VR.
    /// - Launches Real Robot Teleoperation (python main.py --ip <IP>)
    /// - Launches Offline VR Simulation Test (python main.py --sim)
    /// - Monitors process status (PID, memory, exit code)
    /// - Streams stdout/stderr live to VR UI Log & Wrist HUD
    /// - Auto-terminates on Unity quit to prevent orphaned socket locks
    /// </summary>
    public class VRPythonBridgeManager : MonoBehaviour
    {
        public static VRPythonBridgeManager Instance { get; private set; }

        public enum BridgeMode
        {
            Stopped,
            RealRobot,
            Simulation
        }

        [Header("Configuration")]
        public string pythonExePath = "";
        public string scriptDirectory = "";
        public string targetRobotIp = "192.168.1.20";

        [Header("Runtime State")]
        public BridgeMode currentMode = BridgeMode.Stopped;
        public int currentProcessId = 0;
        public bool isStarting = false;
        public string lastExitMessage = "";

        // Process Handle
        private Process pythonProcess = null;
        private readonly object logLock = new object();
        private readonly List<string> recentLogs = new List<string>();
        private readonly Queue<string> pendingLogsToDispatch = new Queue<string>();

        // Events
        public static event Action<BridgeMode, bool> OnStateChanged;
        public static event Action<string> OnLogReceived;

        public bool IsRunning
        {
            get
            {
                if (pythonProcess == null) return false;
                try
                {
                    return !pythonProcess.HasExited;
                }
                catch
                {
                    return false;
                }
            }
        }

        public IReadOnlyList<string> RecentLogs
        {
            get
            {
                lock (logLock)
                {
                    return recentLogs.ToArray();
                }
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            ResolvePaths();
            CheckExistingPythonProcess();
        }

        public void ResolvePaths()
        {
            // 1. Python Executable - Prioritize Python 3.12 where ur_rtde / rtde_control is installed
            pythonExePath = PlayerPrefs.GetString("UR3eVR_PythonExePath", "");
            if (string.IsNullOrEmpty(pythonExePath) || !File.Exists(pythonExePath) || pythonExePath.Contains("Python314"))
            {
                string[] candidates = new string[]
                {
                    @"C:\Users\dhamm\AppData\Local\Programs\Python\Python312\python.exe",
                    @"C:\Python312\python.exe",
                    @"C:\Users\dhamm\AppData\Local\Programs\Python\Python313\python.exe",
                    @"C:\Python313\python.exe",
                    @"C:\Python314\python.exe"
                };

                foreach (var c in candidates)
                {
                    if (File.Exists(c))
                    {
                        pythonExePath = c;
                        break;
                    }
                }

                if (string.IsNullOrEmpty(pythonExePath))
                {
                    pythonExePath = "python.exe"; // Fallback to PATH
                }

                PlayerPrefs.SetString("UR3eVR_PythonExePath", pythonExePath);
                PlayerPrefs.Save();
            }

            // 2. Script Directory
            scriptDirectory = PlayerPrefs.GetString("UR3eVR_PythonScriptDir", "");
            if (string.IsNullOrEmpty(scriptDirectory) || !File.Exists(Path.Combine(scriptDirectory, "main.py")))
            {
                string[] dirCandidates = new string[]
                {
                    @"c:\Users\dhamm\Documents\GitHub\ur3e-vr-bridge",
                    Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "ur3e-vr-bridge")),
                    Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ur3e-vr-bridge")),
                    Path.GetFullPath(Path.Combine(Application.dataPath, ".."))
                };

                foreach (var d in dirCandidates)
                {
                    if (Directory.Exists(d) && File.Exists(Path.Combine(d, "main.py")))
                    {
                        scriptDirectory = d;
                        break;
                    }
                }
            }

            // 3. Target Robot IP
            targetRobotIp = PlayerPrefs.GetString("UR3eVR_TargetRobotIp", "192.168.1.20");

            Debug.Log($"[VRPythonBridgeManager] Python Exe: '{pythonExePath}', Script Dir: '{scriptDirectory}', Target IP: '{targetRobotIp}'");
        }

        private void CheckExistingPythonProcess()
        {
            try
            {
                var procs = Process.GetProcessesByName("python");
                foreach (var p in procs)
                {
                    try
                    {
                        // Check if running in background
                        if (!p.HasExited)
                        {
                            // If process already running, register it
                            Debug.Log($"[VRPythonBridgeManager] Detected background Python PID: {p.Id}");
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        public bool StartRealRobot(string ip = null)
        {
            if (!string.IsNullOrEmpty(ip))
            {
                targetRobotIp = ip;
                PlayerPrefs.SetString("UR3eVR_TargetRobotIp", targetRobotIp);
                PlayerPrefs.Save();
            }

            return LaunchProcess($"main.py --ip {targetRobotIp}", BridgeMode.RealRobot);
        }

        public bool StartSimulation()
        {
            return LaunchProcess("main.py --sim", BridgeMode.Simulation);
        }

        private bool LaunchProcess(string arguments, BridgeMode mode)
        {
            if (IsRunning)
            {
                StopBridge();
            }

            if (!Directory.Exists(scriptDirectory))
            {
                ResolvePaths();
                if (!Directory.Exists(scriptDirectory))
                {
                    AddInternalLog($"[ERROR] Script directory not found: '{scriptDirectory}'");
                    return false;
                }
            }

            string mainPyPath = Path.Combine(scriptDirectory, "main.py");
            if (!File.Exists(mainPyPath))
            {
                AddInternalLog($"[ERROR] main.py not found at: '{mainPyPath}'");
                return false;
            }

            try
            {
                isStarting = true;
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = pythonExePath,
                    Arguments = arguments,
                    WorkingDirectory = scriptDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                // Add UTF-8 force environment variable
                psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
                psi.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";

                pythonProcess = new Process();
                pythonProcess.StartInfo = psi;
                pythonProcess.EnableRaisingEvents = true;

                pythonProcess.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        AddInternalLog(e.Data);
                    }
                };

                pythonProcess.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        AddInternalLog($"[ERR] {e.Data}");
                    }
                };

                pythonProcess.Exited += (s, e) =>
                {
                    int code = -1;
                    try { code = pythonProcess.ExitCode; } catch { }
                    AddInternalLog($"🛑 [Python Bridge] Process exited with code: {code}");
                    lastExitMessage = $"Exited ({code}) at {DateTime.Now:HH:mm:ss}";
                    currentMode = BridgeMode.Stopped;
                    currentProcessId = 0;
                    isStarting = false;
                };

                bool started = pythonProcess.Start();
                if (started)
                {
                    currentProcessId = pythonProcess.Id;
                    currentMode = mode;
                    isStarting = false;
                    pythonProcess.BeginOutputReadLine();
                    pythonProcess.BeginErrorReadLine();

                    string modeStr = mode == BridgeMode.RealRobot ? $"REAL ROBOT (IP: {targetRobotIp})" : "SIMULATION";
                    AddInternalLog($"🚀 [Python Bridge] Started PID {currentProcessId} in {modeStr} mode");
                    OnStateChanged?.Invoke(currentMode, true);
                    return true;
                }
                else
                {
                    isStarting = false;
                    AddInternalLog("[ERROR] Failed to start Python process.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                isStarting = false;
                AddInternalLog($"[ERROR] Exception launching Python: {ex.Message}");
                return false;
            }
        }

        public void StopBridge()
        {
            if (pythonProcess != null)
            {
                try
                {
                    if (!pythonProcess.HasExited)
                    {
                        AddInternalLog($"🛑 [Python Bridge] Stopping PID {pythonProcess.Id}...");
                        pythonProcess.Kill();
                        pythonProcess.WaitForExit(2000);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[VRPythonBridgeManager] StopBridge warning: {ex.Message}");
                }
                finally
                {
                    pythonProcess.Dispose();
                    pythonProcess = null;
                }
            }

            currentMode = BridgeMode.Stopped;
            currentProcessId = 0;
            isStarting = false;
            OnStateChanged?.Invoke(BridgeMode.Stopped, false);
            AddInternalLog("⏹️ [Python Bridge] Process stopped.");
        }

        public void RestartBridge(BridgeMode targetMode = BridgeMode.Stopped)
        {
            BridgeMode modeToRun = targetMode != BridgeMode.Stopped ? targetMode : (currentMode != BridgeMode.Stopped ? currentMode : BridgeMode.RealRobot);
            StartCoroutine(RestartRoutine(modeToRun));
        }

        private IEnumerator RestartRoutine(BridgeMode mode)
        {
            StopBridge();
            yield return new WaitForSeconds(0.8f);
            if (mode == BridgeMode.RealRobot) StartRealRobot();
            else if (mode == BridgeMode.Simulation) StartSimulation();
        }

        private void AddInternalLog(string line)
        {
            lock (logLock)
            {
                recentLogs.Add(line);
                if (recentLogs.Count > 80) recentLogs.RemoveAt(0);
                pendingLogsToDispatch.Enqueue(line);
            }
        }

        private void Update()
        {
            // Dispatch pending logs to main-thread listeners & VRMenuManager
            while (true)
            {
                string msg = null;
                lock (logLock)
                {
                    if (pendingLogsToDispatch.Count > 0) msg = pendingLogsToDispatch.Dequeue();
                }

                if (msg == null) break;

                OnLogReceived?.Invoke(msg);
                if (VRMenuManager.Instance != null)
                {
                    string tag = msg.StartsWith("[ERR]") ? "PY_ERR" : "PYTHON";
                    VRMenuManager.Instance.AddLog(tag, msg);
                }
            }
        }

        private void OnApplicationQuit()
        {
            StopBridge();
        }
    }
}
