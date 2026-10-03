using UnityEngine;

/// <summary>
/// Smoothly translates and/or rotates a door (portcullis, sliding door, drawbridge, counterweight
/// bucket) between its closed pose and an open offset. Moving parts with a kinematic Rigidbody are
/// driven through MovePosition / MoveRotation so anything resting on them rides along.
/// </summary>
public class ChamberDoor : MonoBehaviour
{
    [Tooltip("Added to the closed local position when fully open (parent space).")]
    public Vector3 openPositionOffset = new Vector3(0f, 3.5f, 0f);
    [Tooltip("Euler rotation applied on top of the closed rotation when fully open (parent space, around this pivot).")]
    public Vector3 openRotationOffset;
    public float duration = 2.5f;
    public AnimationCurve easing = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Feedback")]
    public Renderer[] indicators;
    [ColorUsage(false, true)] public Color lockedColor = new Color(2.2f, 0.25f, 0.15f);
    [ColorUsage(false, true)] public Color openColor = new Color(0.3f, 2.4f, 0.5f);
    [Tooltip("Shown on the HUD when the door starts opening. Empty = silent.")]
    public string announce;

    Vector3 closedPosition;
    Quaternion closedRotation;
    float progress, target;
    Rigidbody body;

    public bool IsOpen => target > 0.5f;
    public float Progress => progress;

    void Awake()
    {
        closedPosition = transform.localPosition;
        closedRotation = transform.localRotation;
        body = GetComponent<Rigidbody>();
    }

    void Start() => SetIndicators(lockedColor);

    public void Open()
    {
        if (target >= 1f) return;
        target = 1f;
        SetIndicators(openColor);
        ImpactAudio.Play(ImpactAudio.Rumble, transform.position, 0.9f, 0.9f);
        if (!string.IsNullOrEmpty(announce) && MatterHUD.Instance) MatterHUD.Instance.Flash(announce, new Color(0.55f, 1f, 0.65f));
    }

    public void Close()
    {
        if (target <= 0f) return;
        target = 0f;
        SetIndicators(lockedColor);
        ImpactAudio.Play(ImpactAudio.Rumble, transform.position, 0.9f, 0.8f);
    }

    void Update()
    {
        if (!body) Step(Time.deltaTime);
    }

    void FixedUpdate()
    {
        if (body) Step(Time.fixedDeltaTime);
    }

    void Step(float dt)
    {
        if (Mathf.Approximately(progress, target)) return;
        progress = Mathf.MoveTowards(progress, target, dt / Mathf.Max(0.01f, duration));
        float e = easing.Evaluate(progress);
        Vector3 position = closedPosition + openPositionOffset * e;
        Quaternion rotation = Quaternion.Slerp(closedRotation, Quaternion.Euler(openRotationOffset) * closedRotation, e);

        if (body)
        {
            Transform parent = transform.parent;
            body.MovePosition(parent ? parent.TransformPoint(position) : position);
            body.MoveRotation(parent ? parent.rotation * rotation : rotation);
        }
        else
        {
            transform.SetLocalPositionAndRotation(position, rotation);
        }
    }

    void SetIndicators(Color color)
    {
        if (indicators == null) return;
        foreach (Renderer r in indicators)
            if (r) Glow(r, color);
    }

    /// <summary>Tints an emissive indicator (instanced material) to an HDR colour.</summary>
    public static void Glow(Renderer renderer, Color hdr)
    {
        Material m = renderer.material;
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", hdr);
        float max = Mathf.Max(1f, hdr.maxColorComponent);
        m.SetColor("_BaseColor", new Color(hdr.r / max, hdr.g / max, hdr.b / max, 1f));
    }
}
