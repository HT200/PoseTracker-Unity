using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class HeldItemSettings
{
    public GameObject prefab;
    public Vector3 positionOffset = new Vector3(0.1f, 0.5f, 0f);
    public Vector3 rotationOffset = new Vector3(0f, 0f, 90f);
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
    public int personIndex = 0;

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

    [Header("Smoothing")]
    private Dictionary<string, float> baselineMap = new();
    private bool calibrated = false;
    [Range(0, 1)] public float smoothing = 0.1f;

    // Held item tracking
    private int lastTrackedPersonId = -1; // Track which person ID we're currently following
    private GameObject currentHeldItem = null; // Currently held item instance
    private Dictionary<int, int> personItemAssignments = new Dictionary<int, int>(); // Maps person_id to item index (-1 = no item)
    private int currentItemIndex = -1; // Track which item index is currently held for runtime updates

    void Calibrate(PersonData person)
    {
        baselineMap.Clear();

        string[] bones = {
        "torso","head",
        "left_upper_arm","left_lower_arm",
        "right_upper_arm","right_lower_arm",
        "left_thigh","left_leg",
        "right_thigh","right_leg"
    };

        foreach (var b in bones)
            baselineMap[b] = GetRotationZ(person, b);

        calibrated = true;
        Debug.Log("✅ Pose calibrated");
    }

    void ApplyBone(BoneConfig cfg, string key, float pythonZ)
    {
        if (!cfg.bone) return;

        // delta dựa trên base người dùng nhập tay
        float delta = Mathf.DeltaAngle(cfg.userBindZ, pythonZ);
        // dùng DeltaAngle để tránh lỗi nhảy góc 179 -> -179

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

        smoothMap[key] = Mathf.LerpAngle(
            smoothMap[key],
            target,
            1f - smoothing
        );
        return smoothMap[key];
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

        foreach (var r in person.rotations)
            if (r.name == bone)
                return r.z;

        return 0f;
    }

    void ApplyBoneRelative(BoneConfig cfg, string key, float relativeZ)
    {
        if (!cfg.bone) return;

        relativeZ = -relativeZ;

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

        var person = receiver.GetPerson(personIndex);
        if (person == null) return;

        // Get actual person_id from receiver
        int currentPersonId = GetCurrentPersonId();
        if (currentPersonId != -1)
        {
            // Check if this is a new person ID entering the frame
            if (currentPersonId != lastTrackedPersonId)
            {
                // New person detected - spawn item based on stored assignment or create new
                SpawnHeldItemForPerson(currentPersonId);
                lastTrackedPersonId = currentPersonId;
            }
        }

        if (!calibrated)
        {
            Calibrate(person);
            return; // frame đầu không xoay
        }

        float torsoWorld = GetRotationZ(person, "torso");

        // relative torso vs vertical (hoặc vs bind)
        float torsoRel = Mathf.DeltaAngle(
            LowerBody.userBindZ,
            torsoWorld
        );
        torsoRel *= -2f;

        ApplyBoneRelative(LowerBody, "torso", torsoRel);

        ApplyBone(Head, "head",
            GetRotationZ(person, "head")
        );

        // ===== LEFT ARM =====
        float lUpper = GetRotationZ(person, "left_upper_arm");
        float lLower = GetRotationZ(person, "left_lower_arm");

        ApplyBone(LeftShoulder, "left_upper_arm", lUpper);

        // LOWER ARM = RELATIVE → KHÔNG DÙNG ApplyBone
        float lLowerRel = -Mathf.DeltaAngle(lUpper, lLower);
        ApplyBoneRelative(LeftArm, "left_lower_arm", lLowerRel);


        // ===== RIGHT ARM =====
        float rUpper = GetRotationZ(person, "right_upper_arm");
        float rLower = GetRotationZ(person, "right_lower_arm");

        ApplyBone(RightShoulder, "right_upper_arm", rUpper);

        float rLowerRel = -Mathf.DeltaAngle(rUpper, rLower);
        ApplyBoneRelative(RightArm, "right_lower_arm", rLowerRel);

        ApplyBone(LeftThigh, "left_thigh",
            GetRotationZ(person, "left_thigh")
        );

        ApplyBone(LeftLeg, "left_leg",
            GetRotationZ(person, "left_leg")
        );

        ApplyBone(RightThigh, "right_thigh",
            GetRotationZ(person, "right_thigh")
        );

        ApplyBone(RightLeg, "right_leg",
            GetRotationZ(person, "right_leg")
        );

        // Update held item position to follow left hand
        UpdateHeldItemPosition();
    }

    int GetCurrentPersonId()
    {
        // Get person_id from the current person data
        var person = receiver.GetPerson(personIndex);
        return person != null ? person.person_id : -1;
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
            if (itemSetting.prefab != null && LeftArm.bone != null)
            {
                // Instantiate item and attach to left hand (using LeftArm bone as hand)
                currentHeldItem = Instantiate(itemSetting.prefab, LeftArm.bone);
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
        if (currentHeldItem != null && LeftArm.bone != null && currentItemIndex >= 0 && currentItemIndex < heldItemSettings.Length)
        {
            HeldItemSettings itemSetting = heldItemSettings[currentItemIndex];
            currentHeldItem.transform.localPosition = itemSetting.positionOffset;
            currentHeldItem.transform.localRotation = Quaternion.Euler(itemSetting.rotationOffset);
        }
    }
}