"""Work hull geometry for the Mole extractor and the Petrel surveyor."""
import argparse
import hashlib
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

sys.path.insert(0, str(Path(__file__).resolve().parent))
import build_ship_tern as tern

ship = tern.ship
art = tern.art
ROOT = tern.ROOT
SOURCES = tern.SOURCE


def mole():
    # The spindle processes ore, while independently braced forks cradle the
    # collection heads ahead of the forebody. The bridge overlooks both heads.
    ship.loft("Bronze", [(-5.5, 2.1, .98, .1), (-3.9, 3.35, 1.42, -.05),
        (2.9, 3.5, 1.63, -.2), (6.7, 2.58, 1.39, -.38), (8.0, 1.58, .84, -.45)])
    ship.loft("Frame", [(-4.7, 1.84, .49, -1.45), (5.4, 2.34, .54, -1.55),
        (7.6, 1.72, .45, -1.30)])
    ship.loft("Cream", [(-5.42, 1.36, .51, 1.37), (-4.38, 1.91, .84, 1.67),
        (-2.25, 1.90, 1.04, 1.87), (-.35, 1.28, .65, 1.49)])
    ship.loft("Glass", [(-5.25, 1.28, .48, 1.80), (-4.19, 1.74, .62, 2.38),
        (-3.37, 1.80, .49, 2.55)])
    ship.loft("Cream", [(-4.29, 1.69, .065, 3.01), (-3.23, 1.76, .14, 3.09),
        (-1.81, 1.39, .099, 2.83)])
    ship.marker("PilotEye", (0, 2.63, -4.26))
    for side in (-1, 1):
        x = side * 3.02
        tip = side * 4.26
        ship.beam("Frame", (x, -.54, 1.2), (tip, -.61, -8.2), .65, 10)
        ship.beam("Cream", (x, -.31, -2.4), (tip, -.24, -8.08), .44, 10)
        ship.beam("Frame", (tip, -.66, -5.42), (side * 2.23, -1.37, -.7), .27, 10)
        ship.ring("Frame", tip, -.46, -8.77, 1.01, .72, 1.13, sides=20)
        ship.ring("Safety", tip, -.46, -9.40, 1.05, .76, .22, sides=20)
        for index in range(6):
            angle = index * math.tau / 6
            c, s = math.cos(angle), math.sin(angle)
            ship.beam("Frame", (tip + .83*c, -.46 + .83*s, -9.40), (tip + .40*c, -.46 + .40*s, -9.74), .079)
        ship.marker(f"EquipmentMount_head_{side+2}", (tip, -.46, -9.93))
        ship.box("Safety", (side * 3.18, .51, -3.86), (.95, .21, 2.25))
        ship.box("Emitter", (side * 3.15, .67, -4.52), (.24, .14, .37))
        ship.loft("Frame", [(2.0, .64, .82, -.64), (4.2, 1.34, 1.04, -.53),
            (7.7, 1.40, 1.02, -.53), (8.92, 1.05, .86, -.53)], x=x)
        ship.ring("Frame", x, -.53, 8.96, .94, .69, .24)
        ship.beam("Emitter", (x, -.53, 8.70), (x, -.53, 8.91), .65, 24)
        ship.marker(f"MainNozzle_{side+2}", (x, -.53, 9.12), (0, 0, 1))
        ship.marker(f"Maneuver_side_{side+2}", (side * 4.58, -.42, 6.82), (side, 0, 0))
        ship.marker(f"Maneuver_keel_{side+2}", (side * 3.19, -1.69, 6.62), (0, -1, 0))
        for z in (-1.5, .54, 2.58):
            ship.box("Thermal", (side * 2.43, 1.12, z), (1.38, .32, 1.65))
        ship.beam("Frame", (side*2.60, -1.32, 3.55), (side*2.14, -2.22, 2.52), .19)
        ship.beam("Frame", (side*2.06, -2.20, 1.63), (side*2.06, -2.20, 4.08), .19)
        ship.box("PortLight" if side < 0 else "StarboardLight", (tip, .60, -8.42), (.24, .11, .30))
    ship.marker("DockMount", (0, -2.14, 2.55))
    return [("M O L E", (0, 1.36, .7), .60)]


