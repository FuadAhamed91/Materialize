"""
Materialize: Matter Synthesis - procedural brutalist test chambers.

Run inside Blender (Text Editor > Run Script, or through the Blender MCP):

    exec(open(r"<project>/ArtSource/build_chambers.py").read())

Builds ten scenes and exports each one to Assets/Art/Chambers/<scene>.fbx:

    Chamber_01_Counterweight  10 x 8 x 6 m, pit + pulley gantry + bucket, portcullis exit
    Chamber_02_TheLedge       12 x 8 m, sheer 5 m ledge, exit door on top
    Chamber_03_TheChasm       14 x 8 m, 4 m acid trench, copper terminal pillars, drawbridge
    Chamber_04_TheFlood       7 m of deep water between two platforms (buoyancy)
    Chamber_05_Shatterpoint   raised gallery, launch ramp, glass seal (momentum)
    Chamber_06_LiveWire       10 m electrified deck with tesla coils (insulation)
    Chamber_07_TheScales      beam balance with a sealed counterweight (precise mass)
    Chamber_08_Grip           35-degree slope over acid behind a containment field (friction)
    Chamber_09_MarbleRun      gutter through a porthole to a hidden button (shape)
    Chamber_10_Synthesis      live floor, then a three-bar gate: weight, circuit, momentum

Blender space: X = width, +Y = into the room (Unity +Z), Z = up. Every room's
interior starts at y = 0 (entrance wall) and is centred on x = 0.
Object names are read by Assets/Scripts/Editor/MaterializeSceneBuilder.cs,
so keep them stable when editing.
"""

import math
import os

import bmesh
import bpy
from mathutils import Matrix, Vector

PROJECT_ROOT = globals().get("PROJECT_ROOT", r"C:\Users\fuado\Development\Materialize")
EXPORT_DIR = os.path.join(PROJECT_ROOT, "Assets", "Art", "Chambers")
BLEND_PATH = os.path.join(PROJECT_ROOT, "ArtSource", "Materialize_Chambers.blend")

T = 0.4  # wall / slab thickness (m)
UV_TILE = 2.0  # metres per UV tile; matches the tiling of the Unity concrete texture

# name: (base colour RGBA, metallic, roughness, emission RGB or None)
MATERIALS = {
    "M_Concrete": ((0.42, 0.41, 0.39, 1.0), 0.0, 0.85, None),
    "M_ConcreteDark": ((0.22, 0.22, 0.22, 1.0), 0.0, 0.80, None),
    "M_DarkMetal": ((0.08, 0.085, 0.09, 1.0), 0.9, 0.45, None),
    "M_Copper": ((0.95, 0.50, 0.30, 1.0), 1.0, 0.30, None),
    "M_Acid": ((0.20, 1.00, 0.10, 1.0), 0.0, 0.10, (0.3, 1.0, 0.1)),
    "M_TerminalGlow": ((0.10, 0.60, 1.00, 1.0), 0.0, 0.30, (0.1, 0.6, 1.0)),
    "M_LightPanel": ((1.00, 0.97, 0.90, 1.0), 0.0, 0.50, (1.0, 0.97, 0.9)),
    "M_Hazard": ((0.90, 0.70, 0.05, 1.0), 0.0, 0.60, None),
    "M_Cable": ((0.12, 0.12, 0.13, 1.0), 0.6, 0.55, None),
    "M_Water": ((0.08, 0.32, 0.45, 0.7), 0.0, 0.05, None),
    "M_Glass": ((0.70, 0.90, 1.00, 0.25), 0.0, 0.02, None),
    "M_LiveGlow": ((0.30, 0.70, 1.00, 1.0), 0.0, 0.30, (0.3, 0.7, 1.0)),
    "M_Field": ((0.30, 0.75, 1.00, 0.2), 0.0, 0.10, (0.25, 0.8, 1.0)),
}


# --------------------------------------------------------------------------- helpers

def get_mat(name):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    color, metallic, roughness, emission = MATERIALS[name]
    if mat.node_tree is None:
        try:
            mat.use_nodes = True
        except AttributeError:
            pass
    mat.diffuse_color = color
    bsdf = mat.node_tree.nodes.get("Principled BSDF") if mat.node_tree else None
    if bsdf:
        bsdf.inputs["Base Color"].default_value = color
        bsdf.inputs["Metallic"].default_value = metallic
        bsdf.inputs["Roughness"].default_value = roughness
        if emission:
            bsdf.inputs["Emission Color"].default_value = (*emission, 1.0)
            bsdf.inputs["Emission Strength"].default_value = 4.0
    return mat


def _add_box(bm, mn, mx):
    res = bmesh.ops.create_cube(bm, size=1.0)
    c = (mn + mx) / 2
    s = mx - mn
    for v in res["verts"]:
        v.co = Vector((c.x + v.co.x * s.x, c.y + v.co.y * s.y, c.z + v.co.z * s.z))


def _add_cylinder(bm, center, radius, depth, axis="Z", segments=24):
    res = bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segments,
                                radius1=radius, radius2=radius, depth=depth)
    rot = {
        "Z": Matrix.Identity(4),
        "X": Matrix.Rotation(math.radians(90), 4, "Y"),
        "Y": Matrix.Rotation(math.radians(90), 4, "X"),
    }[axis]
    bmesh.ops.transform(bm, matrix=Matrix.Translation(center) @ rot, verts=res["verts"])
    for f in {f for v in res["verts"] for f in v.link_faces}:
        f.smooth = len(f.verts) == 4


def _add_prism(bm, profile, x0, x1):
    """Extrude a closed (y, z) polygon between x0 and x1: ramps, slopes, wedges."""
    left = [bm.verts.new((x0, y, z)) for y, z in profile]
    right = [bm.verts.new((x1, y, z)) for y, z in profile]
    faces = [bm.faces.new(left), bm.faces.new(list(reversed(right)))]
    for i in range(len(profile)):
        j = (i + 1) % len(profile)
        faces.append(bm.faces.new((left[i], right[i], right[j], left[j])))
    bmesh.ops.recalc_face_normals(bm, faces=faces)


