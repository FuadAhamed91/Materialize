using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Runtime state of one synthesized object: its compiled config, conductivity, impact feedback
/// (sound and camera shake scaled by relativeVelocity x mass) and the materialize / dissolve effects.
/// </summary>
public class SpawnedMatter : MonoBehaviour
{
    static readonly Regex ConductiveWords = new Regex(
        @"\b(copper|iron|steel|alumini?um|gold|silver|brass|bronze|tungsten|lead|titanium|osmium|metal|metallic|graphite|carbon|wire|conductive|conductor)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    static readonly Color MaterializeGlow = new Color(0.4f, 1.6f, 3.2f);
    static readonly Color DissolveGlow = new Color(2.4f, 0.9f, 0.3f);

    static readonly Color ElectrifiedGlow = new Color(0.3f, 0.8f, 2.2f);

    public PhysicalObjectConfig Config { get; private set; }
    public string Source { get; private set; }
    public Rigidbody Body { get; private set; }
    public bool IsDissolving { get; private set; }
    public bool Electrified { get; private set; }
    public IReadOnlyList<Collider> Colliders => colliders;

    /// <summary>Metals, or anything described as conductive, carry current between terminals.</summary>
    public bool IsConductive =>
        Config != null && (Config.metalness >= 0.6f || ConductiveWords.IsMatch(Config.prompt ?? ""));

    public Bounds WorldBounds
    {
        get
        {
            var bounds = new Bounds(transform.position, Vector3.zero);
            bool first = true;
            foreach (Collider c in colliders)
            {
                if (!c) continue;
                if (first) { bounds = c.bounds; first = false; }
                else bounds.Encapsulate(c.bounds);
            }
            return bounds;
        }
    }

    Transform visual;
    Vector3 fullScale;
    Material material;
    PhysicsMaterial physicsMaterial;
    Collider[] colliders = new Collider[0];
    float lastImpact;

    public void Init(PhysicalObjectConfig config, string source, Rigidbody body, Renderer renderer,
        PhysicsMaterial runtimePhysicsMaterial, Material runtimeMaterial, float materializeSeconds)
    {
        Config = config;
        Source = source;
        Body = body;
        visual = renderer.transform;
        fullScale = visual.localScale;
        material = runtimeMaterial;
        physicsMaterial = runtimePhysicsMaterial;
        colliders = GetComponentsInChildren<Collider>();
        StartCoroutine(MaterializeRoutine(Mathf.Max(0.01f, materializeSeconds)));
    }

    IEnumerator MaterializeRoutine(float seconds)
    {
        Body.isKinematic = true;
        material.EnableKeyword("_EMISSION");

        var flash = new GameObject("MaterializeFlash").AddComponent<Light>();
        flash.transform.SetParent(transform, false);
        flash.type = LightType.Point;
        flash.color = new Color(0.45f, 0.8f, 1f);
        flash.range = 4f + Config.Size.magnitude;

        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            float k = t / seconds;
            float eased = 1f - Mathf.Pow(1f - k, 3f);
            visual.localScale = fullScale * Mathf.Max(0.02f, eased);
            material.SetColor("_EmissionColor", MaterializeGlow * (1f - k));
            flash.intensity = 6f * (1f - k);
            yield return null;
        }

        visual.localScale = fullScale;
        material.SetColor("_EmissionColor", Color.black);
        Destroy(flash.gameObject);

        Body.isKinematic = false;
        Body.collisionDetectionMode = CollisionDetectionMode.Continuous;
        Body.WakeUp();
    }

    void OnCollisionEnter(Collision collision)
    {
        if (IsDissolving || Body == null || Body.isKinematic) return;
        float speed = collision.relativeVelocity.magnitude;
        if (speed < 0.8f || Time.time - lastImpact < 0.08f) return;
        lastImpact = Time.time;

        Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
        ImpactAudio.PlayImpact(point, speed, Body.mass, Config.metalness, Config.bounciness);
        CameraShake.AddImpact(point, speed * Body.mass);
    }

    /// <summary>Glows while it carries current from a live floor.</summary>
    public void SetElectrified(bool on)
    {
        if (Electrified == on || IsDissolving) return;
        Electrified = on;
        if (!material) return;
        material.EnableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", on ? ElectrifiedGlow : Color.black);
        if (on) ImpactAudio.Play(ImpactAudio.Zap, transform.position, 0.6f, 1.3f);
    }

    public void Dissolve(float seconds = 0.6f)
    {
        if (IsDissolving) return;
        IsDissolving = true;
        StopAllCoroutines();
        StartCoroutine(DissolveRoutine(seconds));
    }

    IEnumerator DissolveRoutine(float seconds)
    {
        foreach (Collider c in colliders)
            if (c) c.enabled = false;
        if (Body)
        {
            Body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            Body.isKinematic = true;
        }
        if (material) material.EnableKeyword("_EMISSION");

        Vector3 start = visual ? visual.localScale : Vector3.one;
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            float k = t / seconds;
            if (visual) visual.localScale = start * (1f - k * k);
            if (material) material.SetColor("_EmissionColor", DissolveGlow * k);
            yield return null;
        }
        Destroy(gameObject);
    }

    void OnDestroy()
    {
        if (material) Destroy(material);
        if (physicsMaterial) Destroy(physicsMaterial);
    }
}
