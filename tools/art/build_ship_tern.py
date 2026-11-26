"""Tern light freighter. Blender background, original geometry in game metres."""
import hashlib
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

sys.path.insert(0, str(Path(__file__).resolve().parent))
import build_ship as ship

art = ship.art
ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "tools/art/sources"
OUTPUT = ROOT / "client/assets/models/ship_tern.glb"
art.SIZE = 768
art.TEXTURES = SOURCE / "tern_textures"


def chassis():
    loft, box, beam = ship.loft, ship.box, ship.beam
    loft("Frame", [(-11.7, 1.9, .67, -.8), (-8.4, 2.6, .85, -.8),
        (-5.5, 2.1, .73, -1), (7.7, 2.1, .73, -1), (11.2, 1.4, .62, -.85)])
    loft("Ivory", [(-12.2, 1.15, .42, -.62), (-11.1, 2.4, .64, -.64),
        (-6.1, 2.35, .58, -.68), (-5.3, 1.8, .43, -.6)])
    for side in (-1, 1):
        x = side * 5.35
        loft("Ochre", [(-4.5, .72, .62, -.42), (-2.4, 1.55, 1.03, -.44),
            (6.5, 1.55, 1.08, -.46), (8.9, 1.33, .91, -.46)], x=x)
        loft("Frame", [(8.4, 1.28, .91, -.46), (10.5, 1.18, .88, -.46),
            (11.4, .94, .8, -.46)], x=x)
        ship.ring("Frame", x, -.46, 11.36, .87, .63, .26, sides=24)
        beam("Emitter", (x, -.46, 10.99), (x, -.46, 11.19), .60, 24)
        ship.marker("MainNozzle_" + str(side + 2), (x, -.46, 11.51), (0, 0, 1))
        for z in (-4.5, 6.8):
            beam("Frame", (side * 1.64, -.95, z), (side * 5.25, -.43, z + .9), .32, 10)
            beam("Frame", (side * 1.12, -.78, z + 1.45), (side * 5.1, -.29, z + .9), .20, 8)
        for z in (-1.1, 1.2, 3.5, 5.8):
            box("Thermal", (x, .71, z), (1.95, .14, 1.66))
        x_edge = side * 7.02
        for number, z in enumerate((-.2, 7.0)):
            box("Frame", (side * 6.66, -.42, z), (.67, .52, .72))
            ship.marker(f"Maneuver_{side+2}_side_{number}", (x_edge, -.42, z), (side, 0, 0))
        for number, z in enumerate((-2.3, 5.5)):
            beam("Frame", (side * 2.35, -1.28, z - .8), (side * 3.45, -2.0, z), .16)
            beam("Frame", (side * 4.7, -1.19, z + .5), (side * 3.45, -2.0, z + .65), .14)
            loft("Frame", [(z - .8, .40, .15, -2.05), (z + 1.25, .40, .15, -2.05)], x=side * 3.45)
        box("Ivory", (side * 6.38, .22, -1.55), (.64, .25, 1.45))
        box("PortLight" if side < 0 else "StarboardLight", (side * 6.61, .39, -2.03), (.19, .11, .27))
        for z in (-2.7, 8.1):
            ship.marker(f"Maneuver_{side+2}_keel_{z}", (side * 5.5, -1.56, z), (0, -1, 0))


def freight():
    for index, z in enumerate((-2.9, 1.15, 5.2)):
        paint = "Ivory" if index == 1 else "Ochre"
        ship.loft(paint, [(z - 1.82, 2.44, .97, .25), (z - 1.46, 2.72, 1.15, .25),
            (z + 1.46, 2.72, 1.15, .25), (z + 1.82, 2.44, .97, .25)])
        for side in (-1, 1):
            for dz in (-1.14, 1.14):
                ship.box("Frame", (side * 2.56, .27, z + dz), (.34, 1.72, .19))
                ship.box("Safety", (side * 1.76, 1.43, z + dz), (1.62, .13, .21))
            ship.box("Ivory", (side * 2.72, .28, z), (.16, .49, 1.26))
            ship.beam("Frame", (side * 2.85, -.22, z - .65), (side * 2.85, -.22, z + .65), .065, 8)
    for side in (-1, 1):
        ship.beam("Frame", (side * 2.94, -.65, -5.9), (side * 2.94, -.65, 7.62), .16, 8)
        for z in (-4.02, .02, 4.08):
            ship.beam("Frame", (side * 2.93, -.65, z), (side * 1.85, -1.08, z), .15, 8)
    ship.marker("DockMount", (0, -1.75, -7.2))
    ship.marker("EquipmentMount_utility", (1.55, .25, -10.72))
    ship.marker("EquipmentMount_defence", (0, 1.2, 7.8))


def bridge():
    ship.loft("Ivory", [(-11.5, 1.03, .38, .92), (-10.65, 1.46, .67, 1.29),
        (-8.75, 1.64, 1.02, 1.56), (-6.3, 1.53, .93, 1.65), (-5.1, 1.0, .46, 1.38)], x=-1.27)
    ship.loft("Glass", [(-10.71, 1.12, .47, 1.77), (-9.23, 1.43, .58, 2.15), (-8.45, 1.45, .43, 2.29)], x=-1.27)
    ship.loft("Ivory", [(-9.56, 1.34, .083, 2.74), (-8.35, 1.49, .13, 2.84),
        (-6.32, 1.39, .19, 2.60), (-5.56, 1.0, .09, 2.02)], x=-1.27)
    ship.box("Frame", (1.70, .35, -8.65), (1.55, .96, 3.78))
    ship.beam("Frame", (2.06, .73, -7.66), (2.06, 2.61, -7.20), .14)
    ship.beam("Frame", (2.06, 2.61, -7.20), (2.68, 3.23, -7.20), .074)
    ship.beam("Ivory", (1.47, 2.52, -7.2), (2.63, 2.52, -7.2), .19, 12)
    ship.marker("PilotEye", (-1.27, 2.40, -9.48))
    ship.marker("Maneuver_ForwardL", (-1.83, -.53, -11.89), (0, 0, -1))
    ship.marker("Maneuver_ForwardR", (1.83, -.53, -11.89), (0, 0, -1))


