using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class HeldItemSettings
{
    public GameObject prefab;
    public Vector3 positionOffset = new Vector3(0.1f, 0.5f, 0f);
    public Vector3 rotationOffset = new Vector3(0f, 0f, 90f);
}

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
    public float cameraFieldWidth = 30f;  // Unity units width that camera field represents (wider than display)
    public float cameraFieldHeight = 15f; // Unity units height that camera field represents
    public Vector2 cameraFieldOffset = new Vector2(-15f, 0f); // Offset so camera edges are off-screen
    [Range(0,1)] public float positionSmoothing = 0.05f;
    public float maxMovementSpeed = 15f; // Maximum units per second the puppet can move (prevents teleporting)

    [Header("Held Items")]
    public HeldItemSettings[] heldItemSettings; // Array of items with individual offsets
    [Range(0f, 1f)] public float itemSpawnChance = 0.5f; // Probability of spawning an item (0 = never, 1 = always)

    private bool calibrated = false;
    private bool facingLeft = false; // Auto-detected
    private Vector2 smoothedPosition = Vector2.zero;
    private int lastTrackedPersonId = -1; // Track which person ID we're currently following
    private GameObject currentHeldItem = null; // Currently held item instance
    private Dictionary<int, int> personItemAssignments = new Dictionary<int, int>(); // Maps person_id to item index (-1 = no item)
    private int currentItemIndex = -1; // Track which item index is currently held for runtime updates
    private SpriteRenderer[] spriteRenderers; // Cache all sprite renderers for enable/disable

    // Neutral (user)
    private float nTorso, nHead;
    private float nLUArm, nLLArm;
    private float nRUArm, nRLArm;
    private float nLThigh, nLLeg;
    private float nRThigh, nRLeg;

    private Dictionary<string, float> smoothMap = new();

    void Start()
    {
        // Cache all sprite renderers for efficient enable/disable
        spriteRenderers = GetComponentsInChildren<SpriteRenderer>();
    }

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
        if (pose == null || pose.landmarks == null)
        {
            // No pose detected - disable puppet
            SetPuppetActive(false);
            return;
        }

        // Pose detected - ensure puppet is visible
        SetPuppetActive(true);

        // Get actual person_id from PoseReceiver
        int currentPersonId = GetCurrentPersonId();
        if (currentPersonId == -1) return; // No valid person ID

        // Check if this is a new person ID entering the frame
        if (currentPersonId != lastTrackedPersonId)
        {
            // New person detected - spawn item based on stored assignment or create new
            SpawnHeldItemForPerson(currentPersonId);
            lastTrackedPersonId = currentPersonId;
        }

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
            
            // Invert position for puppet index 1 (flipped puppet)
            if (personIndex == 1)
            {
                worldX = -worldX;
            }
            
            Vector2 targetPosition = new Vector2(worldX, 0f);
            Vector2 smoothPos = SmoothPosition(targetPosition);
            
            // Clamp movement speed to prevent teleporting
            Vector3 currentPos = transform.position;
            float maxDelta = maxMovementSpeed * Time.deltaTime;
            float clampedX = Mathf.Clamp(smoothPos.x, currentPos.x - maxDelta, currentPos.x + maxDelta);
            
            // Update puppet root position (only X, Y stays at 0, preserve Z)
            transform.position = new Vector3(clampedX, 0f, transform.position.z);
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

        // Invert deltas for puppet index 1 (flipped puppet)
        float invertMultiplier = (personIndex == 1) ? -1f : 1f;

        // Torso - return to neutral if not visible
        float torsoAngle = torsoVisible 
            ? Smooth("torso", torsoOffset + Mathf.Clamp((torsoA - nTorso) * torsoIntensity * invertMultiplier, -torsoRotationLimit, torsoRotationLimit))
            : Smooth("torso", torsoOffset);
        Upper_body.localRotation = Quaternion.Euler(0, 0, torsoAngle - 90f);
        Lower_body.localRotation = Quaternion.Euler(0, 0, torsoAngle);

        // Head - return to neutral if not visible
        float headAngle = headVisible
            ? Smooth("head", headOffset + Mathf.Clamp((headA - nHead) * headIntensity * invertMultiplier, -headRotationLimit, headRotationLimit))
            : Smooth("head", headOffset);
        Head.localRotation = Quaternion.Euler(0, 0, headAngle);

        // LEFT ARM - return to neutral if not visible
        float lShldr = leftArmVisible
            ? Smooth("lSh", leftShoulderOffset + Mathf.Clamp((lUp - nLUArm) * armIntensity * invertMultiplier, -shoulderRotationLimit, shoulderRotationLimit))
            : Smooth("lSh", leftShoulderOffset);
        float lArm = leftArmVisible
            ? Smooth("lArm", leftArmOffset + Mathf.Clamp((lLo - nLLArm) * armIntensity * invertMultiplier, -armRotationLimit, armRotationLimit))
            : Smooth("lArm", leftArmOffset);

        Left_shoulder.localRotation = Quaternion.Euler(0, 0, lShldr);
        Left_arm.localRotation      = Quaternion.Euler(0, 0, lArm);

        // RIGHT ARM - return to neutral if not visible
        float rShldr = rightArmVisible
            ? Smooth("rSh", rightShoulderOffset + Mathf.Clamp((rUp - nRUArm) * armIntensity * invertMultiplier, -shoulderRotationLimit, shoulderRotationLimit))
            : Smooth("rSh", rightShoulderOffset);
        float rArm = rightArmVisible
            ? Smooth("rArm", rightArmOffset + Mathf.Clamp((rLo - nRLArm) * armIntensity * invertMultiplier, -armRotationLimit, armRotationLimit))
            : Smooth("rArm", rightArmOffset);

        Right_shoulder.localRotation = Quaternion.Euler(0, 0, rShldr);
        Right_arm.localRotation      = Quaternion.Euler(0, 0, rArm);

        // LEFT LEG - return to neutral if not visible
        float lThigh = leftLegVisible
            ? Smooth("lTh", leftThighOffset + Mathf.Clamp((lTh - nLThigh) * legIntensity * invertMultiplier, -thighRotationLimit, thighRotationLimit))
            : Smooth("lTh", leftThighOffset);
        float lLeg = leftLegVisible
            ? Smooth("lLg", leftLegOffset + Mathf.Clamp((lLg - nLLeg) * legIntensity * invertMultiplier, -legRotationLimit, legRotationLimit))
            : Smooth("lLg", leftLegOffset);

        Left_thigh.localRotation = Quaternion.Euler(0, 0, lThigh);
        Left_leg.localRotation   = Quaternion.Euler(0, 0, lLeg);

        // RIGHT LEG - return to neutral if not visible
        float rThigh = rightLegVisible
            ? Smooth("rTh", rightThighOffset + Mathf.Clamp((rTh - nRThigh) * legIntensity * invertMultiplier, -thighRotationLimit, thighRotationLimit))
            : Smooth("rTh", rightThighOffset);
        float rLeg = rightLegVisible
            ? Smooth("rLg", rightLegOffset + Mathf.Clamp((rLg - nRLeg) * legIntensity * invertMultiplier, -legRotationLimit, legRotationLimit))
            : Smooth("rLg", rightLegOffset);

        Right_thigh.localRotation = Quaternion.Euler(0, 0, rThigh);
        Right_leg.localRotation   = Quaternion.Euler(0, 0, rLeg);

        // Update held item position to follow right hand
        UpdateHeldItemPosition();
    }

    int GetCurrentPersonId()
    {
        // Get person_id from the current person data
        var personData = poseReceiver.GetCurrentPersonData();
        return personData != null ? personData.person_id : -1;
    }

    void SpawnHeldItemForPerson(int personId)
    {
        // Clear any existing held item
        if (currentHeldItem != null)
        {
            Destroy(currentHeldItem);
            currentHeldItem = null;
        }

        // Check if we have any item settings
        if (heldItemSettings == null || heldItemSettings.Length == 0)
            return;

        int itemIndex = -1; // -1 means no item

        // Check if this person already has an assignment
        if (personItemAssignments.ContainsKey(personId))
        {
            itemIndex = personItemAssignments[personId];
        }
        else
        {
            // New person - randomly assign item or no item
            if (Random.value <= itemSpawnChance)
            {
                // Assign random item
                itemIndex = Random.Range(0, heldItemSettings.Length);
            }
            // Store assignment for this person
            personItemAssignments[personId] = itemIndex;
        }

        // Spawn item if assigned
        if (itemIndex >= 0 && itemIndex < heldItemSettings.Length)
        {
            HeldItemSettings itemSetting = heldItemSettings[itemIndex];
            if (itemSetting.prefab != null && Right_hand != null)
            {
                // Instantiate item and attach to right hand
                currentHeldItem = Instantiate(itemSetting.prefab, Right_hand);
                currentHeldItem.transform.localPosition = itemSetting.positionOffset;
                currentHeldItem.transform.localRotation = Quaternion.Euler(itemSetting.rotationOffset);
                currentItemIndex = itemIndex; // Store for runtime updates

                Debug.Log($"<color=yellow>[Puppet] Person {personId} holding: {itemSetting.prefab.name}</color>");
            }
        }
        else
        {
            currentItemIndex = -1; // No item
            Debug.Log($"<color=yellow>[Puppet] Person {personId} has no item</color>");
        }
    }

    void UpdateHeldItemPosition()
    {
        // Apply runtime offset adjustments from inspector
        if (currentHeldItem != null && Right_hand != null && currentItemIndex >= 0 && currentItemIndex < heldItemSettings.Length)
        {
            HeldItemSettings itemSetting = heldItemSettings[currentItemIndex];
            currentHeldItem.transform.localPosition = itemSetting.positionOffset;
            currentHeldItem.transform.localRotation = Quaternion.Euler(itemSetting.rotationOffset);
        }
    }

    void SetPuppetActive(bool active)
    {
        // Enable/disable all sprite renderers
        if (spriteRenderers != null)
        {
            foreach (var sr in spriteRenderers)
            {
                if (sr != null) sr.enabled = active;
            }
        }

        // Enable/disable held item
        if (currentHeldItem != null)
        {
            currentHeldItem.SetActive(active);
        }
    }
}
