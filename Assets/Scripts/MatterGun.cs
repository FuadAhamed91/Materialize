using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The Matter Gun. Places the hotbar's selected item where the centre of the screen points, as real
/// physics matter: a Rigidbody with the item's mass, a runtime PhysicsMaterial and a tinted URP Lit
/// material. A translucent preview shows where it will land. Right click recycles the matter under
/// the crosshair and returns it to the inventory.
/// </summary>
[RequireComponent(typeof(Camera))]
public class MatterGun : MonoBehaviour
{
    [Header("References")]
    public Inventory inventory;
    public MatterHUD hud;
    public FirstPersonController player;
    [Tooltip("URP Lit material cloned for every placed object.")]
    public Material baseMaterial;
    [Tooltip("Transparent material for the placement preview.")]
    public Material ghostMaterial;

    [Header("Aiming")]
    public LayerMask aimMask = ~0;
    public float maxRange = 40f;

    [Header("Matter")]
    public float materializeSeconds = 0.35f;
    public string matterLayerName = "Matter";

    struct Aim
    {
        public bool hit;
        public Vector3 point, normal, flatForward;
    }

    Camera cam;
    Transform ghost;
    MeshFilter ghostFilter;
    bool wasLocked;
    int serial;
    readonly List<SpawnedMatter> spawned = new List<SpawnedMatter>();
    readonly Collider[] overlaps = new Collider[32];
    static Mesh wedgeMesh, cylinderMesh, cubeMesh, sphereMesh;

    public IReadOnlyList<SpawnedMatter> Spawned => spawned;

    void Awake()
    {
        cam = GetComponent<Camera>();
        BuildGhost();
        SpawnedMatter.Removed += OnMatterRemoved;
    }

    void OnDestroy() => SpawnedMatter.Removed -= OnMatterRemoved;

    void OnMatterRemoved(SpawnedMatter matter)
    {
        spawned.Remove(matter);
        if (inventory) inventory.Refund(matter.Loadout, matter.Slot);
    }

    void Update()
    {
        if (!inventory) return;

        int slot = GameInput.SlotPressed;
        if (slot >= 0 && slot < inventory.SlotCount) inventory.Select(slot);
        float scroll = GameInput.Scroll;
        if (scroll > 0.01f) inventory.Select(inventory.Selected - 1);
        else if (scroll < -0.01f) inventory.Select(inventory.Selected + 1);

        // act on clicks only once the mouse is already captured: the capturing click isn't a placement
        bool locked = Cursor.lockState == CursorLockMode.Locked;
        bool ready = locked && wasLocked;
        wasLocked = locked;

        if (ready && GameInput.PlacePressed) PlaceSelected(CaptureAim());
        else if (ready && GameInput.RecyclePressed) RecycleAimed();

        UpdateGhost(locked);
    }

    /// <summary>Selects a slot and places it on a surface point (tests, demo scripts).</summary>
    public bool PlaceAt(int slot, Vector3 point, Vector3 normal)
    {
        inventory.Select(slot);
        return PlaceSelected(new Aim { hit = true, point = point, normal = normal.normalized, flatForward = FlatForward() });
    }

    bool PlaceSelected(Aim aim)
    {
        if (!inventory.TryTake(out InventoryItem item, out int slot))
        {
            InventoryItem selected = inventory.SelectedItem;
            if (hud) hud.Flash(selected != null ? $"No {selected.name} left" : "Nothing to place", new Color(1f, 0.6f, 0.4f), 2f);
            return false;
        }

        Quaternion rotation = AimRotation(aim);
        SpawnedMatter matter = Build(item.config.Clone(), Place(aim, item.config.Size, rotation), rotation, item.name);
        matter.Loadout = inventory.loadout;
        matter.Slot = slot;
        spawned.Add(matter);
        return true;
    }