def slope_quad(a, b, t0, t1, below, above):
    """(y, z) quad hugging the line a -> b between fractions t0..t1, from `below` under it to `above` over it."""
    (ay, az), (by, bz) = a, b
    dy, dz = by - ay, bz - az
    length = math.hypot(dy, dz)
    ny, nz = -dz / length, dy / length
    if nz < 0:
        ny, nz = -ny, -nz
    p = [(ay + dy * t, az + dz * t) for t in (t0, t1)]
    return [
        (p[0][0] - ny * below, p[0][1] - nz * below),
        (p[1][0] - ny * below, p[1][1] - nz * below),
        (p[1][0] + ny * above, p[1][1] + nz * above),
        (p[0][0] + ny * above, p[0][1] + nz * above),
    ]


def _add_cable(bm, p0, p1, radius, segments=8):
    p0, p1 = Vector(p0), Vector(p1)
    d = p1 - p0
    res = bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segments,
                                radius1=radius, radius2=radius, depth=d.length)
    rot = Vector((0.0, 0.0, 1.0)).rotation_difference(d.normalized()).to_matrix().to_4x4()
    bmesh.ops.transform(bm, matrix=Matrix.Translation((p0 + p1) / 2) @ rot, verts=res["verts"])


def _add_sphere(bm, center, radius):
    res = bmesh.ops.create_uvsphere(bm, u_segments=20, v_segments=12, radius=radius)
    bmesh.ops.translate(bm, vec=Vector(center), verts=res["verts"])
    for f in {f for v in res["verts"] for f in v.link_faces}:
        f.smooth = True


def _depsgraph():
    dg = bpy.context.evaluated_depsgraph_get()
    dg.update()
    return dg


class Builder:
    def __init__(self, scene):
        self.scene = scene
        self.coll = scene.collection

    def obj(self, name, bm, mat, origin=(0.0, 0.0, 0.0)):
        me = bpy.data.meshes.new(name)
        bm.to_mesh(me)
        bm.free()
        me.materials.append(get_mat(mat))
        ob = bpy.data.objects.new(name, me)
        ob.location = origin
        self.coll.objects.link(ob)
        return ob

    def box(self, name, mn, mx, mat, origin=None):
        mn, mx = Vector(mn), Vector(mx)
        o = Vector(origin) if origin is not None else (mn + mx) / 2
        bm = bmesh.new()
        _add_box(bm, mn - o, mx - o)
        return self.obj(name, bm, mat, o)

    def cyl(self, name, center, radius, depth, mat, axis="Z", segments=24, origin=None):
        center = Vector(center)
        o = Vector(origin) if origin is not None else center
        bm = bmesh.new()
        _add_cylinder(bm, center - o, radius, depth, axis, segments)
        return self.obj(name, bm, mat, o)

    def prism(self, name, profile, x0, x1, mat, origin=None):
        if origin is None:
            origin = ((x0 + x1) / 2, sum(p[0] for p in profile) / len(profile), sum(p[1] for p in profile) / len(profile))
        o = Vector(origin)
        bm = bmesh.new()
        _add_prism(bm, [(y - o.y, z - o.z) for y, z in profile], x0 - o.x, x1 - o.x)
        return self.obj(name, bm, mat, o)

    def bake(self, ob):
        """Apply every modifier on ob."""
        me = bpy.data.meshes.new_from_object(ob.evaluated_get(_depsgraph()))
        old = ob.data
        ob.modifiers.clear()
        ob.data = me
        if old.users == 0:
            bpy.data.meshes.remove(old)
        me.name = ob.name

    def subtract(self, ob, cutters):
        for i, cutter in enumerate(cutters):
            mod = ob.modifiers.new(f"cut{i}", "BOOLEAN")
            mod.operation = "DIFFERENCE"
            mod.solver = "EXACT"
            mod.object = cutter
        self.bake(ob)
        for cutter in cutters:
            me = cutter.data
            bpy.data.objects.remove(cutter, do_unlink=True)
            bpy.data.meshes.remove(me)

    def arch_cutters(self, y0, y1, half_width, spring_z):
        """Round-headed archway: straight jambs up to spring_z, semicircle above."""
        a = self.box("_cut_a", (-half_width, y0, -1.0), (half_width, y1, spring_z), "M_Concrete")
        b = self.cyl("_cut_b", (0.0, (y0 + y1) / 2, spring_z), half_width, y1 - y0, "M_Concrete",
                     axis="Y", segments=32)
        return [a, b]


# --------------------------------------------------------------------------- shared room parts

def side_walls(b, W, L, H, zmin=0.0):
    b.box("Wall_West", (-W / 2 - T, -T, zmin), (-W / 2, L + T, H), "M_Concrete")
    b.box("Wall_East", (W / 2, -T, zmin), (W / 2 + T, L + T, H), "M_Concrete")
    b.box("Wall_West_Plinth", (-W / 2, 0, 0), (-W / 2 + 0.04, L, 0.3), "M_ConcreteDark")
    b.box("Wall_East_Plinth", (W / 2 - 0.04, 0, 0), (W / 2, L, 0.3), "M_ConcreteDark")


def entrance_wall(b, W, H, hatch_z=0.0):
    b.box("Wall_South", (-W / 2, -T, 0), (W / 2, 0, H), "M_Concrete")
    b.box("EntryHatch", (-1.0, 0.0, hatch_z), (1.0, 0.08, hatch_z + 2.8), "M_DarkMetal")


def ceiling(b, W, L, H, beam_ys):
    b.box("Ceiling", (-W / 2 - T, -T, H), (W / 2 + T, L + T, H + T), "M_ConcreteDark")
    for i, y in enumerate(beam_ys):
        b.box(f"Ceiling_Beam{i + 1}", (-W / 2, y - 0.3, H - 0.5), (W / 2, y + 0.3, H), "M_ConcreteDark")
    for i, x in enumerate((-W / 4, W / 4)):
        b.box(f"LightStrip_{i + 1}", (x - 0.15, 1.0, H - 0.06), (x + 0.15, L - 1.0, H), "M_LightPanel")


