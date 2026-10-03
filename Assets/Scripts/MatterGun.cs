using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The Matter Gun. T opens the synthesis prompt, Enter sends it to the PhysicsPromptService, and the
/// compiled matter materializes where the centre of the screen points, with a Rigidbody, a runtime
/// PhysicsMaterial and a tinted URP Lit material. G re-fires the last matter; X recycles the matter
/// under the crosshair.
/// </summary>
[RequireComponent(typeof(Camera))]
public class MatterGun : MonoBehaviour
{
    [Header("References")]
    public PhysicsPromptService promptService;
    public MatterHUD hud;
    public FirstPersonController player;
    [Tooltip("URP Lit material cloned for every synthesized object.")]
    public Material baseMaterial;

    [Header("Aiming")]
    public LayerMask aimMask = ~0;
    public float maxRange = 40f;

    [Header("Matter")]
    public int maxSpawned = 12;
    public float materializeSeconds = 0.35f;
    public string matterLayerName = "Matter";

    struct Aim
    {
        public bool hit;
        public Vector3 point, normal, flatForward;
    }

    Camera cam;
    bool typing;
    PhysicalObjectConfig lastConfig;
    string lastSource;
    int serial;
    readonly List<SpawnedMatter> spawned = new List<SpawnedMatter>();
    readonly Collider[] overlaps = new Collider[32];
    static Mesh wedgeMesh;

    public bool IsTyping => typing;
    public IReadOnlyList<SpawnedMatter> Spawned => spawned;

    void Awake() => cam = GetComponent<Camera>();

    void Update()
    {
        if (typing)
        {
            if (GameInput.SubmitPressed) Submit(hud ? hud.PromptText : "");
            else if (GameInput.CancelPressed) ClosePrompt();
            return;
        }

        if (GameInput.OpenPromptPressed) OpenPrompt();
        else if (GameInput.RepeatPressed) Refire();
        else if (GameInput.DeletePressed) RecycleAimed();
    }

    void OpenPrompt()
    {
        typing = true;
        if (player) player.InputLocked = true;
        if (hud) hud.ShowPrompt(true);
    }

    void ClosePrompt()
    {
        typing = false;
        if (player) player.InputLocked = false;
        if (hud) hud.ShowPrompt(false);
    }

    void Submit(string text)
    {
        ClosePrompt();
        text = (text ?? "").Trim();
        if (text.Length == 0) return;
        Synthesize(text, CaptureAim());
    }

    /// <summary>Compiles <paramref name="description"/> and materializes it at the current crosshair.</summary>
    public void Synthesize(string description) => Synthesize(description, CaptureAim());

    /// <summary>Compiles <paramref name="description"/> and materializes it on a surface point (tests, demo scripts).</summary>
    public void SynthesizeAt(string description, Vector3 point, Vector3 normal)
    {
        Synthesize(description, new Aim { hit = true, point = point, normal = normal.normalized, flatForward = FlatForward() });
    }

    void Synthesize(string description, Aim aim)
    {
        if (hud) hud.BeginSynthesis(description);
        promptService.Compile(description, (config, source) =>
        {
            if (hud) hud.EndSynthesis();
            Materialize(config, aim, source);
        });
    }

    void Refire()
    {
        if (lastConfig == null)
        {
            if (hud) hud.Flash("Nothing synthesized yet · press T");
            return;
        }
        Materialize(lastConfig.Clone(), CaptureAim(), lastSource);
    }

    void RecycleAimed()
    {
        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (!Physics.Raycast(ray, out RaycastHit hit, maxRange, aimMask, QueryTriggerInteraction.Ignore)) return;
        Rigidbody body = hit.collider.attachedRigidbody;
        SpawnedMatter matter = body ? body.GetComponent<SpawnedMatter>() : null;
        if (matter == null) return;
        matter.Dissolve();
        if (hud) hud.Flash($"Recycled {matter.Config.mass:N0} kg of matter");
    }

    Vector3 FlatForward()
    {
        Vector3 flat = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
        if (flat.sqrMagnitude < 1e-4f) flat = player ? player.transform.forward : Vector3.forward;
        return flat.normalized;
    }

    Aim CaptureAim()
    {
        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (Physics.Raycast(ray, out RaycastHit hit, maxRange, aimMask, QueryTriggerInteraction.Ignore))
            return new Aim { hit = true, point = hit.point, normal = hit.normal, flatForward = FlatForward() };
        return new Aim { hit = false, point = ray.GetPoint(6f), normal = Vector3.zero, flatForward = FlatForward() };
    }

    void Materialize(PhysicalObjectConfig config, Aim aim, string source)
    {
        Quaternion rotation = Quaternion.LookRotation(aim.flatForward, Vector3.up);
        // on a ramp or slope, sit flush with it instead of landing on an edge
        if (aim.hit && aim.normal.y > 0.6f && aim.normal.y < 0.98f)
            rotation = Quaternion.FromToRotation(Vector3.up, aim.normal) * rotation;
        Vector3 position = Place(aim, config.Size, rotation);
        SpawnedMatter matter = Build(config, position, rotation, source);

        spawned.RemoveAll(m => m == null);
        spawned.Add(matter);
        while (spawned.Count > maxSpawned)
        {
            SpawnedMatter oldest = spawned[0];
            spawned.RemoveAt(0);
            if (oldest) oldest.Dissolve();
        }

        lastConfig = config.Clone();
        lastSource = source;
        if (hud) hud.ShowTelemetry(config, source);
    }