    void RecycleAimed()
    {
        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (!Physics.Raycast(ray, out RaycastHit hit, maxRange, aimMask, QueryTriggerInteraction.Ignore)) return;
        Rigidbody body = hit.collider.attachedRigidbody;
        SpawnedMatter matter = body ? body.GetComponent<SpawnedMatter>() : null;
        if (matter == null || matter.IsDissolving) return;
        matter.Dissolve(); // refunded when it finishes dissolving
        if (hud) hud.Flash($"Recycled {matter.Source}", 1.5f);
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
        bool solid = Physics.Raycast(ray, out RaycastHit hit, maxRange, aimMask, QueryTriggerInteraction.Ignore);
        // aiming into water drops matter on the surface, where floating and sinking are decided
        float range = solid ? hit.distance : maxRange;
        foreach (BuoyancyVolume water in BuoyancyVolume.All)
            if (water.RaycastSurface(ray, range, out float distance))
                return new Aim { hit = true, point = ray.GetPoint(distance), normal = Vector3.up, flatForward = FlatForward() };
        if (solid) return new Aim { hit = true, point = hit.point, normal = hit.normal, flatForward = FlatForward() };
        return new Aim { hit = false, point = ray.GetPoint(6f), normal = Vector3.zero, flatForward = FlatForward() };
    }

    /// <summary>
    /// Faces away from the player, squared to the room (nearest 90 degrees) so beams and mats bridge
    /// straight across a gap instead of skewing with the view angle. On a ramp or slope, sits flush with it.
    /// </summary>
    static Quaternion AimRotation(Aim aim)
    {
        float yaw = Mathf.Round(Mathf.Atan2(aim.flatForward.x, aim.flatForward.z) * Mathf.Rad2Deg / 90f) * 90f;
        Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
        if (aim.hit && aim.normal.y > 0.6f && aim.normal.y < 0.98f)
            rotation = Quaternion.FromToRotation(Vector3.up, aim.normal) * rotation;
        return rotation;
    }

    // ------------------------------------------------------------------ placement preview

    void BuildGhost()
    {
        var go = new GameObject("PlacementPreview");
        ghost = go.transform;
        ghostFilter = go.AddComponent<MeshFilter>();
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = ghostMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        go.SetActive(false);
    }

    void UpdateGhost(bool locked)
    {
        InventoryItem item = inventory.SelectedItem;
        bool show = locked && ghostMaterial && item != null && inventory.Count(inventory.Selected) > 0;
        if (show != ghost.gameObject.activeSelf) ghost.gameObject.SetActive(show);
        if (!show) return;

        Aim aim = CaptureAim();
        Quaternion rotation = AimRotation(aim);
        Vector3 position = Place(aim, item.config.Size, rotation);
        ShapeVisual(item.config, out Mesh mesh, out Vector3 scale, out Quaternion local);
        ghostFilter.sharedMesh = mesh;
        ghost.SetPositionAndRotation(position, rotation * local);
        ghost.localScale = scale;
    }

    // ------------------------------------------------------------------ placement

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

    // ------------------------------------------------------------------ building matter

    SpawnedMatter Build(PhysicalObjectConfig c, Vector3 position, Quaternion rotation, string label)
    {
        var root = new GameObject($"Matter_{++serial:00}_{label}");
        int layer = LayerMask.NameToLayer(matterLayerName);
        if (layer >= 0) root.layer = layer;
        root.transform.SetPositionAndRotation(position, rotation);

        ShapeVisual(c, out Mesh mesh, out Vector3 scale, out Quaternion local);
        var body = new GameObject("Body", typeof(MeshFilter), typeof(MeshRenderer)) { layer = root.layer };
        body.transform.SetParent(root.transform, false);
        body.transform.localRotation = local;
        body.transform.localScale = scale;
        body.GetComponent<MeshFilter>().sharedMesh = mesh;

        Collider collider;
        switch (c.shape)
        {
            case "sphere":
                collider = body.AddComponent<SphereCollider>(); // unit sphere: radius 0.5
                break;
            case "cylinder":
            case "wedge":
                var hull = body.AddComponent<MeshCollider>();
                hull.sharedMesh = mesh;
                hull.convex = true;
                collider = hull;
                break;
            default:
                collider = body.AddComponent<BoxCollider>(); // unit box
                break;
        }

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
        matter.Init(c, label, rb, renderer, physicsMaterial, material, materializeSeconds);
        return matter;
    }

