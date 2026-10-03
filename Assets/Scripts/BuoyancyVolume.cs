using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A body of water. Every Rigidbody inside gets Archimedes' upthrust (water density x submerged
/// volume x g) plus water drag, so matter less dense than water floats and denser matter sinks.
/// The top of the box is the water surface.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class BuoyancyVolume : MonoBehaviour
{
    [Tooltip("kg/m3 (fresh water = 1000).")]
    public float fluidDensity = 1000f;
    [Tooltip("Velocity damping per second at full submersion.")]
    public float linearDrag = 1.5f;
    public float angularDrag = 2f;

    BoxCollider zone;
    readonly Collider[] hits = new Collider[64];
    readonly HashSet<Rigidbody> bodies = new HashSet<Rigidbody>();

    public float SurfaceHeight => zone ? zone.bounds.max.y : transform.position.y;

    void Awake()
    {
        zone = GetComponent<BoxCollider>();
        zone.isTrigger = true;
    }

    void FixedUpdate()
    {
        Bounds water = zone.bounds;
        int count = Physics.OverlapBoxNonAlloc(water.center, water.extents, hits, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
        bodies.Clear();
        for (int i = 0; i < count; i++)
        {
            Rigidbody rb = hits[i].attachedRigidbody;
            if (rb != null && !rb.isKinematic) bodies.Add(rb);
        }

        float surface = water.max.y;
        foreach (Rigidbody rb in bodies)
        {
            SpawnedMatter matter = rb.GetComponent<SpawnedMatter>();
            Bounds body = matter ? matter.WorldBounds : ColliderBounds(rb);
            if (body.size.y <= 0f) continue;
            float submerged = Mathf.Clamp01((surface - body.min.y) / body.size.y);
            if (submerged <= 0f) continue;

            float volume = matter ? matter.Config.Volume : body.size.x * body.size.y * body.size.z;
            rb.AddForce(Vector3.up * (fluidDensity * volume * submerged * -Physics.gravity.y), ForceMode.Force);
            rb.AddForce(-rb.linearVelocity * (linearDrag * submerged), ForceMode.Acceleration);
            rb.AddTorque(-rb.angularVelocity * (angularDrag * submerged), ForceMode.Acceleration);
        }
    }

    static Bounds ColliderBounds(Rigidbody rb)
    {
        var bounds = new Bounds(rb.position, Vector3.zero);
        bool first = true;
        foreach (Collider c in rb.GetComponentsInChildren<Collider>())
        {
            if (c.isTrigger) continue;
            if (first) { bounds = c.bounds; first = false; }
            else bounds.Encapsulate(c.bounds);
        }
        return bounds;
    }
}