def pilasters(b, W, H, ys, zmin=0.0):
    for i, y in enumerate(ys):
        b.box(f"Pilaster_W{i + 1}", (-W / 2, y - 0.3, zmin), (-W / 2 + 0.35, y + 0.3, H), "M_ConcreteDark")
        b.box(f"Pilaster_E{i + 1}", (W / 2 - 0.35, y - 0.3, zmin), (W / 2, y + 0.3, H), "M_ConcreteDark")


def north_wall_with_door(b, W, L, H, half_width, z0, z1, zmin=0.0):
    parts = {
        "L": b.box("Wall_North_L", (-W / 2, L, zmin), (-half_width, L + T, H), "M_Concrete"),
        "R": b.box("Wall_North_R", (half_width, L, zmin), (W / 2, L + T, H), "M_Concrete"),
        "Top": b.box("Wall_North_Top", (-half_width, L, z1), (half_width, L + T, H), "M_Concrete"),
    }
    if z0 > zmin:
        parts["Bottom"] = b.box("Wall_North_Bottom", (-half_width, L, zmin), (half_width, L + T, z0), "M_Concrete")
    # board-formed door surround
    b.box("Door_Frame_L", (-half_width - 0.35, L - 0.12, z0), (-half_width, L, z1 + 0.35), "M_ConcreteDark")
    b.box("Door_Frame_R", (half_width, L - 0.12, z0), (half_width + 0.35, L, z1 + 0.35), "M_ConcreteDark")
    b.box("Door_Frame_Top", (-half_width, L - 0.12, z1), (half_width, L, z1 + 0.35), "M_ConcreteDark")
    return parts


def sliding_exit_door(b, L, z0, z1):
    b.box("ExitDoor", (-1.1, L - 0.32, z0), (1.1, L - 0.14, z1 + 0.1), "M_DarkMetal", origin=(0.0, L - 0.23, z0))
    b.box("Exit_Light", (-0.5, L - 0.18, z1 + 0.45), (0.5, L - 0.12, z1 + 0.65), "M_TerminalGlow")


def vestibule(b, y0, z0, width=3.2, depth=3.0, height=3.6):
    hw = width / 2
    b.box("Vestibule_Floor", (-hw - T, y0, z0 - T), (hw + T, y0 + depth + T, z0), "M_ConcreteDark")
    b.box("Vestibule_WallW", (-hw - T, y0, z0), (-hw, y0 + depth, z0 + height), "M_Concrete")
    b.box("Vestibule_WallE", (hw, y0, z0), (hw + T, y0 + depth, z0 + height), "M_Concrete")
    b.box("Vestibule_End", (-hw - T, y0 + depth, z0), (hw + T, y0 + depth + T, z0 + height), "M_Concrete")
    b.box("Vestibule_Ceiling", (-hw - T, y0, z0 + height), (hw + T, y0 + depth + T, z0 + height + T),
          "M_ConcreteDark")
    b.box("Vestibule_Light", (-0.6, y0 + depth - 0.06, z0 + 2.2), (0.6, y0 + depth, z0 + 2.6), "M_TerminalGlow")


def hazard_band(b, name, mn, mx):
    b.box(name, (mn[0], mn[1], 0.0), (mx[0], mx[1], 0.015), "M_Hazard")


# --------------------------------------------------------------------------- chambers

