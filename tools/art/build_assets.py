"""Reproducible First Flight art assets for Indifferent Stars.

Run with:
    blender --background --factory-startup --python tools/art/build_assets.py

Legacy ship/asteroid generator; the detailed station uses build_station.py.
Creates GLB models in client/assets/models/ and preview renders in docs/media/.
Only original procedural geometry and materials are used.
"""

import math
import os
import random
import sys

import bpy
from mathutils import Vector

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
MODELS = os.path.join(ROOT, "client", "assets", "models")
MEDIA = os.path.join(ROOT, "docs", "media")
os.makedirs(MODELS, exist_ok=True)
os.makedirs(MEDIA, exist_ok=True)


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for block in (bpy.data.meshes, bpy.data.materials, bpy.data.lights, bpy.data.cameras):
        for item in list(block):
            if item.users == 0:
                block.remove(item)


def material(name, color, metallic=0.0, roughness=0.5, emission=None, emission_strength=0.0):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = roughness
    if emission is not None:
        emission_input = bsdf.inputs.get("Emission Color") or bsdf.inputs.get("Emission")
        if emission_input is not None:
            emission_input.default_value = (*emission, 1.0)
        strength = bsdf.inputs.get("Emission Strength")
        if strength is not None:
            strength.default_value = emission_strength
    return mat


def assign(obj, mat):
    obj.data.materials.clear()
    obj.data.materials.append(mat)


def bevel(obj, width=0.03, segments=2):
    mod = obj.modifiers.new("Bevel", "BEVEL")
    mod.width = width
    mod.segments = segments
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    try:
        bpy.ops.object.modifier_apply(modifier=mod.name)
    except RuntimeError:
        pass
    obj.select_set(False)


def smooth(obj):
    for poly in obj.data.polygons:
        poly.use_smooth = True


def shade_flat(obj):
    for poly in obj.data.polygons:
        poly.use_smooth = False


def cube(name, location, scale, mat, rotation=(0, 0, 0)):
    bpy.ops.mesh.primitive_cube_add(size=1, location=location, rotation=rotation)
    obj = bpy.context.object
    obj.name = name
    obj.scale = scale
    assign(obj, mat)
    return obj


def cylinder(name, location, radius, depth, mat, vertices=24, rotation=(0, 0, 0)):
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=vertices, radius=radius, depth=depth, location=location, rotation=rotation
    )
    obj = bpy.context.object
    obj.name = name
    assign(obj, mat)
    return obj


def sphere(name, location, scale, mat, segments=24, rings=16):
    bpy.ops.mesh.primitive_uv_sphere_add(
        segments=segments, ring_count=rings, radius=1.0, location=location
    )
    obj = bpy.context.object
    obj.name = name
    obj.scale = scale
    assign(obj, mat)
    smooth(obj)
    return obj


def cone(name, location, r1, r2, depth, mat, rotation=(0, 0, 0), vertices=12):
    bpy.ops.mesh.primitive_cone_add(
        vertices=vertices, radius1=r1, radius2=r2, depth=depth, location=location, rotation=rotation
    )
    obj = bpy.context.object
    obj.name = name
    assign(obj, mat)
    return obj


def torus(name, location, major, minor, mat, rotation=(0, 0, 0)):
    bpy.ops.mesh.primitive_torus_add(
        major_radius=major, minor_radius=minor, major_segments=64, minor_segments=12,
        location=location, rotation=rotation
    )
    obj = bpy.context.object
    obj.name = name
    assign(obj, mat)
    smooth(obj)
    return obj


def poly_mesh(name, verts, faces, mat, thickness=None):
    mesh = bpy.data.meshes.new(name + "Mesh")
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    assign(obj, mat)
    if thickness:
        mod = obj.modifiers.new("Solidify", "SOLIDIFY")
        mod.thickness = thickness
        bpy.context.view_layer.objects.active = obj
        obj.select_set(True)
        bpy.ops.object.modifier_apply(modifier=mod.name)
        obj.select_set(False)
    return obj