def petrel():
    # The side panels are a thermal bus, leaving the fore path of the survey
    # instruments open. A narrow hub houses the crew and reference sensors.
    ship.loft("Frame", [(-12.7, .83, .45, -.30), (-9.5, 1.16, .65, -.38),
        (-1.2, 1.20, .84, -.50), (7.4, 1.27, .75, -.45), (12.9, .68, .53, -.33)])
    ship.loft("Cream", [(-13, .68, .40, -.14), (-11.8, 1.44, .78, .05),
        (-6.2, 1.69, 1.10, .17), (-3.1, 1.20, .69, .08)])
    ship.loft("Glass", [(-12, .64, .31, .51), (-10.6, 1.13, .48, .97),
        (-8.8, 1.38, .49, 1.24)])
    ship.loft("Cream", [(-10.75, 1.10, .12, 1.50), (-8.1, 1.47, .21, 1.76),
        (-6.1, 1.29, .13, 1.28)])
    ship.marker("PilotEye", (0, 1.27, -10.28))
    for side in (-1, 1):
        z = 1.18 if side < 0 else 2.08
        ship.beam("Frame", (side*1.15, -.48, z), (side*5.33, .72, z + .48), .21)
        ship.beam("Frame", (side*1.05, -.57, z + .84), (side*4.72, .59, z + .53), .14)
        ship.loft("Frame", [(z-.55, 1.35, .105, .71), (z+.56, 1.66, .105, .71),
            (z+3.24, 1.18, .105, .72), (z+4.11, .73, .095, .72)], x=side*5.35)
        for index in range(5):
            ship.box("Thermal", (side*(4.27+index*.53), .844, z+1.66),
                (.42, .16, 2.43 - .18*abs(index-2)))
        ship.box("PortLight" if side < 0 else "StarboardLight", (side*6.67, .87, z+1.28), (.22,.12,.27))
        ship.loft("Teal", [(5.13, .56, .62, -.42), (7.21, .97,.79, -.47),
            (11.4, .88, .74, -.47), (12.42, .67, .60, -.47)], x=side*2.28)
        ship.beam("Frame", (side*.94,-.56, 6.55),(side*2.26,-.42, 7.48),.16)
        ship.ring("Frame", side*2.28, -.47, 12.39, .64, .47, .22)
        ship.beam("Emitter", (side*2.28, -.47, 12.19), (side*2.28,-.47,12.36),.42,20)
        ship.marker(f"MainNozzle_{side+2}", (side*2.28,-.47,12.56), (0,0,1))
        ship.marker(f"Maneuver_tail_{side+2}", (side*3.24,-.42,9.12), (side,0,0))
        ship.marker(f"Maneuver_keel_{side+2}", (side*2.29,-1.26,8.13), (0,-1,0))
    # Open lattice sensor frame with a segmented parabolic dish and receiver.
    center = Vector((0, 2.62, -.95))
    for x in (-.86, .86):
        ship.beam("Frame", (x, .15, -1.95), center, .14)
    axis = Vector((0, .49, -.87)).normalized()
    right = Vector((1, 0, 0))
    up = axis.cross(right).normalized()
    for index in range(40):
        lo, hi = index * math.tau / 40, (index+1) * math.tau / 40
        def point(radius, angle, layer=0):
            return center + (right * math.cos(angle) + up * math.sin(angle)) * radius + axis * (radius**2*.17 + layer)
        for inner, outer in ((.10, .65), (.71, 1.25), (1.31, 1.71)):
            corners = [(inner,lo),(outer,lo),(outer,hi),(inner,hi)]
            front = [point(*corner) for corner in corners]
            back = [point(*corner, -.027) for corner in corners]
            ship.face("Teal", front)
            ship.face("Teal", back[::-1])
            for a, b in ((0, 3), (2, 1)):
                ship.face("Frame", [front[a], front[b], back[b], back[a]])
            if index % 10 == 0:
                ship.beam("Cream", point(inner,lo), point(outer,lo), .055, 6)
    ship.beam("Frame", center, center + axis*1.31, .11)
    ship.beam("Emitter", center + axis*1.20, center + axis*1.43, .133)
    ship.marker("EquipmentMount_array", tuple(center + axis*1.6))
    ship.marker("DockMount", (0,-1.34,5.54))
    return [("P E T R E L", (0, 1.97, -7.38), .39)]


