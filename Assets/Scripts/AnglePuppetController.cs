using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Angle-based 2D puppet driver.
/// Reads MediaPipe joints from PoseReceiver and rotates bones
/// by matching joint angles (no IK, no position mapping).
/// Works with Test_Puppet hierarchy:
/// Test_Puppet
///   ├─ Lower_body
///   ├─ Upper_body
///   ├─ Head
///   ├─ Left_shoulder → Left_arm → Left_hand
///   ├─ Right_shoulder → Right_arm → Right_hand
///   ├─ Left_thigh → Left_leg → Left_foot
///   └─ Right_thigh → Right_leg → Right_foot
/// </summary>
public class AnglePuppetController : MonoBehaviour
{
    [Header("Pose input")]
    public PoseReceiver poseReceiver;
    public int personIndex = 0;

    [Header("Bones (assign from hierarchy)")]
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

    [Header("Settings")]
    public bool autoCalibrateOnFirstPose = true;
    public float angleSmoothing = 0.4f; // 0 = snappy, 1 = very smooth
    public float armIntensity = 1.0f;
    public float legIntensity = 1.0f;
    public float torsoIntensity = 0.7f;
    public float headIntensity = 0.8f;

    private bool calibrated = false;

    // Rest angles from your rig
    private float restTorsoAngle;
    private float restHeadAngle;
    private float restLeftUpperArmAngle;
    private float restLeftLowerArmAngle;
    private float restRightUpperArmAngle;
    private float restRightLowerArmAngle;
    private float restLeftThighAngle;
    private float restLeftLegAngle;
    private float restRightThighAngle;
    private float restRightLegAngle;

    // Neutral mocap angles captured at calibration
    private float neutralTorsoAngle;
    private float neutralHeadAngle;
    private float neutralLeftUpperArmAngle;
    private float neutralLeftLowerArmAngle;
    private float neutralRightUpperArmAngle;
    private float neutralRightLowerArmAngle;
    private float neutralLeftThighAngle;
    private float neutralLeftLegAngle;
    private float neutralRightThighAngle;
    private float neutralRightLegAngle;

    // Smoothing cache
    private readonly Dictionary<string, float> _smoothedAngles = new();

    void Start()
    {
        // capture rig rest pose angles (what you see in editor)
        restTorsoAngle           = Upper_body.localEulerAngles.z;
        restHeadAngle            = Head.localEulerAngles.z;
        restLeftUpperArmAngle    = Left_arm.localEulerAngles.z;
        restLeftLowerArmAngle    = Left_hand.localEulerAngles.z;
        restRightUpperArmAngle   = Right_arm.localEulerAngles.z;
        restRightLowerArmAngle   = Right_hand.localEulerAngles.z;
        restLeftThighAngle       = Left_thigh.localEulerAngles.z;
        restLeftLegAngle         = Left_leg.localEulerAngles.z;
        restRightThighAngle      = Right_thigh.localEulerAngles.z;
        restRightLegAngle        = Right_leg.localEulerAngles.z;
    }

