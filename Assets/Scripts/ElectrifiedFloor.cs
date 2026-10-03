using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Live floor. Touching it, or any conductive matter lying on it (or touching that matter),
/// shocks the player back to their checkpoint. Insulating matter laid across it is safe to walk on.
/// </summary>
public class ElectrifiedFloor : MonoBehaviour
{
    static readonly HashSet<Collider> LiveColliders = new HashSet<Collider>();

    public Collider surface;
    [Tooltip("Bus bars that pulse with the current.")]
    public Renderer[] glow;
    [ColorUsage(false, true)] public Color glowColor = new Color(0.4f, 1f, 2.6f);
    [Tooltip("How close (m) matter must be to count as touching.")]
    public float contactTolerance = 0.06f;

    readonly HashSet<SpawnedMatter> energized = new HashSet<SpawnedMatter>();
    readonly Collider[] hits = new Collider[64];
    float nextCheck;

    /// <summary>True if touching this collider shocks the player.</summary>
    public static bool IsLive(Collider c) => c != null && LiveColliders.Contains(c);

    void OnEnable()
    {
        if (surface) LiveColliders.Add(surface);
    }

    void OnDisable()
    {
        if (surface) LiveColliders.Remove(surface);
        foreach (SpawnedMatter m in energized) SetLive(m, false);
        energized.Clear();
    }

    void Update()
    {
        if (glow == null) return;
        float pulse = 0.55f + 0.45f * Mathf.PerlinNoise(Time.time * 6f, 0.37f);
        foreach (Renderer r in glow)
            if (r) ChamberDoor.Glow(r, glowColor * pulse);
    }

    void FixedUpdate()
    {
        if (Time.time < nextCheck || surface == null) return;
        nextCheck = Time.time + 0.1f;

        // conductors touching the deck, then conductors touching those
        var live = new HashSet<SpawnedMatter>();
        var queue = new Queue<SpawnedMatter>();
        foreach (SpawnedMatter m in ConductorsTouching(surface.bounds))
            if (live.Add(m)) queue.Enqueue(m);
        while (queue.Count > 0)
            foreach (SpawnedMatter next in ConductorsTouching(queue.Dequeue().WorldBounds))
                if (live.Add(next)) queue.Enqueue(next);

        foreach (SpawnedMatter m in energized)
            if (m && !live.Contains(m)) SetLive(m, false);
        foreach (SpawnedMatter m in live)
            if (!energized.Contains(m)) SetLive(m, true);
        energized.Clear();
        energized.UnionWith(live);
    }

    List<SpawnedMatter> ConductorsTouching(Bounds bounds)
    {
        var found = new List<SpawnedMatter>();
        int count = Physics.OverlapBoxNonAlloc(bounds.center, bounds.extents + Vector3.one * contactTolerance, hits,
            Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Rigidbody rb = hits[i].attachedRigidbody;
            SpawnedMatter matter = rb ? rb.GetComponent<SpawnedMatter>() : null;
            if (matter && matter.IsConductive && !matter.IsDissolving && !found.Contains(matter)) found.Add(matter);
        }
        return found;
    }

    static void SetLive(SpawnedMatter matter, bool on)
    {
        if (!matter) return;
        matter.SetElectrified(on);
        foreach (Collider c in matter.Colliders)
        {
            if (!c) continue;
            if (on) LiveColliders.Add(c);
            else LiveColliders.Remove(c);
        }
    }
}
