"""Original Wren utility courier. Blender CLI; reuses the station's mesh/UV helpers.

BLENDER_BIN --background --factory-startup --python tools/art/build_ship.py
Game coordinates: metres, +Y up, nose -Z. Only ship outputs are replaced.
"""
import hashlib
import json
import math
import sys
from pathlib import Path

import bpy
import bmesh
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

sys.path.insert(0, str(Path(__file__).resolve().parent))
import build_station as art

ROOT = Path(__file__).resolve().parents[2]
SOURCES = ROOT / "tools/art/sources"
TEXTURES = SOURCES / "ship_textures"
art.TEXTURES = TEXTURES
art.SIZE = 2048
MARKERS = []


def p(v):
    return (v[0], -v[2], v[1])


def face(mat, points, uv=None):
    art.face(mat, [p(v) for v in points], uv)


def box(mat, center, size):
    art.box(mat, p(center), (size[0], size[2], size[1]))


def beam(mat, a, b, radius=.06, sides=8):
    art.beam(mat, p(a), p(b), radius, sides)


def marker(name, position, exhaust=None):
    if name.startswith("Maneuver_"):
        axis=Vector(exhaust)
        center=Vector(position)-axis*.01
        ring("Frame",*center,.155,.105,.04,sides=12,direction=exhaust)
        position=Vector(position)+axis*.015
    obj = art.anchor(name, p(position))
    if exhaust is not None:
        obj.rotation_mode = "QUATERNION"
        obj.rotation_quaternion = Vector(p(exhaust)).to_track_quat("Y", "Z")
    MARKERS.append(obj)
    return obj


