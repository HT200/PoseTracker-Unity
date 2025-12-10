using System.Collections.Generic;
using UnityEngine;

public class PuppetController : MonoBehaviour
{
    [Header("Pose Input")]
    public PoseReceiver poseReceiver;
    public int personIndex = 0;

    [Header("Bones")]
    public Transform Lower_body;
    public Transform Upper_body;
    public Transform Head;

    public Transform Left_shoulder;
    public Transform Left_arm;
    public Transform Left_hand;

    public Transform Right_shoulder;
    public Transform Right_arm;
    public Transform Right_hand;

    public Transform Left_thigh;
    public Transform Left_leg;
    public Transform Left_foot;

    public Transform Right_thigh;
    public Transform Right_leg;
    public Transform Right_foot;

    [Header("Bone Angle Offsets (set once per character)")]
    public float torsoOffset = 90f;
    public float headOffset = -90f;

    public float leftShoulderOffset = 130f;
    public float leftArmOffset = 180f;

    public float rightShoulderOffset = -130f;
    public float rightArmOffset = 180f;

    public float leftThighOffset = 180f;
    public float leftLegOffset = 0f;

    public float rightThighOffset = 180f;
    public float rightLegOffset = 0f;

    [Header("Intensity")]
    [Range(0,1)] public float torsoIntensity = 0.7f;
    [Range(0,1)] public float headIntensity = 0.8f;
    [Range(0,1)] public float armIntensity = 1.0f;
    [Range(0,1)] public float legIntensity = 1.0f;

    [Header("Motion Smoothing")]
    [Range(0,1)] public float smoothing = 0.25f;

    [Header("Rotation Limits (degrees)")]
    public float torsoRotationLimit = 45f;
    public float headRotationLimit = 60f;
    public float shoulderRotationLimit = 120f;
    public float armRotationLimit = 150f;
    public float thighRotationLimit = 90f;
    public float legRotationLimit = 120f;

    private bool calibrated = false;

    // Neutral (user)
    private float nTorso, nHead;
    private float nLUArm, nLLArm;
    private float nRUArm, nRLArm;
    private float nLThigh, nLLeg;
    private float nRThigh, nRLeg;

    private Dictionary<string, float> smoothMap = new();

    float Smooth(string key, float target)
    {
        if (!smoothMap.ContainsKey(key))
            smoothMap[key] = target;

        float s = Mathf.LerpAngle(smoothMap[key], target, 1f - smoothing);
        smoothMap[key] = s;
        return s;
    }

    float Angle(Vector2 a, Vector2 b)
    {
        Vector2 d = (b - a).normalized;
        return Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
    }

    void LateUpdate()
    {
        if (!poseReceiver) return;

        // Use personIndex to get specific person's pose
        PoseReceiver.SimplePoseData pose = poseReceiver.GetPoseForPerson(personIndex);
        if (pose == null || pose.landmarks == null) return;

        var lm = new Dictionary<string, Vector2>();
        foreach (var p in pose.landmarks)
            lm[p.name] = new Vector2(p.position.x, p.position.y);

        if (!lm.ContainsKey("left_shoulder")) return;

        Vector2 hipC = (lm["left_hip"] + lm["right_hip"]) * 0.5f;
        Vector2 shC  = (lm["left_shoulder"] + lm["right_shoulder"]) * 0.5f;

        float torsoA = Angle(hipC, shC);
        float headA  = lm.ContainsKey("nose") ? Angle(shC, lm["nose"]) : torsoA;

        float lUp = Angle(lm["left_shoulder"], lm["left_elbow"]);
        float lLo = Angle(lm["left_elbow"], lm["left_wrist"]);

        float rUp = Angle(lm["right_shoulder"], lm["right_elbow"]);
        float rLo = Angle(lm["right_elbow"], lm["right_wrist"]);

        float lTh = Angle(lm["left_hip"], lm["left_knee"]);
        float lLg = Angle(lm["left_knee"], lm["left_ankle"]);

        float rTh = Angle(lm["right_hip"], lm["right_knee"]);
        float rLg = Angle(lm["right_knee"], lm["right_ankle"]);

        // --- First frame: capture user neutral pose ---
        if (!calibrated)
        {
            nTorso = torsoA;
            nHead = headA;

            nLUArm = lUp; nLLArm = lLo;
            nRUArm = rUp; nRLArm = rLo;

            nLThigh = lTh; nLLeg = lLg;
            nRThigh = rTh; nRLeg = rLg;

            calibrated = true;
            Debug.Log("<color=cyan>[Puppet] Neutral pose captured ✓</color>");
            return;
        }

        // --- Apply deltas with base offsets ---

        float torsoAngle = Smooth("torso", torsoOffset + Mathf.Clamp((torsoA - nTorso) * torsoIntensity, -torsoRotationLimit, torsoRotationLimit));
        Upper_body.localRotation = Quaternion.Euler(0, 0, torsoAngle - 90f);
        Lower_body.localRotation = Quaternion.Euler(0, 0, torsoAngle);

        float headAngle = Smooth("head", headOffset + Mathf.Clamp((headA - nHead) * headIntensity, -headRotationLimit, headRotationLimit));
        Head.localRotation = Quaternion.Euler(0, 0, headAngle);

        // LEFT ARM
        float lShldr = Smooth("lSh", leftShoulderOffset + Mathf.Clamp((lUp - nLUArm) * armIntensity, -shoulderRotationLimit, shoulderRotationLimit));
        float lArm   = Smooth("lArm", leftArmOffset     + Mathf.Clamp((lLo - nLLArm) * armIntensity, -armRotationLimit, armRotationLimit));

        Left_shoulder.localRotation = Quaternion.Euler(0, 0, lShldr);
        Left_arm.localRotation      = Quaternion.Euler(0, 0, lArm);

        // RIGHT ARM
        float rShldr = Smooth("rSh", rightShoulderOffset + Mathf.Clamp((rUp - nRUArm) * armIntensity, -shoulderRotationLimit, shoulderRotationLimit));
        float rArm   = Smooth("rArm", rightArmOffset      + Mathf.Clamp((rLo - nRLArm) * armIntensity, -armRotationLimit, armRotationLimit));

        Right_shoulder.localRotation = Quaternion.Euler(0, 0, rShldr);
        Right_arm.localRotation      = Quaternion.Euler(0, 0, rArm);

        // LEFT LEG
        float lThigh = Smooth("lTh", leftThighOffset + Mathf.Clamp((lTh - nLThigh) * legIntensity, -thighRotationLimit, thighRotationLimit));
        float lLeg   = Smooth("lLg", leftLegOffset   + Mathf.Clamp((lLg - nLLeg)   * legIntensity, -legRotationLimit, legRotationLimit));

        Left_thigh.localRotation = Quaternion.Euler(0, 0, lThigh);
        Left_leg.localRotation   = Quaternion.Euler(0, 0, lLeg);

        // RIGHT LEG
        float rThigh = Smooth("rTh", rightThighOffset + Mathf.Clamp((rTh - nRThigh) * legIntensity, -thighRotationLimit, thighRotationLimit));
        float rLeg   = Smooth("rLg", rightLegOffset   + Mathf.Clamp((rLg - nRLeg)   * legIntensity, -legRotationLimit, legRotationLimit));

        Right_thigh.localRotation = Quaternion.Euler(0, 0, rThigh);
        Right_leg.localRotation   = Quaternion.Euler(0, 0, rLeg);
    }
}