def bake_meshes():
    objects = []
    for name, (vertices, faces, _) in art.PARTS.items():
        mesh = bpy.data.meshes.new(name)
        mesh.from_pydata(vertices, [], faces)
        mesh.materials.append(art.MATERIALS[name])
        mesh.update()
        obj = bpy.data.objects.new(name, mesh)
        bpy.context.collection.objects.link(obj)
        ship.clean_mesh(mesh)
        uv = mesh.uv_layers.new(name="UVMap")
        for polygon in mesh.polygons:
            axis = max(range(3), key=lambda i: abs(polygon.normal[i]))
            sign = 1 if polygon.normal[axis] >= 0 else -1
            for index in polygon.loop_indices:
                vertex = mesh.vertices[mesh.loops[index].vertex_index].co
                u, v = ((vertex.y * sign, vertex.z), (-vertex.x * sign, vertex.z),
                    (vertex.x * sign, vertex.y))[axis]
                uv.data[index].uv = (round(u * .16, 6), round(v * .16, 6))
        objects.append(obj)
    return objects


def build():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    art.TEXTURES.mkdir(parents=True, exist_ok=True)
    for name, color, metal, rough, kind in [
        ("Ivory", (.65, .71, .68), .12, .54, "panels"),
        ("Ochre", (.57, .26, .075), .12, .62, "panels"),
        ("Frame", (.12, .17, .20), .84, .48, "panels"),
        ("Thermal", (.16, .19, .205), .83, .48, "thermal"),
        ("Safety", (.92, .61, .15), .03, .54, "safety"),
        ("Glass", (.055, .20, .24), .74, .19, "glass"),
        ("Emitter", (.20, .67, .90), .26, .34, "emitter"),
    ]:
        ship.material(name, color, metal, rough, kind)
    for name, color in [("PortLight", (1, .055, .07, 1)), ("StarboardLight", (.06, 1, .29, 1))]:
        material = bpy.data.materials.new(name)
        material.use_nodes = True
        bsdf = next(node for node in material.node_tree.nodes if node.type == "BSDF_PRINCIPLED")
        bsdf.inputs["Base Color"].default_value = color
        bsdf.inputs["Emission Color"].default_value = color
        bsdf.inputs["Emission Strength"].default_value = 2
        art.MATERIALS[name] = material
    chassis()
    freight()
    bridge()
    objects = bake_meshes()
    for name, position, size in [("T E R N", (0, 1.41, .9), .56), ("L / 04", (-1.28, 2.97, -8.15), .32)]:
        lettering = art.label(name, ship.p(position), size, rotation=(0, 0, 0), mat="Frame")
        ship.clean_mesh(lettering.data)
        objects.append(lettering)

    vertices, faces = [], []
    for obj in objects:
        offset = len(vertices)
        vertices.extend(obj.matrix_world @ v.co for v in obj.data.vertices)
        faces.extend(tuple(offset + i for i in face.vertices) for face in obj.data.polygons)
    tree = BVHTree.FromPolygons(vertices, faces, all_triangles=True)
    for marker in ship.MARKERS:
        if not marker.name.startswith(("MainNozzle_", "Maneuver_")):
            continue
        axis = marker.rotation_quaternion @ Vector((0, 1, 0))
        if tree.ray_cast(marker.location + axis * .002, axis, 2)[0] is not None:
            raise RuntimeError("Blocked exhaust: " + marker.name)

    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects + ship.MARKERS:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.export_scene.gltf(filepath=str(OUTPUT), export_format="GLB",
        use_selection=True, export_apply=True, export_yup=True, export_cameras=False, export_lights=False)
    for image in bpy.data.images:
        if image.packed_file:
            image.filepath_raw = "//tern_textures/" + image.name + ".png"
            for packed in image.packed_files:
                packed.filepath = image.filepath_raw
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE / "ship_tern.blend"), compress=True)

    points = [(v.x, v.z, -v.y) for v in vertices]
    limits = [[function(p[i] for p in points) for i in range(3)] for function in (min, max)]
    attachments = {obj.name: [obj.location.x, obj.location.z, -obj.location.y] for obj in ship.MARKERS}
    receipt = {
        "asset": "ship_tern", "design": "Tern light freighter", "blender": bpy.app.version_string,
        "scale_metres": 1, "nose": "-Z", "textures": [art.SIZE, art.SIZE], "triangles": len(faces),
        "bounds_metres": limits, "attachments_metres": attachments,
        "provenance": "Original project geometry, markings and procedural materials. No third-party assets.",
        "source_sha256": {Path(source).name: hashlib.sha256(Path(source).read_bytes()).hexdigest()
            for source in (__file__, ship.__file__, art.__file__)},
        "glb_sha256": hashlib.sha256(OUTPUT.read_bytes()).hexdigest(),
    }
    (SOURCE / "ship_tern.json").write_text(json.dumps(receipt, indent=2) + "\n")
    print("TERN BUILT", json.dumps(receipt))


if __name__ == "__main__":
    build()