def material(name, color, metal, rough, kind="panels"):
    size=art.SIZE
    y,x=np.mgrid[0:size,0:size]/size
    rng=np.random.default_rng(20261103)
    grain=rng.normal(0,.012,x.shape)
    # Multiple scales of pigment, abrasive marks and contamination, not baked lighting.
    def cloud(cells):
        values=rng.random((cells+1,cells+1))
        xx=x*cells; yy=y*cells
        ix=xx.astype(int);iy=yy.astype(int)
        fx=xx-ix;fy=yy-iy
        fx=fx*fx*(3-2*fx);fy=fy*fy*(3-2*fy)
        return (values[iy,ix]*(1-fx)+values[iy,ix+1]*fx)*(1-fy)+(values[iy+1,ix]*(1-fx)+values[iy+1,ix+1]*fx)*fy
    coarse=cloud(8);fine=cloud(256)
    u,v=x*4%1,y*4%1
    edge=np.minimum.reduce([u,1-u,v,1-v])
    seam=edge<.008
    grime=np.exp(-edge*32)*(.15+.35*coarse)
    chip=(edge<.009)&(fine+coarse*.25>.92)
    bolt_distance=np.minimum((u-.055)**2+(v-.055)**2,(u-.945)**2+(v-.055)**2)
    bolt=bolt_distance<.00016
    socket=(bolt_distance<.000055)
    hatch=(u>.25)&(u<.80)&(v>.32)&(v<.76)
    hatch_edge=hatch&((u<.26)|(u>.79)|(v<.33)|(v>.75))
    pigment=.83+.20*coarse+.035*np.sin(np.floor(x*4)*7+np.floor(y*4)*13)
    rgb=pigment[:,:,None]*np.array(color)
    rgb*=1-grime[:,:,None]
    rgb+=grain[:,:,None]*.25
    rgb[hatch]*=.88
    rgb[seam|hatch_edge]*=.25
    exposed=chip|bolt
    rgb[exposed]=np.array((.37,.40,.42))*(.8+.2*fine[exposed,None])
    rgb[socket]*=.25
    # Short abrasions follow the manufactured surface direction.
    scratches=np.zeros_like(x,dtype=bool)
    for _ in range(1800):
        sx,sy=rng.integers(0,size,2);length=int(rng.integers(3,50))
        scratches[sy:min(size,sy+1),sx:min(size,sx+length)]=True
    scratches &= fine>.40
    rgb[scratches]*=.65
    relief=np.where(seam|hatch_edge,-.0015,0.0)
    relief=np.where(bolt,.002,relief)
    relief=np.where(socket,-.0006,relief)
    relief=np.where(chip,-.00025,relief)
    emission=None
    if kind=="thermal":
        fins=y*64%1
        rgb*=np.where(fins<.32,.22,1)[:,:,None]
        rgb*=np.stack((.82+.25*coarse,.75+.2*fine,.66+.22*coarse),axis=2)
        relief=np.where(fins<.32,-.002,0)
        exposed=np.ones_like(x,dtype=bool)
    elif kind=="glass":
        rgb=np.ones_like(rgb)*np.array(color)*(1+grain[:,:,None]*.3)
        relief=np.zeros_like(x);exposed=np.zeros_like(x,dtype=bool)
    elif kind=="safety":
        rgb[((x+y)*12)%1<.35]*=.12
    elif kind=="emitter":
        grille=((x*32)%1<.09)|((y*32)%1<.09)
        rgb=np.ones_like(rgb)*np.array(color);rgb[grille]*=.16
        emission=rgb*.8
        relief=np.where(grille,-.001,0)
    # Own tiny stencil font, only pigment; no extruded letters or external font files.
    glyphs={"N":["101","111","111","101","101"],"O":["111","101","101","101","111"],
        "S":["111","100","111","001","111"],"T":["111","010","010","010","010"],
        "E":["111","100","110","100","111"],"P":["110","101","110","100","100"],
        "0":["111","101","101","101","111"],"7":["111","001","010","010","010"]}
    if kind=="panels":
        ox,oy=68,100
        for letter in "NO STEP 07":
            for row,line in enumerate(glyphs.get(letter,[])):
                for col,pixel in enumerate(line):
                    if pixel=="1":rgb[oy+(4-row)*3:oy+(4-row)*3+3,ox+col*3:ox+col*3+3]=(.68,.65,.52)
            ox+=12
    packed=np.zeros_like(rgb);packed[:,:,0]=1
    packed[:,:,1]=np.clip(rough+(coarse-.5)*.25+grain*2+grime*.22,0,1)
    packed[:,:,2]=metal
    if kind=="glass":packed[:,:,1]=rough+grain*.2
    if kind in ["panels","safety"]:
        packed[:,:,2]=np.where(exposed,.94,metal)
        packed[:,:,1]=np.where(exposed,.27+fine*.12,packed[:,:,1])
    dy,dx=np.gradient(relief,1/(size*.13))
    normal=np.dstack((-dx,-dy,np.ones_like(x)))
    normal/=np.linalg.norm(normal,axis=2)[:,:,None]
    mat=bpy.data.materials.new(name);mat.use_nodes=True
    nodes,links=mat.node_tree.nodes,mat.node_tree.links
    bsdf=nodes.get("Principled BSDF")
    def texture(label,pixels,data=False):
        node=nodes.new("ShaderNodeTexImage");node.image=art.image(name+"_"+label,pixels,data)
        return node
    links.new(texture("base",rgb).outputs["Color"],bsdf.inputs["Base Color"])
    orm=texture("orm",packed,True);separate=nodes.new("ShaderNodeSeparateColor")
    links.new(orm.outputs["Color"],separate.inputs[0])
    links.new(separate.outputs["Green"],bsdf.inputs["Roughness"])
    links.new(separate.outputs["Blue"],bsdf.inputs["Metallic"])
    bump=nodes.new("ShaderNodeNormalMap");bump.inputs["Strength"].default_value=1
    links.new(texture("normal",normal*.5+.5,True).outputs["Color"],bump.inputs["Color"])
    links.new(bump.outputs[0],bsdf.inputs["Normal"])
    if emission is not None:
        links.new(texture("emission",emission).outputs["Color"],bsdf.inputs["Emission Color"])
        bsdf.inputs["Emission Strength"].default_value=1.5
    art.MATERIALS[name]=mat


def loft(mat, sections, x=0, y=0):
    loops=[]
    for z,w,h,cy in sections:
        loops.append([(x+a,y+cy+b,z) for a,b in
            [(-w*.65,-h),(w*.65,-h),(w,-h*.55),(w,h*.55),
             (w*.65,h),(-w*.65,h),(-w,h*.55),(-w,-h*.55)]])
    for a,b in zip(loops,loops[1:]):
        for i in range(8):
            j=(i+1)%8
            face(mat,[a[i],a[j],b[j],b[i]])
    for loop,reverse in [(loops[0],True),(loops[-1],False)]:
        center=tuple(sum(v[k] for v in loop)/8 for k in range(3))
        for i in range(8):
            j=(i+1)%8
            points=[center,loop[j],loop[i]] if reverse else [center,loop[i],loop[j]]
            face(mat,points,[(v[0]*.65,v[1]*.65) for v in points])


