using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class HeldItemSettings
{
    public GameObject prefab;
    public Vector3 positionOffset = new Vector3(0.1f, 0.5f, 0f);
    public Vector3 rotationOffset = new Vector3(0f, 0f, 90f);
    public Vector3 scaleOffset = new Vector3(1f, 1f, 1f);  // Scale multiplier for the held item
}

[System.Serializable]
public class BoneConfig
{
    public Transform bone;
    public float bindZ;     // góc khởi đầu của nhân vật
    public float userBindZ; // góc khởi đầu của người dùng
    public float minDelta;  // giới hạn dưới
    public float maxDelta;  // giới hạn trên
}
public class PuppetController : MonoBehaviour
{
    [Header("Pose Input")]
    public PoseReceiver receiver;
    public int targetPersonId = 0;

    [Header("Rig Config")]
    public BoneConfig LowerBody;    // lock

    public BoneConfig Head;

    public BoneConfig LeftShoulder;
    public BoneConfig LeftArm;

    public BoneConfig RightShoulder;
    public BoneConfig RightArm;

    public BoneConfig LeftThigh;
    public BoneConfig LeftLeg;

    public BoneConfig RightThigh;
    public BoneConfig RightLeg;

    [Header("Held Items")]
    public HeldItemSettings[] heldItemSettings; // Array of items with individual offsets
    [Range(0f, 1f)] public float itemSpawnChance = 0.5f; // Probability of spawning an item (0 = never, 1 = always)

    [Header("Position Tracking")]
    public bool enablePositionTracking = true;
    public float cameraFieldWidth = 10f;  // Reduced from 30f to bring puppets closer together
    [Range(0,1)] public float positionSmoothing = 0.01f;  // Reduced from 0.05f for faster response
    public float maxMovementSpeed = 15f; // Increased from 15f for faster movement (prevents teleporting)
    
    [Header("Y Axis Lock")]
    public bool lockYAxis = false;  // Enable Y axis locking
    public float bindYPosition = 0f;  // Y position to lock puppet to

    [Header("Smoothing")]
    // Removed baselineMap and calibration system to eliminate stuttering
    [Range(0, 1)] public float smoothing = 0.02f;  // Reduced from 0.1f for faster bone response

    // Held item tracking
    private int lastTrackedPersonId = -1; // Track which person ID we're currently following
    private GameObject currentHeldItem = null; // Currently held item instance
    private Dictionary<int, int> personItemAssignments = new Dictionary<int, int>(); // Maps person_id to item index (-1 = no item)
    private int currentItemIndex = -1; // Track which item index is currently held for runtime updates
    private Vector2 smoothedPosition = Vector2.zero; // For position tracking
    
    // Person change detection - track more than just person ID
    private float lastPersonCenterX = -999f; // Track center position to detect person swaps
    private float personSwapThreshold = 0.3f; // How much center position can change before we consider it a new person
    
    // Puppet visibility control
    private bool isPuppetVisible = true;
    private Renderer[] puppetRenderers; // Cache all renderers for performance
    private Collider[] puppetColliders; // Cache all colliders for performance

    // Calibration system removed to eliminate stuttering
    // Now using direct pose data for better performance
    
    void Start()
    {
        // Cache all renderers and colliders in the puppet hierarchy for efficient visibility toggling
        puppetRenderers = GetComponentsInChildren<Renderer>();
        puppetColliders = GetComponentsInChildren<Collider>();
    }
    
    void SetPuppetVisibility(bool visible)
    {
        if (isPuppetVisible == visible) return; // Skip if already in correct state
        
        isPuppetVisible = visible;
        
        // Toggle all renderers
        if (puppetRenderers != null)
        {
            for (int i = 0; i < puppetRenderers.Length; i++)
            {
                puppetRenderers[i].enabled = visible;
            }
        }
        
        // Toggle all colliders
        if (puppetColliders != null)
        {
            for (int i = 0; i < puppetColliders.Length; i++)
            {
                puppetColliders[i].enabled = visible;
            }
        }
        
        // Hide held items when puppet is hidden
        if (currentHeldItem != null)
        {
            currentHeldItem.SetActive(visible);
        }
    }

    void ApplyBone(BoneConfig cfg, string key, float pythonZ, float angleMultiplier = 1f)
    {
        if (!cfg.bone) return;

        // delta dựa trên base người dùng nhập tay
        float delta = Mathf.DeltaAngle(cfg.userBindZ, pythonZ);
        // dùng DeltaAngle để tránh lỗi nhảy góc 179 -> -179

        // Apply angle inversion for puppet index 1
        delta *= angleMultiplier;

        float clamped = Mathf.Clamp(delta, cfg.minDelta, cfg.maxDelta);

        float finalZ = cfg.bindZ + clamped;

        cfg.bone.localRotation = Quaternion.Euler(
            0, 0,
            Smooth(key, finalZ)
        );
    }

