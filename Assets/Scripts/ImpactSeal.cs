using UnityEngine;

/// <summary>
/// Brittle glass seal. Shatters when struck by a Rigidbody whose impact momentum
/// (relative speed x mass) reaches the threshold, then opens its target doors.
/// Gentle contact (matter simply placed against it) does nothing.
/// </summary>
[RequireComponent(typeof(Collider))]
public class ImpactSeal : MonoBehaviour
{
    [Tooltip("kg·m/s needed to break the seal.")]
    public float momentumThreshold = 6000f;
    public ChamberDoor[] targets;
    [Tooltip("Extra renderers hidden when the seal breaks (e.g. the core inside a glass case).")]
    public Renderer[] hideOnBreak;
    public string announce = "";
    public int shardCount = 28;

    public bool Broken { get; private set; }
    public float StrongestHit { get; private set; }

    void OnCollisionEnter(Collision collision)
    {
        if (Broken || collision.rigidbody == null) return;
        float momentum = collision.relativeVelocity.magnitude * collision.rigidbody.mass;
        StrongestHit = Mathf.Max(StrongestHit, momentum);
        Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
        if (momentum >= momentumThreshold) Shatter(point, collision.relativeVelocity);
        else if (momentum > 50f) ImpactAudio.Play(ImpactAudio.Clang, point, Mathf.Clamp01(momentum / momentumThreshold), 2.2f);
    }

    public void Shatter(Vector3 point, Vector3 velocity)
    {
        if (Broken) return;
        Broken = true;

        var surface = GetComponent<Renderer>();
        Bounds bounds = surface ? surface.bounds : GetComponent<Collider>().bounds;
        Material glass = surface ? surface.sharedMaterial : null;
        GetComponent<Collider>().enabled = false;
        if (surface) surface.enabled = false;
        if (hideOnBreak != null)
            foreach (Renderer r in hideOnBreak)
                if (r) r.enabled = false;

        for (int i = 0; i < shardCount; i++)
        {
            var shard = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shard.name = "Shard";
            shard.transform.SetPositionAndRotation(
                new Vector3(Random.Range(bounds.min.x, bounds.max.x), Random.Range(bounds.min.y, bounds.max.y), Random.Range(bounds.min.z, bounds.max.z)),
                Random.rotation);
            float size = Random.Range(0.12f, 0.35f);
            shard.transform.localScale = new Vector3(size, size * Random.Range(0.6f, 1.2f), 0.03f);
            if (glass) shard.GetComponent<Renderer>().sharedMaterial = glass;
            var body = shard.AddComponent<Rigidbody>();
            body.mass = 2f;
            body.linearVelocity = velocity * Random.Range(0.15f, 0.4f) + Random.insideUnitSphere * 2.5f;
            Destroy(shard, Random.Range(4f, 7f));
        }

        ImpactAudio.Play(ImpactAudio.Shatter, point, 1f, 1f);
        CameraShake.AddImpact(point, momentumThreshold * 2f);
        if (targets != null)
            foreach (ChamberDoor door in targets)
                if (door) door.Open();
        if (!string.IsNullOrEmpty(announce) && MatterHUD.Instance) MatterHUD.Instance.Flash(announce, new Color(0.55f, 1f, 0.65f));
    }
}
