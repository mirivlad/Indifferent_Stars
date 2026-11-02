"""Original Kestrel Reach asset. Blender 5.2 CLI, no external assets or services.

BLENDER_BIN --background --factory-startup --python tools/art/build_station.py
Writes only the station GLB, packed editable source, textures and asset receipt.
Blender Z-up is exported to glTF Y-up; model units are multiplied by 275 in ModelPose.
"""
import hashlib
import json
import math
from pathlib import Path

import bpy
import numpy as np
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
SOURCES = ROOT / "tools/art/sources"
TEXTURES = SOURCES / "station_textures"
SIZE = 1024
SCALE = 275
PARTS = {}
MATERIALS = {}


def image(name, rgb, data=False):
    result = bpy.data.images.new(name, width=SIZE, height=SIZE, alpha=False)
    result.colorspace_settings.name = "Non-Color" if data else "sRGB"
    pixels = np.ones((SIZE, SIZE, 4), dtype=np.float32)
    pixels[:, :, :3] = np.clip(rgb, 0, 1)
    result.pixels.foreach_set(pixels.ravel())
    result.filepath_raw = str(TEXTURES / (name + ".png"))
    result.file_format = "PNG"
    result.save()
    result.pack()
    return result


def material(name, color, metal, rough, kind="hull"):
    y, x = np.mgrid[0:SIZE, 0:SIZE] / SIZE
    rng = np.random.default_rng(20261102)
    grain = rng.normal(0, 0.008, x.shape)
    u, v = (x * 4 + np.floor(y * 8) * .5) % 1, (y * 8) % 1
    seam = (u < 0.009) | (v < 0.014)
    edge = ((u > 0.023) & (u < 0.035)) | ((v > 0.027) & (v < 0.038))
    rivet = (((u - .09)**2 + (v - .09)**2) < .001) | (((u - .91)**2 + (v - .09)**2) < .001)
    values = 0.92 + grain + .035 * np.sin(np.floor(x * 8) * 7 + np.floor(y * 8) * 13)
    values = np.where(seam, .28, np.where(edge, 1.07, values))
    values = np.where(rivet, .48, values)
    relief = np.where(seam, .2, np.where(rivet, .75, .55))
    rgb = values[:, :, None] * np.array(color)
    hatch = (u > .63) & (u < .92) & (v > .30) & (v < .74)
    hatch_edge = hatch & ((u < .65) | (u > .90) | (v < .34) | (v > .70))
    rgb[hatch] *= .87
    rgb[hatch_edge] *= .56
    emission = None
    if kind == "solar":
        cells = ((x * 32) % 1 < .035) | ((y * 16) % 1 < .04)
        buses = ((y * 4) % 1 < .018)
        rgb = np.tile(np.array(color), (*x.shape, 1)) * (.78 + .18 * np.sin(x * 220)**2)[:, :, None]
        rgb[cells] = (.08, .13, .22)
        rgb[buses] = (.48, .36, .16)
        relief = np.where(cells, .4, .55)
    elif kind == "thermal":
        fins = (y * 64) % 1
        rgb *= np.where(fins < .3, .36, 1.0)[:, :, None]
        relief = np.where(fins < .3, .3, .65)
    elif kind == "windows":
        u, v = (x * 8) % 1, (y * 8) % 1
        lit = (u > .16) & (u < .72) & (v > .24) & (v < .65)
        occupied = np.sin(np.floor(x * 8) * 7 + np.floor(y * 8) * 17) > -.35
        warm = np.sin(np.floor(x * 8) * 13 + np.floor(y * 8) * 3) > .1
        rgb = np.tile(np.array((.10, .13, .16)), (*x.shape, 1))
        rgb[lit] = (.035, .055, .075)
        light = lit & occupied
        rgb[light & warm] = (.70, .46, .19)
        rgb[light & ~warm] = (.25, .51, .62)
        emission = np.zeros_like(rgb)
        emission[light & warm] = (1, .62, .24)
        emission[light & ~warm] = (.22, .63, .85)
    elif kind == "safety":
        stripe = ((x + y) * 12) % 1 < .30
        rgb[stripe] *= .24
    packed = np.zeros_like(rgb)
    packed[:, :, 0] = 1
    packed[:, :, 1] = np.clip(rough + grain * 3 + seam * .15, 0, 1)
    packed[:, :, 2] = metal
    dy, dx = np.gradient(relief)
    normal = np.dstack((-dx * 1.3, -dy * 1.3, np.ones_like(x)))
    normal /= np.linalg.norm(normal, axis=2)[:, :, None]
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    bsdf = nodes.get("Principled BSDF")
    def texture(label, pixels, data=False):
        node = nodes.new("ShaderNodeTexImage")
        node.image = image(name + "_" + label, pixels, data)
        return node
    base = texture("base", rgb)
    links.new(base.outputs["Color"], bsdf.inputs["Base Color"])
    orm = texture("orm", packed, True)
    separate = nodes.new("ShaderNodeSeparateColor")
    links.new(orm.outputs["Color"], separate.inputs[0])
    links.new(separate.outputs["Green"], bsdf.inputs["Roughness"])
    links.new(separate.outputs["Blue"], bsdf.inputs["Metallic"])
    normal_map = nodes.new("ShaderNodeNormalMap")
    normal_map.inputs["Strength"].default_value = .45
    links.new(texture("normal", normal * .5 + .5, True).outputs["Color"], normal_map.inputs["Color"])
    links.new(normal_map.outputs[0], bsdf.inputs["Normal"])
    if emission is not None:
        links.new(texture("emission", emission).outputs["Color"], bsdf.inputs["Emission Color"])
        bsdf.inputs["Emission Strength"].default_value = 2.2
    MATERIALS[name] = mat
    return name


