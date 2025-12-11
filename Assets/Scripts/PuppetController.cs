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
    [Range(0,1)] public float armIntensity = 0.6f;
    [Range(0,1)] public float legIntensity = 1.0f;

    [Header("Motion Smoothing")]
    [Range(0,1)] public float smoothing = 0.05f;

    [Header("Rotation Limits (degrees)")]
    public float torsoRotationLimit = 45f;
    public float headRotationLimit = 60f;
    public float shoulderRotationLimit = 120f;
    public float armRotationLimit = 150f;
    public float thighRotationLimit = 90f;
    public float legRotationLimit = 120f;

    [Header("Position Tracking")]
    public bool enablePositionTracking = true;
    public float cameraFieldWidth = 20f;  // Unity units width that camera field represents
    public float cameraFieldHeight = 15f; // Unity units height that camera field represents
    public Vector2 cameraFieldOffset = new Vector2(-5f, 0f); // Offset so puppet can enter from sides
    [Range(0,1)] public float positionSmoothing = 0.05f;

    private bool calibrated = false;
    private bool facingLeft = false; // Auto-detected
    private Vector2 smoothedPosition = Vector2.zero;

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

    Vector2 SmoothPosition(Vector2 target)
    {
        smoothedPosition = Vector2.Lerp(smoothedPosition, target, 1f - positionSmoothing);
        return smoothedPosition;
    }

    float Angle(Vector2 a, Vector2 b)
    {
        Vector2 d = (b - a).normalized;
        return Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
    }

    float CalculateElbowBend(Vector3 shoulder, Vector3 elbow, Vector3 wrist)
    {
        // Calculate vectors
        Vector3 upperArm = elbow - shoulder;
        Vector3 forearm = wrist - elbow;
        
        // Calculate the angle between upper arm and forearm
        // This gives us the actual elbow bend angle
        float dotProduct = Vector3.Dot(upperArm.normalized, forearm.normalized);
        float elbowBendAngle = Mathf.Acos(Mathf.Clamp(dotProduct, -1f, 1f)) * Mathf.Rad2Deg;
        
        // Convert to rotation angle (180 = straight, 0 = fully bent)
        return 180f - elbowBendAngle;
    }

    float ArmAngleWithDepth(Vector3 shoulder, Vector3 elbow, Vector3 wrist, bool isLeftArm)
    {
        // For side view puppets, we need to consider all dimensions for proper arm tracking
        // Use X (horizontal), Y (vertical), and Z (depth) for complete 3D tracking
        
        Vector3 upperArm = elbow - shoulder;
        
        // For side-view puppets, primary rotation is around the Z axis (forward/back swing)
        // But we also need to consider vertical component (Y)
        // Project the arm direction onto the YZ plane for side view
        // Use positive Z for natural forward direction
        float shoulderAngle = Mathf.Atan2(upperArm.y, upperArm.z) * Mathf.Rad2Deg;
        
        return shoulderAngle;
    }

    float ForearmAngleWithDepth(Vector3 shoulder, Vector3 elbow, Vector3 wrist, bool isLeftArm)
    {
        // Calculate the forearm angle relative to the upper arm
        // This creates natural elbow bending
        
        Vector3 upperArm = (elbow - shoulder).normalized;
        Vector3 forearm = wrist - elbow;
        
        // Project forearm onto YZ plane for side view
        // Use positive Z for natural forward direction
        float forearmAngle = Mathf.Atan2(forearm.y, forearm.z) * Mathf.Rad2Deg;
        
        return forearmAngle;
    }

    void LateUpdate()
    {
        if (!poseReceiver) return;

        // Use personIndex to get specific person's pose
        PoseReceiver.SimplePoseData pose = poseReceiver.GetPoseForPerson(personIndex);
        if (pose == null || pose.landmarks == null) return;

        var lm = new Dictionary<string, Vector2>();
        var lm3d = new Dictionary<string, Vector3>();
        var visibility = new Dictionary<string, float>();
        foreach (var p in pose.landmarks)
        {
            lm[p.name] = new Vector2(p.position.x, p.position.y);
            lm3d[p.name] = p.position; // Keep 3D for depth-aware calculations
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

        // Update puppet position based on lower body (hip center)
        if (enablePositionTracking && torsoVisible)
        {
            // Use hip center for X position (lower body position)
            Vector2 lowerBodyPosition = hipC;
            
            // Map camera space (0-1) to world space with offset
            // Camera field extends beyond screen bounds so puppets can enter from sides
            float worldX = (lowerBodyPosition.x * cameraFieldWidth) + cameraFieldOffset.x;
            
            Vector2 targetPosition = new Vector2(worldX, 0f);
            Vector2 smoothPos = SmoothPosition(targetPosition);
            
            // Update puppet root position (only X, Y stays at 0, preserve Z)
            transform.position = new Vector3(smoothPos.x, 0f, transform.position.z);
        }

        // Auto-detect facing direction based on shoulder depth (x position in camera space)
        // Assuming mirrored camera view: if left shoulder has higher x, person is facing left
        facingLeft = lm["left_shoulder"].x > lm["right_shoulder"].x;

        float torsoA = Angle(hipC, shC);
        
        // Calculate head rotation based on forward/backward lean using depth (z)
        float headA = torsoA; // Default fallback
        if (lm.ContainsKey("nose") && lm3d.ContainsKey("nose"))
        {
            Vector3 nosePos = lm3d["nose"];
            Vector3 shoulderCenter3d = (lm3d["left_shoulder"] + lm3d["right_shoulder"]) * 0.5f;
            
            // Calculate head lean using Y (vertical) and Z (depth/forward-backward)
            Vector3 headVector = nosePos - shoulderCenter3d;
            // For side-view, use depth (z) and vertical (y) to determine forward/backward lean
            headA = Mathf.Atan2(headVector.y, headVector.z) * Mathf.Rad2Deg;
        }

        // Calculate arm angles using depth (z) for proper side-view tracking
        // For sideways puppets, depth represents forward/backward arm movement
        float lUp, lLo, rUp, rLo;
        
        if (lm3d.ContainsKey("left_shoulder") && lm3d.ContainsKey("left_elbow") && lm3d.ContainsKey("left_wrist"))
        {
            // Left puppet arm tracks left body arm (direct mapping)
            lUp = ArmAngleWithDepth(lm3d["left_shoulder"], lm3d["left_elbow"], lm3d["left_wrist"], true);
            lLo = ForearmAngleWithDepth(lm3d["left_shoulder"], lm3d["left_elbow"], lm3d["left_wrist"], true);
        }
        else
        {
            // Fallback to 2D calculation
            lUp = -Angle(lm["left_shoulder"], lm["left_elbow"]);
            lLo = -Angle(lm["left_elbow"], lm["left_wrist"]);
        }
        
        if (lm3d.ContainsKey("right_shoulder") && lm3d.ContainsKey("right_elbow") && lm3d.ContainsKey("right_wrist"))
        {
            // Right puppet arm tracks right body arm (direct mapping)
            rUp = ArmAngleWithDepth(lm3d["right_shoulder"], lm3d["right_elbow"], lm3d["right_wrist"], false);
            rLo = ForearmAngleWithDepth(lm3d["right_shoulder"], lm3d["right_elbow"], lm3d["right_wrist"], false);
        }
        else
        {
            // Fallback to 2D calculation
            rUp = -Angle(lm["right_shoulder"], lm["right_elbow"]);
            rLo = -Angle(lm["right_elbow"], lm["right_wrist"]);
        }

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