def export_selected(path, objects):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.export_scene.gltf(
        filepath=path,
        export_format="GLB",
        use_selection=True,
        export_apply=True,
        export_yup=True,
    )
    print("EXPORTED", path)


def setup_render_environment():
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 1100
    scene.render.resolution_y = 620
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    scene.eevee.taa_render_samples = 24
    world = bpy.data.worlds.new("SpaceWorld")
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    bg.inputs[0].default_value = (0.004, 0.007, 0.018, 1.0)
    bg.inputs[1].default_value = 1.0
    scene.world = world

    bpy.ops.object.light_add(type="SUN", location=(8, -10, 12))
    sun = bpy.context.object
    sun.data.energy = 5.0
    sun.data.color = (1.0, 0.92, 0.82)
    sun.rotation_euler = (math.radians(55), 0, math.radians(35))

    bpy.ops.object.light_add(type="AREA", location=(-7, 4, 5))
    fill = bpy.context.object
    fill.data.energy = 1400
    fill.data.size = 8
    fill.data.color = (0.35, 0.55, 1.0)
    fill.rotation_euler = (math.radians(-50), math.radians(-20), math.radians(-140))

    bpy.ops.object.light_add(type="AREA", location=(6, 6, -4))
    rim = bpy.context.object
    rim.data.energy = 900
    rim.data.size = 6
    rim.data.color = (1.0, 0.45, 0.2)


def render_view(path, camera_location, look_at, lens=55):
    bpy.ops.object.camera_add(location=camera_location)
    camera = bpy.context.object
    camera.data.lens = lens
    direction = Vector(look_at) - camera.location
    camera.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    bpy.context.scene.camera = camera
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    print("RENDERED", path)
    bpy.data.objects.remove(camera, do_unlink=True)


def build_ship():
    hull = material("Hull", (0.16, 0.20, 0.27), metallic=0.65, roughness=0.38)
    dark = material("DarkMetal", (0.035, 0.042, 0.055), metallic=0.85, roughness=0.42)
    accent = material("Accent", (0.9, 0.32, 0.08), metallic=0.35, roughness=0.35)
    glow = material("EngineGlow", (0.1, 0.75, 1.0), emission=(0.15, 0.8, 1.0), emission_strength=3.5)
    canopy = material("Canopy", (0.01, 0.06, 0.09), metallic=0.6, roughness=0.06)
    warm = material("WarmLight", (1.0, 0.55, 0.2), emission=(1.0, 0.45, 0.15), emission_strength=2.0)

    objects = []

    body = sphere("Hull_Main", (0, 0.45, 0), (1.15, 3.55, 0.55), hull, segments=32, rings=20)
    objects.append(body)

    nose = cone("Hull_Nose", (0, 3.75, 0.01), 1.08, 0.04, 3.2, hull, rotation=(math.radians(-90), 0, 0), vertices=16)
    smooth(nose)
    objects.append(nose)

    spine = cube("Spine", (0, -0.6, 0.5), (0.42, 4.6, 0.22), dark)
    bevel(spine, 0.05)
    objects.append(spine)

    # Swept delta wings (custom mirrored polygons).
    wing_verts = [
        (-0.7, 1.15, -0.05), (-4.7, -1.35, -0.18), (-4.15, -2.55, -0.18), (-0.7, -2.35, -0.05),
        (0.7, 1.15, -0.05), (4.7, -1.35, -0.18), (4.15, -2.55, -0.18), (0.7, -2.35, -0.05),
    ]
    wing_faces = [(0, 1, 2, 3), (4, 7, 6, 5)]
    wings = poly_mesh("Wings", wing_verts, wing_faces, hull, thickness=0.16)
    bevel(wings, 0.035)
    objects.append(wings)

    for side in (-1, 1):
        tip = cube(f"WingAccent_{side}", (side * 4.4, -1.9, -0.02), (0.85, 0.11, 0.05), accent,
                   rotation=(0, 0, side * math.radians(-12)))
        bevel(tip, 0.02)
        objects.append(tip)

        fin = cube(f"Fin_{side}", (side * 1.25, -3.0, 0.72), (0.09, 0.95, 0.85), dark,
                   rotation=(math.radians(-16), 0, side * math.radians(-16)))
        bevel(fin, 0.03)
        objects.append(fin)

        engine = cylinder(f"Engine_{side}", (side * 0.78, -3.7, 0.0), 0.46, 1.5, dark,
                          vertices=20, rotation=(math.radians(90), 0, 0))
        objects.append(engine)

        nozzle = cylinder(f"Nozzle_{side}", (side * 0.78, -4.48, 0.0), 0.33, 0.16, glow,
                          vertices=20, rotation=(math.radians(90), 0, 0))
        objects.append(nozzle)

        stripe = cube(f"Stripe_{side}", (side * 0.9, 0.2, -0.38), (0.055, 3.2, 0.035), warm)
        objects.append(stripe)

    engine_block = cube("EngineBlock", (0, -3.45, 0.02), (1.9, 1.1, 0.62), dark)
    bevel(engine_block, 0.06)
    objects.append(engine_block)

    cockpit = sphere("Canopy", (0, 1.75, 0.52), (0.56, 1.15, 0.38), canopy, segments=24, rings=14)
    objects.append(cockpit)

    cockpit_frame = cube("CanopyFrame", (0, 1.75, 0.62), (0.68, 1.35, 0.06), dark)
    objects.append(cockpit_frame)

    for i in range(4):
        light = cube(f"RunningLight_{i}", (-0.75 + i * 0.5, 2.9, -0.28), (0.04, 0.1, 0.03), warm)
        objects.append(light)

    antenna = cylinder("Antenna", (0, 0.2, 0.85), 0.025, 1.2, dark, vertices=8,
                       rotation=(math.radians(20), 0, 0))
    objects.append(antenna)

    return objects


