using UnityEngine;

/// <summary>Electrical arc flicker for the tesla coils.</summary>
[RequireComponent(typeof(Light))]
public class FlickerLight : MonoBehaviour
{
    [Range(0f, 1f)] public float depth = 0.65f;
    public float speed = 22f;

    Light source;
    float baseIntensity, seed;

    void Awake()
    {
        source = GetComponent<Light>();
        baseIntensity = source.intensity;
        seed = Random.value * 50f;
    }

    void Update()
    {
        float n = Mathf.PerlinNoise(seed, Time.time * speed);
        source.intensity = baseIntensity * (1f - depth + depth * n * n * 1.6f);
    }
}
