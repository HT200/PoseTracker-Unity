using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.IO;

[System.Serializable]
public class PoseData
{
    public float timestamp;
    public int frame_id;
    public int people_count;
    public List<PersonData> people;
}

[System.Serializable]
public class PersonData
{
    public int person_id;
    public int landmark_count;
    public List<LandmarkData> landmarks;
    public List<List<int>> bone_connections;
}

[System.Serializable]
public class LandmarkData
{
    public int id;
    public string name;
    public Vector3 position;
    public float visibility;
}

public class PoseReceiver : MonoBehaviour
{
    [Header("Network Settings")]
    public string host = "127.0.0.1";
    public int port = 12345;
    public bool useTCP = false;
    public bool autoStart = true;

    [Header("Pose Data")]
    public int targetPersonId = 0; // Which person to track (0 = first person)
    public bool filterLowVisibility = true;
    public float visibilityThreshold = 0.5f;

    [Header("Visualization")]
    public bool showVisualization = true;
    public GameObject landmarkPrefab;
    public LineRenderer bonePrefab;
    public Transform poseParent;

    [Header("Debug")]
    public bool logReceivedData = false;
    public bool logParseErrors = true;

    // Simplified pose data structure for PuppetController compatibility
    [System.Serializable]
    public class SimplePoseData
    {
        public LandmarkData[] landmarks;
        public float timestamp;
        public int frame_id;
    }

    // Current pose data for puppet controller
    private PoseData currentPoseData;
    private PersonData currentPersonData;
    private SimplePoseData cachedSimplePose;
    private bool hasValidPose = false;
    private float lastPoseUpdateTime = 0f;
    private readonly float poseTimeoutSeconds = 1.0f;

    private UdpClient udpClient;
    private TcpClient tcpClient;
    private Thread receiveThread;
    private bool isReceiving = false;

    private List<GameObject> currentLandmarks = new List<GameObject>();
    private List<LineRenderer> currentBones = new List<LineRenderer>();

    // Thread-safe queue for pose updates
    private readonly ConcurrentQueue<PoseData> poseUpdateQueue = new ConcurrentQueue<PoseData>();

    // TCP message buffering
    private StringBuilder tcpBuffer = new StringBuilder();

    void Start()
    {
        if (autoStart)
        {
            if (useTCP)
                StartTCPReceiver();
            else
                StartUDPReceiver();
        }
    }

    void Update()
    {
        // Process pose updates on main thread
        while (poseUpdateQueue.TryDequeue(out var poseData))
        {
            ProcessPoseUpdate(poseData);
        }
    }

    void StartUDPReceiver()
    {
        try
        {
            udpClient = new UdpClient(port);
            isReceiving = true;
            receiveThread = new Thread(ReceiveUDPData) { IsBackground = true };
            receiveThread.Start();
            Debug.Log("UDP Receiver started on port " + port);
        }
        catch (Exception e)
        {
            Debug.LogError("Failed to start UDP receiver: " + e.Message);
        }
    }

    void StartTCPReceiver()
    {
        try
        {
            tcpClient = new TcpClient(host, port);
            isReceiving = true;
            receiveThread = new Thread(ReceiveTCPData) { IsBackground = true };
            receiveThread.Start();
            Debug.Log("TCP connected to " + host + ":" + port);
        }
        catch (Exception e)
        {
            Debug.LogError("Failed to connect TCP: " + e.Message);
        }
    }

    void ReceiveUDPData()
    {
        while (isReceiving)
        {
            try
            {
                IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = udpClient.Receive(ref remoteEndPoint);
                string jsonData = Encoding.UTF8.GetString(data);

                Debug.Log($"[PoseReceiver] UDP packet received - {data.Length} bytes from {remoteEndPoint}");
                
                if (logReceivedData)
                {
                    Debug.Log($"UDP Received ({data.Length} bytes): {jsonData.Substring(0, Math.Min(200, jsonData.Length))}...");
                }

                // Handle potentially concatenated JSON messages
                ProcessJsonData(jsonData);
            }
            catch (SocketException se)
            {
                if (isReceiving)
                    Debug.LogError("UDP receive socket error: " + se.Message);
            }
            catch (Exception e)
            {
                Debug.LogError("UDP receive error: " + e.Message);
            }
        }
    }

