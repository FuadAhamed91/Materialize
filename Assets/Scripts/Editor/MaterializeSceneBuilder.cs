using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif
using Object = UnityEngine.Object;

/// <summary>
/// Assembles Assets/Scenes/Materialize.unity from the Blender chamber exports in Assets/Art/Chambers:
/// URP materials and import remaps, colliders and physics materials, the ten puzzle rigs, the
/// first-person player with the Matter Gun, the HUD, lighting and post-processing.
/// In-game text names each chamber but never explains its puzzle.
/// Menu: Materialize > Build Scene. Re-running rebuilds the scene from scratch.
/// </summary>
public static class MaterializeSceneBuilder
{
    public const string ScenePath = "Assets/Scenes/Materialize.unity";
    public const string EnvironmentLayer = "Environment";
    public const string MatterLayer = "Matter";
    public const string PlayerLayer = "Player";
    public const int ChamberCount = 10;

    const string ChamberFolder = "Assets/Art/Chambers";
    const string MaterialFolder = "Assets/Art/Materials";
    const string PhysicsFolder = "Assets/Art/PhysicsMaterials";
    const string TextureFolder = "Assets/Art/Textures";
    const string VolumeProfilePath = "Assets/Settings/MaterializeVolume.asset";
    const float LabelScale = 0.004f; // world-space UI: 250 px per metre

    /// <summary>Decorative meshes that get no collider (plus anything named *_Light / *_Light_N).</summary>
    static readonly string[] DecorativeParts =
    {
        "LightStrip", "Rope_", "_Hazard", "Hazard_", "Acid_", "Water_Surface", "_Plinth", "Conduit_", "_Glow",
        "Core_Orb", "Scale_Beam",
    };

    /// <summary>Parts whose colliders and bodies are added by their puzzle rig.</summary>
    static readonly string[] RiggedParts =
    {
        "Bucket", "Portcullis", "ExitDoor", "Drawbridge", "Glass_Seal", "Core_Glass", "Containment_Field",
        "Pan_Sensor", "Pan_Weighted", "Lock_Bar_1", "Lock_Bar_2", "Lock_Bar_3",
    };

    static readonly string[] MetalParts =
    {
        "Gantry", "Pulley", "Bracket", "EntryHatch", "Portcullis", "Bucket", "Drawbridge", "ExitDoor", "LiveFloor",
        "Coil", "Gutter", "Pan_", "Scale_", "Bollard", "Field_Emitter", "Lock_Bar", "Core_Pedestal", "Seal_Sill",
    };

    static readonly Color PanelColor = new Color(0.03f, 0.05f, 0.07f, 0.74f);
    static readonly Color Accent = new Color(0.25f, 0.8f, 1f, 1f);
    static readonly Color Amber = new Color(1f, 0.76f, 0.25f, 1f);
    static readonly Color TextColor = new Color(0.86f, 0.92f, 0.98f, 1f);

    class Chamber
    {
        public string name, title;
        public Vector3 origin;
        public float width, length, height;
        public float doorZ;    // height of the exit doorway's sill
        public Vector3 spawn;  // Blender x, y, z of the player's feet
        public Vector3 sign;   // Blender x, y, z of the title board
        public GameObject root;
        public Transform spawnPoint;
        public ChamberExit exit;

        /// <summary>
        /// Blender coordinates (x across, y into the room, z up) to Unity world space. The FBX export
        /// (-Z forward, Y up, baked space transform) lands Blender +Y on Unity -Z, so rooms run towards -Z.
        /// </summary>
        public Vector3 B(float x, float y, float z) => origin + new Vector3(-x, z, -y);

        /// <summary>Facing into the room (towards the exit).</summary>
        public static readonly Quaternion Inward = Quaternion.Euler(0f, 180f, 0f);