def build_station():
    hull = material("StationHull", (0.46, 0.48, 0.52), metallic=0.55, roughness=0.42)
    dark = material("StationDark", (0.05, 0.055, 0.065), metallic=0.85, roughness=0.45)
    panel = material("SolarPanel", (0.02, 0.06, 0.15), metallic=0.5, roughness=0.18)
    cyan = material("StationCyan", (0.2, 0.8, 1.0), emission=(0.15, 0.7, 1.0), emission_strength=2.4)
    warm = material("StationWarm", (1.0, 0.7, 0.35), emission=(1.0, 0.55, 0.2), emission_strength=1.6)
    panel_glow = material("PanelGlow", (0.2, 0.45, 1.0), emission=(0.1, 0.3, 1.0), emission_strength=0.8)

    objects = []

    hub = cylinder("Hub", (0, 0, 0), 1.25, 2.7, hull, vertices=32)
    objects.append(hub)

    cap_top = cylinder("HubCapTop", (0, 0, 1.45), 0.9, 0.35, dark, vertices=32)
    cap_bottom = cylinder("HubCapBottom", (0, 0, -1.45), 0.9, 0.35, dark, vertices=32)
    objects.extend([cap_top, cap_bottom])

    ring = torus("Ring", (0, 0, 0), 2.9, 0.2, hull)
    objects.append(ring)

    ring_dark = torus("RingDark", (0, 0, 0), 2.9, 0.225, dark)
    # Slightly larger torus used only as a dark trim behind the ring; offset in z.
    ring_dark.location.z = -0.2
    objects.append(ring_dark)

    for i in range(4):
        angle = i * math.pi / 2 + math.pi / 4
        spoke = cube(f"Spoke_{i}", (math.cos(angle) * 1.6, math.sin(angle) * 1.6, 0),
                     (1.6, 0.16, 0.16), dark, rotation=(0, 0, angle))
        objects.append(spoke)

    for i in range(12):
        angle = i * math.pi * 2 / 12
        light = cube(
            f"Window_{i}",
            (math.cos(angle) * 2.9, math.sin(angle) * 2.9, 0.26),
            (0.12, 0.3, 0.06), warm if i % 3 == 0 else cyan,
            rotation=(0, 0, angle),
        )
        objects.append(light)

    for side in (-1, 1):
        arm = cube(f"PanelArm_{side}", (side * 4.3, 0, 0), (3.0, 0.13, 0.13), dark)
        objects.append(arm)
        solar = cube(f"SolarPanel_{side}", (side * 5.6, 0, 0.0), (2.7, 1.55, 0.06), panel)
        objects.append(solar)
        for j in range(4):
            line = cube(f"PanelLine_{side}_{j}", (side * (4.6 + j * 0.62), 0, 0.045), (0.025, 1.5, 0.01), panel_glow)
            objects.append(line)
        tip = cube(f"PanelTip_{side}", (side * 7.05, 0, 0), (0.1, 1.7, 0.12), warm)
        objects.append(tip)

    tower = cylinder("DockTower", (0, 0, 2.1), 0.42, 1.4, hull, vertices=16)
    objects.append(tower)
    beacon = sphere("Beacon", (0, 0, 2.95), (0.2, 0.2, 0.2), cyan, segments=16, rings=10)
    objects.append(beacon)

    for i in range(6):
        angle = i * math.pi * 2 / 6
        beacon_light = cube(
            f"ApproachLight_{i}",
            (math.cos(angle) * 3.4, math.sin(angle) * 3.4, -0.55),
            (0.08, 0.08, 0.08), cyan,
        )
        objects.append(beacon_light)

    bpy.ops.object.light_add(type="POINT", location=(0, 0, 3.2))
    light = bpy.context.object
    light.data.energy = 90
    light.data.color = (0.6, 0.85, 1.0)
    light.data.shadow_soft_size = 0.8
    objects.append(light)

    return objects