    /// <summary>Where the matter's centre goes so it rests on (or against) the aimed surface.</summary>
    Vector3 Place(Aim aim, Vector3 size, Quaternion rotation)
    {
        Vector3 right = rotation * Vector3.right, forward = rotation * Vector3.forward;
        float halfHeight = size.y * 0.5f;
        Vector3 position;

        if (aim.hit && aim.normal.y > 0.6f)
        {
            // floor-like: sit on it. Long matter lands with its far end at the crosshair, so a beam
            // aimed at the far terminal spans back towards the player.
            Vector3 up = rotation * Vector3.up;
            position = aim.point + up * (halfHeight + (aim.normal.y < 0.98f ? 0.02f : 0.05f));
            if (size.z >= 2.5f) position -= forward * (size.z * 0.5f - 0.3f);
        }
        else if (aim.hit && aim.normal.y < -0.6f)
        {
            position = aim.point + Vector3.down * (halfHeight + 0.05f);
        }
        else
        {
            float extent = 0f;
            if (aim.hit)
            {
                Vector3 n = aim.normal;
                extent = Mathf.Abs(Vector3.Dot(n, right)) * size.x * 0.5f
                       + Mathf.Abs(Vector3.Dot(n, forward)) * size.z * 0.5f
                       + Mathf.Abs(n.y) * halfHeight;
            }
            position = aim.point + aim.normal * (extent + 0.05f);
        }

        // never bury it: lift it clear of whatever is directly underneath
        Vector3 probe = position + Vector3.up * (halfHeight + 0.25f);
        if (Physics.Raycast(probe, Vector3.down, out RaycastHit ground, size.y + 30f, aimMask, QueryTriggerInteraction.Ignore))
            position.y = Mathf.Max(position.y, ground.point.y + halfHeight + 0.02f);

        // and never inside the player
        if (player)
        {
            Bounds body = player.Bounds;
            body.Expand(0.15f);
            Vector3 extents = WorldExtents(size, rotation);
            for (int i = 0; i < 24 && body.Intersects(new Bounds(position, extents * 2f)); i++)
                position += aim.flatForward * 0.25f;
        }
        return position;
    }

    static Vector3 WorldExtents(Vector3 size, Quaternion rotation)
    {
        Vector3 h = size * 0.5f;
        Matrix4x4 m = Matrix4x4.Rotate(rotation);
        return new Vector3(
            Mathf.Abs(m.m00) * h.x + Mathf.Abs(m.m01) * h.y + Mathf.Abs(m.m02) * h.z,
            Mathf.Abs(m.m10) * h.x + Mathf.Abs(m.m11) * h.y + Mathf.Abs(m.m12) * h.z,
            Mathf.Abs(m.m20) * h.x + Mathf.Abs(m.m21) * h.y + Mathf.Abs(m.m22) * h.z);
    }

    SpawnedMatter Build(PhysicalObjectConfig c, Vector3 position, Quaternion rotation, string source)
    {
        var root = new GameObject($"Matter_{++serial:00}_{c.shape}");
        int layer = LayerMask.NameToLayer(matterLayerName);
        if (layer >= 0) root.layer = layer;
        root.transform.SetPositionAndRotation(position, rotation);

        Vector3 s = c.Size;
        GameObject body;
        Collider collider;
        switch (c.shape)
        {
            case "sphere":
                body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                body.transform.localScale = Vector3.one * s.x;
                collider = body.GetComponent<Collider>();
                break;
            case "cylinder":
                body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                DestroyImmediate(body.GetComponent<Collider>()); // the default capsule rolls like a pill
                if (PhysicalObjectConfig.CylinderLiesAlongZ(s))
                {
                    float d = Mathf.Max(s.x, s.y);
                    body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    body.transform.localScale = new Vector3(d, s.z * 0.5f, d);
                }
                else
                {
                    float d = Mathf.Max(s.x, s.z);
                    body.transform.localScale = new Vector3(d, s.y * 0.5f, d);
                }
                var cylinderCollider = body.AddComponent<MeshCollider>();
                cylinderCollider.sharedMesh = body.GetComponent<MeshFilter>().sharedMesh;
                cylinderCollider.convex = true;
                collider = cylinderCollider;
                break;
            case "wedge":
                body = new GameObject("Body", typeof(MeshFilter), typeof(MeshRenderer));
                body.GetComponent<MeshFilter>().sharedMesh = WedgeMesh;
                body.transform.localScale = s;
                var wedgeCollider = body.AddComponent<MeshCollider>();
                wedgeCollider.sharedMesh = WedgeMesh;
                wedgeCollider.convex = true;
                collider = wedgeCollider;
                break;
            default:
                body = GameObject.CreatePrimitive(PrimitiveType.Cube);
                body.transform.localScale = s;
                collider = body.GetComponent<Collider>();
                break;
        }
        body.name = "Body";
        body.layer = root.layer;
        body.transform.SetParent(root.transform, false);

        Depenetrate(root.transform, collider);

        var rb = root.AddComponent<Rigidbody>();
        rb.mass = c.mass;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.linearDamping = 0.02f;
        rb.angularDamping = 0.1f;
        // Held kinematic while it materializes; SpawnedMatter switches it to
        // CollisionDetectionMode.Continuous the moment it goes dynamic.
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        rb.isKinematic = true;

        var physicsMaterial = new PhysicsMaterial("PM_" + root.name)
        {
            bounciness = c.bounciness,
            dynamicFriction = c.dynamicFriction,
            staticFriction = c.staticFriction,
            frictionCombine = PhysicsMaterialCombine.Multiply,
            bounceCombine = PhysicsMaterialCombine.Maximum,
        };
        collider.sharedMaterial = physicsMaterial;

        var material = baseMaterial ? new Material(baseMaterial) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.name = "M_" + root.name;
        material.SetColor("_BaseColor", c.Color);
        material.SetFloat("_Metallic", c.metalness);
        material.SetFloat("_Smoothness", 1f - c.roughness);
        var renderer = body.GetComponent<Renderer>();
        renderer.sharedMaterial = material;

        var matter = root.AddComponent<SpawnedMatter>();
        matter.Init(c, source, rb, renderer, physicsMaterial, material, materializeSeconds);
        return matter;
    }