    /// <summary>Mesh and local transform that give a unit shape the config's size.</summary>
    public static void ShapeVisual(PhysicalObjectConfig c, out Mesh mesh, out Vector3 scale, out Quaternion local)
    {
        Vector3 s = c.Size;
        local = Quaternion.identity;
        switch (c.shape)
        {
            case "sphere":
                mesh = SphereMesh;
                scale = Vector3.one * s.x;
                break;
            case "cylinder":
                mesh = CylinderMesh;
                if (PhysicalObjectConfig.CylinderLiesAlongZ(s))
                {
                    float d = Mathf.Max(s.x, s.y);
                    local = Quaternion.Euler(90f, 0f, 0f);
                    scale = new Vector3(d, s.z * 0.5f, d);
                }
                else
                {
                    float d = Mathf.Max(s.x, s.z);
                    scale = new Vector3(d, s.y * 0.5f, d);
                }
                break;
            case "wedge":
                mesh = WedgeMesh;
                scale = s;
                break;
            default:
                mesh = CubeMesh;
                scale = s;
                break;
        }
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

    // ------------------------------------------------------------------ meshes

    static Mesh CubeMesh => cubeMesh ? cubeMesh : (cubeMesh = PrimitiveMesh(PrimitiveType.Cube));
    static Mesh SphereMesh => sphereMesh ? sphereMesh : (sphereMesh = PrimitiveMesh(PrimitiveType.Sphere));
    // generated rather than built-in so it is always readable for convex collision cooking in builds
    static Mesh CylinderMesh => cylinderMesh ? cylinderMesh : (cylinderMesh = BuildCylinderMesh());
    static Mesh WedgeMesh => wedgeMesh ? wedgeMesh : (wedgeMesh = BuildWedgeMesh());

    /// <summary>
    /// The engine's own unit primitive mesh, as CreatePrimitive uses it. (The built-in resource
    /// "Sphere.fbx" is a legacy 2 m sphere, so it can't be looked up by name.)
    /// </summary>
    static Mesh PrimitiveMesh(PrimitiveType type)
    {
        var temp = GameObject.CreatePrimitive(type);
        Mesh mesh = temp.GetComponent<MeshFilter>().sharedMesh;
        DestroyImmediate(temp); // gone before the next physics step
        return mesh;
    }

    /// <summary>Unit cylinder matching Unity's primitive: radius 0.5, height 2 along Y.</summary>
    static Mesh BuildCylinderMesh(int segments = 24)
    {
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var triangles = new List<int>();
        for (int i = 0; i <= segments; i++)
        {
            float a = 2f * Mathf.PI * i / segments;
            var n = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            vertices.Add(n * 0.5f + Vector3.down);
            vertices.Add(n * 0.5f + Vector3.up);
            normals.Add(n);
            normals.Add(n);
        }
        for (int i = 0; i < segments; i++)
        {
            int b = i * 2;
            triangles.AddRange(new[] { b, b + 1, b + 2, b + 2, b + 1, b + 3 });
        }
        foreach (float y in new[] { -1f, 1f })
        {
            int center = vertices.Count;
            vertices.Add(new Vector3(0f, y, 0f));
            normals.Add(new Vector3(0f, y, 0f));
            for (int i = 0; i <= segments; i++)
            {
                float a = 2f * Mathf.PI * i / segments;
                vertices.Add(new Vector3(Mathf.Cos(a) * 0.5f, y, Mathf.Sin(a) * 0.5f));
                normals.Add(new Vector3(0f, y, 0f));
            }
            for (int i = 0; i < segments; i++)
            {
                if (y > 0f) triangles.AddRange(new[] { center, center + i + 2, center + i + 1 });
                else triangles.AddRange(new[] { center, center + i + 1, center + i + 2 });
            }
        }
        var mesh = new Mesh { name = "MatterCylinder" };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

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