    void ReceiveTCPData()
    {
        try
        {
            NetworkStream stream = tcpClient.GetStream();
            byte[] buffer = new byte[4096];

            while (isReceiving)
            {
                try
                {
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead > 0)
                    {
                        string receivedData = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                        if (logReceivedData)
                        {
                            Debug.Log($"TCP Received ({bytesRead} bytes): {receivedData.Substring(0, Math.Min(200, receivedData.Length))}...");
                        }

                        // Append to buffer and process complete messages
                        tcpBuffer.Append(receivedData);
                        ProcessTcpBuffer();
                    }
                    else
                    {
                        break; // Connection closed
                    }
                }
                catch (IOException ioex)
                {
                    if (isReceiving)
                        Debug.LogError("TCP receive IO error: " + ioex.Message);
                    break;
                }
                catch (Exception e)
                {
                    Debug.LogError("TCP receive error: " + e.Message);
                    break;
                }
            }
        }
        catch (Exception e)
        {
            if (isReceiving)
                Debug.LogError("TCP receive setup error: " + e.Message);
        }
    }

    void ProcessTcpBuffer()
    {
        string bufferContent = tcpBuffer.ToString();

        // Look for complete JSON messages (assuming each message ends with newline or is a complete JSON object)
        int lastProcessedIndex = 0;

        // Try to find complete JSON objects
        for (int i = 0; i < bufferContent.Length; i++)
        {
            if (bufferContent[i] == '\n' || bufferContent[i] == '\r')
            {
                // Extract potential JSON message
                string jsonCandidate = bufferContent.Substring(lastProcessedIndex, i - lastProcessedIndex).Trim();
                if (!string.IsNullOrEmpty(jsonCandidate))
                {
                    ProcessJsonData(jsonCandidate);
                }
                lastProcessedIndex = i + 1;
            }
        }

        // If no newlines found, try to process the entire buffer as JSON
        if (lastProcessedIndex == 0)
        {
            ProcessJsonData(bufferContent.Trim());
            tcpBuffer.Clear();
        }
        else
        {
            // Remove processed data from buffer, keep unprocessed data
            string remainingData = bufferContent.Substring(lastProcessedIndex);
            tcpBuffer.Clear();
            tcpBuffer.Append(remainingData);
        }
    }

    void ProcessJsonData(string jsonData)
    {
        if (string.IsNullOrEmpty(jsonData))
            return;

        // Handle multiple JSON objects in one message (separated by newlines)
        string[] jsonMessages = jsonData.Split(new char[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (string jsonMessage in jsonMessages)
        {
            string trimmedJson = jsonMessage.Trim();
            if (string.IsNullOrEmpty(trimmedJson))
                continue;

            try
            {
                // Validate JSON starts and ends properly
                if (!trimmedJson.StartsWith("{") || !trimmedJson.EndsWith("}"))
                {
                    if (logParseErrors)
                        Debug.LogWarning($"Invalid JSON format: {trimmedJson.Substring(0, Math.Min(100, trimmedJson.Length))}...");
                    continue;
                }

                Debug.Log($"[PoseReceiver] Parsing JSON (length: {trimmedJson.Length}): {trimmedJson.Substring(0, Math.Min(200, trimmedJson.Length))}...");
                PoseData poseData = JsonUtility.FromJson<PoseData>(trimmedJson);
                if (poseData != null)
                {
                    Debug.Log($"[PoseReceiver] Successfully parsed PoseData - people_count: {poseData.people_count}, people list: {poseData.people?.Count ?? 0}");
                    poseUpdateQueue.Enqueue(poseData);
                }
                else
                {
                    if (logParseErrors)
                        Debug.LogWarning("JSON parsed to null PoseData");
                }
            }
            catch (System.ArgumentException ex)
            {
                if (logParseErrors)
                    Debug.LogError($"JSON parse error: {ex.Message}. Data: {trimmedJson.Substring(0, Math.Min(200, trimmedJson.Length))}...");
            }
            catch (Exception ex)
            {
                if (logParseErrors)
                    Debug.LogError($"Unexpected error parsing JSON: {ex.Message}");
            }
        }
    }

    void ProcessPoseUpdate(PoseData poseData)
    {
        // Store pose data for puppet controller
        currentPoseData = poseData;
        lastPoseUpdateTime = Time.time;

        Debug.Log($"[PoseReceiver] Received pose data - Frame: {poseData?.frame_id}, People: {poseData?.people_count}, Has people list: {poseData?.people != null}, People list count: {poseData?.people?.Count}");

        // Select target person (default to first person or specified person ID)
        if (poseData != null && poseData.people != null && poseData.people.Count > 0)
        {
            if (targetPersonId < poseData.people.Count)
            {
                currentPersonData = poseData.people[targetPersonId];
            }
            else
            {
                currentPersonData = poseData.people[0]; // Fallback to first person
            }

            // Create simplified pose data for PuppetController
            if (currentPersonData != null && currentPersonData.landmarks != null)
            {
                cachedSimplePose = new SimplePoseData
                {
                    landmarks = currentPersonData.landmarks.ToArray(),
                    timestamp = poseData.timestamp,
                    frame_id = poseData.frame_id
                };
                Debug.Log($"[PoseReceiver] Created SimplePoseData with {cachedSimplePose.landmarks.Length} landmarks");
            }
            else
            {
                Debug.LogWarning($"[PoseReceiver] currentPersonData is null or has no landmarks");
            }

            hasValidPose = true;
        }
        else
        {
            Debug.LogWarning($"[PoseReceiver] Invalid pose data - poseData null: {poseData == null}, people null: {poseData?.people == null}, people count: {poseData?.people?.Count}");
            currentPersonData = null;
            cachedSimplePose = null;
            hasValidPose = false;
        }

        // Update pose visualization
        if (showVisualization)
        {
            VisualizePose(poseData);
        }
    }

    void VisualizePose(PoseData poseData)
    {
        ClearCurrentPose();

        if (poseData?.people != null && poseData.people.Count > 0)
        {
            PersonData person = poseData.people[0]; // Use first person

            if (person?.landmarks != null)
            {
                // Create landmarks
                foreach (LandmarkData landmark in person.landmarks)
                {
                    if (landmark != null && landmark.visibility > visibilityThreshold && landmarkPrefab != null)
                    {
                        GameObject landmarkObj = Instantiate(landmarkPrefab, poseParent);
                        landmarkObj.transform.localPosition = landmark.position * 5; // Scale up
                        landmarkObj.name = landmark.name;
                        currentLandmarks.Add(landmarkObj);
                    }
                }
            }

            // Create bones
            if (person?.bone_connections != null)
            {
                foreach (List<int> connection in person.bone_connections)
                {
                    if (connection != null && connection.Count == 2)
                    {
                        int startIdx = connection[0];
                        int endIdx = connection[1];

                        if (person.landmarks != null && startIdx < person.landmarks.Count && endIdx < person.landmarks.Count)
                        {
                            LandmarkData start = person.landmarks[startIdx];
                            LandmarkData end = person.landmarks[endIdx];

                            if (start != null && end != null && start.visibility > visibilityThreshold && end.visibility > visibilityThreshold && bonePrefab != null)
                            {
                                LineRenderer bone = Instantiate(bonePrefab, poseParent);
                                bone.positionCount = 2;
                                bone.SetPosition(0, start.position * 5);
                                bone.SetPosition(1, end.position * 5);
                                currentBones.Add(bone);
                            }
                        }
                    }
                }
            }
        }
    }

    void ClearCurrentPose()
    {
        foreach (GameObject landmark in currentLandmarks)
            if (landmark != null) DestroyImmediate(landmark);
        currentLandmarks.Clear();

        foreach (LineRenderer bone in currentBones)
            if (bone != null) DestroyImmediate(bone.gameObject);
        currentBones.Clear();
    }

    // Public interface for Puppet Controller
    public bool HasCurrentPose()
    {
        return hasValidPose &&
               currentPersonData != null &&
               cachedSimplePose != null &&
               (Time.time - lastPoseUpdateTime) < poseTimeoutSeconds;
    }

    public SimplePoseData GetCurrentPose()
    {
        return HasCurrentPose() ? cachedSimplePose : null;
    }

    // Get pose data for a specific person index
    public bool HasPoseForPerson(int personIndex)
    {
        return hasValidPose &&
               currentPoseData != null &&
               currentPoseData.people != null &&
               personIndex >= 0 &&
               personIndex < currentPoseData.people.Count &&
               (Time.time - lastPoseUpdateTime) < poseTimeoutSeconds;
    }

    public SimplePoseData GetPoseForPerson(int personIndex)
    {
        if (!HasPoseForPerson(personIndex)) return null;

        PersonData person = currentPoseData.people[personIndex];
        if (person == null || person.landmarks == null) return null;

        SimplePoseData simplePose = new SimplePoseData
        {
            landmarks = person.landmarks.ToArray(),
            timestamp = currentPoseData.timestamp,
            frame_id = currentPoseData.frame_id
        };

        return simplePose;
    }

    // Legacy support for direct PersonData access
    public PersonData GetCurrentPersonData()
    {
        return currentPersonData;
    }

    public PoseData GetCurrentPoseData()
    {
        return currentPoseData;
    }

    public LandmarkData GetLandmark(string landmarkName)
    {
        if (!HasCurrentPose()) return null;

        foreach (var landmark in currentPersonData.landmarks)
        {
            if (landmark.name.Equals(landmarkName, System.StringComparison.OrdinalIgnoreCase))
            {
                if (!filterLowVisibility || landmark.visibility >= visibilityThreshold)
                    return landmark;
            }
        }
        return null;
    }

    public LandmarkData GetLandmark(int landmarkId)
    {
        if (!HasCurrentPose()) return null;

        foreach (var landmark in currentPersonData.landmarks)
        {
            if (landmark.id == landmarkId)
            {
                if (!filterLowVisibility || landmark.visibility >= visibilityThreshold)
                    return landmark;
            }
        }
        return null;
    }

    public Vector3 GetLandmarkPosition(string landmarkName)
    {
        var landmark = GetLandmark(landmarkName);
        return landmark != null ? landmark.position : Vector3.zero;
    }

    public Vector3 GetLandmarkPosition(int landmarkId)
    {
        var landmark = GetLandmark(landmarkId);
        return landmark != null ? landmark.position : Vector3.zero;
    }

    public float GetLandmarkVisibility(string landmarkName)
    {
        var landmark = GetLandmark(landmarkName);
        return landmark != null ? landmark.visibility : 0f;
    }

    public bool IsLandmarkVisible(string landmarkName, float threshold = -1f)
    {
        if (threshold < 0) threshold = visibilityThreshold;
        var landmark = GetLandmark(landmarkName);
        return landmark != null && landmark.visibility >= threshold;
    }

    void OnDestroy()
    {
        isReceiving = false;

        // Close sockets first to unblock blocking reads
        try { udpClient?.Close(); } catch { }
        try { tcpClient?.Close(); } catch { }

        if (receiveThread != null && receiveThread.IsAlive)
        {
            if (!receiveThread.Join(500))
            {
#pragma warning disable SYSLIB0006
                receiveThread.Abort();
#pragma warning restore SYSLIB0006
            }
        }
    }
}