using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Copper terminal circuit. Closed when conductive placed matter (alone, or a chain of touching
/// conductors) connects node A to node B; then it opens the target doors.
/// </summary>
public class ElectricalBridge : MonoBehaviour
{
    public Collider nodeA;
    public Collider nodeB;
    public ChamberDoor[] targets;
    [Tooltip("Stay powered once the circuit has closed.")]
    public bool latch = true;
    [Tooltip("How close (m) a conductor must be to count as touching.")]
    public float contactTolerance = 0.06f;

    [Header("Feedback")]
    public Renderer[] indicators;
    [ColorUsage(false, true)] public Color idleColor = new Color(0.15f, 0.55f, 1.6f);
    [ColorUsage(false, true)] public Color poweredColor = new Color(0.3f, 2.6f, 0.5f);
    public string announce = "Circuit closed · power restored";

    public bool Powered { get; private set; }

    readonly Collider[] hits = new Collider[64];
    float nextCheck;

    void Start() => SetIndicators(idleColor);

    void FixedUpdate()
    {
        if (Time.time < nextCheck || (Powered && latch) || !nodeA || !nodeB) return;
        nextCheck = Time.time + 0.1f;

        bool closed = CircuitClosed();
        if (closed == Powered) return;
        Powered = closed;
        SetIndicators(closed ? poweredColor : idleColor);
        foreach (ChamberDoor door in targets)
        {
            if (!door) continue;
            if (closed) door.Open();
            else door.Close();
        }
        if (closed)
        {
            ImpactAudio.Play(ImpactAudio.Zap, nodeB.bounds.center, 1f, 1f);
            ImpactAudio.Play(ImpactAudio.Zap, nodeA.bounds.center, 1f, 1.1f);
            if (!string.IsNullOrEmpty(announce) && MatterHUD.Instance) MatterHUD.Instance.Flash(announce, new Color(0.55f, 1f, 0.65f));
        }
    }

    bool CircuitClosed()
    {
        HashSet<SpawnedMatter> start = ConductorsTouching(nodeA.bounds);
        if (start.Count == 0) return false;
        HashSet<SpawnedMatter> goal = ConductorsTouching(nodeB.bounds);
        if (goal.Count == 0) return false;

        var visited = new HashSet<SpawnedMatter>(start);
        var queue = new Queue<SpawnedMatter>(start);
        while (queue.Count > 0)
        {
            SpawnedMatter conductor = queue.Dequeue();
            if (goal.Contains(conductor)) return true;
            foreach (SpawnedMatter next in ConductorsTouching(conductor.WorldBounds))
                if (visited.Add(next)) queue.Enqueue(next);
        }
        return false;
    }

    HashSet<SpawnedMatter> ConductorsTouching(Bounds bounds)
    {
        var found = new HashSet<SpawnedMatter>();
        int count = Physics.OverlapBoxNonAlloc(bounds.center, bounds.extents + Vector3.one * contactTolerance, hits,
            Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Rigidbody rb = hits[i].attachedRigidbody;
            if (rb == null) continue;
            SpawnedMatter matter = rb.GetComponent<SpawnedMatter>();
            if (matter && matter.IsConductive && !matter.IsDissolving) found.Add(matter);
        }
        return found;
    }

    void SetIndicators(Color color)
    {
        if (indicators == null) return;
        foreach (Renderer r in indicators)
            if (r) ChamberDoor.Glow(r, color);
    }
}
