"""Independent GLB/collision-surface check. Run with Blender --background --python."""
import collections
import pathlib
import struct

import bpy

ROOT = pathlib.Path(__file__).resolve().parents[2]


def key(vertices):
    return tuple(sorted(tuple(round(float(c), 4) for c in v) for v in vertices))


for name in ["ship_player", "station_kestrel"]:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(ROOT / "client/assets/models" / (name + ".glb")))
    groups = []
    for obj in bpy.context.scene.objects:
        if obj.type != "MESH":
            continue
        obj.data.calc_loop_triangles()
        triangles = []
        for tri in obj.data.loop_triangles:
            vertices = []
            for idx in tri.vertices:
                v = obj.matrix_world @ obj.data.vertices[idx].co
                vertices.append((v.x, v.z, -v.y))
            triangles.append(vertices)
        groups.append((obj.name, triangles))
    actual = collections.Counter(key(tri) for _, tris in groups for tri in tris)
    data = (ROOT / "src/IndifferentStars.Shared/Collision" / (name + ".bin")).read_bytes()
    expected = collections.Counter()
    for offset in range(40, len(data), 36):
        values = struct.unpack_from("<9f", data, offset)
        expected[key([values[0:3], values[3:6], values[6:9]])] += 1
    assert actual == expected, f"{name}: mismatched GLB and collision triangles ({sum(actual.values())}/{sum(expected.values())})"
    print(f"BLENDER SURFACE VERIFIED: {name}, {sum(actual.values())} triangles")