def build_chamber_01(b):
    """Counterweight: fill the suspended bucket to lift the portcullis."""
    W, L, H = 8.0, 10.0, 6.0
    px0, px1, py0, py1, pd = -1.3, 1.3, 4.2, 6.8, 2.5  # pit footprint and depth
    pt = 0.3

    # floor slabs around the pit
    b.box("Floor_South", (-W / 2 - T, -T, -T), (W / 2 + T, py0, 0), "M_Concrete")
    b.box("Floor_North", (-W / 2 - T, py1, -T), (W / 2 + T, L + T, 0), "M_Concrete")
    b.box("Floor_West", (-W / 2 - T, py0, -T), (px0, py1, 0), "M_Concrete")
    b.box("Floor_East", (px1, py0, -T), (W / 2 + T, py1, 0), "M_Concrete")

    # pit
    b.box("Pit_WallS", (px0 - pt, py0 - pt, -pd - pt), (px1 + pt, py0, -T), "M_ConcreteDark")
    b.box("Pit_WallN", (px0 - pt, py1, -pd - pt), (px1 + pt, py1 + pt, -T), "M_ConcreteDark")
    b.box("Pit_WallW", (px0 - pt, py0, -pd - pt), (px0, py1, -T), "M_ConcreteDark")
    b.box("Pit_WallE", (px1, py0, -pd - pt), (px1 + pt, py1, -T), "M_ConcreteDark")
    b.box("Pit_Floor", (px0, py0, -pd - pt), (px1, py1, -pd), "M_ConcreteDark")
    hazard_band(b, "Pit_Hazard_S", (px0 - 0.25, py0 - 0.25), (px1 + 0.25, py0))
    hazard_band(b, "Pit_Hazard_N", (px0 - 0.25, py1), (px1 + 0.25, py1 + 0.25))
    hazard_band(b, "Pit_Hazard_W", (px0 - 0.25, py0), (px0, py1))
    hazard_band(b, "Pit_Hazard_E", (px1, py0), (px1 + 0.25, py1))

    # shell
    side_walls(b, W, L, H)
    entrance_wall(b, W, H)
    ceiling(b, W, L, H, beam_ys=(2.5, 8.0))
    pilasters(b, W, H, ys=(2.5, 8.0))

    # exit wall with a round-headed archway (2.4 m wide, 3.4 m tall)
    wall = b.box("Wall_North", (-W / 2, L, 0), (W / 2, L + T, H), "M_Concrete")
    b.subtract(wall, b.arch_cutters(L - 0.5, L + T + 0.5, 1.2, 2.2))
    frame = b.box("Arch_Frame", (-1.8, L - 0.15, 0), (1.8, L, 3.9), "M_ConcreteDark")
    b.subtract(frame, b.arch_cutters(L - 0.5, L + T + 0.5, 1.2, 2.2))
    b.box("Exit_Light", (-0.5, L - 0.06, 4.1), (0.5, L, 4.3), "M_TerminalGlow")

    # heavy portcullis sitting in the wall slot; origin at bottom centre so Unity can lift it
    bm = bmesh.new()
    for i in range(9):
        _add_cylinder(bm, Vector((-1.2 + i * 0.3, 0.0, 1.75)), 0.045, 3.5, "Z", 12)
    for z in (0.15, 1.0, 1.9, 2.8, 3.4):
        _add_box(bm, Vector((-1.25, -0.05, z - 0.05)), Vector((1.25, 0.05, z + 0.05)))
    b.obj("Portcullis", bm, "M_DarkMetal", origin=(0.0, L + T / 2, 0.0))

    # pulley gantry straddling the pit
    gy0, gy1 = 5.7, 6.0
    b.box("Gantry_PostW", (-2.15, gy0, 0), (-1.85, gy1, 5.5), "M_DarkMetal")
    b.box("Gantry_PostE", (1.85, gy0, 0), (2.15, gy1, 5.5), "M_DarkMetal")
    b.box("Gantry_Beam", (-2.3, gy0 - 0.05, 5.15), (2.3, gy1 + 0.05, 5.5), "M_DarkMetal")
    b.box("Gantry_BracketW", (-0.15, 5.7, 4.6), (-0.09, 6.0, 5.15), "M_DarkMetal")
    b.box("Gantry_BracketE", (0.09, 5.7, 4.6), (0.15, 6.0, 5.15), "M_DarkMetal")
    b.cyl("Pulley_Wheel", (0.0, 5.85, 4.7), 0.35, 0.12, "M_DarkMetal", axis="X", segments=32)
    b.cyl("Pulley_Axle", (0.0, 5.85, 4.7), 0.04, 0.34, "M_DarkMetal", axis="X", segments=12)
    # second pulley on the exit wall, cable runs over the top to the portcullis
    b.box("WallPulley_BracketW", (-0.15, 9.55, 4.62), (-0.09, L, 4.78), "M_DarkMetal")
    b.box("WallPulley_BracketE", (0.09, 9.55, 4.62), (0.15, L, 4.78), "M_DarkMetal")
    b.cyl("WallPulley_Wheel", (0.0, 9.6, 4.7), 0.35, 0.12, "M_DarkMetal", axis="X", segments=32)
    b.cyl("Rope_Over", (0.0, 7.725, 5.05), 0.025, 3.75, "M_Cable", axis="Y", segments=8)
    b.cyl("Rope_Wall", (0.0, 9.95, 4.3), 0.025, 0.8, "M_Cable", segments=8)

    # suspended industrial bucket (1.5 m diameter), origin at the bottom centre
    radius, height, bottom = 0.75, 1.0, -0.4
    bm = bmesh.new()
    res = bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=28,
                                radius1=radius, radius2=radius, depth=height)
    bmesh.ops.translate(bm, vec=Vector((0, 0, height / 2)), verts=res["verts"])
    bm.normal_update()
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.normal.z > 0.9], context="FACES_ONLY")
    for f in bm.faces:
        f.smooth = abs(f.normal.z) < 0.5
    bucket = b.obj("Bucket", bm, "M_DarkMetal", origin=(0.0, 5.5, bottom))
    solid = bucket.modifiers.new("wall", "SOLIDIFY")
    solid.thickness = 0.06
    solid.offset = -1.0
    solid.use_even_offset = True
    b.bake(bucket)
    # bail handle, merged into the bucket mesh so it moves with it
    pts = [(radius * math.cos(t), 0.0, height + radius * math.sin(t))
           for t in (math.pi * i / 16 for i in range(17))]
    cu = bpy.data.curves.new("BailCurve", "CURVE")
    cu.dimensions = "3D"
    cu.bevel_depth = 0.025
    cu.bevel_resolution = 2
    cu.use_fill_caps = True
    spline = cu.splines.new("POLY")
    spline.points.add(len(pts) - 1)
    for p, co in zip(spline.points, pts):
        p.co = (co[0], co[1], co[2], 1.0)
    tmp = bpy.data.objects.new("_bail", cu)
    b.coll.objects.link(tmp)
    bail_me = bpy.data.meshes.new_from_object(tmp.evaluated_get(_depsgraph()))
    bpy.data.objects.remove(tmp, do_unlink=True)
    bpy.data.curves.remove(cu)
    bm = bmesh.new()
    bm.from_mesh(bucket.data)
    bm.from_mesh(bail_me)
    bm.to_mesh(bucket.data)
    bm.free()
    bpy.data.meshes.remove(bail_me)

    # cable from the bail to the pulley; origin at the top so Unity can stretch it downwards
    bail_top = bottom + height + radius
    b.cyl("Rope_Bucket", (0.0, 5.5, (bail_top + 4.7) / 2), 0.025, 4.7 - bail_top, "M_Cable",
          segments=8, origin=(0.0, 5.5, 4.7))

    vestibule(b, L + T, 0.0)


def build_chamber_02(b):
    """The Ledge: a sheer 5 m wall to the exit, no stairs, no ladders."""
    W, L, H = 8.0, 12.0, 9.0
    LY, LH = 8.0, 5.0  # ledge front face and height

    b.box("Floor", (-W / 2 - T, -T, -T), (W / 2 + T, L + T, 0), "M_Concrete")
    b.box("Ledge", (-W / 2, LY, 0), (W / 2, L, LH), "M_Concrete")
    for i, x in enumerate((-3.0, -2.0, -1.0, 0.0, 1.0, 2.0, 3.0)):
        b.box(f"Ledge_Rib{i + 1}", (x - 0.12, LY - 0.15, 0), (x + 0.12, LY, LH - 0.05), "M_ConcreteDark")
    b.box("Ledge_Hazard", (-W / 2, LY, LH), (W / 2, LY + 0.25, LH + 0.015), "M_Hazard")

    side_walls(b, W, L, H)
    entrance_wall(b, W, H)
    ceiling(b, W, L, H, beam_ys=(2.5, 5.5, 9.5))
    pilasters(b, W, H, ys=(2.5, 5.5))

    north_wall_with_door(b, W, L, H, half_width=1.0, z0=LH, z1=LH + 3.0)
    sliding_exit_door(b, L, LH, LH + 3.0)
    vestibule(b, L + T, LH)


