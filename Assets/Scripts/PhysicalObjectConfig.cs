using System;
using UnityEngine;

/// <summary>
/// Physics profile for one inventory item: shape, size, mass, restitution, friction and PBR look.
/// The scene builder authors one per item; the Matter Gun builds the Rigidbody, PhysicsMaterial and
/// material from it.
/// </summary>
[Serializable]
public class PhysicalObjectConfig
{
    public string shape = "cube";                       // "cube", "sphere", "cylinder", "wedge"
    public float[] dimensions = { 0.5f, 0.5f, 0.5f };   // [x, y, z] metres; z points away from the player
    public float mass = 125f;                           // kg
    public float bounciness = 0.1f;                     // 0..1
    public float dynamicFriction = 0.5f;                // 0..1
    public float staticFriction = 0.6f;                 // 0..1
    public string hexColor = "#8C8C8C";                 // PBR base tint
    public float roughness = 0.5f;                      // 0..1
    public float metalness;                             // 0..1
    public string prompt = "";                          // item name

    public const float MinDimension = 0.05f, MaxDimension = 12f;
    public const float MinMass = 0.05f, MaxMass = 50000f;

    public Vector3 Size =>
        dimensions != null && dimensions.Length >= 3
            ? new Vector3(dimensions[0], dimensions[1], dimensions[2])
            : new Vector3(0.5f, 0.5f, 0.5f);

    public float Volume => ShapeVolume(shape, Size);

    public float Density => Volume > 0f ? mass / Volume : 0f;

    public Color Color => ColorUtility.TryParseHtmlString(hexColor, out Color c) ? c : new Color(0.55f, 0.55f, 0.55f);

    /// <summary>
    /// Cylinders lie along z (logs, rods) when z is strictly their longest side; otherwise they stand
    /// on y with diameter max(x, z), so (d, h, d) is an upright disc or post.
    /// </summary>
    public static bool CylinderLiesAlongZ(Vector3 size) => size.z > size.y && size.z > size.x;

    public static float ShapeVolume(string shape, Vector3 s)
    {
        switch (shape)
        {
            case "sphere":
                return Mathf.PI / 6f * s.x * s.x * s.x;
            case "cylinder":
                if (CylinderLiesAlongZ(s))
                {
                    float d = Mathf.Max(s.x, s.y);
                    return Mathf.PI / 4f * d * d * s.z;
                }
                else
                {
                    float d = Mathf.Max(s.x, s.z);
                    return Mathf.PI / 4f * d * d * s.y;
                }
            case "wedge":
                return 0.5f * s.x * s.y * s.z;
            default:
                return s.x * s.y * s.z;
        }
    }

    public PhysicalObjectConfig Clone()
    {
        var copy = (PhysicalObjectConfig)MemberwiseClone();
        copy.dimensions = dimensions != null ? (float[])dimensions.Clone() : null;
        return copy;
    }

    /// <summary>Clamps every field into a range the physics engine and the chambers can handle.</summary>
    public void Sanitize()
    {
        shape = NormalizeShape(shape);
        Vector3 s = Size;
        float x = Dimension(s.x), y = Dimension(s.y), z = Dimension(s.z);
        if (shape == "sphere") x = y = z = Mathf.Max(x, Mathf.Max(y, z));
        dimensions = new[] { x, y, z };

        if (!IsFinite(mass) || mass <= 0f) mass = 1000f * Volume;
        mass = Mathf.Clamp(mass, MinMass, MaxMass);
        bounciness = Unit(bounciness, 0.1f);
        dynamicFriction = Unit(dynamicFriction, 0.5f);
        staticFriction = Unit(staticFriction, 0.6f);
        roughness = Unit(roughness, 0.5f);
        metalness = Unit(metalness, 0f);

        hexColor = string.IsNullOrWhiteSpace(hexColor) ? "#8C8C8C" : hexColor.Trim();
        if (!hexColor.StartsWith("#")) hexColor = "#" + hexColor;
        if (!ColorUtility.TryParseHtmlString(hexColor, out _)) hexColor = "#8C8C8C";
        prompt ??= "";
    }

    public static string NormalizeShape(string raw)
    {
        switch ((raw ?? "").Trim().ToLowerInvariant())
        {
            case "sphere": case "ball": case "orb":
                return "sphere";
            case "cylinder": case "rod": case "pipe": case "disc": case "disk":
                return "cylinder";
            case "wedge": case "ramp": case "slope": case "prism":
                return "wedge";
            default:
                return "cube";
        }
    }

    static float Dimension(float v) => IsFinite(v) && v > 0f ? Mathf.Clamp(v, MinDimension, MaxDimension) : 0.5f;
    static float Unit(float v, float fallback) => IsFinite(v) ? Mathf.Clamp01(v) : fallback;
    static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

    public override string ToString()
    {
        Vector3 s = Size;
        return FormattableString.Invariant(
            $"{shape} {s.x:0.##}x{s.y:0.##}x{s.z:0.##} m, {mass:0.#} kg, bounce {bounciness:0.##}, friction {dynamicFriction:0.##}/{staticFriction:0.##}, {hexColor}");
    }
}
