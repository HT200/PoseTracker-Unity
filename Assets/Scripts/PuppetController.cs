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
    private bool facingLeft = false; // Auto-detected

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
        var visibility = new Dictionary<string, float>();
        foreach (var p in pose.landmarks)
        {
            lm[p.name] = new Vector2(p.position.x, p.position.y);
            visibility[p.name] = p.visibility;
        }

        if (!lm.ContainsKey("left_shoulder")) return;

        // Check visibility for each limb (threshold of 0.5)
        bool torsoVisible = visibility.GetValueOrDefault("left_hip", 0) > 0.5f && visibility.GetValueOrDefault("right_hip", 0) > 0.5f &&
                            visibility.GetValueOrDefault("left_shoulder", 0) > 0.5f && visibility.GetValueOrDefault("right_shoulder", 0) > 0.5f;
        bool headVisible = visibility.GetValueOrDefault("nose", 0) > 0.5f;
        bool leftArmVisible = visibility.GetValueOrDefault("left_shoulder", 0) > 0.5f && visibility.GetValueOrDefault("left_elbow", 0) > 0.5f && visibility.GetValueOrDefault("left_wrist", 0) > 0.5f;
        bool rightArmVisible = visibility.GetValueOrDefault("right_shoulder", 0) > 0.5f && visibility.GetValueOrDefault("right_elbow", 0) > 0.5f && visibility.GetValueOrDefault("right_wrist", 0) > 0.5f;
        bool leftLegVisible = visibility.GetValueOrDefault("left_hip", 0) > 0.5f && visibility.GetValueOrDefault("left_knee", 0) > 0.5f && visibility.GetValueOrDefault("left_ankle", 0) > 0.5f;
        bool rightLegVisible = visibility.GetValueOrDefault("right_hip", 0) > 0.5f && visibility.GetValueOrDefault("right_knee", 0) > 0.5f && visibility.GetValueOrDefault("right_ankle", 0) > 0.5f;

        Vector2 hipC = (lm["left_hip"] + lm["right_hip"]) * 0.5f;
        Vector2 shC  = (lm["left_shoulder"] + lm["right_shoulder"]) * 0.5f;

        // Auto-detect facing direction based on shoulder depth (x position in camera space)
        // Assuming mirrored camera view: if left shoulder has higher x, person is facing left
        facingLeft = lm["left_shoulder"].x > lm["right_shoulder"].x;

        float torsoA = Angle(hipC, shC);
        float headA  = lm.ContainsKey("nose") ? Angle(shC, lm["nose"]) : torsoA;

        // Get arm angles and inverse them for side view puppet
        float lUp = -Angle(lm["right_shoulder"], lm["right_elbow"]);
        float lLo = -Angle(lm["right_elbow"], lm["right_wrist"]);

        float rUp = -Angle(lm["left_shoulder"], lm["left_elbow"]);
        float rLo = -Angle(lm["left_elbow"], lm["left_wrist"]);

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

        // Torso - return to neutral if not visible
        float torsoAngle = torsoVisible 
            ? Smooth("torso", torsoOffset + Mathf.Clamp((torsoA - nTorso) * torsoIntensity, -torsoRotationLimit, torsoRotationLimit))
            : Smooth("torso", torsoOffset);
        Upper_body.localRotation = Quaternion.Euler(0, 0, torsoAngle - 90f);
        Lower_body.localRotation = Quaternion.Euler(0, 0, torsoAngle);

        // Head - return to neutral if not visible
        float headAngle = headVisible
            ? Smooth("head", headOffset + Mathf.Clamp((headA - nHead) * headIntensity, -headRotationLimit, headRotationLimit))
            : Smooth("head", headOffset);
        Head.localRotation = Quaternion.Euler(0, 0, headAngle);

        // LEFT ARM - return to neutral if not visible
        float lShldr = leftArmVisible
            ? Smooth("lSh", leftShoulderOffset + Mathf.Clamp((lUp - nLUArm) * armIntensity, -shoulderRotationLimit, shoulderRotationLimit))
            : Smooth("lSh", leftShoulderOffset);
        float lArm = leftArmVisible
            ? Smooth("lArm", leftArmOffset + Mathf.Clamp((lLo - nLLArm) * armIntensity, -armRotationLimit, armRotationLimit))
            : Smooth("lArm", leftArmOffset);

        Left_shoulder.localRotation = Quaternion.Euler(0, 0, lShldr);
        Left_arm.localRotation      = Quaternion.Euler(0, 0, lArm);

        // RIGHT ARM - return to neutral if not visible
        float rShldr = rightArmVisible
            ? Smooth("rSh", rightShoulderOffset + Mathf.Clamp((rUp - nRUArm) * armIntensity, -shoulderRotationLimit, shoulderRotationLimit))
            : Smooth("rSh", rightShoulderOffset);
        float rArm = rightArmVisible
            ? Smooth("rArm", rightArmOffset + Mathf.Clamp((rLo - nRLArm) * armIntensity, -armRotationLimit, armRotationLimit))
            : Smooth("rArm", rightArmOffset);

        Right_shoulder.localRotation = Quaternion.Euler(0, 0, rShldr);
        Right_arm.localRotation      = Quaternion.Euler(0, 0, rArm);

        // LEFT LEG - return to neutral if not visible
        float lThigh = leftLegVisible
            ? Smooth("lTh", leftThighOffset + Mathf.Clamp((lTh - nLThigh) * legIntensity, -thighRotationLimit, thighRotationLimit))
            : Smooth("lTh", leftThighOffset);
        float lLeg = leftLegVisible
            ? Smooth("lLg", leftLegOffset + Mathf.Clamp((lLg - nLLeg) * legIntensity, -legRotationLimit, legRotationLimit))
            : Smooth("lLg", leftLegOffset);

        Left_thigh.localRotation = Quaternion.Euler(0, 0, lThigh);
        Left_leg.localRotation   = Quaternion.Euler(0, 0, lLeg);

        // RIGHT LEG - return to neutral if not visible
        float rThigh = rightLegVisible
            ? Smooth("rTh", rightThighOffset + Mathf.Clamp((rTh - nRThigh) * legIntensity, -thighRotationLimit, thighRotationLimit))
            : Smooth("rTh", rightThighOffset);
        float rLeg = rightLegVisible
            ? Smooth("rLg", rightLegOffset + Mathf.Clamp((rLg - nRLeg) * legIntensity, -legRotationLimit, legRotationLimit))
            : Smooth("rLg", rightLegOffset);

        Right_thigh.localRotation = Quaternion.Euler(0, 0, rThigh);
        Right_leg.localRotation   = Quaternion.Euler(0, 0, rLeg);
    }
}