    void LateUpdate()
    {
        if (poseReceiver == null || !poseReceiver.HasCurrentPose())
            return;

        var pose = poseReceiver.GetCurrentPose();
        if (pose == null || pose.landmarks == null || pose.landmarks.Length == 0)
            return;

        // make quick lookup by name
        var lm = new Dictionary<string, Vector2>();
        foreach (var l in pose.landmarks)
        {
            lm[l.name] = new Vector2(l.position.x, l.position.y);
        }

        // require core joints
        if (!lm.ContainsKey("left_shoulder") || !lm.ContainsKey("right_shoulder") ||
            !lm.ContainsKey("left_hip")      || !lm.ContainsKey("right_hip"))
            return;

        // helper to get angle (in degrees) from a → b
        float A(string a, string b)
        {
            var p1 = lm[a];
            var p2 = lm[b];
            var d = (p2 - p1).normalized;
            return Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        }

        // ------------- compute current mocap angles -------------

        Vector2 hipMid   = (lm["left_hip"] + lm["right_hip"]) * 0.5f;
        Vector2 shoulderMid = (lm["left_shoulder"] + lm["right_shoulder"]) * 0.5f;

        float torsoAngle      = AngleDeg(hipMid, shoulderMid);
        float headAngle       = lm.ContainsKey("nose") ? AngleDeg(shoulderMid, lm["nose"]) : torsoAngle;

        float lUpperArmAngle  = A("left_shoulder", "left_elbow");
        float lLowerArmAngle  = A("left_elbow", "left_wrist");

        float rUpperArmAngle  = A("right_shoulder", "right_elbow");
        float rLowerArmAngle  = A("right_elbow", "right_wrist");

        float lThighAngle     = A("left_hip", "left_knee");
        float lLegAngle       = A("left_knee", "left_ankle");

        float rThighAngle     = A("right_hip", "right_knee");
        float rLegAngle       = A("right_knee", "right_ankle");

        // --------- first valid frame: capture NEUTRAL mocap pose ----------
        if (autoCalibrateOnFirstPose && !calibrated)
        {
            neutralTorsoAngle        = torsoAngle;
            neutralHeadAngle         = headAngle;
            neutralLeftUpperArmAngle = lUpperArmAngle;
            neutralLeftLowerArmAngle = lLowerArmAngle;
            neutralRightUpperArmAngle= rUpperArmAngle;
            neutralRightLowerArmAngle= rLowerArmAngle;
            neutralLeftThighAngle    = lThighAngle;
            neutralLeftLegAngle      = lLegAngle;
            neutralRightThighAngle   = rThighAngle;
            neutralRightLegAngle     = rLegAngle;

            calibrated = true;
            Debug.Log("<color=green>[AnglePuppet] Calibrated neutral pose.</color>");
        }

        if (!calibrated)
            return;

        // ---------- apply delta angles (mocap - neutral) to bones -----------

        ApplyBoneAngle("torso",
            Upper_body, restTorsoAngle,
            torsoAngle - neutralTorsoAngle,
            torsoIntensity);

        ApplyBoneAngle("lower_torso",
            Lower_body, restTorsoAngle,
            torsoAngle - neutralTorsoAngle,
            torsoIntensity);

        ApplyBoneAngle("head",
            Head, restHeadAngle,
            headAngle - neutralHeadAngle,
            headIntensity);

        // left arm
        ApplyBoneAngle("lUpperArm",
            Left_arm, restLeftUpperArmAngle,
            lUpperArmAngle - neutralLeftUpperArmAngle,
            armIntensity);

        ApplyBoneAngle("lLowerArm",
            Left_hand, restLeftLowerArmAngle,
            lLowerArmAngle - neutralLeftLowerArmAngle,
            armIntensity);

        // right arm
        ApplyBoneAngle("rUpperArm",
            Right_arm, restRightUpperArmAngle,
            rUpperArmAngle - neutralRightUpperArmAngle,
            armIntensity);

        ApplyBoneAngle("rLowerArm",
            Right_hand, restRightLowerArmAngle,
            rLowerArmAngle - neutralRightLowerArmAngle,
            armIntensity);

        // left leg
        ApplyBoneAngle("lThigh",
            Left_thigh, restLeftThighAngle,
            lThighAngle - neutralLeftThighAngle,
            legIntensity);

        ApplyBoneAngle("lLeg",
            Left_leg, restLeftLegAngle,
            lLegAngle - neutralLeftLegAngle,
            legIntensity);

        // right leg
        ApplyBoneAngle("rThigh",
            Right_thigh, restRightThighAngle,
            rThighAngle - neutralRightThighAngle,
            legIntensity);

        ApplyBoneAngle("rLeg",
            Right_leg, restRightLegAngle,
            rLegAngle - neutralRightLegAngle,
            legIntensity);
    }

    float AngleDeg(Vector2 a, Vector2 b)
    {
        Vector2 d = (b - a).normalized;
        return Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
    }

    void ApplyBoneAngle(string key, Transform bone, float restAngle, float deltaFromNeutral, float intensity)
    {
        float target = restAngle + deltaFromNeutral * intensity;

        if (!_smoothedAngles.ContainsKey(key))
            _smoothedAngles[key] = target;

        float smoothed = Mathf.LerpAngle(_smoothedAngles[key], target, 1f - angleSmoothing);
        _smoothedAngles[key] = smoothed;

        bone.localRotation = Quaternion.Euler(0, 0, smoothed);
    }
}
