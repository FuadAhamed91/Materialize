using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Hotbar pictures. A tiny CPU ray tracer draws each item's real shape, proportions and colour in
/// isometric view, so the inventory needs no icon art. Shapes are intersections of convex pieces
/// (half-spaces, a sphere, an infinite cylinder), traced in the item's local space.
/// </summary>
public static class ShapeIcon
{
    const int Resolution = 64, Supersample = 3;

    // orthographic camera above, left of and in front of the item (item z points away from the player)
    static readonly Vector3 View = new Vector3(1f, -1f, 1f).normalized;
    static readonly Vector3 Right = new Vector3(1f, 0f, -1f).normalized;
    static readonly Vector3 Up = new Vector3(1f, 2f, 1f).normalized;
    static readonly Vector3 Light = new Vector3(-0.5f, 1f, -0.3f).normalized;

    // keyed by look, not by instance: identical items share an icon, and icons survive a level restart
    static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();

    public static Texture2D For(PhysicalObjectConfig config)
    {
        if (config == null) return null;
        string key = $"{config.shape}|{config.Size}|{config.hexColor}|{config.metalness}";
        if (Cache.TryGetValue(key, out Texture2D texture) && texture) return texture;
        texture = Render(config);
        Cache[key] = texture;
        return texture;
    }

