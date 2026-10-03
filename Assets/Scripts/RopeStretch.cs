using UnityEngine;

/// <summary>
/// Keeps a vertical cable stretched from its fixed top (this pivot) down to a moving attach point,
/// e.g. the pulley rope as the counterweight bucket sinks.
/// </summary>
public class RopeStretch : MonoBehaviour
{
    [Tooltip("Point the bottom of the rope follows. The rope's pivot must sit at its fixed top end.")]
    public Transform target;

    float restLength;
    Vector3 restScale;

    void Start()
    {
        restScale = transform.localScale;
        restLength = target ? Mathf.Max(0.01f, transform.position.y - target.position.y) : 1f;
    }

    void LateUpdate()
    {
        if (!target) return;
        float length = Mathf.Max(0.01f, transform.position.y - target.position.y);
        Vector3 scale = restScale;
        scale.y = restScale.y * length / restLength;
        transform.localScale = scale;
    }
}
