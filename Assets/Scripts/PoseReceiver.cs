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
    public int person_id;
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
        udp = new UdpClient(port);
        running = true;

        receiveThread = new Thread(ReceiveLoop)
        {
            IsBackground = true
        };
        receiveThread.Start();

        Debug.Log($"[PoseReceiver] Listening UDP on {port}");
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

                Debug.Log("[RAW JSON] " + json);

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

    void OnDestroy()
    {
        running = false;
        udp?.Close();
        receiveThread?.Abort();
    }
}
