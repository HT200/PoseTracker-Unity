using UnityEngine;

/// <summary>
/// Add this script to any GameObject to debug puppet setup issues
/// Checks PoseReceiver and PuppetController configuration
/// </summary>
public class PuppetDebugger : MonoBehaviour
{
    [Header("References")]
    public PoseReceiver poseReceiver;
    public PuppetController puppetController;

    [Header("Debug Settings")]
    public bool logEveryFrame = false;
    public int logInterval = 60; // Log every 60 frames (1 second at 60fps)

    private int frameCount = 0;

    void Start()
    {
        Debug.Log("=== Puppet Debugger Started ===");
        CheckSetup();
    }

    void Update()
    {
        frameCount++;

        if (logEveryFrame || frameCount % logInterval == 0)
        {
            LogStatus();
        }
    }

    void CheckSetup()
    {
        Debug.Log("--- Checking Setup ---");

        // Check PoseReceiver
        if (poseReceiver == null)
        {
            poseReceiver = FindObjectOfType<PoseReceiver>();
            if (poseReceiver == null)
            {
                Debug.LogError("❌ PoseReceiver not found! Add PoseReceiver script to a GameObject.");
            }
            else
            {
                Debug.Log("✅ PoseReceiver found automatically");
            }
        }
        else
        {
            Debug.Log("✅ PoseReceiver assigned");
        }

        // Check PuppetController
        if (puppetController == null)
        {
            puppetController = FindObjectOfType<PuppetController>();
            if (puppetController == null)
            {
                Debug.LogError("❌ PuppetController not found! Add PuppetController script to your puppet.");
            }
            else
            {
                Debug.Log("✅ PuppetController found automatically");
            }
        }
        else
        {
            Debug.Log("✅ PuppetController assigned");
        }

        // Check PoseReceiver configuration
        if (poseReceiver != null)
        {
            Debug.Log($"PoseReceiver Config:");
            Debug.Log($"  - Port: {poseReceiver.port}");
            Debug.Log($"  - Auto Start: {poseReceiver.autoStart}");
            Debug.Log($"  - Use TCP: {poseReceiver.useTCP}");
            Debug.Log($"  - Visualization: {poseReceiver.showVisualization}");
        }

        Debug.Log("--- Setup Check Complete ---");
    }

    void LogStatus()
    {
        if (poseReceiver == null || puppetController == null)
            return;

        bool hasPose = poseReceiver.HasCurrentPose();
        string status = hasPose ? "✅ RECEIVING" : "⏸️  NO DATA";

        Debug.Log($"[Frame {frameCount}] Pose Status: {status}");

        if (hasPose)
        {
            var poseData = poseReceiver.GetCurrentPose();
            if (poseData != null && poseData.landmarks != null)
            {
                Debug.Log($"  - Landmarks: {poseData.landmarks.Length}");
                Debug.Log($"  - Frame ID: {poseData.frame_id}");

                // Log sample landmark positions
                if (poseData.landmarks.Length > 0)
                {
                    var nose = System.Array.Find(poseData.landmarks, l => l.name == "nose");
                    if (nose != null)
                    {
                        Debug.Log($"  - Nose position: {nose.position}");
                    }

                    var leftWrist = System.Array.Find(poseData.landmarks, l => l.name == "left_wrist");
                    if (leftWrist != null)
                    {
                        Debug.Log($"  - Left wrist position: {leftWrist.position}");
                    }
                }
            }
        }
        else
        {
            Debug.Log("  - Waiting for pose data...");
            Debug.Log("  - Make sure Python script is running and sending UDP to port 12345");
        }
    }

    [ContextMenu("Force Check Setup")]
    public void ForceCheckSetup()
    {
        CheckSetup();
    }

    [ContextMenu("Log Current Status")]
    public void ForceLogStatus()
    {
        LogStatus();
    }

    [ContextMenu("Test Python Connection")]
    public void TestConnection()
    {
        Debug.Log("=== Testing Python Connection ===");
        Debug.Log("1. Make sure Python is running: .venv\\Scripts\\python.exe test_unity_sender.py");
        Debug.Log("2. Watch for 'UDP Received' messages in Console");
        Debug.Log("3. If no messages appear after 5 seconds, check firewall/port settings");
        
        if (poseReceiver != null)
        {
            Debug.Log($"Listening on port: {poseReceiver.port}");
        }
    }
}
