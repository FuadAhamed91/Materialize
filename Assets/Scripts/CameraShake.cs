using UnityEngine;

/// <summary>
/// Trauma-based camera shake. Sits on the camera itself (pitch lives on the parent pivot), so it
/// never fights the mouse look.
/// </summary>
public class CameraShake : MonoBehaviour
{
    public static CameraShake Instance { get; private set; }

    public float maxAngle = 3.5f;
    public float maxOffset = 0.08f;
    public float frequency = 22f;
    [Tooltip("Trauma lost per second.")]
    public float recovery = 1.4f;

    float trauma;
    float seed;
    Vector3 restPosition;
    Quaternion restRotation;

    void Awake()
    {
        Instance = this;
        seed = Random.value * 100f;
        restPosition = transform.localPosition;
        restRotation = transform.localRotation;
    }

    public static void AddTrauma(float amount)
    {
        if (Instance) Instance.trauma = Mathf.Clamp01(Instance.trauma + amount);
    }

    /// <summary>Shake from an impact with momentum (relative speed x mass), fading with distance.</summary>
    public static void AddImpact(Vector3 point, float momentum)
    {
        if (!Instance) return;
        float strength = Mathf.Clamp01((Mathf.Log10(1f + momentum) - 1.2f) / 3f); // ~16 kg m/s -> 0, ~16,000 -> 1
        float distance = Vector3.Distance(Instance.transform.position, point);
        AddTrauma(strength * 0.8f / (1f + distance * distance / 36f));
    }

    void LateUpdate()
    {
        trauma = Mathf.Max(0f, trauma - recovery * Time.deltaTime);
        float shake = trauma * trauma;
        float t = Time.time * frequency;
        var offset = new Vector3(Noise(t, 0), Noise(t, 1), 0f) * (maxOffset * shake);
        var angles = new Vector3(Noise(t, 2), Noise(t, 3), Noise(t, 4) * 0.5f) * (maxAngle * shake);
        transform.localPosition = restPosition + offset;
        transform.localRotation = restRotation * Quaternion.Euler(angles);
    }

    float Noise(float t, int channel) => Mathf.PerlinNoise(seed + channel * 13.7f, t) * 2f - 1f;
}