def build_chamber_03(b):
    """The Chasm: bridge the copper terminals across the acid trench."""
    W, L, H = 8.0, 14.0, 6.0
    cy0, cy1, cd = 5.0, 9.0, 3.0  # trench edges and depth

    b.box("Floor_South", (-W / 2 - T, -T, -T), (W / 2 + T, cy0, 0), "M_Concrete")
    b.box("Floor_North", (-W / 2 - T, cy1, -T), (W / 2 + T, L + T, 0), "M_Concrete")
    b.box("Chasm_WallS", (-W / 2, cy0 - 0.3, -cd - 0.3), (W / 2, cy0, -T), "M_ConcreteDark")
    b.box("Chasm_WallN", (-W / 2, cy1, -cd - 0.3), (W / 2, cy1 + 0.3, -T), "M_ConcreteDark")
    b.box("Chasm_Floor", (-W / 2, cy0, -cd - 0.3), (W / 2, cy1, -cd), "M_ConcreteDark")
    b.box("Acid_Surface", (-W / 2, cy0, -2.25), (W / 2, cy1, -2.2), "M_Acid")
    hazard_band(b, "Chasm_Hazard_S", (-W / 2, cy0 - 0.3), (W / 2, cy0))
    hazard_band(b, "Chasm_Hazard_N", (-W / 2, cy1), (W / 2, cy1 + 0.3))

    side_walls(b, W, L, H, zmin=-cd - 0.3)
    entrance_wall(b, W, H)
    ceiling(b, W, L, H, beam_ys=(2.5, 11.5))
    pilasters(b, W, H, ys=(2.5, 11.5))

    # copper terminal pillars, one on each side of the trench (5.2 m apart)
    tx = -2.8
    for tag, ty in (("A", cy0 - 0.6), ("B", cy1 + 0.6)):
        b.box(f"Terminal_{tag}_Pillar", (tx - 0.3, ty - 0.3, 0), (tx + 0.3, ty + 0.3, 1.1), "M_ConcreteDark")
        b.box(f"Terminal_{tag}_Node", (tx - 0.4, ty - 0.4, 1.1), (tx + 0.4, ty + 0.4, 1.25), "M_Copper")
        b.box(f"Terminal_{tag}_Light", (tx - 0.32, ty - 0.32, 0.9), (tx + 0.32, ty + 0.32, 1.0), "M_TerminalGlow")
    b.box("Conduit_A", (tx + 0.3, cy0 - 0.65, 0), (W / 2 - 0.4, cy0 - 0.55, 0.06), "M_Copper")
    b.box("Conduit_B", (tx + 0.3, cy1 + 0.55, 0), (0.0, cy1 + 0.65, 0.06), "M_Copper")

    # powered drawbridge, modelled lowered; origin on the far-side hinge
    b.box("Drawbridge", (0.0, cy0 - 0.15, -0.18), (2.4, cy1, 0.02), "M_DarkMetal", origin=(1.2, cy1, 0.0))

    north_wall_with_door(b, W, L, H, half_width=1.0, z0=0.0, z1=3.0)
    sliding_exit_door(b, L, 0.0, 3.0)
    vestibule(b, L + T, 0.0)


def shell(b, W, L, H, *, zmin=0.0, beam_ys=(), pilaster_ys=(), door_z=0.0, door_half=1.0, hatch_z=0.0,
          sliding_door=True):
    """Walls, ceiling, exit wall + door + vestibule shared by the later chambers."""
    side_walls(b, W, L, H, zmin=zmin)
    entrance_wall(b, W, H, hatch_z=hatch_z)
    ceiling(b, W, L, H, beam_ys=beam_ys)
    pilasters(b, W, H, ys=pilaster_ys)
    parts = north_wall_with_door(b, W, L, H, half_width=door_half, z0=door_z, z1=door_z + 3.0)
    if sliding_door:
        sliding_exit_door(b, L, door_z, door_z + 3.0)
    vestibule(b, L + T, door_z)
    return parts


def live_deck(b, W, y0, y1, coil_x, coil_ys):
    """Electrified metal deck with glowing bus bars and tesla coils."""
    b.box("LiveFloor", (-W / 2, y0, -T), (W / 2, y1, 0), "M_DarkMetal")
    for i in range(int(y1 - y0)):
        y = y0 + 0.5 + i
        b.box(f"LiveFloor_Glow{i + 1}", (-W / 2 + 0.3, y - 0.035, 0), (W / 2 - 0.3, y + 0.035, 0.012), "M_LiveGlow")
    hazard_band(b, "Live_Hazard_S", (-W / 2, y0 - 0.3), (W / 2, y0))
    hazard_band(b, "Live_Hazard_N", (-W / 2, y1), (W / 2, y1 + 0.3))
    n = 0
    for y in coil_ys:
        for x in (-coil_x, coil_x):
            n += 1
            b.cyl(f"Coil_{n}", (x, y, 1.0), 0.2, 2.0, "M_Copper", segments=16)
            b.cyl(f"Coil_Cap_{n}", (x, y, 2.1), 0.32, 0.18, "M_LiveGlow", segments=24)


def terminal(b, tag, x, y):
    b.box(f"Terminal_{tag}_Pillar", (x - 0.3, y - 0.3, 0), (x + 0.3, y + 0.3, 1.1), "M_ConcreteDark")
    b.box(f"Terminal_{tag}_Node", (x - 0.4, y - 0.4, 1.1), (x + 0.4, y + 0.4, 1.25), "M_Copper")
    b.box(f"Terminal_{tag}_Light", (x - 0.32, y - 0.32, 0.9), (x + 0.32, y + 0.32, 1.0), "M_TerminalGlow")


