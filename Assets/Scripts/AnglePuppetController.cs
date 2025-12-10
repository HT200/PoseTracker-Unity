using System.Collections.Generic;
using UnityEngine;

public class AnglePuppetController : MonoBehaviour
{
    [Header("Pose Input")]
    public PoseReceiver poseReceiver;
    public int personIndex = 0;

    [Header("Bones")]
    public Transform Lower_body;
    public Transform Upper_body;
    public Transform Head;

    public Transform Left_arm;
    public Transform Left_hand;

    public Transform Right_arm;
    public Transform Right_hand;

    public Transform Left_thigh;
    public Transform Left_leg;

    public Transform Right_thigh;
    public Transform Right_leg;

    [Header("Bone Angle Offsets (edit until correct)")]
    public float torsoBaseOffset = 0f;
    public float headBaseOffset = 0f;
    public float leftUpperArmBaseOffset = 0f;
    public float leftLowerArmBaseOffset = 0f;
    public float rightUpperArmBaseOffset = 0f;
    public float rightLowerArmBaseOffset = 0f;
    public float leftThighBaseOffset = 0f;
    public float leftLegBaseOffset = 0f;
    public float rightThighBaseOffset = 0f;
    public float rightLegBaseOffset = 0f;

    [Header("Intensity")]
    public float torsoIntensity = 0.7f;
    public float headIntensity = 0.8f;
    public float armIntensity = 1f;
    public float legIntensity = 1f;

    [Header("Smoothing")]
    public float smoothing = 0.3f;

    private bool calibrated = false;

    private float neutralTorso, neutralHead;
    private float neutralLUArm, neutralLLArm;
    private float neutralRUArm, neutralRLArm;
    private float neutralLThigh, neutralLLeg;
    private float neutralRThigh, neutralRLeg;

    // smoothing cache
    private readonly Dictionary<string, float> smoothMap = new();

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
        if (!poseReceiver || !poseReceiver.HasCurrentPose()) return;

        var pose = poseReceiver.GetCurrentPose();
        if (pose == null || pose.landmarks == null) return;

        var lm = new Dictionary<string, Vector2>();
        foreach (var p in pose.landmarks)
            lm[p.name] = new Vector2(p.position.x, p.position.y);

        if (!lm.ContainsKey("left_shoulder")) return;

        Vector2 hipC = (lm["left_hip"] + lm["right_hip"]) * 0.5f;
        Vector2 shC = (lm["left_shoulder"] + lm["right_shoulder"]) * 0.5f;

        float torsoA = Angle(hipC, shC);
        float headA = lm.ContainsKey("nose") ? Angle(shC, lm["nose"]) : torsoA;

        float lUpperA = Angle(lm["left_shoulder"], lm["left_elbow"]);
        float lLowerA = Angle(lm["left_elbow"], lm["left_wrist"]);

        float rUpperA = Angle(lm["right_shoulder"], lm["right_elbow"]);
        float rLowerA = Angle(lm["right_elbow"], lm["right_wrist"]);

        float lThighA = Angle(lm["left_hip"], lm["left_knee"]);
        float lLegA   = Angle(lm["left_knee"], lm["left_ankle"]);

        float rThighA = Angle(lm["right_hip"], lm["right_knee"]);
        float rLegA   = Angle(lm["right_knee"], lm["right_ankle"]);

        // FIRST FRAME → capture neutral pose
        if (!calibrated)
        {
            neutralTorso = torsoA;
            neutralHead = headA;
            neutralLUArm = lUpperA;
            neutralLLArm = lLowerA;
            neutralRUArm = rUpperA;
            neutralRLArm = rLowerA;
            neutralLThigh = lThighA;
            neutralLLeg = lLegA;
            neutralRThigh = rThighA;
            neutralRLeg = rLegA;

            calibrated = true;
            Debug.Log("<color=yellow>[Offsets Controller] Neutral captured.</color>");
            return;
        }

        // apply delta angles + base offsets
        float t = Smooth("torso", torsoBaseOffset + (torsoA - neutralTorso) * torsoIntensity);
        Upper_body.localRotation = Quaternion.Euler(0, 0, t);
        Lower_body.localRotation = Quaternion.Euler(0, 0, t);

        float h = Smooth("head", headBaseOffset + (headA - neutralHead) * headIntensity);
        Head.localRotation = Quaternion.Euler(0, 0, h);

        Left_arm.localRotation  = Quaternion.Euler(0, 0,
            leftUpperArmBaseOffset + (lUpperA - neutralLUArm) * armIntensity);
        Left_hand.localRotation = Quaternion.Euler(0, 0,
            leftLowerArmBaseOffset + (lLowerA - neutralLLArm) * armIntensity);

        Right_arm.localRotation = Quaternion.Euler(0, 0,
            rightUpperArmBaseOffset + (rUpperA - neutralRUArm) * armIntensity);
        Right_hand.localRotation = Quaternion.Euler(0, 0,
            rightLowerArmBaseOffset + (rLowerA - neutralRLArm) * armIntensity);

        Left_thigh.localRotation = Quaternion.Euler(0, 0,
            leftThighBaseOffset + (lThighA - neutralLThigh) * legIntensity);
        Left_leg.localRotation = Quaternion.Euler(0, 0,
            leftLegBaseOffset + (lLegA - neutralLLeg) * legIntensity);

        Right_thigh.localRotation = Quaternion.Euler(0, 0,
            rightThighBaseOffset + (rThighA - neutralRThigh) * legIntensity);
        Right_leg.localRotation = Quaternion.Euler(0, 0,
            rightLegBaseOffset + (rLegA - neutralRLeg) * legIntensity);
    }
}