        public GameObject Find(string objectName)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == objectName) return t.gameObject;
            throw new InvalidOperationException($"{name}: no object named '{objectName}' in the chamber export.");
        }
    }

    static Chamber Define(int number, string name, string title, float width, float length, float height,
        Vector3 spawn, Vector3 sign, float doorZ = 0f) => new Chamber
    {
        name = name, title = title, origin = new Vector3(40f * (number - 1), 0f, 0f),
        width = width, length = length, height = height, spawn = spawn, sign = sign, doorZ = doorZ,
    };

    static Chamber[] DefineChambers() => new[]
    {
        Define(1, "Chamber_01_Counterweight", "CHAMBER 01 · COUNTERWEIGHT", 8f, 10f, 6f, new Vector3(0f, 1.5f, 0f), new Vector3(0f, 9.97f, 5.45f)),
        Define(2, "Chamber_02_TheLedge", "CHAMBER 02 · THE LEDGE", 8f, 12f, 9f, new Vector3(0f, 1.5f, 0f), new Vector3(0f, 7.83f, 3.1f), 5f),
        Define(3, "Chamber_03_TheChasm", "CHAMBER 03 · THE CHASM", 8f, 14f, 6f, new Vector3(-2f, 1.2f, 0f), new Vector3(0f, 13.97f, 4.6f)),
        Define(4, "Chamber_04_TheFlood", "CHAMBER 04 · THE FLOOD", 10f, 16f, 7f, new Vector3(0f, 1.5f, 0f), new Vector3(0f, 15.97f, 4.8f)),
        Define(5, "Chamber_05_Shatterpoint", "CHAMBER 05 · SHATTERPOINT", 8f, 18f, 8f, new Vector3(0f, 1.5f, 4f), new Vector3(0f, 13.73f, 5.4f)),
        Define(6, "Chamber_06_LiveWire", "CHAMBER 06 · LIVE WIRE", 8f, 16f, 6f, new Vector3(0f, 1.2f, 0f), new Vector3(0f, 15.97f, 4.6f)),
        Define(7, "Chamber_07_TheScales", "CHAMBER 07 · THE SCALES", 10f, 14f, 8f, new Vector3(0f, 1.5f, 0f), new Vector3(0f, 13.97f, 6.2f)),
        Define(8, "Chamber_08_Grip", "CHAMBER 08 · GRIP", 8f, 17f, 9f, new Vector3(0f, 1.5f, 0f), new Vector3(0f, 4.95f, 3.4f), 5.6f),
        Define(9, "Chamber_09_MarbleRun", "CHAMBER 09 · MARBLE RUN", 8f, 10f, 6f, new Vector3(-1.4f, 0.9f, 0f), new Vector3(0f, 9.97f, 4.6f)),
        Define(10, "Chamber_10_Synthesis", "CHAMBER 10 · SYNTHESIS", 12f, 22f, 8f, new Vector3(0f, 1.5f, 0f), new Vector3(0f, 21.97f, 7.1f)),
    };

    [MenuItem("Materialize/Build Scene", priority = 0)]
    public static void Build()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[Materialize] Exit Play mode before building the scene.");
            return;
        }

        EnsureFolders();
        EnsureLayers();
        Dictionary<string, Material> materials = CreateMaterials();
        Dictionary<string, PhysicsMaterial> physics = CreatePhysicsMaterials();
        Chamber[] chambers = DefineChambers();
        ConfigureChamberImports(chambers, materials);
        VolumeProfile volumeProfile = CreateVolumeProfile();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        ConfigureRenderSettings();

        var environment = new GameObject("Environment").transform;
        foreach (Chamber chamber in chambers) PlaceChamber(chamber, environment, physics);
        RigCounterweight(chambers[0], physics);
        RigLedge(chambers[1], physics);
        RigChasm(chambers[2], physics);
        RigFlood(chambers[3], physics);
        RigShatterpoint(chambers[4], physics);
        RigLiveWire(chambers[5], physics);
        RigScales(chambers[6], physics);
        RigGrip(chambers[7], physics);
        RigMarbleRun(chambers[8], physics);
        RigSynthesis(chambers[9], physics);
        for (int i = 0; i < chambers.Length; i++)
        {
            Chamber c = chambers[i];
            c.exit = ExitTrigger(c, c.B(0f, c.length + 2.6f, c.doorZ + 1.5f));
            if (i + 1 >= chambers.Length) continue;
            c.exit.nextSpawn = chambers[i + 1].spawnPoint;
            c.exit.nextTitle = chambers[i + 1].title;
            c.exit.nextObjective = "";
        }

        FirstPersonController player = BuildPlayer(chambers[0].spawnPoint);
        var service = new GameObject("Systems").AddComponent<PhysicsPromptService>();
        MatterHUD hud = BuildHud(chambers[0]);
        BuildEventSystem();

        var gun = player.GetComponentInChildren<Camera>().gameObject.AddComponent<MatterGun>();
        gun.promptService = service;
        gun.hud = hud;
        gun.player = player;
        gun.baseMaterial = materials["M_SpawnedMatter"];
        gun.aimMask = ~((1 << LayerMask.NameToLayer(PlayerLayer)) | (1 << LayerMask.NameToLayer("Ignore Raycast")));
        gun.matterLayerName = MatterLayer;

        var volume = new GameObject("Global Volume").AddComponent<Volume>();
        volume.isGlobal = true;
        volume.sharedProfile = volumeProfile;

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
        Debug.Log($"[Materialize] Built {ScenePath}: {chambers.Length} chambers, player spawned in {chambers[0].name}.");
    }

    // ------------------------------------------------------------------ project setup

    static void EnsureFolders()
    {
        foreach (string folder in new[] { "Assets/Art", MaterialFolder, PhysicsFolder, TextureFolder, "Assets/Scenes", "Assets/Settings" })
        {
            if (AssetDatabase.IsValidFolder(folder)) continue;
            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }

    static void EnsureLayers()
    {
        var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        SerializedProperty layers = tagManager.FindProperty("layers");
        foreach (string layer in new[] { EnvironmentLayer, MatterLayer, PlayerLayer })
        {
            bool exists = false;
            for (int i = 0; i < layers.arraySize; i++)
                if (layers.GetArrayElementAtIndex(i).stringValue == layer) exists = true;
            if (exists) continue;
            for (int i = 8; i < layers.arraySize; i++)
            {
                SerializedProperty slot = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(slot.stringValue)) continue;
                slot.stringValue = layer;
                break;
            }
        }
        tagManager.ApplyModifiedPropertiesWithoutUndo();
    }

    static Dictionary<string, Material> CreateMaterials()
    {
        Texture2D concrete = GeneratedTexture("T_Concrete", 512, ConcretePixel);
        Texture2D hazard = GeneratedTexture("T_HazardStripes", 256, HazardPixel);
        return new Dictionary<string, Material>
        {
            ["M_Concrete"] = Lit("M_Concrete", new Color(0.66f, 0.65f, 0.62f), 0f, 0.12f, concrete),
            ["M_ConcreteDark"] = Lit("M_ConcreteDark", new Color(0.4f, 0.4f, 0.41f), 0f, 0.1f, concrete),
            ["M_DarkMetal"] = Lit("M_DarkMetal", new Color(0.12f, 0.125f, 0.13f), 0.85f, 0.55f),
            ["M_Copper"] = Lit("M_Copper", new Color(0.96f, 0.56f, 0.36f), 1f, 0.72f),
            ["M_Acid"] = Lit("M_Acid", new Color(0.12f, 0.75f, 0.08f), 0f, 0.92f, null, new Color(0.35f, 2.4f, 0.12f)),
            ["M_TerminalGlow"] = Lit("M_TerminalGlow", new Color(0.1f, 0.5f, 1f), 0f, 0.6f, null, new Color(0.15f, 0.9f, 2.4f)),
            ["M_LightPanel"] = Lit("M_LightPanel", new Color(1f, 0.97f, 0.9f), 0f, 0.5f, null, new Color(2.6f, 2.5f, 2.3f)),
            ["M_Hazard"] = Lit("M_Hazard", Color.white, 0f, 0.35f, hazard),
            ["M_Cable"] = Lit("M_Cable", new Color(0.07f, 0.07f, 0.075f), 0.7f, 0.45f),
            ["M_SpawnedMatter"] = Lit("M_SpawnedMatter", new Color(0.7f, 0.7f, 0.7f), 0f, 0.5f, null, Color.black),
            ["M_Water"] = Transparent(Lit("M_Water", new Color(0.08f, 0.3f, 0.42f, 0.72f), 0f, 0.95f, null, new Color(0.02f, 0.07f, 0.1f))),
            ["M_Glass"] = Transparent(Lit("M_Glass", new Color(0.7f, 0.9f, 1f, 0.22f), 0f, 0.95f)),
            ["M_LiveGlow"] = Lit("M_LiveGlow", new Color(0.3f, 0.7f, 1f), 0f, 0.4f, null, new Color(0.4f, 1f, 2.6f)),
            ["M_Field"] = Transparent(Lit("M_Field", new Color(0.3f, 0.75f, 1f, 0.16f), 0f, 0.6f, null, new Color(0.12f, 0.4f, 0.9f))),
        };
    }

    /// <summary>Switches a URP Lit material to alpha-blended transparency.</summary>
    static Material Transparent(Material material)
    {
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 0f);
        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0f);
        material.SetOverrideTag("RenderType", "Transparent");
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = (int)RenderQueue.Transparent;
        material.SetShaderPassEnabled("ShadowCaster", false);
        material.SetShaderPassEnabled("DepthOnly", false);
        EditorUtility.SetDirty(material);
        return material;
    }

    static Material Lit(string name, Color color, float metallic, float smoothness, Texture2D albedo = null, Color? emission = null)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = shader;
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", smoothness);
        material.SetTexture("_BaseMap", albedo);
        if (emission.HasValue)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", emission.Value);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        else
        {
            material.DisableKeyword("_EMISSION");
        }
        EditorUtility.SetDirty(material);
        return material;
    }

    static Dictionary<string, PhysicsMaterial> CreatePhysicsMaterials() => new Dictionary<string, PhysicsMaterial>
    {
        ["PM_Concrete"] = PhysicsAsset("PM_Concrete", 0.6f, 0.7f, 0.05f),
        ["PM_Metal"] = PhysicsAsset("PM_Metal", 0.4f, 0.5f, 0.1f),
        ["PM_Copper"] = PhysicsAsset("PM_Copper", 0.35f, 0.5f, 0.1f),
        ["PM_Glass"] = PhysicsAsset("PM_Glass", 0.25f, 0.4f, 0.1f),
        // full grip with Multiply combine: the matter's own friction decides whether it holds a slope
        ["PM_Grip"] = PhysicsAsset("PM_Grip", 1f, 1f, 0.05f, PhysicsMaterialCombine.Multiply),
    };

    static PhysicsMaterial PhysicsAsset(string name, float dynamicFriction, float staticFriction, float bounciness,
        PhysicsMaterialCombine frictionCombine = PhysicsMaterialCombine.Average)
    {
        string path = $"{PhysicsFolder}/{name}.asset";
        var material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
        if (material == null)
        {
            material = new PhysicsMaterial(name);
            AssetDatabase.CreateAsset(material, path);
        }
        material.dynamicFriction = dynamicFriction;
        material.staticFriction = staticFriction;
        material.bounciness = bounciness;
        material.frictionCombine = frictionCombine;
        material.bounceCombine = PhysicsMaterialCombine.Average;
        EditorUtility.SetDirty(material);
        return material;
    }

    static void ConfigureChamberImports(Chamber[] chambers, Dictionary<string, Material> materials)
    {
        foreach (Chamber chamber in chambers)
        {
            string path = $"{ChamberFolder}/{chamber.name}.fbx";
            if (!(AssetImporter.GetAtPath(path) is ModelImporter importer))
                throw new FileNotFoundException($"Missing {path}. Run ArtSource/build_chambers.py in Blender first.");
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importBlendShapes = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            foreach (KeyValuePair<string, Material> pair in materials)
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), pair.Key), pair.Value);
            importer.SaveAndReimport();
        }
    }

    static VolumeProfile CreateVolumeProfile()
    {
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
        if (profile != null) return profile;

        profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, VolumeProfilePath);
        profile.Add<Tonemapping>(true).mode.Override(TonemappingMode.ACES);
        var bloom = profile.Add<Bloom>(true);
        bloom.threshold.Override(0.95f);
        bloom.intensity.Override(0.9f);
        bloom.scatter.Override(0.65f);
        var vignette = profile.Add<Vignette>(true);
        vignette.intensity.Override(0.3f);
        vignette.smoothness.Override(0.45f);
        var grade = profile.Add<ColorAdjustments>(true);
        grade.postExposure.Override(0.25f);
        grade.contrast.Override(12f);
        grade.saturation.Override(-8f);
        foreach (VolumeComponent component in profile.components)
        {
            component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(component, profile);
        }
        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        return profile;
    }

    static void ConfigureRenderSettings()
    {
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.21f, 0.22f, 0.24f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = 0.022f;
        RenderSettings.fogColor = new Color(0.06f, 0.07f, 0.08f);
    }

    // ------------------------------------------------------------------ chambers

    static void PlaceChamber(Chamber c, Transform parent, Dictionary<string, PhysicsMaterial> physics)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{ChamberFolder}/{c.name}.fbx");
        c.root = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
        c.root.name = c.name;
        c.root.transform.SetPositionAndRotation(c.origin, Quaternion.identity);

        int environment = LayerMask.NameToLayer(EnvironmentLayer);
        foreach (Transform t in c.root.GetComponentsInChildren<Transform>(true))
        {
            t.gameObject.layer = environment;
            // Blender object names are unique per .blend, so repeats across chambers arrive as "Name.001"
            if (t != c.root.transform) t.name = Regex.Replace(t.name, @"\.\d{3}$", "");
        }
        foreach (MeshFilter filter in c.root.GetComponentsInChildren<MeshFilter>(true))
        {
            GameObject go = filter.gameObject;
            if (IsDecorative(go.name) || Array.IndexOf(RiggedParts, go.name) >= 0) continue;
            var collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = filter.sharedMesh;
            collider.sharedMaterial = PhysicsFor(go.name, physics);
            GameObjectUtility.SetStaticEditorFlags(go,
                StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic |
                StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
        }

        c.spawnPoint = new GameObject("PlayerSpawn").transform;
        c.spawnPoint.SetParent(c.root.transform, false);
        c.spawnPoint.SetPositionAndRotation(c.B(c.spawn.x, c.spawn.y, c.spawn.z + 0.05f), Chamber.Inward);

        AddLighting(c);
        // the name only: no instructions, no hints
        WorldLabel("ChamberSign", c.root.transform, c.B(c.sign.x, c.sign.y, c.sign.z),
            $"<b>{c.title}</b>", 64, new Vector2(7.2f, 0.5f), new Color(0.86f, 0.9f, 0.95f, 0.92f));
    }

    static void RigCounterweight(Chamber c, Dictionary<string, PhysicsMaterial> physics)
    {
        GameObject bucket = c.Find("Bucket");
        ChamberDoor bucketDrop = MakeMover(bucket, new Vector3(0f, -1.2f, 0f), Vector3.zero, 3.5f, physics["PM_Metal"], true, null);
        ChamberDoor portcullis = MakeMover(c.Find("Portcullis"), new Vector3(0f, 3.6f, 0f), Vector3.zero, 4f, physics["PM_Metal"], false,
            "Counterweight engaged · portcullis rising", c.Find("Exit_Light").GetComponent<Renderer>());

        var sensor = new GameObject("LoadSensor") { layer = LayerMask.NameToLayer("Ignore Raycast") };
        sensor.transform.SetParent(bucket.transform, false);
        sensor.transform.localPosition = new Vector3(0f, 0.55f, 0f);
        var zone = sensor.AddComponent<BoxCollider>();
        zone.isTrigger = true;
        zone.size = new Vector3(1.3f, 0.95f, 1.3f);
        var plate = sensor.AddComponent<WeightTriggerPlate>();
        plate.requiredMass = 500f;
        plate.targets = new[] { bucketDrop, portcullis };
        // in the gantry plane at eye level, offset so the bucket cable doesn't cross it
        plate.readout = WorldLabel("LoadReadout", c.root.transform, c.B(-1f, 5.62f, 2.5f), "LOAD  0 kg", 40,
            new Vector2(1.8f, 0.26f), Amber);
        NewLight("Pit Light", c.root.transform, c.B(0f, 4.6f, 0.9f), new Color(1f, 0.85f, 0.65f), 2.5f, 4.5f, false);

        var attach = new GameObject("RopeAttach").transform;
        attach.SetParent(bucket.transform, false);
        attach.localPosition = new Vector3(0f, 1.75f, 0f);
        c.Find("Rope_Bucket").AddComponent<RopeStretch>().target = attach;

        Hazard(c, "PitSafetyNet", c.B(0f, 5.5f, -2.2f), new Vector3(2.6f, 0.6f, 2.6f), "Fell into the pit · respawning", "Matter recycled");
    }

    static void RigLedge(Chamber c, Dictionary<string, PhysicsMaterial> physics)
    {
        ChamberDoor door = MakeMover(c.Find("ExitDoor"), new Vector3(2.3f, 0f, 0f), Vector3.zero, 2f, physics["PM_Metal"], false,
            "Ledge reached · exit unlocked", c.Find("Exit_Light").GetComponent<Renderer>());
        GameObject top = NewTrigger("LedgeTopTrigger", c, c.B(0f, 10.2f, 6f), new Vector3(7.6f, 2f, 3.4f));
        top.AddComponent<ZoneTrigger>().targets = new[] { door };
        NewLight("Ledge Fill", c.root.transform, c.B(0f, 4.5f, 3.5f), new Color(1f, 0.93f, 0.84f), 7f, 11f, false);
    }

    static void RigChasm(Chamber c, Dictionary<string, PhysicsMaterial> physics)
    {
        GameObject bridge = c.Find("Drawbridge");
        // modelled lowered (deck runs from the far-side hinge towards +Z); it stands raised on the
        // hinge until the circuit closes
        bridge.transform.localRotation = Quaternion.AngleAxis(-90f, Vector3.right) * bridge.transform.localRotation;
        ChamberDoor drawbridge = MakeMover(bridge, Vector3.zero, new Vector3(90f, 0f, 0f), 3f, physics["PM_Metal"], true, null);
        ChamberDoor door = MakeMover(c.Find("ExitDoor"), new Vector3(2.3f, 0f, 0f), Vector3.zero, 2f, physics["PM_Metal"], false,
            null, c.Find("Exit_Light").GetComponent<Renderer>());

        var grid = new GameObject("PowerGrid");
        grid.transform.SetParent(c.root.transform, false);
        var circuit = grid.AddComponent<ElectricalBridge>();
        circuit.nodeA = c.Find("Terminal_A_Node").GetComponent<Collider>();
        circuit.nodeB = c.Find("Terminal_B_Node").GetComponent<Collider>();
        circuit.targets = new[] { drawbridge, door };
        circuit.indicators = new[]
        {
            c.Find("Terminal_A_Light").GetComponent<Renderer>(),
            c.Find("Terminal_B_Light").GetComponent<Renderer>(),
        };
        circuit.announce = "Circuit closed · drawbridge lowering · exit unlocked";

        Hazard(c, "AcidTrench", c.B(0f, 7f, -1.9f), new Vector3(8f, 0.8f, 4f), "Dissolved by acid · respawning", "Matter dissolved in acid");
        NewLight("Acid Glow", c.root.transform, c.B(0f, 7f, -1.2f), new Color(0.45f, 1f, 0.3f), 4f, 9f, false);
    }

    static void RigFlood(Chamber c, Dictionary<string, PhysicsMaterial> physics)
    {
        ChamberDoor door = StandardExitDoor(c, physics, "Far shore reached · exit unlocked");
        NewTrigger("FarShoreTrigger", c, c.B(0f, 13.6f, 1f), new Vector3(9.6f, 2f, 4.4f)).AddComponent<ZoneTrigger>().targets = new[] { door };
        // the top of this box is the water surface (Blender z = -0.6)
        NewTrigger("Water", c, c.B(0f, 7.5f, -2.3f), new Vector3(10f, 3.4f, 7f)).AddComponent<BuoyancyVolume>();
        Hazard(c, "DeepWater", c.B(0f, 7.5f, -3.1f), new Vector3(10f, 1.8f, 7f), "You can't swim · respawning", null, false);
        NewLight("Pool Glow", c.root.transform, c.B(0f, 7.5f, -1.4f), new Color(0.3f, 0.75f, 1f), 3f, 9f, false);
    }

    static void RigShatterpoint(Chamber c, Dictionary<string, PhysicsMaterial> physics)
    {
        ChamberDoor door = StandardExitDoor(c, physics, null);
        GameObject glass = c.Find("Glass_Seal");
        AddBox(glass, physics["PM_Glass"]);
        var seal = glass.AddComponent<ImpactSeal>();
        seal.momentumThreshold = 6000f;
        seal.targets = new[] { door };
        seal.announce = "Seal shattered";
    }

    static void RigLiveWire(Chamber c, Dictionary<string, PhysicsMaterial> physics)
    {
        Electrify(c);
        ChamberDoor door = StandardExitDoor(c, physics, "Crossed · exit unlocked");
        NewTrigger("FarSideTrigger", c, c.B(0f, 14.6f, 1f), new Vector3(7.6f, 2f, 2.8f)).AddComponent<ZoneTrigger>().targets = new[] { door };
    }

    static void RigScales(Chamber c, Dictionary<string, PhysicsMaterial> physics)
    {
        ChamberDoor door = StandardExitDoor(c, physics, "Balanced · exit unlocked");
        GameObject sensorPan = c.Find("Pan_Sensor");
        Rigidbody sensorBody = KinematicMeshBody(sensorPan, physics["PM_Metal"]);
        Rigidbody weightedBody = KinematicMeshBody(c.Find("Pan_Weighted"), physics["PM_Metal"]);

        Bounds tray = sensorPan.GetComponent<MeshFilter>().sharedMesh.bounds;
        var zoneObject = new GameObject("PanSensor") { layer = LayerMask.NameToLayer("Ignore Raycast") };
        zoneObject.transform.SetParent(sensorPan.transform, false);
        zoneObject.transform.localPosition = new Vector3(tray.center.x, tray.min.y + 0.55f, tray.center.z);
        var zone = zoneObject.AddComponent<BoxCollider>();
        zone.isTrigger = true;
        zone.size = new Vector3(1.44f, 0.9f, 1.44f);

        var balance = new GameObject("Balance").AddComponent<BalanceScale>();
        balance.transform.SetParent(c.root.transform, false);
        balance.beam = c.Find("Scale_Beam").transform;
        balance.sensorPan = sensorBody;
        balance.weightedPan = weightedBody;
        balance.sensorZone = zone;
        balance.counterweightMass = 640f;
        balance.targets = new[] { door };
        balance.indicators = new[] { c.Find("Scale_Light").GetComponent<Renderer>() };
    }

    static void RigGrip(Chamber c, Dictionary<string, PhysicsMaterial> physics)
    {
        ChamberDoor door = StandardExitDoor(c, physics, null);
        GameObject fieldObject = c.Find("Containment_Field");
        ChamberDoor field = MakeMover(fieldObject, new Vector3(0f, -10f, 0f), Vector3.zero, 1.6f, physics["PM_Metal"], false, "Containment released");
        fieldObject.layer = LayerMask.NameToLayer("Ignore Raycast"); // the Matter Gun aims through it; bodies can't pass
        c.Find("ChamberSign").transform.SetParent(fieldObject.transform, true);

        // pressure sensor lying on the 35-degree slope (it faces back towards the player, +Z)
        const float slope = 35f * Mathf.Deg2Rad;
        var normal = new Vector3(0f, Mathf.Cos(slope), Mathf.Sin(slope));
        var sensor = new GameObject("GripSensor") { layer = LayerMask.NameToLayer("Ignore Raycast") };
        sensor.transform.SetParent(c.root.transform, false);
        sensor.transform.SetPositionAndRotation(c.B(0f, 10.6f, 2.8f) + normal * 0.48f, Quaternion.FromToRotation(Vector3.up, normal));
        var box = sensor.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(2.6f, 0.9f, 2.7f);
        var plate = sensor.AddComponent<WeightTriggerPlate>();
        plate.requiredMass = 150f;
        plate.holdSeconds = 1.5f; // slippery matter slides across it; only grip counts
        plate.targets = new[] { field, door };

        Hazard(c, "AcidGutter", c.B(0f, 6f, -0.9f), new Vector3(8f, 0.8f, 1.2f), "Dissolved by acid · respawning", "Matter dissolved in acid");
        NewLight("Acid Glow", c.root.transform, c.B(0f, 6f, -0.4f), new Color(0.45f, 1f, 0.3f), 2.5f, 6f, false);
    }

    static void RigMarbleRun(Chamber c, Dictionary<string, PhysicsMaterial> physics)
    {
        ChamberDoor door = StandardExitDoor(c, physics, "Mechanism engaged");
        // the gutter falls from (y 1.8, z 1.25) to (y 12.8, z 0.35); the button sits at its low end
        const float gx = -3f, endY = 12.8f;
        float buttonY = endY - 0.4f;
        float channelZ = 1.25f + (0.35f - 1.25f) * (buttonY - 1.8f) / (endY - 1.8f);
        var sensor = NewTrigger("ButtonSensor", c, c.B(gx, buttonY, channelZ + 0.3f), new Vector3(0.6f, 0.6f, 0.8f));
        var plate = sensor.AddComponent<WeightTriggerPlate>();
        plate.requiredMass = 3f;
        plate.targets = new[] { door };
        plate.indicators = new[] { c.Find("Button_Glow").GetComponent<Renderer>() };
    }

    static void RigSynthesis(Chamber c, Dictionary<string, PhysicsMaterial> physics)
    {
        Electrify(c);
        var bars = new ChamberDoor[3];
        for (int i = 0; i < bars.Length; i++)
            bars[i] = MakeMover(c.Find($"Lock_Bar_{i + 1}"), new Vector3(0f, 3.4f, 0f), Vector3.zero, 2f, physics["PM_Metal"], false,
                null, c.Find($"Lock_Light_{i + 1}").GetComponent<Renderer>());

        // lock 1: weight
        var weigh = NewTrigger("WeighSensor", c, c.B(-4f, 12f, 0.5f), new Vector3(2f, 0.9f, 2f)).AddComponent<WeightTriggerPlate>();
        weigh.requiredMass = 1500f;
        weigh.targets = new[] { bars[0] };

        // lock 2: circuit
        var grid = new GameObject("PowerGrid");
        grid.transform.SetParent(c.root.transform, false);
        var circuit = grid.AddComponent<ElectricalBridge>();
        circuit.nodeA = c.Find("Terminal_A_Node").GetComponent<Collider>();
        circuit.nodeB = c.Find("Terminal_B_Node").GetComponent<Collider>();
        circuit.targets = new[] { bars[1] };
        circuit.indicators = new[] { c.Find("Terminal_A_Light").GetComponent<Renderer>(), c.Find("Terminal_B_Light").GetComponent<Renderer>() };
        circuit.announce = "";

        // lock 3: momentum
        GameObject glass = c.Find("Core_Glass");
        AddBox(glass, physics["PM_Glass"]);
        var seal = glass.AddComponent<ImpactSeal>();
        seal.momentumThreshold = 4000f;
        seal.targets = new[] { bars[2] };
        seal.hideOnBreak = new[] { c.Find("Core_Orb").GetComponent<Renderer>() };
    }

    static ChamberDoor StandardExitDoor(Chamber c, Dictionary<string, PhysicsMaterial> physics, string announce) =>
        MakeMover(c.Find("ExitDoor"), new Vector3(2.3f, 0f, 0f), Vector3.zero, 2f, physics["PM_Metal"], false, announce,
            c.Find("Exit_Light").GetComponent<Renderer>());

    /// <summary>Live deck: the floor plus every tesla coil, with flickering arc lights.</summary>
    static void Electrify(Chamber c)
    {
        var current = new GameObject("LiveCurrent");
        current.transform.SetParent(c.root.transform, false);
        var deck = current.AddComponent<ElectrifiedFloor>();
        deck.surface = c.Find("LiveFloor").GetComponent<Collider>();
        deck.glow = c.root.GetComponentsInChildren<Renderer>(true).Where(r => r.name.StartsWith("LiveFloor_Glow")).ToArray();
        foreach (Transform cap in c.root.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Coil_Cap_")).ToArray())
            NewLight("Arc Light", cap, cap.position + Vector3.up * 0.5f, new Color(0.45f, 0.75f, 1f), 2.5f, 6f, false)
                .gameObject.AddComponent<FlickerLight>();
    }

    static BoxCollider AddBox(GameObject go, PhysicsMaterial physics)
    {
        Mesh mesh = go.GetComponent<MeshFilter>().sharedMesh;
        var box = go.AddComponent<BoxCollider>();
        box.center = mesh.bounds.center;
        box.size = mesh.bounds.size;
        box.sharedMaterial = physics;
        return box;
    }

    static Rigidbody KinematicMeshBody(GameObject go, PhysicsMaterial physics)
    {
        var collider = go.AddComponent<MeshCollider>();
        collider.sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
        collider.sharedMaterial = physics;
        var body = go.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        return body;
    }

    static ChamberDoor MakeMover(GameObject go, Vector3 openOffset, Vector3 openEuler, float seconds, PhysicsMaterial physics,
        bool meshCollider, string announce, params Renderer[] indicators)
    {
        if (meshCollider)
        {
            var collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
            collider.sharedMaterial = physics;
        }
        else
        {
            AddBox(go, physics);
        }

        var body = go.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;

        var door = go.AddComponent<ChamberDoor>();
        door.openPositionOffset = openOffset;
        door.openRotationOffset = openEuler;
        door.duration = seconds;
        door.announce = announce;
        door.indicators = indicators;
        return door;
    }

    static GameObject NewTrigger(string name, Chamber c, Vector3 position, Vector3 size)
    {
        var go = new GameObject(name) { layer = LayerMask.NameToLayer("Ignore Raycast") };
        go.transform.SetParent(c.root.transform, false);
        go.transform.position = position;
        var box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = size;
        return go;
    }

    static void Hazard(Chamber c, string name, Vector3 position, Vector3 size, string playerMessage, string matterMessage,
        bool dissolvesMatter = true)
    {
        var hazard = NewTrigger(name, c, position, size).AddComponent<HazardZone>();
        hazard.playerMessage = playerMessage;
        hazard.matterMessage = matterMessage;
        hazard.dissolvesMatter = dissolvesMatter;
    }

    static ChamberExit ExitTrigger(Chamber c, Vector3 position) =>
        NewTrigger("ExitTrigger", c, position, new Vector3(3f, 3f, 1.4f)).AddComponent<ChamberExit>();

    static PhysicsMaterial PhysicsFor(string objectName, Dictionary<string, PhysicsMaterial> physics)
    {
        if (objectName.EndsWith("_Node")) return physics["PM_Copper"];
        if (objectName.StartsWith("Grip_")) return physics["PM_Grip"];
        if (MetalParts.Any(part => objectName.Contains(part))) return physics["PM_Metal"];
        return physics["PM_Concrete"];
    }

    public static bool IsDecorative(string objectName) =>
        DecorativeParts.Any(part => objectName.Contains(part)) || objectName.EndsWith("_Light") || objectName.Contains("_Light_");

    static void AddLighting(Chamber c)
    {
        var group = new GameObject("Lighting").transform;
        group.SetParent(c.root.transform, false);
        // taller rooms need more output to reach the floor (inverse-square falloff)
        float intensity = 6f * Mathf.Pow(c.height / 6f, 2f);
        int index = 0;
        foreach (float x in new[] { -c.width / 4f, c.width / 4f })
        foreach (float y in new[] { c.length * 0.25f, c.length * 0.75f })
        {
            index++;
            // one shadow caster per room: point-light shadows cost six maps each, which WebGL feels
            NewLight($"Ceiling Light {index}", group, c.B(x, y, c.height - 0.5f), new Color(1f, 0.93f, 0.84f), intensity, c.height + 9f, index == 1);
        }

        var probe = new GameObject("Reflection Probe").AddComponent<ReflectionProbe>();
        probe.transform.SetParent(group, false);
        probe.transform.position = c.B(0f, c.length / 2f, c.height / 2f);
        probe.mode = ReflectionProbeMode.Realtime;
        probe.refreshMode = ReflectionProbeRefreshMode.OnAwake;
        probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
        probe.size = new Vector3(c.width + 0.5f, c.height + 7f, c.length + 0.5f);
        probe.boxProjection = true;
        probe.resolution = 128;
    }

    static Light NewLight(string name, Transform parent, Vector3 position, Color color, float intensity, float range, bool shadows)
    {
        var light = new GameObject(name).AddComponent<Light>();
        light.transform.SetParent(parent, false);
        light.transform.position = position;
        light.type = LightType.Point;
        light.color = color;
        light.intensity = intensity;
        light.range = range;
        light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
        light.lightmapBakeType = LightmapBakeType.Realtime;
        return light;
    }

    // ------------------------------------------------------------------ player, HUD, input

    static FirstPersonController BuildPlayer(Transform spawn)
    {
        var player = new GameObject("Player") { tag = "Player", layer = LayerMask.NameToLayer(PlayerLayer) };
        player.transform.SetPositionAndRotation(spawn.position, spawn.rotation);
        var controller = player.AddComponent<CharacterController>();
        controller.height = 1.8f;
        controller.radius = 0.35f;
        controller.center = new Vector3(0f, 0.9f, 0f);
        controller.stepOffset = 0.35f;
        controller.slopeLimit = 50f;
        controller.skinWidth = 0.04f;

        var pivot = new GameObject("CameraPivot").transform;
        pivot.SetParent(player.transform, false);
        pivot.localPosition = new Vector3(0f, 1.62f, 0f);

        var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)) { tag = "MainCamera" };
        cameraObject.transform.SetParent(pivot, false);
        var camera = cameraObject.GetComponent<Camera>();
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = 60f; // chambers are 40 m apart and walled in; no need to draw the others
        camera.fieldOfView = 72f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.04f, 0.05f, 0.06f);
        UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
        cameraData.renderPostProcessing = true;
        cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        cameraObject.AddComponent<CameraShake>();

        var fps = player.AddComponent<FirstPersonController>();
        fps.cameraPivot = pivot;
        return fps;
    }

    static MatterHUD BuildHud(Chamber first)
    {
        var canvasObject = new GameObject("HUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        Transform root = canvasObject.transform;
        var middle = new Vector2(0.5f, 0.5f);

        // minimalist crosshair
        RectTransform crosshair = Rect("Crosshair", root, middle, Vector2.zero, new Vector2(40f, 40f));
        var tick = new Color(1f, 1f, 1f, 0.8f);
        Box("Dot", crosshair, middle, Vector2.zero, new Vector2(4f, 4f), new Color(1f, 1f, 1f, 0.95f));
        Box("Top", crosshair, middle, new Vector2(0f, 11f), new Vector2(2f, 9f), tick);
        Box("Bottom", crosshair, middle, new Vector2(0f, -11f), new Vector2(2f, 9f), tick);
        Box("Left", crosshair, middle, new Vector2(-11f, 0f), new Vector2(9f, 2f), tick);
        Box("Right", crosshair, middle, new Vector2(11f, 0f), new Vector2(9f, 2f), tick);

        // telemetry readout, top-left
        Image telemetryPanel = Box("TelemetryPanel", root, new Vector2(0f, 1f), new Vector2(24f, -24f), new Vector2(600f, 262f), PanelColor);
        Box("Accent", telemetryPanel.transform, new Vector2(0f, 1f), Vector2.zero, new Vector2(5f, 262f), Accent);
        Text telemetry = Label("TelemetryText", telemetryPanel.transform, 21, TextAnchor.UpperLeft, TextColor);
        Pad(telemetry.rectTransform, 24f, 16f, 16f, 12f);
        telemetry.text = "<b>MATTER TELEMETRY</b>\nNo matter synthesized yet.\nPress <b>T</b>, describe an object, press <b>Enter</b>.";

        // current chamber, top-right: the name only, no hints
        Image objectivePanel = Box("ObjectivePanel", root, new Vector2(1f, 1f), new Vector2(-24f, -24f), new Vector2(560f, 60f), PanelColor);
        Text objectiveTitle = Label("ObjectiveTitle", objectivePanel.transform, 22, TextAnchor.MiddleLeft, Amber);
        objectiveTitle.fontStyle = FontStyle.Bold;
        Pad(objectiveTitle.rectTransform, 20f, 16f, 6f, 6f);
        Text objectiveText = Label("ObjectiveText", objectivePanel.transform, 19, TextAnchor.UpperLeft, TextColor);
        Pad(objectiveText.rectTransform, 20f, 16f, 46f, 10f);
        objectiveTitle.text = first.title;
        objectiveText.text = "";

        // flash messages
        RectTransform messageRect = Rect("Message", root, new Vector2(0.5f, 1f), new Vector2(0f, -170f), new Vector2(1500f, 70f));
        Text message = Label("Text", messageRect, 34, TextAnchor.MiddleCenter, Color.white);
        message.fontStyle = FontStyle.Bold;
        message.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(2f, -2f);

        // synthesis status, just above the prompt bar
        RectTransform statusRect = Rect("Status", root, new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(1500f, 40f));
        Text status = Label("Text", statusRect, 22, TextAnchor.MiddleCenter, Accent);

        // controls hint
        RectTransform controlsRect = Rect("Controls", root, new Vector2(0f, 0f), new Vector2(24f, 16f), new Vector2(1200f, 30f));
        Label("Text", controlsRect, 17, TextAnchor.MiddleLeft, new Color(0.62f, 0.7f, 0.78f, 0.9f)).text =
            "WASD move · Space jump · Shift sprint · T synthesize · Enter fire · G re-fire · X recycle · Esc release mouse";

        // synthesis prompt bar, hidden until T
        Image bar = Box("PromptBar", root, new Vector2(0.5f, 0f), new Vector2(0f, 64f), new Vector2(1180f, 72f), new Color(0.03f, 0.05f, 0.07f, 0.92f));
        Box("Accent", bar.transform, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(6f, 72f), Accent);
        RectTransform tagRect = Rect("Tag", bar.transform, new Vector2(0f, 0.5f), new Vector2(22f, 0f), new Vector2(160f, 72f));
        Text tag = Label("Text", tagRect, 20, TextAnchor.MiddleLeft, Accent);
        tag.fontStyle = FontStyle.Bold;
        tag.text = "SYNTHESIZE ›";

        RectTransform fieldRect = Rect("PromptField", bar.transform, middle, Vector2.zero, Vector2.zero);
        Pad(fieldRect, 186f, 22f, 8f, 8f);
        var fieldBackground = fieldRect.gameObject.AddComponent<Image>();
        fieldBackground.color = new Color(1f, 1f, 1f, 0.04f);
        var field = fieldRect.gameObject.AddComponent<InputField>();
        Text fieldText = Label("Text", fieldRect, 25, TextAnchor.MiddleLeft, Color.white);
        Pad(fieldText.rectTransform, 14f, 14f, 0f, 0f);
        fieldText.supportRichText = false;
        Text placeholder = Label("Placeholder", fieldRect, 25, TextAnchor.MiddleLeft, new Color(0.55f, 0.62f, 0.7f, 0.85f));
        Pad(placeholder.rectTransform, 14f, 14f, 0f, 0f);
        placeholder.fontStyle = FontStyle.Italic;
        placeholder.text = "Describe matter to synthesize... (e.g. 500kg tungsten weight, rubber bouncy wedge)";
        field.textComponent = fieldText;
        field.placeholder = placeholder;
        field.targetGraphic = fieldBackground;
        field.lineType = InputField.LineType.SingleLine;
        field.characterLimit = 160;
        field.customCaretColor = true;
        field.caretColor = Accent;
        field.caretWidth = 2;
        field.selectionColor = new Color(0.2f, 0.75f, 1f, 0.35f);

        var hud = canvasObject.AddComponent<MatterHUD>();
        hud.promptBar = bar.gameObject;
        hud.promptField = field;
        hud.telemetryText = telemetry;
        hud.statusText = status;
        hud.messageText = message;
        hud.objectiveTitle = objectiveTitle;
        hud.objectiveText = objectiveText;
        bar.gameObject.SetActive(false);
        return hud;
    }

    static void BuildEventSystem()
    {
        var eventSystem = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
        eventSystem.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
#else
        eventSystem.AddComponent<StandaloneInputModule>();
#endif
    }

    // ------------------------------------------------------------------ UI helpers

    static Font UiFont => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

    static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    static Image Box(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size, Color color)
    {
        var image = Rect(name, parent, anchor, position, size).gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    static Text Label(string name, Transform parent, int fontSize, TextAnchor alignment, Color color)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        Pad(rect, 0f, 0f, 0f, 0f);
        var text = rect.gameObject.AddComponent<Text>();
        text.font = UiFont;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = color;
        text.supportRichText = true;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }

    static void Pad(RectTransform rect, float left, float right, float top, float bottom)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    /// <summary>World-space text board (depth-tested, unlike TextMesh).</summary>
    static Text WorldLabel(string name, Transform parent, Vector3 position, string content, int fontSize, Vector2 sizeMetres, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Canvas));
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        rect.sizeDelta = sizeMetres / LabelScale;
        rect.localScale = Vector3.one * LabelScale;
        rect.SetPositionAndRotation(position, Chamber.Inward); // readable from the entrance side
        Text text = Label("Text", rect, fontSize, TextAnchor.MiddleCenter, color);
        text.text = content;
        return text;
    }

    // ------------------------------------------------------------------ generated textures

    static Texture2D GeneratedTexture(string name, int size, Func<float, float, Color> pixel)
    {
        string path = $"{TextureFolder}/{name}.png";
        if (!File.Exists(path))
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                pixels[y * size + x] = pixel((x + 0.5f) / size, (y + 0.5f) / size);
            texture.SetPixels(pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;
            importer.anisoLevel = 4;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static readonly Vector2[] TieHoles = { new Vector2(0.25f, 0.25f), new Vector2(0.75f, 0.25f), new Vector2(0.25f, 0.75f), new Vector2(0.75f, 0.75f) };

    /// <summary>Board-formed concrete, one texture = 2 x 2 m: two 1 m pour panels with tie holes and air pockets.</summary>
    static Color ConcretePixel(float u, float v)
    {
        float n = 0.45f * PeriodicNoise(u, v, 4, 11) + 0.25f * PeriodicNoise(u, v, 8, 23)
                + 0.18f * PeriodicNoise(u, v, 24, 37) + 0.12f * PeriodicNoise(u, v, 96, 51);
        float value = 0.8f + (n - 0.5f) * 0.42f;
        if (Hash(Mathf.FloorToInt(u * 512f), Mathf.FloorToInt(v * 512f), 77) > 0.9965f) value *= 0.55f;
        float seamV = Mathf.Min(Mathf.Abs(v - 0.5f), Mathf.Min(v, 1f - v)) * 512f;
        float seamU = Mathf.Min(u, 1f - u) * 512f;
        if (seamV < 1.5f || seamU < 1.5f) value *= 0.82f;
        foreach (Vector2 hole in TieHoles)
        {
            float d = Vector2.Distance(new Vector2(u, v), hole) * 512f;
            if (d < 6f) value *= d < 4.5f ? 0.45f : 0.75f;
        }
        return new Color(value, value, value, 1f);
    }

    static Color HazardPixel(float u, float v) =>
        Mathf.Repeat((u + v) * 4f, 1f) < 0.5f ? new Color(0.95f, 0.74f, 0.06f) : new Color(0.07f, 0.07f, 0.07f);

    static float PeriodicNoise(float u, float v, int period, int seed)
    {
        float x = u * period, y = v * period;
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = x - x0, fy = y - y0;
        float sx = fx * fx * (3f - 2f * fx), sy = fy * fy * (3f - 2f * fy);
        int x1 = (x0 + 1) % period, y1 = (y0 + 1) % period;
        x0 %= period;
        y0 %= period;
        float a = Hash(x0, y0, seed), b = Hash(x1, y0, seed), c = Hash(x0, y1, seed), d = Hash(x1, y1, seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, sx), Mathf.Lerp(c, d, sx), sy);
    }

    static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xFFFFFF) / 16777215f;
        }
    }
}