def build_chamber_04(b):
    """The Flood: seven metres of deep water. Only what floats will carry you."""
    W, L, H = 10.0, 16.0, 7.0
    p0, p1, depth, water = 4.0, 11.0, 4.0, -0.6
    b.box("Floor_South", (-W / 2 - T, -T, -T), (W / 2 + T, p0, 0), "M_Concrete")
    b.box("Floor_North", (-W / 2 - T, p1, -T), (W / 2 + T, L + T, 0), "M_Concrete")
    b.box("Pool_WallS", (-W / 2, p0 - 0.3, -depth - 0.3), (W / 2, p0, -T), "M_ConcreteDark")
    b.box("Pool_WallN", (-W / 2, p1, -depth - 0.3), (W / 2, p1 + 0.3, -T), "M_ConcreteDark")
    b.box("Pool_Floor", (-W / 2, p0, -depth - 0.3), (W / 2, p1, -depth), "M_ConcreteDark")
    b.box("Water_Surface", (-W / 2, p0, water - 0.05), (W / 2, p1, water), "M_Water")
    hazard_band(b, "Pool_Hazard_S", (-W / 2, p0 - 0.3), (W / 2, p0))
    hazard_band(b, "Pool_Hazard_N", (-W / 2, p1), (W / 2, p1 + 0.3))
    for i, (x, y) in enumerate(((-4.2, 3.3), (4.2, 3.3), (-4.2, 11.7), (4.2, 11.7))):
        b.cyl(f"Bollard_{i + 1}", (x, y, 0.35), 0.16, 0.7, "M_DarkMetal", segments=16)
    shell(b, W, L, H, zmin=-depth - 0.3, beam_ys=(2.0, 14.0), pilaster_ys=(2.0, 14.0))


def build_chamber_05(b):
    """Shatterpoint: a glass seal that only yields to real momentum."""
    W, L, H = 8.0, 18.0, 8.0
    gallery, gz, ramp_end, glass = 4.0, 4.0, 12.0, 14.0
    b.box("Floor", (-W / 2 - T, -T, -T), (W / 2 + T, L + T, 0), "M_Concrete")
    b.box("Gallery", (-W / 2, 0, 0), (W / 2, gallery, gz), "M_Concrete")
    b.box("Gallery_Hazard", (-W / 2, gallery - 0.3, gz), (W / 2, gallery, gz + 0.015), "M_Hazard")
    b.prism("Launch_Ramp", [(gallery, 0.0), (ramp_end, 0.0), (gallery, gz)], -W / 2, W / 2, "M_Concrete")
    b.box("Seal_Frame_Top", (-W / 2, glass - 0.25, 3.2), (W / 2, glass + 0.25, H), "M_ConcreteDark")
    b.box("Glass_Seal", (-W / 2, glass - 0.06, 0), (W / 2, glass + 0.06, 3.2), "M_Glass")
    b.box("Seal_Sill", (-W / 2, glass - 0.25, 0), (W / 2, glass + 0.25, 0.03), "M_DarkMetal")
    shell(b, W, L, H, beam_ys=(2.0, 16.0), pilaster_ys=(16.0,), hatch_z=gz)


def build_chamber_06(b):
    """Live Wire: ten metres of electrified deck between you and the exit."""
    W, L, H = 8.0, 16.0, 6.0
    l0, l1 = 3.0, 13.0
    b.box("Floor_South", (-W / 2 - T, -T, -T), (W / 2 + T, l0, 0), "M_Concrete")
    b.box("Floor_North", (-W / 2 - T, l1, -T), (W / 2 + T, L + T, 0), "M_Concrete")
    live_deck(b, W, l0, l1, coil_x=3.5, coil_ys=(5.5, 10.5))
    shell(b, W, L, H, beam_ys=(1.5, 14.5), pilaster_ys=(1.5, 14.5))


def build_chamber_07(b):
    """The Scales: a beam balance against a sealed counterweight of unknown mass."""
    W, L, H = 10.0, 14.0, 8.0
    cy, pz, arm, tray_z = 7.0, 5.2, 3.2, 1.2
    b.box("Floor", (-W / 2 - T, -T, -T), (W / 2 + T, L + T, 0), "M_Concrete")
    b.box("Scale_Base", (-0.9, cy - 0.9, 0), (0.9, cy + 0.9, 0.3), "M_ConcreteDark")
    b.box("Scale_Pillar", (-0.25, cy - 0.25, 0.3), (0.25, cy + 0.25, pz - 0.25), "M_DarkMetal")
    b.cyl("Scale_Pivot", (0.0, cy, pz), 0.2, 0.7, "M_Copper", axis="Y", segments=24)
    b.box("Scale_Light", (-0.28, cy - 0.28, 2.6), (0.28, cy + 0.28, 2.75), "M_TerminalGlow")
    b.box("Scale_Beam", (-arm - 0.25, cy - 0.12, pz - 0.16), (arm + 0.25, cy + 0.12, pz + 0.16), "M_DarkMetal",
          origin=(0.0, cy, pz))
    base = tray_z - pz  # tray floor relative to the hang point
    for name, x, weighted in (("Pan_Sensor", -arm, False), ("Pan_Weighted", arm, True)):
        bm = bmesh.new()
        _add_box(bm, Vector((-0.8, -0.8, base)), Vector((0.8, 0.8, base + 0.08)))
        for mn, mx in (((-0.8, -0.8), (0.8, -0.72)), ((-0.8, 0.72), (0.8, 0.8)),
                       ((-0.8, -0.72), (-0.72, 0.72)), ((0.72, -0.72), (0.8, 0.72))):
            _add_box(bm, Vector((mn[0], mn[1], base)), Vector((mx[0], mx[1], base + 0.38)))
        for sx, sy in ((-0.74, -0.74), (0.74, -0.74), (0.74, 0.74), (-0.74, 0.74)):
            _add_cable(bm, (0.0, 0.0, -0.12), (sx, sy, base + 0.38), 0.02)
        _add_box(bm, Vector((-0.09, -0.09, -0.24)), Vector((0.09, 0.09, 0.0)))
        if weighted:  # the sealed counterweight
            _add_box(bm, Vector((-0.32, -0.32, base + 0.08)), Vector((0.32, 0.32, base + 0.72)))
        b.obj(name, bm, "M_DarkMetal", origin=(x, cy, pz))
    # viewing step in front of the sensor pan: from the floor you can't see into it once it rises
    for name, y0, y1, top in (("Viewing_Stair1", cy - 4.4, cy - 3.8, 0.3), ("Viewing_Stair2", cy - 3.8, cy - 3.2, 0.6),
                              ("Viewing_Stair3", cy - 3.2, cy - 2.6, 0.9), ("Viewing_Step", cy - 2.6, cy - 1.4, 1.2)):
        b.box(name, (-arm - 1.4, y0, 0), (-arm + 1.4, y1, top), "M_ConcreteDark")
    shell(b, W, L, H, beam_ys=(2.0, 12.0), pilaster_ys=(2.0, 12.0))