    private Dictionary<string, float> smoothMap = new();

    float Smooth(string key, float target)
    {
        if (!smoothMap.ContainsKey(key))
            smoothMap[key] = target;

        // Use faster lerp with reduced smoothing for better responsiveness
        float lerpFactor = 1f - smoothing;
        smoothMap[key] = Mathf.LerpAngle(smoothMap[key], target, lerpFactor);
        return smoothMap[key];
    }

    Vector2 SmoothPosition(Vector2 target)
    {
        // Use faster position smoothing for immediate response
        float lerpFactor = 1f - positionSmoothing;
        smoothedPosition = Vector2.Lerp(smoothedPosition, target, lerpFactor);
        return smoothedPosition;
    }

    float GetRelativeRotation(string parent, string child, PersonData person)
    {
        float parentZ = GetRotationZ(person, parent);
        float childZ = GetRotationZ(person, child);

        // child relative to parent
        return Mathf.DeltaAngle(parentZ, childZ);
    }

    float GetRotationZ(PersonData person, string bone)
    {
        if (person.rotations == null) return 0f;

        // Optimized lookup - avoid creating objects in loop
        for (int i = 0; i < person.rotations.Length; i++)
        {
            if (person.rotations[i].name == bone)
                return person.rotations[i].z;
        }

        return 0f;
    }

    void ApplyBoneRelative(BoneConfig cfg, string key, float relativeZ, float angleMultiplier = 1f)
    {
        if (!cfg.bone) return;

        relativeZ = -relativeZ;

        // Apply angle inversion for puppet index 1
        relativeZ *= angleMultiplier;

        // relativeZ đã là delta rồi → clamp trực tiếp
        float clamped = Mathf.Clamp(relativeZ, cfg.minDelta, cfg.maxDelta);

        float finalZ = cfg.bindZ + clamped;

        cfg.bone.localRotation = Quaternion.Euler(
            0, 0,
            Smooth(key, finalZ)
        );
    }

    void LateUpdate()
    {
        if (!receiver) return;

        var person = receiver.GetPerson(targetPersonId);
        
        // Hide puppet if no pose data available for this index
        if (person == null)
        {
            SetPuppetVisibility(false);
            return;
        }
        
        // Show puppet if pose data is available
        SetPuppetVisibility(true);

        // Get actual person_id from receiver
        int currentPersonId = GetCurrentPersonId();
        float currentCenterX = person.center_x;
        
        if (currentPersonId != -1)
        {
            // Check if this is a new person entering the frame
            bool isNewPersonId = (currentPersonId != lastTrackedPersonId);
            bool isPersonSwapped = (lastTrackedPersonId != -1 && 
                                   Mathf.Abs(currentCenterX - lastPersonCenterX) > personSwapThreshold);
            
            // Spawn new item if it's a new person ID OR if someone swapped into the same ID slot
            if (isNewPersonId || isPersonSwapped)
            {
                // New person detected - spawn item based on stored assignment or create new
                SpawnHeldItemForPerson(currentPersonId);
                lastTrackedPersonId = currentPersonId;
                
                Debug.Log($"Person change detected: ID={currentPersonId}, CenterX={currentCenterX:F2}, " +
                         $"NewID={isNewPersonId}, Swapped={isPersonSwapped}");
            }
            
            // Update tracking variables
            lastPersonCenterX = currentCenterX;
        }

        // Removed calibration system - using direct pose data for better performance
        
        // Update puppet position based on person's center_x
        if (enablePositionTracking)
        {
            // Use person's center_x for position
            float personCenterX = person.center_x;
            
            // Map camera space (0-1) to world space
            float worldX = (personCenterX * cameraFieldWidth) - (cameraFieldWidth * 0.5f);
            
            // Determine Y position based on lock setting
            float targetY = lockYAxis ? bindYPosition : 0f;
            
            Vector2 targetPosition = new Vector2(worldX, targetY);
            Vector2 smoothPos = SmoothPosition(targetPosition);
            
            // Update puppet root position - respect Y axis lock setting
            float finalY = lockYAxis ? bindYPosition : smoothPos.y;
            transform.position = new Vector3(smoothPos.x, finalY, transform.position.z);
        }

        // Apply angle inversion for puppet index 1 (because scale is inverted)
        float angleMultiplier = (targetPersonId == 1) ? -1f : 1f;

        // Cache rotation values to avoid repeated dictionary lookups
        float torsoWorld = GetRotationZ(person, "torso");
        float headRot = GetRotationZ(person, "head");
        float lUpper = GetRotationZ(person, "left_upper_arm");
        float lLower = GetRotationZ(person, "left_lower_arm");
        float rUpper = GetRotationZ(person, "right_upper_arm");
        float rLower = GetRotationZ(person, "right_lower_arm");
        float leftThighRot = GetRotationZ(person, "left_thigh");
        float leftLegRot = GetRotationZ(person, "left_leg");
        float rightThighRot = GetRotationZ(person, "right_thigh");
        float rightLegRot = GetRotationZ(person, "right_leg");

        // Apply rotations using cached values
        float torsoRel = Mathf.DeltaAngle(LowerBody.userBindZ, torsoWorld) * -2f;
        ApplyBoneRelative(LowerBody, "torso", torsoRel, angleMultiplier);

        ApplyBone(Head, "head", headRot, angleMultiplier);

        // Arms
        ApplyBone(LeftShoulder, "left_upper_arm", lUpper, angleMultiplier);
        float lLowerRel = -Mathf.DeltaAngle(lUpper, lLower);
        ApplyBoneRelative(LeftArm, "left_lower_arm", lLowerRel, angleMultiplier);

        ApplyBone(RightShoulder, "right_upper_arm", rUpper, angleMultiplier);
        float rLowerRel = -Mathf.DeltaAngle(rUpper, rLower);
        ApplyBoneRelative(RightArm, "right_lower_arm", rLowerRel, angleMultiplier);

        // Legs
        ApplyBone(LeftThigh, "left_thigh", leftThighRot, angleMultiplier);
        ApplyBone(LeftLeg, "left_leg", leftLegRot, angleMultiplier);
        ApplyBone(RightThigh, "right_thigh", rightThighRot, angleMultiplier);
        ApplyBone(RightLeg, "right_leg", rightLegRot, angleMultiplier);

        // Update held item position
        UpdateHeldItemPosition();
    }