def face(mat, points, uv=None):
    verts, faces, uvs = PARTS.setdefault(mat, ([], [], []))
    start = len(verts)
    verts.extend(points)
    faces.append(tuple(range(start, start + len(points))))
    if uv is None:
        width = (Vector(points[1]) - Vector(points[0])).length
        height = (Vector(points[-1]) - Vector(points[0])).length
        scale = .65 if mat in ("Hull", "Structure") else 1
        uv = [(0, 0), (width*scale, 0), (width*scale, height*scale), (0, height*scale)]
    uvs.extend(uv)


def box(mat, center, size, angle=0):
    x, y, z = (s / 2 for s in size)
    verts = [(-x,-y,-z), (x,-y,-z), (x,y,-z), (-x,y,-z),
             (-x,-y,z), (x,-y,z), (x,y,z), (-x,y,z)]
    c, s = math.cos(angle), math.sin(angle)
    verts = [(center[0]+a*c-b*s, center[1]+a*s+b*c, center[2]+d) for a,b,d in verts]
    for indices in [(0,3,2,1), (4,5,6,7), (0,1,5,4), (1,2,6,5), (2,3,7,6), (3,0,4,7)]:
        face(mat, [verts[i] for i in indices])


def beam(mat, a, b, radius=.025, sides=6):
    axis = Vector(b) - Vector(a)
    basis = axis.to_track_quat("Z", "Y")
    rings = [[Vector(p) + basis @ Vector((radius*math.cos(i*math.tau/sides), radius*math.sin(i*math.tau/sides), 0))
              for i in range(sides)] for p in (a, b)]
    # Fans are explicitly triangulated and closed, like all structural parts.
    cap_uv = [(radius*math.cos(i*math.tau/sides)*.65,
               radius*math.sin(i*math.tau/sides)*.65) for i in range(sides)]
    for i in range(sides):
        j = (i + 1) % sides
        face(mat, [rings[0][i], rings[0][j], rings[1][j], rings[1][i]])
        face(mat, [Vector(a), rings[0][j], rings[0][i]], [(0,0),cap_uv[j],cap_uv[i]])
        face(mat, [Vector(b), rings[1][i], rings[1][j]], [(0,0),cap_uv[i],cap_uv[j]])


def arc(mat, radius, section, start=0, end=math.tau, steps=72, window_side=-1):
    for i in range(steps):
        a, b = start + (end-start)*i/steps, start + (end-start)*(i+1)/steps
        def point(theta, radial, z):
            return ((radius+radial)*math.cos(theta), (radius+radial)*math.sin(theta), z)
        for j, (radial,z) in enumerate(section):
            other = section[(j+1) % len(section)]
            face("Windows" if j == window_side else mat,
                 [point(a,radial,z), point(b,radial,z), point(b,*other), point(a,*other)])
    if end-start < math.tau-.001:
        # Caps of segmented habitation modules, triangulated around their center.
        for theta, reverse in [(start, False), (end, True)]:
            center = (radius*math.cos(theta), radius*math.sin(theta), sum(z for _,z in section)/len(section))
            for j in range(len(section)):
                p, q = point(theta,*section[j]), point(theta,*section[(j+1)%len(section)])
                face(mat, [center, q, p] if reverse else [center, p, q], [(0,0),(.5,0),(0,.5)])