def build_chamber_08(b):
    """Grip: a 35-degree slope over acid behind a containment field."""
    W, L, H = 8.0, 17.0, 9.0
    field_y, g0, g1, top_z = 5.0, 5.4, 6.6, 5.6
    slope_end = g1 + top_z / math.tan(math.radians(35.0))
    b.box("Floor_South", (-W / 2 - T, -T, -T), (W / 2 + T, g0, 0), "M_Concrete")
    b.box("Gutter_WallS", (-W / 2, g0 - 0.3, -1.6), (W / 2, g0, -T), "M_ConcreteDark")
    b.box("Gutter_Floor", (-W / 2, g0, -1.6), (W / 2, g1, -1.3), "M_ConcreteDark")
    b.box("Acid_Gutter", (-W / 2, g0, -1.05), (W / 2, g1, -1.0), "M_Acid")
    hazard_band(b, "Gutter_Hazard", (-W / 2, g0 - 0.3), (W / 2, g0))
    b.prism("Grip_Slope", [(g1, -1.6), (L + T, -1.6), (L + T, top_z), (slope_end, top_z), (g1, 0.0)],
            -W / 2, W / 2, "M_Concrete")
    b.prism("Grip_Plate", slope_quad((g1, 0.0), (slope_end, top_z), 0.36, 0.64, 0.0, 0.03), -1.3, 1.3, "M_Hazard")
    b.box("Field_EmitterW", (-W / 2, field_y - 0.2, 0), (-W / 2 + 0.25, field_y + 0.2, H), "M_DarkMetal")
    b.box("Field_EmitterE", (W / 2 - 0.25, field_y - 0.2, 0), (W / 2, field_y + 0.2, H), "M_DarkMetal")
    b.box("Containment_Field", (-W / 2 + 0.25, field_y - 0.03, 0), (W / 2 - 0.25, field_y + 0.03, H), "M_Field")
    shell(b, W, L, H, zmin=-1.6, beam_ys=(2.5,), pilaster_ys=(2.5,), door_z=top_z)


def build_chamber_09(b):
    """Marble Run: a narrow gutter carries matter through the wall to a hidden button."""
    W, L, H = 8.0, 10.0, 6.0
    gx, y0, z0 = -3.0, 1.8, 1.25
    y1, z1 = L + T + 2.4, 0.35

    def channel_z(y):
        return z0 + (z1 - z0) * (y - y0) / (y1 - y0)

    b.box("Floor", (-W / 2 - T, -T, -T), (W / 2 + T, L + T, 0), "M_Concrete")
    wall = shell(b, W, L, H, beam_ys=(5.0,), pilaster_ys=(5.0,))

    # U-channel falling from the catch end, through a porthole, to the button
    a, c = (y0, z0), (y1, z1)
    bm = bmesh.new()
    _add_prism(bm, slope_quad(a, c, 0.0, 1.0, 0.06, 0.0), gx - 0.36, gx + 0.36)
    _add_prism(bm, slope_quad(a, c, 0.0, 1.0, 0.0, 0.32), gx - 0.36, gx - 0.30)
    _add_prism(bm, slope_quad(a, c, 0.0, 1.0, 0.0, 0.32), gx + 0.30, gx + 0.36)
    _add_box(bm, Vector((gx - 0.36, y0 - 0.08, z0 - 0.06)), Vector((gx + 0.36, y0, z0 + 0.34)))
    _add_box(bm, Vector((gx - 0.36, y1, z1 - 0.06)), Vector((gx + 0.36, y1 + 0.08, z1 + 0.34)))
    b.obj("Gutter", bm, "M_DarkMetal")
    for i, y in enumerate((3.0, 6.0, 9.0)):
        b.box(f"Gutter_Post{i + 1}", (gx - 0.06, y - 0.06, 0.0), (gx + 0.06, y + 0.06, channel_z(y) - 0.06),
              "M_DarkMetal")

    hole_y = L + T / 2
    cutter = b.cyl("_cut_hole", (gx, hole_y, channel_z(hole_y) + 0.32), 0.45, T + 1.0, "M_Concrete",
                   axis="Y", segments=32)
    b.subtract(wall["L"], [cutter])

    # sealed machine room behind the exit wall, holding the button
    my0, my1 = L + T, L + T + 3.0
    b.box("Machine_Floor", (-W / 2 - T, my0, -T), (-2.0, my1 + T, 0.0), "M_ConcreteDark")
    b.box("Machine_WallW", (-W / 2 - T, my0, 0.0), (-W / 2, my1, 2.8), "M_Concrete")
    b.box("Machine_WallEnd", (-W / 2 - T, my1, 0.0), (-2.0, my1 + T, 2.8), "M_Concrete")
    b.box("Machine_Ceiling", (-W / 2 - T, my0, 2.8), (-2.0, my1 + T, 2.8 + T), "M_ConcreteDark")
    bz = channel_z(y1 - 0.35)
    b.box("Button_Glow", (gx - 0.28, y1 - 0.7, bz), (gx + 0.28, y1 - 0.05, bz + 0.02), "M_TerminalGlow")


