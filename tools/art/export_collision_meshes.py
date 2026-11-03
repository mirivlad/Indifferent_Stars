"""Bake the visible GLB surfaces for the engine-independent server. No Blender required."""
import argparse
import hashlib
import json
import pathlib
import struct

ROOT = pathlib.Path(__file__).resolve().parents[2]
OUT = ROOT / "src/IndifferentStars.Shared/Collision"


def matrix(node):
    if "matrix" in node:
        m = node["matrix"]
        return [[m[j * 4 + i] for j in range(4)] for i in range(4)]
    x, y, z, w = node.get("rotation", [0, 0, 0, 1])
    s = node.get("scale", [1, 1, 1])
    t = node.get("translation", [0, 0, 0])
    r = [[1-2*(y*y+z*z), 2*(x*y-z*w), 2*(x*z+y*w)],
         [2*(x*y+z*w), 1-2*(x*x+z*z), 2*(y*z-x*w)],
         [2*(x*z-y*w), 2*(y*z+x*w), 1-2*(x*x+y*y)]]
    return [[r[i][j]*s[j] for j in range(3)] + [t[i]] for i in range(3)] + [[0, 0, 0, 1]]


def multiply(a, b):
    return [[sum(a[i][k]*b[k][j] for k in range(4)) for j in range(4)] for i in range(4)]


def bake(path):
    raw = path.read_bytes()
    magic, version, length = struct.unpack_from("<III", raw)
    assert magic == 0x46546C67 and version == 2 and length == len(raw)
    offset, chunks = 12, {}
    while offset < len(raw):
        size, kind = struct.unpack_from("<II", raw, offset)
        chunks[kind] = raw[offset+8:offset+8+size]
        offset += size+8
    doc = json.loads(chunks[0x4E4F534A])
    binary = chunks[0x004E4942]

    def accessor(index):
        a = doc["accessors"][index]
        assert not a.get("sparse"), "Sparse mesh accessors are not supported."
        view = doc["bufferViews"][a["bufferView"]]
        assert view["buffer"] == 0
        typ = {5121: "B", 5123: "H", 5125: "I", 5126: "f"}[a["componentType"]]
        count = {"SCALAR": 1, "VEC3": 3}[a["type"]]
        fmt = "<" + typ * count
        stride = view.get("byteStride", struct.calcsize(fmt))
        start = view.get("byteOffset", 0) + a.get("byteOffset", 0)
        return [struct.unpack_from(fmt, binary, start+i*stride) for i in range(a["count"])]

    meshes = []
    identity = [[int(i == j) for j in range(4)] for i in range(4)]

    def visit(index, parent):
        node = doc["nodes"][index]
        transform = multiply(parent, matrix(node))
        if "mesh" in node:
            triangles = []
            for primitive in doc["meshes"][node["mesh"]]["primitives"]:
                assert primitive.get("mode", 4) == 4, "Only triangle geometry is supported."
                positions = accessor(primitive["attributes"]["POSITION"])
                # Asteroid instancing uses the mesh only, without GLB node offsets.
                vertices = positions if path.stem == "asteroids" else [
                    tuple(sum(transform[i][j]*p[j] for j in range(3))+transform[i][3] for i in range(3))
                    for p in positions]
                indices = [v[0] for v in accessor(primitive["indices"])] if "indices" in primitive else range(len(vertices))
                for k in range(0, len(indices), 3):
                    triangles.append(tuple(c for idx in indices[k:k+3] for c in vertices[idx]))
            meshes.append((node.get("name", str(index)), triangles))
        for child in node.get("children", []):
            visit(child, transform)

    for root in doc["scenes"][doc.get("scene", 0)]["nodes"]:
        visit(root, identity)
    groups = meshes if path.stem == "asteroids" else [(path.stem, [t for _, ts in meshes for t in ts])]
    outputs = {}
    for i, (name, triangles) in enumerate(groups):
        output_name = f"asteroid_{i}" if path.stem == "asteroids" else path.stem
        data = b"ISCM" + hashlib.sha256(raw).digest() + struct.pack("<I", len(triangles))
        def pack_triangle(triangle):
            packed = struct.pack("<9f", *triangle)
            t = struct.unpack("<9f", packed)
            a = tuple(t[3+i]-t[i] for i in range(3))
            b = tuple(t[6+i]-t[i] for i in range(3))
            c = tuple(t[6+i]-t[3+i] for i in range(3))
            normal = (a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0])
            edge = max(sum(v*v for v in axis) for axis in (a,b,c))
            if sum(v*v for v in normal) <= edge*edge*1e-24:
                raise ValueError(f"Degenerate triangle in {path.name}/{name}; regenerate a clean source model.")
            return packed
        data += b"".join(pack_triangle(tri) for tri in triangles)
        outputs[output_name + ".bin"] = data
        print(f"{output_name}: {len(triangles)} triangles ({name})")
    return outputs


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    for filename in ["ship_player.glb", "station_kestrel.glb", "asteroids.glb"]:
        for name, data in bake(ROOT / "client/assets/models" / filename).items():
            path = OUT / name
            if args.check:
                assert path.read_bytes() == data, f"Collision mesh drift: regenerate {path.name}"
            else:
                path.write_bytes(data)


if __name__ == "__main__":
    main()
