using UnityEngine;
using UnityEngine.U2D.IK;
using System.Collections.Generic;

public class PuppetController : MonoBehaviour
{
    [Header("Pose Input")]
    public PoseReceiver poseReceiver;
    public int personIndex = 0;

    [Header("Puppet Root")]
    public Transform puppetRoot;

    [Header("Body")]
    public Transform Head;
    public Transform Upper_body;
    public Transform Lower_body;

    [Header("IK Targets - Arms")]
    public Transform LeftHandTarget;
    public Transform RightHandTarget;

    [Header("IK Targets - Legs")]
    public Transform LeftFootTarget;
    public Transform RightFootTarget;

    [Header("IK Solvers")]
    public LimbSolver2D LeftArmSolver;
    public LimbSolver2D RightArmSolver;
    public LimbSolver2D LeftLegSolver;
    public LimbSolver2D RightLegSolver;

    [Header("Settings")]
    public float smoothing = 0.35f;
    public bool autoCalibrate = true;
    public bool calibrated = false;

    // CALIBRATION RESULTS
    private float torsoScale = 1f;
    private float upperArmScale, lowerArmScale;
    private float upperLegScale, lowerLegScale;
    private Vector2 puppetHipLocal;

    private Dictionary<string, Vector2> cache = new();

    public bool HasCurrentPose()
    {
        return hasValidPose && cachedSimplePose != null;
    }

    public SimplePoseData GetCurrentPose()
    {
        return cachedSimplePose;
    }

    Vector2 Smooth(string key, Vector2 newVal)
    {
        if (!cache.ContainsKey(key)) cache[key] = newVal;
        cache[key] = Vector2.Lerp(cache[key], newVal, smoothing);
        return cache[key];
    }

    float Distance(Vector2 a, Vector2 b) => (a - b).magnitude;
    float Angle(Vector2 a, Vector2 b) => Mathf.Atan2((b - a).y, (b - a).x) * Mathf.Rad2Deg;

    void Start()
    {
        puppetHipLocal = Lower_body.localPosition;
        torsoScale = 1f;
    }

    void LateUpdate()
    {
        if (!poseReceiver || !poseReceiver.HasCurrentPose()) return;

        var pose = poseReceiver.GetCurrentPose();
        if (pose == null || pose.landmarks == null) return;

        // Build raw 0–1 mocap dictionary
        Dictionary<string, Vector2> raw = new();
        foreach (var lm in pose.landmarks)
            raw[lm.name] = new Vector2(lm.position.x, lm.position.y);

        if (!raw.ContainsKey("left_hip") || !raw.ContainsKey("right_hip")) return;

        Vector2 hipC = (raw["left_hip"] + raw["right_hip"]) * 0.5f;
        Vector2 shC  = (raw["left_shoulder"] + raw["right_shoulder"]) * 0.5f;

        // ==============================
        // AUTO CALIBRATION (one time)
        // ==============================
        if (autoCalibrate && !calibrated)
        {
            float mocapTorso = Distance(hipC, shC);
            float puppetTorso = Distance(Lower_body.localPosition, Upper_body.localPosition);

            torsoScale = puppetTorso / mocapTorso;

            // ARM LENGTHS
            float mocapUpperArm = Distance(raw["left_shoulder"], raw["left_elbow"]);
            float puppetUpperArm = Distance(Upper_body.localPosition, LeftHandTarget.localPosition); // approximate
            upperArmScale = puppetUpperArm / (mocapUpperArm * torsoScale);

            float mocapLowerArm = Distance(raw["left_elbow"], raw["left_wrist"]);
            float puppetLowerArm = Distance(LeftHandTarget.localPosition, LeftFootTarget.localPosition) * 0.5f; 
            lowerArmScale = puppetLowerArm / (mocapLowerArm * torsoScale);

            // LEG LENGTHS
            float mocapUpperLeg = Distance(raw["left_hip"], raw["left_knee"]);
            float puppetUpperLeg = Distance(Lower_body.localPosition, LeftFootTarget.localPosition) * 0.7f;
            upperLegScale = puppetUpperLeg / (mocapUpperLeg * torsoScale);

            float mocapLowerLeg = Distance(raw["left_knee"], raw["left_ankle"]);
            float puppetLowerLeg = Distance(LeftFootTarget.localPosition, Lower_body.localPosition) * 0.5f;
            lowerLegScale = puppetLowerLeg / (mocapLowerLeg * torsoScale);

            calibrated = true;
            Debug.Log("<color=green>[CALIBRATED] Proportion retargeting enabled ✔</color>");
            return;
        }

        if (!calibrated) return;

        // ==============================
        // RETARGETING HELPERS
        // ==============================
        Vector2 MapLimb(Vector2 baseHip, Vector2 rawJoint, float limbScale)
        {
            Vector2 rel = (rawJoint - baseHip) * torsoScale * limbScale;
            return puppetHipLocal + rel;
        }

        // ==============================
        // ARM TARGETS
        // ==============================

        Vector2 lElbow = raw["left_elbow"];
        Vector2 lWrist = raw["left_wrist"];

        Vector2 rElbow = raw["right_elbow"];
        Vector2 rWrist = raw["right_wrist"];

        LeftHandTarget.localPosition =
            Smooth("lh", MapLimb(hipC, lWrist, lowerArmScale));

        RightHandTarget.localPosition =
            Smooth("rh", MapLimb(hipC, rWrist, lowerArmScale));

        LeftArmSolver.UpdateIK(0);
        RightArmSolver.UpdateIK(0);

        // ==============================
        // LEG TARGETS
        // ==============================
        Vector2 lAnk = raw["left_ankle"];
        Vector2 rAnk = raw["right_ankle"];

        LeftFootTarget.localPosition =
            Smooth("lf", MapLimb(hipC, lAnk, lowerLegScale));

        RightFootTarget.localPosition =
            Smooth("rf", MapLimb(hipC, rAnk, lowerLegScale));

        LeftLegSolver.UpdateIK(0);
        RightLegSolver.UpdateIK(0);

        // ==============================
        // BODY ROTATION
        // ==============================
        float torsoAngle = Angle(hipC, shC);
        Upper_body.localRotation = Quaternion.Euler(0, 0, torsoAngle);
        Lower_body.localRotation = Quaternion.Euler(0, 0, torsoAngle);

        float headAngle = Angle(shC, raw["nose"]);
        Head.localRotation = Quaternion.Euler(0, 0, headAngle);
    }
}