def cylinder(mat, z, radius, height, sides=24):
    beam(mat, (0,0,z-height/2), (0,0,z+height/2), radius, sides)


def label(text, location, size, rotation=(math.pi/2, 0, 0), mat="Hull"):
    curve = bpy.data.curves.new("Marking_" + text, "FONT")
    curve.body = text
    curve.align_x = "CENTER"
    curve.size = size
    curve.extrude = .0003
    obj = bpy.data.objects.new(curve.name, curve)
    bpy.context.collection.objects.link(obj)
    obj.location, obj.rotation_euler = location, rotation
    curve.materials.append(MATERIALS[mat])
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.convert(target="MESH")
    obj.select_set(False)
    return obj


def anchor(name, location):
    obj = bpy.data.objects.new(name, None)
    obj.location = location
    bpy.context.collection.objects.link(obj)
    return obj


def build():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    material("Hull", (.46,.52,.57), .65, .43)
    material("Structure", (.14,.19,.23), .82, .48)
    material("Solar", (.11,.26,.42), .38, .34, "solar")
    material("Thermal", (.29,.23,.18), .78, .59, "thermal")
    material("Safety", (.82,.32,.055), .35, .50, "safety")
    material("Windows", (.1,.14,.18), .35, .30, "windows")
    anchors = []
    # Octagonal cross section gives the ring hard, beveled shoulders.
    section = [(-.28,-.13),(-.19,-.23),(.19,-.23),(.28,-.13),
               (.28,.13),(.19,.23),(-.19,.23),(-.28,.13)]
    for i in range(24):
        a, b = i*math.tau/24+.009, (i+1)*math.tau/24-.009
        arc("Hull", 2.9, section, a,b,3,window_side=3)
        theta = (a+b)/2
        lamp = (3.20*math.cos(theta),3.20*math.sin(theta),.02)
        anchors.append(anchor(f"Light_{'Warm' if i%3==0 else 'Cool'}_{i:02}",lamp))
        box("Structure",(3.19*math.cos(theta),3.19*math.sin(theta),.02),(.015,.025,.018),theta)
        box("Windows",lamp,(.006,.018,.009),theta)
        # Visible structural band clamps unite the segmented habitation ring.
        arc("Structure", 2.9, [(-.30,-.26),(.30,-.26),(.30,-.22),(-.30,-.22)], a,b,3)
        box("Safety" if i % 6 == 0 else "Structure",
            (2.9*math.cos(a),2.9*math.sin(a),0), (.075,.58,.53), a+math.pi/2)
        # Service units on the upper deck, regularly spaced and connected.
        if i % 3 == 0:
            box("Structure", (2.9*math.cos(theta),2.9*math.sin(theta),.28), (.23,.35,.10),theta)
            box("Thermal", (2.9*math.cos(theta),2.9*math.sin(theta),.342), (.19,.29,.03),theta)
    arc("Structure", 2.9, [(-.22,-.36),(.22,-.36),(.22,-.28),(-.22,-.28)])
    cylinder("Hull", 0, 1.03, 2.5, 32)
    cylinder("Structure", -1.26, 1.12, .16, 32)
    cylinder("Structure", 1.26, 1.12, .16, 32)
    cylinder("Thermal", -1.48, .72, .28, 24)
    cylinder("Hull", 1.57, .72, .48, 24)
    cylinder("Structure", 2.06, .46, .50, 24)
    cylinder("Hull", 3.12, .32, 1.64, 16)
    for z in (-.9,-.3,.3,.9):
        arc("Structure",1.075,[(-.015,z-.018),(.015,z-.018),(.015,z+.018),(-.015,z+.018)],steps=48)
    for i in range(12):
        angle = i*math.tau/12
        r = 1.045
        box("Structure", (r*math.cos(angle),r*math.sin(angle),0),(.075,.13,2.35),angle)
    # Pressure corridors and triangulated load-bearing trusses.
    for i in range(4):
        theta = math.pi/4+i*math.pi/2
        def p(r, t, z):
            return (r*math.cos(theta)-t*math.sin(theta),r*math.sin(theta)+t*math.cos(theta),z)
        box("Hull",p(1.85,0,0), (1.72,.16,.22), theta)
        for side in (-1,1):
            beam("Structure",p(1.04,side*.19,-.18),p(2.64,side*.19,-.18),.025)
            for j in range(5):
                beam("Structure",p(1.04+j*.32,side*.19,-.18),p(1.36+j*.32,side*.19,.11),.017)
                beam("Structure",p(1.04+j*.32,side*.19,.11),p(1.36+j*.32,side*.19,-.18),.017)
    # Solar wings retain the original extents; frames and busbars explain their structure.
    for side in (-1,1):
        wing_starts = {name:len(data[0]) for name,data in PARTS.items()}
        beam("Structure", (side*3.05,0,-.18),(side*6.97,0,-.18),.065,8)
        for i in range(3):
            x = side*(4.63+i*.90)
            box("Solar", (x,0,.04),(.84,1.48,.035))
            for y in (-.78,.78):
                box("Structure", (x,y,.025),(.90,.055,.10))
            for dx in (-.45,.45):
                box("Structure", (x+dx,0,.025),(.055,1.60,.10))
            beam("Structure",(side*3.12,0,-.22),(x,.70,-.02),.025)
            beam("Structure",(side*3.12,0,-.22),(x,-.70,-.02),.025)
        box("Safety",(side*7.05,0,.04),(.10,1.70,.12))
        # Cant the complete photovoltaic assembly towards the grazing starlight.
        # The exported solid geometry, not a client-only material, defines this pose.
        for name,(vertices,_,_) in PARTS.items():
            for index in range(wing_starts.get(name,0),len(vertices)):
                x,y,z = vertices[index]
                vertices[index] = (x,(y-z)/math.sqrt(2),(y+z)/math.sqrt(2))
        # Independent thermal wings, perpendicular to the photovoltaics.
        beam("Structure",(0,side*3.10,-.23),(0,side*4.50,-.23),.06)
        for x in (-.42,.42):
            box("Thermal",(x,side*4.05,-.22),(.76,1.42,.07))
            for dx in (-.40,.40):
                box("Structure",(x+dx,side*4.05,-.22),(.035,1.50,.11))
        # Radial cargo pods attached to the hub, away from the four ring passages.
        box("Hull",(0,side*1.17,.62),(.72,.62,.72))
        box("Safety",(0,side*1.50,.62),(.60,.035,.18))
    # Axial service berth; ship-sized clearance stays fixed when the station grows.
    cylinder("Structure", 4.00, .74, .16, 32)
    arc("Hull",0,[(.53,4.04),(.74,4.04),(.74,4.45),(.53,4.45)],steps=48)
    arc("Safety",0,[(.52,4.45),(.76,4.45),(.76,4.48),(.52,4.48)],steps=48)
    for i in range(8):
        theta = i*math.tau/8
        beam("Structure",(.30*math.cos(theta),.30*math.sin(theta),3.30),
             (.70*math.cos(theta),.70*math.sin(theta),4.0),.028)
        box("Windows",(.66*math.cos(theta),.66*math.sin(theta),4.50),(.08,.12,.04),theta)
        box("Structure",(.75*math.cos(theta),.75*math.sin(theta),4.27),(.10,.16,.36),theta)
    for i in range(4):
        theta = math.pi/4+i*math.pi/2
        point = (.46*math.cos(theta),.46*math.sin(theta),4.56)
        anchors.append(anchor(f"Light_Dock_{i:02}",(point[0],point[1],point[2]-.018)))
        beam("Structure",(.67*math.cos(theta),.67*math.sin(theta),4.42),point,.006)
        box("Structure",point,(.025,.045,.025),theta)
    # External maintenance platforms connect to the solid outer habitation wall.
    for i in range(6):
        theta = math.pi/6+i*math.pi/3
        def deck(r,t,z):
            return (r*math.cos(theta)-t*math.sin(theta),r*math.sin(theta)+t*math.cos(theta),z)
        box("Structure",deck(3.40,0,-.23),(.50,.55,.035),theta)
        box("Safety",deck(3.40,0,-.209),(.45,.50,.006),theta)
        box("Hull",deck(3.24,0,-.13),(.16,.29,.16),theta)
        box("Thermal",deck(3.25,0,-.043),(.13,.25,.018),theta)
        for side in (-1,1):
            beam("Structure",deck(3.17,side*.24,-.33),deck(3.63,side*.24,-.23),.008)
            for r in (3.22,3.42,3.62):
                beam("Structure",deck(r,side*.265,-.21),deck(r,side*.265,-.198),.0015)
            beam("Hull",deck(3.20,side*.265,-.198),deck(3.64,side*.265,-.198),.0015)
    # External service lines stop under the berth; the approach volume stays open.
    for angle in (math.pi/2, math.pi, math.pi*1.5):
        for offset in (-.035,.035):
            x,y = .35*math.cos(angle)-offset*math.sin(angle),.35*math.sin(angle)+offset*math.cos(angle)
            beam("Thermal",(x,y,2.4),(x,y,3.8),.018,8)
        for z in (2.5,3.0,3.5):
            box("Structure",(.36*math.cos(angle),.36*math.sin(angle),z),(.09,.17,.055),angle)
    box("Structure",(0,-1.055,.0),(1.35,.10,.61))
    labels = [label("KESTREL", (0,-1.112,.10),.22), label("R E A C H",(0,-1.112,-.16),.16),
              label("01",(0,-.748,4.16),.21,mat="Structure"),
              label("01",(.64,0,4.49),.16,rotation=(0,0,math.pi/2),mat="Structure")]
    # Batch by material: six mesh surfaces, plus tiny solid raised markings.
    objects = []
    for name,(vertices,faces,uvs) in PARTS.items():
        mesh = bpy.data.meshes.new(name)
        mesh.from_pydata(vertices,[],faces)
        mesh.materials.append(MATERIALS[name])
        mesh.update()
        uv = mesh.uv_layers.new(name="UVMap")
        for loop,coord in zip(uv.data,uvs):
            loop.uv = (coord[0]*SCALE/55,coord[1]*SCALE/55)
        obj = bpy.data.objects.new(name,mesh)
        bpy.context.collection.objects.link(obj)
        objects.append(obj)
    objects.extend(labels)
    objects.extend(anchors)
    marker = bpy.data.objects.new("DockingPoint",None)
    marker.location = (0,0,4.48+13.6/SCALE)
    bpy.context.collection.objects.link(marker)
    objects.append(marker)
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.export_scene.gltf(filepath=str(ROOT / "client/assets/models/station_kestrel.glb"),
        export_format="GLB", use_selection=True, export_apply=True, export_yup=True,
        export_cameras=False, export_lights=False)
    for img in bpy.data.images:
        if img.packed_file:
            img.filepath_raw = "//station_textures/" + img.name + ".png"
            for packed in img.packed_files:
                packed.filepath = img.filepath_raw
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCES / "station_kestrel.blend"))
    triangles = sum(len(poly.vertices)-2 for obj in objects if obj.type == "MESH" for poly in obj.data.polygons)
    receipt = {"asset":"station_kestrel","source":"tools/art/build_station.py", "seed":20261102,
               "blender":bpy.app.version_string,"provenance":"original project geometry, textures and markings; no third-party assets",
               "license":"distribution license follows the project owner's decision; not yet declared in repository",
               "model_scale_metres":SCALE,"docking_point_metres":[0,4.48*SCALE+13.6,0],
               "triangles":triangles,"mesh_nodes":sum(obj.type=="MESH" for obj in objects),"materials":len(MATERIALS),
               "light_markers":[obj.name for obj in anchors],
               "texture_size":SIZE,"texture_files":sorted(p.name for p in TEXTURES.glob("*.png")),
               "glb_sha256":hashlib.sha256((ROOT / "client/assets/models/station_kestrel.glb").read_bytes()).hexdigest(),
               "generator_sha256":hashlib.sha256(Path(__file__).read_bytes()).hexdigest()}
    (SOURCES / "station_kestrel.json").write_text(json.dumps(receipt,indent=2)+"\n")
    print("STATION BUILT",json.dumps(receipt))


if __name__ == "__main__":
    TEXTURES.mkdir(parents=True,exist_ok=True)
    SOURCES.mkdir(parents=True,exist_ok=True)
    build()