def bright(name, color):
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    node = next(node for node in material.node_tree.nodes if node.type == "BSDF_PRINCIPLED")
    node.inputs["Base Color"].default_value = color
    node.inputs["Emission Color"].default_value = color
    node.inputs["Emission Strength"].default_value = 1.7
    art.MATERIALS[name] = material


def build_variant(variant, shape, generator):
    art.SIZE = 768
    art.TEXTURES = SOURCES / "profession_textures"
    art.TEXTURES.mkdir(parents=True, exist_ok=True)
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for name, color, metal, rough, kind in [
        ("Cream", (.61,.68,.65), .08,.61, "panels"),
        ("Frame", (.13,.17,.19), .82,.52, "panels"),
        ("Thermal", (.12,.19,.23), .78,.47, "thermal"),
        ("Safety", (.90,.64,.19), .03,.55, "safety"),
        ("Glass", (.052,.18,.24), .61,.18, "glass"),
        ("Bronze", (.48,.302,.17), .19,.62, "panels"),
        ("Teal", (.16,.37,.40), .13,.53, "panels"),
        ("Emitter", (.38,.69,.93), .27,.38, "emitter"),
    ]:
        ship.material(name, color, metal, rough, kind)
    bright("PortLight", (1,.045,.085,1))
    bright("StarboardLight", (.09,.95,.37,1))
    signs = shape()
    objects = tern.bake_meshes()
    for marking, position, size in signs:
        obj = art.label(marking, ship.p(position), size, rotation=(0,0,0), mat="Frame")
        ship.clean_mesh(obj.data)
        objects.append(obj)
    vertices, faces = [], []
    for obj in objects:
        offset = len(vertices)
        vertices.extend(obj.matrix_world @ item.co for item in obj.data.vertices)
        faces.extend(tuple(offset+i for i in face.vertices) for face in obj.data.polygons)
    tree = BVHTree.FromPolygons(vertices, faces, all_triangles=True)
    for anchor in ship.MARKERS:
        if not anchor.name.startswith(("MainNozzle_", "Maneuver_")):
            continue
        axis = anchor.rotation_quaternion @ Vector((0,1,0))
        if tree.ray_cast(anchor.location + axis*.002, axis, 2)[0] is not None:
            raise RuntimeError("Blocked exhaust: " + anchor.name)
    name = "ship_" + variant
    output = ROOT / "client/assets/models" / (name + ".glb")
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects + ship.MARKERS:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.export_scene.gltf(filepath=str(output), export_format="GLB", use_selection=True,
        export_apply=True, export_yup=True, export_cameras=False, export_lights=False)
    for image in bpy.data.images:
        if image.packed_file:
            image.filepath_raw = "//profession_textures/" + image.name + ".png"
            for packed in image.packed_files:
                packed.filepath = image.filepath_raw
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCES / (name + ".blend")), compress=True)
    points = [(v.x, v.z, -v.y) for v in vertices]
    bounds = [[f(point[i] for point in points) for i in range(3)] for f in (min, max)]
    receipt = {
        "asset": name, "generator": Path(generator).name, "blender": bpy.app.version_string,
        "bounds_metres": bounds, "triangles": len(faces),
        "attachments_metres": {obj.name: [obj.location.x,obj.location.z,-obj.location.y] for obj in ship.MARKERS},
        "textures": [art.SIZE, art.SIZE],
        "source_sha256": {Path(source).name: hashlib.sha256(Path(source).read_bytes()).hexdigest()
            for source in (generator, shape.__code__.co_filename, __file__, tern.__file__, ship.__file__, art.__file__)},
        "provenance": "Original project geometry, markings and procedural materials. No third-party assets.",
        "glb_sha256": hashlib.sha256(output.read_bytes()).hexdigest(),
    }
    (SOURCES / (name + ".json")).write_text(json.dumps(receipt, indent=2) + "\n")
    print("HULL BUILT", json.dumps(receipt))


def main():
    shapes = {"mole": mole, "petrel": petrel}
    args = argparse.ArgumentParser()
    args.add_argument("key", choices=shapes)
    variant = args.parse_args(sys.argv[sys.argv.index("--") + 1:]).key
    build_variant(variant, shapes[variant], __file__)


if __name__ == "__main__":
    main()