def build_asteroid(name, seed, scale, stretch):
    random.seed(seed)
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=2, radius=1.0)
    obj = bpy.context.object
    obj.name = name
    mesh = obj.data
    for vertex in mesh.vertices:
        v = vertex.co
        n = 1.0
        n += 0.22 * math.sin(v.x * 4.3 + seed * 1.7) * math.cos(v.y * 3.7 + seed * 0.9)
        n += 0.13 * math.sin(v.z * 6.1 + v.x * 2.2 + seed * 2.3)
        n += 0.06 * math.cos(v.y * 9.0 + seed)
        vertex.co = v * n
    mesh.update()
    obj.scale = (scale * stretch, scale, scale * 0.88)
    shade_flat(obj)
    rock = material("AsteroidRock", (0.055, 0.052, 0.048), metallic=0.15, roughness=0.92)
    assign(obj, rock)
    bevel(obj, 0.02, 1)
    return obj


def main():
    clear_scene()
    setup_render_environment()

    print("Detailed ship is built separately with tools/art/build_ship.py")

    print("Detailed station is built separately with tools/art/build_station.py")

    clear_scene()
    setup_render_environment()
    asteroid_a = build_asteroid("AsteroidA", 11, 1.0, 1.15)
    asteroid_b = build_asteroid("AsteroidB", 29, 1.0, 0.82)
    asteroid_b.location = (3.2, 0, 0)
    asteroid_c = build_asteroid("AsteroidC", 47, 1.0, 1.35)
    asteroid_c.location = (-3.0, 0.6, 0.4)
    export_selected(os.path.join(MODELS, "asteroids.glb"), [asteroid_a, asteroid_b, asteroid_c])
    render_view(os.path.join(MEDIA, "asteroid_preview.png"), (5.5, -6.5, 3.0), (0.3, 0, 0), lens=52)

    print("ALL ASSETS DONE")


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        print("ASSET BUILD FAILED:", exc)
        raise
