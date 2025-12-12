using System.Collections.Generic;
using UnityEngine;

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

    [Header("Smoothing")]
    private Dictionary<string, float> baselineMap = new();
    private bool calibrated = false;
    [Range(0, 1)] public float smoothing = 0.1f;

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
    }
}