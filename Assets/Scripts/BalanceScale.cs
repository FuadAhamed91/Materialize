using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Beam balance. Weighs the matter in the sensor pan against a sealed counterweight of unknown
/// mass: the beam tilts towards the heavier side, and the exit opens once it holds level.
/// The pans hang from the beam ends, so whatever sits in them rides along.
/// </summary>
public class BalanceScale : MonoBehaviour
{
    public Transform beam;
    [Tooltip("Kinematic pan bodies; their pivots sit at the beam's hanging points.")]
    public Rigidbody sensorPan;
    public Rigidbody weightedPan;
    [Tooltip("Trigger box inside the sensor pan.")]
    public BoxCollider sensorZone;
    public float counterweightMass = 640f;
    [Range(0.01f, 0.3f)] public float tolerance = 0.08f;
    public float maxTilt = 12f;
    [Tooltip("Degrees of tilt per 1% of imbalance, before clamping.")]
    public float sensitivity = 0.4f;
    public float tiltSpeed = 18f;

    [Header("Feedback")]
    public ChamberDoor[] targets;
    public Renderer[] indicators;
    [ColorUsage(false, true)] public Color unsettledColor = new Color(2.2f, 1.3f, 0.2f);
    [ColorUsage(false, true)] public Color levelColor = new Color(0.3f, 2.4f, 0.5f);
    public string announce = "";

    public float SensorMass { get; private set; }
    public float Tilt { get; private set; }
    public bool Balanced { get; private set; }

    Vector3 pivot, sensorArm, weightedArm;
    Quaternion beamRest;
    float levelTime;
    readonly Collider[] hits = new Collider[64];
    readonly HashSet<Rigidbody> bodies = new HashSet<Rigidbody>();

    void Awake()
    {
        pivot = beam.position;
        beamRest = beam.rotation;
        sensorArm = sensorPan.position - pivot;
        weightedArm = weightedPan.position - pivot;
        sensorZone.isTrigger = true;
    }

    void Start() => SetIndicators(unsettledColor);

    void FixedUpdate()
    {
        SensorMass = Measure();
        float imbalance = (SensorMass - counterweightMass) / counterweightMass; // + : sensor side heavier
        float target = Balanced ? 0f : Mathf.Clamp(imbalance * 100f * sensitivity, -maxTilt, maxTilt);
        Tilt = Mathf.MoveTowards(Tilt, target, tiltSpeed * Time.fixedDeltaTime);

        // lower the sensor side when it is heavier
        Quaternion roll = Quaternion.AngleAxis(-Tilt * Mathf.Sign(sensorArm.x), Vector3.forward);
        beam.rotation = roll * beamRest;
        sensorPan.MovePosition(pivot + roll * sensorArm);
        weightedPan.MovePosition(pivot + roll * weightedArm);

        if (Balanced) return;
        bool level = SensorMass > 0f && Mathf.Abs(imbalance) <= tolerance && Mathf.Abs(Tilt - target) < 0.5f;
        levelTime = level ? levelTime + Time.fixedDeltaTime : 0f;
        if (levelTime >= 0.75f) Balance();
    }

    float Measure()
    {
        Vector3 center = sensorZone.transform.TransformPoint(sensorZone.center);
        Vector3 half = Vector3.Scale(sensorZone.size * 0.5f, sensorZone.transform.lossyScale);
        int count = Physics.OverlapBoxNonAlloc(center, half, hits, sensorZone.transform.rotation, ~0, QueryTriggerInteraction.Ignore);
        bodies.Clear();
        float total = 0f;
        for (int i = 0; i < count; i++)
        {
            Rigidbody rb = hits[i].attachedRigidbody;
            if (rb == null || rb == sensorPan || rb.isKinematic) continue;
            if (bodies.Add(rb)) total += rb.mass;
        }
        return total;
    }

    void Balance()
    {
        Balanced = true;
        SetIndicators(levelColor);
        ImpactAudio.Play(ImpactAudio.Clang, beam.position, 0.8f, 1.6f);
        if (targets != null)
            foreach (ChamberDoor door in targets)
                if (door) door.Open();
        if (!string.IsNullOrEmpty(announce) && MatterHUD.Instance) MatterHUD.Instance.Flash(announce, new Color(0.55f, 1f, 0.65f));
    }

    void SetIndicators(Color color)
    {
        if (indicators == null) return;
        foreach (Renderer r in indicators)
            if (r) ChamberDoor.Glow(r, color);
    }
}
