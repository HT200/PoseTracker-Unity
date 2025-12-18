using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

#region DATA MODELS

[Serializable]
public class PoseData
{
    public float timestamp;
    public int frame_id;
    public int people_count;
    public PersonData[] people;
}

[Serializable]
public class PersonData
{
    public int person_id;          // Actual person identity for tracking
    public int puppet_index;       // Which puppet renders (0=right-facing, 1=left-facing)
    public float center_x;
    public BoneRotation[] rotations;
}

[Serializable]
public class BoneRotation
{
    public string name;
    public float z;
}


#endregion

public class PoseReceiver : MonoBehaviour
{
    [Header("Network")]
    public int port = 12345;

    private UdpClient udp;
    private Thread receiveThread;
    private bool running;

    private readonly ConcurrentQueue<PoseData> queue = new();
    private PoseData latestFrame;

    public float lastReceiveTime { get; private set; }

    void Start()
    {
        // Ensure no frame rate restrictions for optimal UDP packet processing
        Application.targetFrameRate = -1;  // Unlimited frame rate
        QualitySettings.vSyncCount = 0;    // Disable VSync
        
        udp = new UdpClient(port);
        running = true;

        receiveThread = new Thread(ReceiveLoop)
        {
            IsBackground = true
        };
        receiveThread.Start();

        Debug.Log($"[PoseReceiver] Listening UDP on port {port} from ANY IP address");
        Debug.Log($"[PoseReceiver] Ready to receive data from Ubuntu machine");
        Debug.Log($"[PoseReceiver] Frame rate restrictions removed for optimal UDP processing");
    }

    void Update()
    {
        bool gotFrame = false;

        while (queue.TryDequeue(out var frame))
        {
            latestFrame = frame;
            gotFrame = true;
        }

        if (gotFrame)
            lastReceiveTime = Time.time;
    }

    void ReceiveLoop()
    {
        IPEndPoint ep = new IPEndPoint(IPAddress.Any, port);

        while (running)
        {
            try
            {
                byte[] data = udp.Receive(ref ep);
                string json = Encoding.UTF8.GetString(data);

                // Debug.Log($"[PoseReceiver] Received {data.Length} bytes from {ep.Address}:{ep.Port}");  // Commented for performance
                // Debug.Log("[RAW JSON] " + json);  // Commented for performance - this creates significant overhead

                PoseData frame = JsonUtility.FromJson<PoseData>(json);

                if (frame != null && frame.people != null)
                    queue.Enqueue(frame);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[PoseReceiver] UDP error: " + e.Message);
            }
        }
    }

    public PersonData GetPerson(int index)
    {
        if (latestFrame == null || latestFrame.people == null)
            return null;

        if (index < 0 || index >= latestFrame.people.Length)
            return null;

        return latestFrame.people[index];
    }

    public PersonData GetPersonByPuppetIndex(int puppetIndex)
    {
        if (latestFrame == null || latestFrame.people == null)
            return null;

        // Find person data that should be rendered by the specified puppet index
        foreach (PersonData person in latestFrame.people)
        {
            if (person.puppet_index == puppetIndex)
                return person;
        }

        return null;
    }

    public PersonData GetCurrentPersonData()
    {
        // Return the first available person
        return GetPerson(0);
    }

    public PersonData GetPersonById(int personId)
    {
        if (latestFrame == null || latestFrame.people == null)
            return null;

        for (int i = 0; i < latestFrame.people.Length; i++)
        {
            if (latestFrame.people[i].person_id == personId)
                return latestFrame.people[i];
        }

        return null;
    }

    void OnDestroy()
    {
        running = false;
        udp?.Close();
        receiveThread?.Abort();
    }
}