    static Texture2D Render(PhysicalObjectConfig c)
    {
        // fit the projected bounding box, with a small margin
        Vector3 half = BoundingSize(c) * 0.5f;
        float minU = float.MaxValue, maxU = float.MinValue, minV = float.MaxValue, maxV = float.MinValue;
        if (c.shape == "sphere")
        {
            minU = minV = -half.x;
            maxU = maxV = half.x;
        }
        else
        {
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? -half.x : half.x, (i & 2) == 0 ? -half.y : half.y, (i & 4) == 0 ? -half.z : half.z);
                float u = Vector3.Dot(corner, Right), v = Vector3.Dot(corner, Up);
                minU = Mathf.Min(minU, u);
                maxU = Mathf.Max(maxU, u);
                minV = Mathf.Min(minV, v);
                maxV = Mathf.Max(maxV, v);
            }
        }
        float span = Mathf.Max(maxU - minU, maxV - minV) / 0.86f;
        float centerU = (minU + maxU) * 0.5f, centerV = (minV + maxV) * 0.5f;
        float back = 2f * half.magnitude + 1f;

        // lift very dark materials a little so they read against the dark hotbar
        Color albedo = Color.Lerp(c.Color, Color.white, 0.12f);
        var pixels = new Color32[Resolution * Resolution];
        const int samples = Supersample * Supersample;
        for (int y = 0; y < Resolution; y++)
        for (int x = 0; x < Resolution; x++)
        {
            Color sum = Color.clear;
            int covered = 0;
            for (int sy = 0; sy < Supersample; sy++)
            for (int sx = 0; sx < Supersample; sx++)
            {
                float u = centerU + ((x + (sx + 0.5f) / Supersample) / Resolution - 0.5f) * span;
                float v = centerV + ((y + (sy + 0.5f) / Supersample) / Resolution - 0.5f) * span;
                if (!Trace(Right * u + Up * v - View * back, c, out Vector3 normal)) continue;
                sum += Shade(albedo, normal, c.metalness);
                covered++;
            }
            if (covered == 0) continue;
            Color color = sum / covered;
            color.a = (float)covered / samples;
            pixels[y * Resolution + x] = color;
        }

        var texture = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false)
        {
            name = "Icon_" + c.shape,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }

    static Vector3 BoundingSize(PhysicalObjectConfig c)
    {
        Vector3 s = c.Size;
        switch (c.shape)
        {
            case "sphere":
                return Vector3.one * s.x;
            case "cylinder":
                if (PhysicalObjectConfig.CylinderLiesAlongZ(s))
                {
                    float d = Mathf.Max(s.x, s.y);
                    return new Vector3(d, d, s.z);
                }
                else
                {
                    float d = Mathf.Max(s.x, s.z);
                    return new Vector3(d, s.y, d);
                }
            default:
                return s;
        }
    }

    static Color Shade(Color albedo, Vector3 n, float metalness)
    {
        float diffuse = Mathf.Max(0f, Vector3.Dot(n, Light));
        Vector3 halfway = (Light - View).normalized;
        float specular = Mathf.Pow(Mathf.Max(0f, Vector3.Dot(n, halfway)), 32f) * Mathf.Lerp(0.12f, 0.55f, metalness);
        float rim = Mathf.Pow(1f - Mathf.Clamp01(-Vector3.Dot(n, View)), 4f) * 0.25f;
        Color lit = albedo * (0.34f + 0.66f * diffuse) + Color.white * (specular + rim);
        lit.a = 1f;
        return lit;
    }

    /// <summary>Ray (origin <paramref name="o"/>, direction View) against the item; normal at the entry point.</summary>
    static bool Trace(Vector3 o, PhysicalObjectConfig c, out Vector3 normal)
    {
        Vector3 d = View, s = c.Size, h = s * 0.5f;
        float tIn = float.NegativeInfinity, tOut = float.PositiveInfinity;
        normal = Vector3.zero;
        switch (c.shape)
        {
            case "sphere":
                return Sphere(o, d, h.x, ref tIn, ref tOut, ref normal);
            case "cylinder":
            {
                bool alongZ = PhysicalObjectConfig.CylinderLiesAlongZ(s);
                Vector3 axis = alongZ ? Vector3.forward : Vector3.up;
                float radius = 0.5f * (alongZ ? Mathf.Max(s.x, s.y) : Mathf.Max(s.x, s.z));
                float length = 0.5f * (alongZ ? s.z : s.y);
                return Cylinder(o, d, axis, radius, ref tIn, ref tOut, ref normal)
                    && HalfSpace(o, d, axis, length, ref tIn, ref tOut, ref normal)
                    && HalfSpace(o, d, -axis, length, ref tIn, ref tOut, ref normal);
            }
            case "wedge":
                // the slope runs from the low front edge to the high back edge, through the centre
                if (!HalfSpace(o, d, new Vector3(0f, s.z, -s.y).normalized, 0f, ref tIn, ref tOut, ref normal)) return false;
                goto default;
            default:
                return HalfSpace(o, d, Vector3.right, h.x, ref tIn, ref tOut, ref normal)
                    && HalfSpace(o, d, Vector3.left, h.x, ref tIn, ref tOut, ref normal)
                    && HalfSpace(o, d, Vector3.up, h.y, ref tIn, ref tOut, ref normal)
                    && HalfSpace(o, d, Vector3.down, h.y, ref tIn, ref tOut, ref normal)
                    && HalfSpace(o, d, Vector3.forward, h.z, ref tIn, ref tOut, ref normal)
                    && HalfSpace(o, d, Vector3.back, h.z, ref tIn, ref tOut, ref normal);
        }
    }

    /// <summary>Clips the ray interval to the half-space n·p ≤ offset.</summary>
    static bool HalfSpace(Vector3 o, Vector3 d, Vector3 n, float offset, ref float tIn, ref float tOut, ref Vector3 normal)
    {
        float denom = Vector3.Dot(n, d), distance = offset - Vector3.Dot(n, o);
        if (Mathf.Abs(denom) < 1e-7f) return distance >= 0f;
        float t = distance / denom;
        if (denom < 0f)
        {
            if (t > tIn)
            {
                tIn = t;
                normal = n;
            }
        }
        else if (t < tOut)
        {
            tOut = t;
        }
        return tIn <= tOut;
    }

    static bool Sphere(Vector3 o, Vector3 d, float radius, ref float tIn, ref float tOut, ref Vector3 normal)
    {
        float b = Vector3.Dot(o, d), c = Vector3.Dot(o, o) - radius * radius, disc = b * b - c;
        if (disc < 0f) return false;
        float root = Mathf.Sqrt(disc), t0 = -b - root, t1 = -b + root;
        if (t0 > tIn)
        {
            tIn = t0;
            normal = (o + d * t0) / radius;
        }
        if (t1 < tOut) tOut = t1;
        return tIn <= tOut;
    }

    /// <summary>Infinite cylinder around <paramref name="axis"/> through the origin.</summary>
    static bool Cylinder(Vector3 o, Vector3 d, Vector3 axis, float radius, ref float tIn, ref float tOut, ref Vector3 normal)
    {
        Vector3 op = o - axis * Vector3.Dot(o, axis), dp = d - axis * Vector3.Dot(d, axis);
        float a = Vector3.Dot(dp, dp), b = Vector3.Dot(op, dp), c = Vector3.Dot(op, op) - radius * radius;
        if (a < 1e-9f) return c <= 0f;
        float disc = b * b - a * c;
        if (disc < 0f) return false;
        float root = Mathf.Sqrt(disc), t0 = (-b - root) / a, t1 = (-b + root) / a;
        if (t0 > tIn)
        {
            tIn = t0;
            normal = (op + dp * t0) / radius;
        }
        if (t1 < tOut) tOut = t1;
        return tIn <= tOut;
    }
}