    int GetCurrentPersonId()
    {
        return targetPersonId;
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

        // Always assign a new random item when a new person ID is detected
        if (Random.value <= itemSpawnChance)
        {
            // Assign random item (different from previous if possible)
            if (heldItemSettings.Length > 1)
            {
                // Try to get a different item than the current one
                int newItemIndex;
                do
                {
                    newItemIndex = Random.Range(0, heldItemSettings.Length);
                } while (newItemIndex == currentItemIndex && heldItemSettings.Length > 1);
                itemIndex = newItemIndex;
            }
            else
            {
                // Only one item available
                itemIndex = 0;
            }
        }
        
        // Update the assignment for this person
        personItemAssignments[personId] = itemIndex;

        // Spawn item if assigned
        if (itemIndex >= 0 && itemIndex < heldItemSettings.Length)
        {
            HeldItemSettings itemSetting = heldItemSettings[itemIndex];
            if (itemSetting.prefab != null && RightArm.bone != null)
            {
                // Instantiate item and attach to right hand (using RightArm bone as hand)
                currentHeldItem = Instantiate(itemSetting.prefab, RightArm.bone);
                currentHeldItem.transform.localPosition = itemSetting.positionOffset;
                currentHeldItem.transform.localRotation = Quaternion.Euler(itemSetting.rotationOffset);
                currentHeldItem.transform.localScale = itemSetting.scaleOffset;  // Apply scale offset
                currentItemIndex = itemIndex; // Store for runtime updates

                // Debug.Log($"<color=yellow>[Puppet] Person {personId} holding: {itemSetting.prefab.name}</color>");  // Commented out for performance
            }
        }
        else
        {
            currentItemIndex = -1; // No item
            // Debug.Log($"<color=yellow>[Puppet] Person {personId} has no item</color>");  // Commented out for performance
        }
    }

    void UpdateHeldItemPosition()
    {
        // Apply runtime offset adjustments from inspector
        if (currentHeldItem != null && RightArm.bone != null && currentItemIndex >= 0 && currentItemIndex < heldItemSettings.Length)
        {
            HeldItemSettings itemSetting = heldItemSettings[currentItemIndex];
            currentHeldItem.transform.localPosition = itemSetting.positionOffset;
            currentHeldItem.transform.localRotation = Quaternion.Euler(itemSetting.rotationOffset);
            currentHeldItem.transform.localScale = itemSetting.scaleOffset;  // Apply scale offset
        }
    }
}