def ring(mat,x,y,z,outer,inner,depth,sides=24,direction=(0,0,1)):
    axis=Vector(direction)
    reference=Vector((0,1,0)) if abs(axis.y)<.9 else Vector((1,0,0))
    right=reference.cross(axis).normalized()
    up=axis.cross(right)
    center=Vector((x,y,z))
    for i in range(sides):
        a,b=i*math.tau/sides,(i+1)*math.tau/sides
        def q(t,r,d):return center+right*(r*math.cos(t))+up*(r*math.sin(t))+axis*d
        face(mat,[q(a,outer,-depth/2),q(b,outer,-depth/2),q(b,outer,depth/2),q(a,outer,depth/2)])
        face(mat,[q(a,inner,depth/2),q(b,inner,depth/2),q(b,inner,-depth/2),q(a,inner,-depth/2)])
        for d in [-depth/2,depth/2]:
            points=[q(a,inner,d),q(a,outer,d),q(b,outer,d),q(b,inner,d)]
            face(mat,points if d>0 else points[::-1])


def clean_mesh(mesh):
    bm=bmesh.new();bm.from_mesh(mesh)
    bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=1e-7)
    bmesh.ops.dissolve_degenerate(bm,edges=list(bm.edges),dist=1e-7)
    bmesh.ops.triangulate(bm,faces=list(bm.faces),quad_method="FIXED",ngon_method="EAR_CLIP")
    empty=[face for face in bm.faces if face.calc_area()<=5e-13]
    if empty:bmesh.ops.delete(bm,geom=empty,context="FACES_ONLY")
    bm.to_mesh(mesh);bm.free();mesh.update()


