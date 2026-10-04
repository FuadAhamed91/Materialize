using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Smoke checks for the assembled scene: layers, colliders and PhysicsMaterials, the player rig,
/// the Matter Gun and inventory wiring and every puzzle hook-up. Menu: Materialize > Validate Scene.
/// </summary>
public static class MaterializeValidator
{
    [MenuItem("Materialize/Validate Scene", priority = 1)]
    public static string Validate()
    {
        var report = new StringBuilder();
        int failures = 0;
        void Fail(string message) { failures++; report.AppendLine("FAIL  " + message); }
        void Pass(string message) => report.AppendLine("PASS  " + message);

        foreach (string layer in new[] { MaterializeSceneBuilder.EnvironmentLayer, MaterializeSceneBuilder.MatterLayer, MaterializeSceneBuilder.PlayerLayer })
        {
            int index = LayerMask.NameToLayer(layer);
            if (index < 0) Fail($"layer '{layer}' is missing");
            else Pass($"layer '{layer}' = {index}");
        }

        int environment = LayerMask.NameToLayer(MaterializeSceneBuilder.EnvironmentLayer);
        var colliders = Object.FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var solid = colliders.Where(c => c.gameObject.layer == environment && !c.isTrigger).ToList();
        var noMaterial = solid.Where(c => c.sharedMaterial == null).Select(c => c.name).ToList();
        if (noMaterial.Count > 0)
            Fail($"{noMaterial.Count} environment colliders have no PhysicsMaterial: {string.Join(", ", noMaterial.Take(8))}");
        else
            Pass($"{solid.Count} environment colliders, every one with a PhysicsMaterial ({string.Join(", ", solid.Select(c => c.sharedMaterial.name).Distinct().OrderBy(n => n))})");

        var bare = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(r => r.gameObject.layer == environment && r.GetComponent<Collider>() == null && !MaterializeSceneBuilder.IsDecorative(r.name))
            .Select(r => r.name).ToList();
        if (bare.Count > 0) Fail($"{bare.Count} structural meshes have no collider: {string.Join(", ", bare.Take(8))}");
        else Pass("every floor, wall, ledge and fixture mesh has a collider (decorative strips, ropes and lights excluded)");

        int walkable = solid.Count(c => c.name.StartsWith("Floor") || c.name == "Ledge" || c.name.Contains("Vestibule_Floor") || c.name.StartsWith("Pit_Floor") || c.name.StartsWith("Chasm_Floor"));
        int walls = solid.Count(c => c.name.StartsWith("Wall") || c.name.Contains("Vestibule_Wall") || c.name.Contains("Pit_Wall") || c.name.Contains("Chasm_Wall"));
        Pass($"{walkable} floor/ledge colliders and {walls} wall colliders");

        var player = Object.FindFirstObjectByType<FirstPersonController>();
        if (player == null)
        {
            Fail("no FirstPersonController in the scene");
        }
        else
        {
            var controller = player.GetComponent<CharacterController>();
            if (controller == null) Fail("player has no CharacterController");
            else if (!player.CompareTag("Player")) Fail("player is not tagged Player");
            else if (player.gameObject.layer != LayerMask.NameToLayer(MaterializeSceneBuilder.PlayerLayer)) Fail("player is not on the Player layer");
            else if (player.cameraPivot == null) Fail("player has no camera pivot");
            else Pass($"player rig: CharacterController (height {controller.height}, radius {controller.radius}), mouse look pivot, layer Player");
        }

        var gun = Object.FindFirstObjectByType<MatterGun>();
        if (gun == null) Fail("no MatterGun in the scene");
        else if (gun.inventory == null || gun.hud == null || gun.player == null || gun.baseMaterial == null || gun.ghostMaterial == null)
            Fail("MatterGun has unassigned references");
        else
        {
            bool ignoresPlayer = (gun.aimMask.value & (1 << LayerMask.NameToLayer(MaterializeSceneBuilder.PlayerLayer))) == 0;
            if (!ignoresPlayer) Fail("MatterGun aim mask still hits the Player layer");
            else Pass($"MatterGun on '{gun.name}' wired to the inventory, HUD, URP base material '{gun.baseMaterial.name}' and placement preview '{gun.ghostMaterial.name}'");
        }

        foreach (var plate in Object.FindObjectsByType<WeightTriggerPlate>(FindObjectsSortMode.None))
        {
            var zone = plate.GetComponent<BoxCollider>();
            if (plate.targets == null || plate.targets.Length == 0 || plate.targets.Any(t => t == null)) Fail($"WeightTriggerPlate '{plate.name}' has no door targets");
            else if (zone == null || !zone.isTrigger) Fail($"WeightTriggerPlate '{plate.name}' needs a trigger BoxCollider");
            else Pass($"WeightTriggerPlate '{plate.transform.parent?.name}/{plate.name}' needs {plate.requiredMass:0} kg -> opens {string.Join(", ", plate.targets.Select(t => t.name))}");
        }

        foreach (var circuit in Object.FindObjectsByType<ElectricalBridge>(FindObjectsSortMode.None))
        {
            if (circuit.nodeA == null || circuit.nodeB == null) Fail($"ElectricalBridge '{circuit.name}' is missing a terminal node");
            else if (circuit.targets == null || circuit.targets.Length == 0) Fail($"ElectricalBridge '{circuit.name}' has no targets");
            else
            {
                float gap = Vector3.Distance(circuit.nodeA.bounds.center, circuit.nodeB.bounds.center);
                Pass($"ElectricalBridge {circuit.nodeA.name} <-> {circuit.nodeB.name} ({gap:0.0} m apart) -> opens {string.Join(", ", circuit.targets.Select(t => t.name))}");
            }
        }

        var doors = Object.FindObjectsByType<ChamberDoor>(FindObjectsSortMode.None);
        var looseDoors = doors.Where(d => d.GetComponent<Collider>() == null || d.GetComponent<Rigidbody>() == null || !d.GetComponent<Rigidbody>().isKinematic).Select(d => d.name).ToList();
        if (looseDoors.Count > 0) Fail($"doors without a collider or kinematic Rigidbody: {string.Join(", ", looseDoors)}");
        else Pass($"{doors.Length} ChamberDoor movers, each with a collider, PhysicsMaterial and kinematic Rigidbody");

        var zones = Object.FindObjectsByType<ZoneTrigger>(FindObjectsSortMode.None);
        if (zones.Length == 0) Fail("no ZoneTrigger on the ledge");
        else Pass($"{zones.Length} ledge-top ZoneTrigger(s) -> {string.Join(", ", zones.SelectMany(z => z.targets).Select(t => t.name))}");

        var exits = Object.FindObjectsByType<ChamberExit>(FindObjectsSortMode.None);
        int linked = exits.Count(e => e.nextSpawn != null && e.nextLoadout != null && e.inventory != null);
        int expected = MaterializeSceneBuilder.ChamberCount;
        if (exits.Length != expected || linked != expected - 1)
            Fail($"expected {expected} chamber exits ({expected - 1} linked onward with the next inventory), found {exits.Length} ({linked} linked)");
        else Pass($"{expected} chamber exits chained 01 -> {expected:00} -> complete, each loading the next chamber's inventory");

        var loadouts = Object.FindObjectsByType<ChamberLoadout>(FindObjectsSortMode.None).OrderBy(l => l.name).ToList();
        var inventory = Object.FindFirstObjectByType<Inventory>();
        var broken = loadouts.Where(l => l.items == null || l.items.Length == 0 || l.items.Length > 9 ||
            l.items.Any(item => item == null || item.config == null || item.count < 1 || string.IsNullOrEmpty(item.name))).Select(l => l.name).ToList();
        if (loadouts.Count != expected) Fail($"expected {expected} chamber inventories, found {loadouts.Count}");
        else if (broken.Count > 0) Fail($"inventories that are empty, over 9 slots or hold a broken item: {string.Join(", ", broken)}");
        else if (inventory == null || inventory.loadout == null) Fail("no Inventory with a starting loadout");
        else Pass($"{loadouts.Count} chamber inventories ({string.Join(" / ", loadouts.Select(l => l.items.Length))} slots, {loadouts.Sum(l => l.items.Sum(i => i.count))} items); the run starts with {inventory.loadout.name}");

        var flow = Object.FindFirstObjectByType<LevelFlow>();
        if (flow == null || flow.player == null || flow.inventory == null || flow.restartButton == null || flow.restartFill == null)
            Fail("LevelFlow (level restart) is not wired");
        else if (flow.chambers.Length != expected || flow.spawns.Length != expected || flow.titles.Length != expected ||
                 flow.chambers.Any(c => c == null) || flow.spawns.Any(t => t == null))
            Fail($"LevelFlow needs {expected} chambers, spawns and titles");
        else Pass($"level restart: R or the RESTART LEVEL button returns to the current chamber's entrance ({flow.chambers.Length} chambers)");

        int water = Object.FindObjectsByType<BuoyancyVolume>(FindObjectsSortMode.None).Length;
        var seals = Object.FindObjectsByType<ImpactSeal>(FindObjectsSortMode.None);
        var decks = Object.FindObjectsByType<ElectrifiedFloor>(FindObjectsSortMode.None);
        var scales = Object.FindObjectsByType<BalanceScale>(FindObjectsSortMode.None);
        if (water < 1) Fail("no BuoyancyVolume (The Flood)");
        else Pass($"{water} water volume(s) with buoyancy");
        if (seals.Length < 2 || seals.Any(s => s.targets == null || s.targets.Length == 0)) Fail("glass seals missing or unwired");
        else Pass($"{seals.Length} momentum seals ({string.Join(", ", seals.Select(s => $"{s.name} ≥ {s.momentumThreshold:0} kg·m/s"))})");
        if (decks.Length < 2 || decks.Any(d => d.surface == null)) Fail("electrified decks missing a surface");
        else Pass($"{decks.Length} electrified decks");
        if (scales.Length != 1 || scales[0].sensorPan == null || scales[0].weightedPan == null || scales[0].beam == null) Fail("balance scale is not wired");
        else Pass($"balance scale wired (sealed counterweight {scales[0].counterweightMass:0} kg, tolerance ±{scales[0].tolerance:P0})");

        var hazards = Object.FindObjectsByType<HazardZone>(FindObjectsSortMode.None);
        if (hazards.Any(h => !h.GetComponent<Collider>().isTrigger)) Fail("a HazardZone collider is not a trigger");
        else Pass($"{hazards.Length} hazard volumes ({string.Join(", ", hazards.Select(h => h.name))})");

        var hud = Object.FindFirstObjectByType<MatterHUD>();
        if (hud == null || hud.inventory == null || hud.telemetryText == null || hud.slots == null || hud.slots.Length < 9 ||
            hud.slots.Any(slot => slot == null || slot.root == null || slot.frame == null || slot.icon == null || slot.count == null))
            Fail("HUD is missing its hotbar, inventory or telemetry");
        else Pass($"HUD: crosshair, telemetry, {hud.slots.Length}-slot hotbar bound to the inventory");

        string summary = $"Materialize validation: {(failures == 0 ? "ALL CHECKS PASSED" : failures + " FAILURE(S)")}\n{report}";
        if (failures == 0) Debug.Log(summary);
        else Debug.LogError(summary);
        return summary;
    }
}