    /// <summary>
    /// Moves freshly built matter out of anything it intersects, so the physics engine never has to
    /// fling it clear: first sideways out of walls, then up over whatever is still underneath
    /// (fixtures, other matter). Hollow static meshes can report "out" as straight down, which is
    /// why the vertical part only ever lifts.
    /// </summary>
    void Depenetrate(Transform root, Collider collider)
    {
        for (int pass = 0; pass < 6; pass++)
        {
            Vector3 push = Vector3.zero;
            foreach ((Vector3 direction, float distance) in Penetrations(collider))
            {
                var flat = new Vector3(direction.x, 0f, direction.z);
                if (flat.sqrMagnitude > 0.04f) push += flat.normalized * (distance / flat.magnitude + 0.01f);
            }
            if (push == Vector3.zero) break;
            root.position += push;
        }
        for (int step = 0; step < 16 && Penetrations(collider).Count > 0; step++)
            root.position += Vector3.up * 0.2f;
    }

    List<(Vector3 direction, float distance)> Penetrations(Collider collider)
    {
        Physics.SyncTransforms();
        var found = new List<(Vector3, float)>();
        Bounds bounds = collider.bounds;
        int count = Physics.OverlapBoxNonAlloc(bounds.center, bounds.extents, overlaps, Quaternion.identity, aimMask,
            QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider other = overlaps[i];
            if (other == collider) continue;
            if (Physics.ComputePenetration(collider, collider.transform.position, collider.transform.rotation,
                    other, other.transform.position, other.transform.rotation, out Vector3 direction, out float distance)
                && distance > 0.005f)
                found.Add((direction, distance));
        }
        return found;
    }

    static Mesh WedgeMesh => wedgeMesh ? wedgeMesh : (wedgeMesh = BuildWedgeMesh());

    /// <summary>Unit ramp (1 x 1 x 1): low edge at z = -0.5, rising to a ridge at z = +0.5.</summary>
    static Mesh BuildWedgeMesh()
    {
        Vector3 a = new Vector3(-0.5f, -0.5f, -0.5f), b = new Vector3(0.5f, -0.5f, -0.5f);
        Vector3 c = new Vector3(0.5f, -0.5f, 0.5f), d = new Vector3(-0.5f, -0.5f, 0.5f);
        Vector3 e = new Vector3(-0.5f, 0.5f, 0.5f), f = new Vector3(0.5f, 0.5f, 0.5f);
        Vector3 centroid = new Vector3(0f, -1f / 6f, 1f / 6f);

        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();

        void Face(params Vector3[] p)
        {
            Vector3 normal = Vector3.Cross(p[1] - p[0], p[2] - p[0]);
            Vector3 center = Vector3.zero;
            foreach (Vector3 v in p) center += v;
            center /= p.Length;
            bool flip = Vector3.Dot(normal, center - centroid) < 0f;
            int start = vertices.Count;
            foreach (Vector3 v in p)
            {
                vertices.Add(v);
                uvs.Add(new Vector2(v.x + 0.5f, v.y + v.z + 1f));
            }
            for (int i = 1; i < p.Length - 1; i++)
            {
                triangles.Add(start);
                triangles.Add(start + (flip ? i + 1 : i));
                triangles.Add(start + (flip ? i : i + 1));
            }
        }

        Face(a, b, c, d); // floor
        Face(d, c, f, e); // vertical back
        Face(a, b, f, e); // slope
        Face(a, d, e);    // left side
        Face(b, c, f);    // right side

        var mesh = new Mesh { name = "MatterWedge" };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