def build():
    bpy.ops.object.select_all(action="SELECT");bpy.ops.object.delete(use_global=False)
    material("Paint",(.32,.36,.38),0,.58)
    material("Ceramic",(.055,.10,.12),0,.64)
    material("Frame",(.23,.25,.26),.95,.32)
    material("Thermal",(.22,.17,.13),.94,.48,"thermal")
    material("Glass",(.045,.12,.16),0,.10,"glass")
    material("Safety",(.82,.36,.07),.25,.57,"safety")
    material("Emitter",(.16,.66,.92),.3,.35,"emitter")
    loft("Paint",[(-11.8,.7,.45,-.3),(-9,1.8,.8,-.2),(-5,2.4,1.35,-.1),
        (1,2.5,1.6,-.1),(6.8,2.0,1.2,-.2),(8.8,1.3,.7,-.2)])
    # Dorsal spine and ventral cargo keel explain the long utility hull.
    loft("Ceramic",[(-10, .45,.15,.6),(-4,.60,.18,1.32),(2,.60,.18,1.52),(7,.40,.15,1.1)])
    loft("Frame",[(-7,.65,.25,-1.1),(-3,.95,.3,-1.6),(4,.95,.3,-1.6),(7,.6,.2,-1.25)])
    for side in [-1,1]:
        # Engine nacelles are integrated with the pressure hull, rather than wings.
        loft("Frame",[(-1.6,.65,.65,0),(0,1.05,1.0,0),(6.8,1.05,1.0,0),(9.1,.85,.85,0)],x=side*3.35,y=-.65)
        box("Paint",(side*2.55,-.45,2.6),(1.0,1.1,6.4))
        for z in [0.1,2.5,4.9,7.4]:
            ring("Paint",side*3.35,-.65,z,1.10,1.02,.16)
        ring("Thermal",side*3.35,-.65,9.1,.91,.66,.90)
        ring("Frame",side*3.35,-.65,9.65,.94,.70,.28)
        beam("Emitter",(side*3.35,-.65,9.24),(side*3.35,-.65,9.26),.64,24)
        marker("MainNozzle_"+("Left" if side<0 else "Right"),(side*3.35,-.65,9.85),(0,0,1))
        # Replaceable side equipment, radiator plates, attachment saddles and service lines.
        for z in [-4.2,-1.9,.4,2.7]:
            box("Ceramic",(side*2.47,.25,z),(.26,1.65,1.9))
            box("Frame",(side*2.64,.25,z),(.12,.9,1.30))
            box("Safety",(side*2.71,.65,z),(.025,.12,.72))
        for z in [2.4,4.2,6.0]:
            box("Thermal",(side*1.35,1.2,z),(1.0,.12,1.5))
            for x in [.81,1.89]:box("Frame",(side*x,1.28,z),(.07,.14,1.62))
        beam("Thermal",(side*2.8,.20,-1),(side*2.8,.20,6.7),.08)
        for z in [-1,1,3,5,6.7]:box("Paint",(side*2.8,.2,z),(.28,.25,.12))
        box("Safety",(side*1.1,.72,-8.6),(.32,.06,1.7))
        # Solid RCS housings and their exported outlet poses.
        for z,word in [(-7.1,"Front"),(6.6,"Rear")]:
            x=side*(2.05 if z<0 else 4.40)
            box("Frame",(x,-.35,z),(.38,.72,.62))
            marker(f"Maneuver_{word}_{'Left' if side<0 else 'Right'}",(x+side*.21,-.35,z),(side,0,0))
            for dy,label in [(-1,"Down"),(1,"Up")]:
                port_x=side*(1.85 if z<0 and dy>0 else 1.55)
                beam("Frame",(port_x,dy*.55,z),(port_x,dy*1.45,z),.13)
                box("Frame",(port_x,dy*1.45,z),(.5,.22,.5))
                marker(f"Maneuver_{word}_{'L' if side<0 else 'R'}_{label}",(port_x,dy*1.58,z),(0,dy,0))
        box("Frame",(side*1.05,-.2,-10.95),(.34,.4,.65))
        marker(f"Maneuver_Retro_{'L' if side<0 else 'R'}",(side*1.05,-.2,-11.29),(0,0,-1))
    # Raked bridge glazing grows out of an armored foredeck and tapers into the spine.
    loft("Paint",[(-8.8,1.4,.25,.6),(-7.1,1.95,.45,.95),
        (-3.1,1.9,.4,1.15),(-1.7,1.4,.15,1.4)])
    front=[(-1.5,1.33,-7.75),(1.5,1.33,-7.75),(1.55,2.45,-6.85),
        (.95,2.7,-6.65),(-.95,2.7,-6.65),(-1.55,2.45,-6.85)]
    back=[(-1.5,1.2,-3.1),(1.5,1.2,-3.1),(1.25,2.25,-3.6),
        (.75,2.45,-3.6),(-.75,2.45,-3.6),(-1.25,2.25,-3.6)]
    face("Glass",front[::-1],[(v[0]*.65,v[1]*.65) for v in front[::-1]])
    face("Frame",back,[(v[0]*.65,v[1]*.65) for v in back])
    for i in range(6):
        j=(i+1)%6
        face("Glass" if i in [1,5] else "Ceramic",[front[i],front[j],back[j],back[i]])
        beam("Paint",front[i],front[j],.035)
        beam("Paint",front[i],back[i],.045)
    loft("Paint",[(-3.6,1.25,.45,2.0),(-1.2,1.2,.12,1.42)])
    marker("PilotEye",(0,1.85,-5.25))
    # Avionics and sensor stalks, deliberately modest relative to the hull.
    loft("Paint",[(-.45,.24,.10,1.6),(0,.45,.24,1.72),
        (.65,.38,.20,1.70),(1.1,.20,.08,1.57)],x=.35)
    beam("Frame",(.35,1.97,.35),(.35,2.75,.25),.045)
    box("Ceramic",(.35,2.79,.25),(.5,.08,.65))
    for side in [-1,1]:
        box("Emitter",(side*2.08,.3,-7.5),(.08,.12,.3))
        marker(f"RunningLight_{'L' if side<0 else 'R'}",(side*2.15,.3,-7.5))
    box("Paint",(0,1.60,3.6),(2.4,.08,1.0))
    box("Paint",(0,.64,-9.3),(1.35,.14,.65))
    objects=[]
    for name,(vertices,faces,uvs) in art.PARTS.items():
        mesh=bpy.data.meshes.new(name);mesh.from_pydata(vertices,[],faces);mesh.materials.append(art.MATERIALS[name]);mesh.update()
        uv=mesh.uv_layers.new(name="UVMap")
        obj=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(obj)
        bm=bmesh.new();bm.from_mesh(mesh)
        bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.00001)
        for edge in bm.edges:edge.smooth=edge.calc_face_angle(0)<.5
        bm.to_mesh(mesh);bm.free()
        for poly in mesh.polygons:poly.use_smooth=True
        bpy.context.view_layer.objects.active=obj;obj.select_set(True)
        if name!="Glass":
            bevel=obj.modifiers.new("Machined edges","BEVEL");bevel.width=.025;bevel.segments=1
            bevel.limit_method="ANGLE";bevel.angle_limit=.5
            bevel.harden_normals=True
            bpy.ops.object.modifier_apply(modifier=bevel.name)
        clean_mesh(obj.data)
        if name!="Glass":
            normals=obj.modifiers.new("Panel normals","WEIGHTED_NORMAL");normals.keep_sharp=True
            bpy.ops.object.modifier_apply(modifier=normals.name)
        # Bevel's UV interpolation varies by float epsilon across runs. Project the
        # final mesh instead, keeping density and handedness explicit and reproducible.
        mesh=obj.data;mesh.update();uv=mesh.uv_layers["UVMap"]
        for poly in mesh.polygons:
            axis=max(range(3),key=lambda i:abs(poly.normal[i]))
            sign=1 if poly.normal[axis]>=0 else -1
            for index in poly.loop_indices:
                vertex=mesh.vertices[mesh.loops[index].vertex_index].co
                a,b=((vertex.y*sign,vertex.z) if axis==0 else (-vertex.x*sign,vertex.z) if axis==1 else (vertex.x*sign,vertex.y))
                uv.data[index].uv=(round(a*.13,6),round(b*.13,6))
        obj.select_set(False);objects.append(obj)
    for text,location,size in [("W R E N",(0,1.642,3.6),.55),("C / 07",(0,.712,-9.3),.28)]:
        obj=art.label(text,p(location),size,rotation=(0,0,0),mat="Ceramic")
        clean_mesh(obj.data);objects.append(obj)
    objects.extend(MARKERS)
    vertices=[];polygons=[]
    for obj in objects:
        if obj.type!="MESH":continue
        offset=len(vertices)
        vertices.extend(obj.matrix_world @ vertex.co for vertex in obj.data.vertices)
        polygons.extend(tuple(offset+i for i in poly.vertices) for poly in obj.data.polygons)
    tree=BVHTree.FromPolygons(vertices,polygons)
    for obj in MARKERS:
        if not obj.name.startswith(("MainNozzle_","Maneuver_")):continue
        axis=obj.rotation_quaternion @ Vector((0,1,0))
        right=obj.rotation_quaternion @ Vector((1,0,0))
        up=obj.rotation_quaternion @ Vector((0,0,1))
        radius=.55 if obj.name.startswith("MainNozzle_") else .08
        for offset in [Vector(),right*radius,-right*radius,up*radius,-up*radius]:
            hit=tree.ray_cast(obj.location+offset+axis*.001,axis,2.0)
            if hit[0] is not None:raise RuntimeError(f"Blocked exhaust: {obj.name}, distance {hit[3]}")
    print("SHIP OUTLETS VERIFIED: 16 outlets, five rays each, 2 metres clear.")
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:obj.select_set(True)
    bpy.context.view_layer.objects.active=objects[0]
    output=ROOT/"client/assets/models/ship_player.glb"
    bpy.ops.export_scene.gltf(filepath=str(output),export_format="GLB",use_selection=True,export_apply=True,export_yup=True,export_cameras=False,export_lights=False)
    for img in bpy.data.images:
        if img.packed_file:
            img.filepath_raw="//ship_textures/"+img.name+".png"
            for packed in img.packed_files:packed.filepath=img.filepath_raw
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCES/"ship_wren.blend"))
    triangles=sum(len(poly.vertices)-2 for obj in objects if obj.type=="MESH" for poly in obj.data.polygons)
    vertices=[obj.matrix_world @ vertex.co for obj in objects if obj.type=="MESH" for vertex in obj.data.vertices]
    points=[(v.x,v.z,-v.y) for v in vertices]
    lower=[min(point[i] for point in points) for i in range(3)]
    upper=[max(point[i] for point in points) for i in range(3)]
    attachments={}
    for obj in MARKERS:
        v=obj.location
        pose={"position_metres":[v.x,v.z,-v.y]}
        if obj.name.startswith(("MainNozzle_","Maneuver_")):
            axis=obj.rotation_quaternion @ Vector((0,1,0))
            pose["exhaust_direction"]=[axis.x,axis.z,-axis.y]
        attachments[obj.name]=pose
    receipt={"asset":"ship_player","design":"Wren utility courier","source":"tools/art/build_ship.py","helper":"tools/art/build_station.py",
        "blender":bpy.app.version_string,"seed":20261103,"provenance":"original project geometry and textures; no third-party assets",
        "license":"distribution license follows the project owner's decision; not yet declared in repository",
        "scale_metres":1,"nose":"-Z","pilot_eye_metres":[0,1.85,-5.25],"triangles":triangles,"materials":len(art.MATERIALS),
        "mesh_nodes":sum(obj.type=="MESH" for obj in objects),"texture_size":art.SIZE,
        "markers":[obj.name for obj in MARKERS],"glb_sha256":hashlib.sha256(output.read_bytes()).hexdigest(),
        "bounds_metres":{"min":lower,"max":upper,"size":[upper[i]-lower[i] for i in range(3)]},
        "attachments":attachments,
        "generator_sha256":hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
        "helper_sha256":hashlib.sha256(Path(art.__file__).read_bytes()).hexdigest()}
    (SOURCES/"ship_wren.json").write_text(json.dumps(receipt,indent=2)+"\n")
    print("SHIP BUILT",json.dumps(receipt))


if __name__=="__main__":
    TEXTURES.mkdir(parents=True,exist_ok=True)
    build()
