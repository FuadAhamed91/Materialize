using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Pressure switch. Sums the mass of every dynamic Rigidbody resting inside its trigger box and opens
/// the target doors once the total reaches <see cref="requiredMass"/>. Works on a static plate or on a
/// moving one (the counterweight bucket): its own Rigidbody is ignored.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class WeightTriggerPlate : MonoBehaviour
{
    public float requiredMass = 500f;
    [Tooltip("The load must rest on the plate this long, so matter sliding or bouncing across doesn't count.")]
    public float holdSeconds = 0.4f;
    public ChamberDoor[] targets;
    [Tooltip("Stay triggered once activated, even if the load is removed.")]
    public bool latch = true;

    [Header("Feedback")]
    public Text readout;
    public Renderer[] indicators;
    public Color underColor = new Color(1f, 0.75f, 0.2f);
    public Color metColor = new Color(0.45f, 1f, 0.55f);

    public float CurrentMass { get; private set; }
    public bool Activated { get; private set; }

    BoxCollider zone;
    Rigidbody ownBody;
    float heldFor;
    readonly Collider[] hits = new Collider[64];
    readonly HashSet<Rigidbody> bodies = new HashSet<Rigidbody>();

    void Awake()
    {
        zone = GetComponent<BoxCollider>();
        zone.isTrigger = true;
        ownBody = GetComponentInParent<Rigidbody>();
    }

    void Start() => UpdateReadout();

    void FixedUpdate()
    {
        Vector3 center = transform.TransformPoint(zone.center);
        Vector3 halfExtents = Vector3.Scale(zone.size * 0.5f, transform.lossyScale);
        int count = Physics.OverlapBoxNonAlloc(center, halfExtents, hits, transform.rotation, ~0, QueryTriggerInteraction.Ignore);

        bodies.Clear();
        float total = 0f;
        for (int i = 0; i < count; i++)
        {
            Rigidbody rb = hits[i].attachedRigidbody;
            if (rb == null || rb == ownBody || rb.isKinematic) continue;
            if (bodies.Add(rb)) total += rb.mass;
        }
        CurrentMass = total;
        heldFor = total >= requiredMass ? heldFor + Time.fixedDeltaTime : 0f;

        if (!Activated && heldFor >= holdSeconds) SetActivated(true);
        else if (Activated && !latch && total < requiredMass * 0.95f) SetActivated(false);
        UpdateReadout();
    }

    void SetActivated(bool on)
    {
        Activated = on;
        if (targets != null)
            foreach (ChamberDoor door in targets)
            {
                if (!door) continue;
                if (on) door.Open();
                else door.Close();
            }
        if (indicators != null)
            foreach (Renderer r in indicators)
                if (r) ChamberDoor.Glow(r, (on ? metColor : underColor) * 2f);
    }

    void UpdateReadout()
    {
        if (!readout) return;
        readout.text = $"LOAD  {CurrentMass:N0} kg"; // no target shown: players find the threshold themselves
        readout.color = Activated || CurrentMass >= requiredMass ? metColor : underColor;
    }
}