def build_chamber_10(b):
    """Synthesis: a live floor, then three locks on one gate, each opened by a different law."""
    W, L, H = 12.0, 22.0, 8.0
    l0, l1 = 3.0, 9.0
    b.box("Floor_South", (-W / 2 - T, -T, -T), (W / 2 + T, l0, 0), "M_Concrete")
    b.box("Floor_North", (-W / 2 - T, l1, -T), (W / 2 + T, L + T, 0), "M_Concrete")
    live_deck(b, W, l0, l1, coil_x=5.5, coil_ys=(4.5, 7.5))

    # lock 1: weight
    b.box("Weigh_Plate", (-5.0, 11.0, 0.0), (-3.0, 13.0, 0.03), "M_Hazard")
    # lock 2: circuit, terminals 7.4 m apart
    terminal(b, "A", 4.4, 10.6)
    terminal(b, "B", 4.4, 18.0)
    # lock 3: momentum, a launch ramp aimed at a glass core
    b.prism("Kick_Ramp", [(13.5, 0.0), (19.5, 0.0), (19.5, 3.0)], -1.5, 1.5, "M_Concrete")
    b.box("Kick_Deck", (-1.5, 19.5, 0.0), (1.5, 21.0, 3.0), "M_Concrete")
    b.box("Core_Pedestal", (-0.7, 11.4, 0.0), (0.7, 12.8, 0.3), "M_DarkMetal")
    b.box("Core_Glass", (-0.6, 11.5, 0.3), (0.6, 12.7, 2.1), "M_Glass")
    bm = bmesh.new()
    _add_sphere(bm, (0.0, 0.0, 0.0), 0.35)
    b.obj("Core_Orb", bm, "M_LiveGlow", origin=(0.0, 12.1, 1.2))

    # the gate: three bars, one per lock
    for i, x in enumerate((-0.7, 0.0, 0.7)):
        b.box(f"Lock_Bar_{i + 1}", (x - 0.13, L - 0.45, 0.0), (x + 0.13, L - 0.19, 3.15), "M_DarkMetal",
              origin=(x, L - 0.32, 0.0))
        b.box(f"Lock_Light_{i + 1}", (x - 0.2, L - 0.12, 3.5), (x + 0.2, L - 0.06, 3.75), "M_TerminalGlow")
    shell(b, W, L, H, beam_ys=(1.5, 10.0, 20.5), pilaster_ys=(1.5, 10.0, 20.5), door_half=1.2, sliding_door=False)


# --------------------------------------------------------------------------- pipeline

def project_uvs(scene):
    """World-space box projection so concrete tiles at a constant texel density."""
    for ob in scene.objects:
        if ob.type != "MESH":
            continue
        mw = ob.matrix_world
        rot = mw.to_3x3()
        bm = bmesh.new()
        bm.from_mesh(ob.data)
        bm.normal_update()
        uv = bm.loops.layers.uv.verify()
        for f in bm.faces:
            n = rot @ f.normal
            axis = max(range(3), key=lambda i: abs(n[i]))
            for loop in f.loops:
                p = mw @ loop.vert.co
                u, v = ((p.y, p.z), (p.x, p.z), (p.x, p.y))[axis]
                loop[uv].uv = (u / UV_TILE, v / UV_TILE)
        bm.to_mesh(ob.data)
        bm.free()


def reset_scene(name, fallback):
    old = bpy.data.scenes.get(name)
    if old:
        for win in bpy.context.window_manager.windows:
            if win.scene == old:
                win.scene = fallback
        for ob in list(old.objects):
            bpy.data.objects.remove(ob, do_unlink=True)
        bpy.data.scenes.remove(old)
    scene = bpy.data.scenes.new(name)
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    return scene


def export_fbx(path):
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=False,
        use_visible=False,
        use_active_collection=False,
        object_types={"MESH", "EMPTY"},
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z",
        axis_up="Y",
        bake_space_transform=True,
        use_mesh_modifiers=True,
        use_triangles=True,  # boolean-cut walls have concave n-gons Unity would discard
        mesh_smooth_type="FACE",
        add_leaf_bones=False,
        bake_anim=False,
        path_mode="AUTO",
    )


CHAMBERS = (
    ("Chamber_01_Counterweight", build_chamber_01),
    ("Chamber_02_TheLedge", build_chamber_02),
    ("Chamber_03_TheChasm", build_chamber_03),
    ("Chamber_04_TheFlood", build_chamber_04),
    ("Chamber_05_Shatterpoint", build_chamber_05),
    ("Chamber_06_LiveWire", build_chamber_06),
    ("Chamber_07_TheScales", build_chamber_07),
    ("Chamber_08_Grip", build_chamber_08),
    ("Chamber_09_MarbleRun", build_chamber_09),
    ("Chamber_10_Synthesis", build_chamber_10),
)


def build_all(save_blend=True):
    os.makedirs(EXPORT_DIR, exist_ok=True)
    os.makedirs(os.path.dirname(BLEND_PATH), exist_ok=True)
    win = bpy.context.window_manager.windows[0]
    chamber_names = {name for name, _ in CHAMBERS}
    home = next((s for s in bpy.data.scenes if s.name not in chamber_names), None) \
        or bpy.data.scenes.new("Scene")
    report = {}
    for name, build in CHAMBERS:
        scene = reset_scene(name, home)
        win.scene = scene
        with bpy.context.temp_override(window=win, scene=scene):
            build(Builder(scene))
            bpy.context.view_layer.update()
            project_uvs(scene)
            path = os.path.join(EXPORT_DIR, name + ".fbx")
            export_fbx(path)
        report[name] = {"objects": len(scene.objects), "fbx": path}
    for me in [m for m in bpy.data.meshes if m.users == 0]:
        bpy.data.meshes.remove(me)
    win.scene = bpy.data.scenes[CHAMBERS[0][0]]
    if save_blend:
        bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH, check_existing=False)
        report["blend"] = BLEND_PATH
    return report


result = build_all()
