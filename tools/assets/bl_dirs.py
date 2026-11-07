# Runs inside Blender (background). Renders one tower: the turning parts in N directions and the fixed parts once,
# colour + normal pass, orthographic camera at a fixed elevation. Also writes where the muzzles land in each image.
# blender -b <tower.blend> --python bl_dirs.py -- <outdir> <elev> <ndirs> <ppm> <head,objs> <base,objs|-> [<pitch,objs>@<deg,deg,...>]
# Pitch: the listed head parts (a launcher on its fork) also tilt up about the horizontal axis through the first one's
# origin (its trunnion); every direction is rendered at each angle, level first (d00..), then d00_p1.. for the others.
import bpy, sys, math, os, json
from mathutils import Vector, Matrix
from bpy_extras.object_utils import world_to_camera_view
a = sys.argv[sys.argv.index("--") + 1:]
out, elev, nd, ppm = a[0], float(a[1]), int(a[2]), float(a[3])
heads = a[4].split(","); bases = [] if a[5] == "-" else a[5].split(",")
pitch_names, pitch_deg = [], [0.0]
if len(a) > 6 and a[6] != "-":
    pn, pd = a[6].split("@"); pitch_names = pn.split(","); pitch_deg = [float(x) for x in pd.split(",")]
os.makedirs(out, exist_ok=True); sc = bpy.context.scene
for o in list(bpy.data.objects):
    if o.type in ("LIGHT", "CAMERA"): bpy.data.objects.remove(o)
meshes = {o.name: o for o in bpy.data.objects if o.type == "MESH"}
H = [meshes[n] for n in heads]; B = [meshes[n] for n in bases]
for n, o in meshes.items():
    if o not in H and o not in B: o.hide_render = True
mw0 = {o.name: o.matrix_world.copy() for o in H}
R0 = Matrix.Rotation(math.pi, 4, 'Z')            # the source models face -Y; turn them to face +Y (screen up)
for o in H + B: o.matrix_world = R0 @ o.matrix_world
for o in H: mw0[o.name] = o.matrix_world.copy()
bpy.context.view_layer.update()
P = [meshes[n] for n in pitch_names]
pivot = P[0].matrix_world.translation.copy() if P else Vector((0, 0, 0))
def pitch_m(deg):   # tilt about the axis across the barrel (x) through the trunnion; positive = muzzle up
    return Matrix.Translation(pivot) @ Matrix.Rotation(math.radians(deg), 4, 'X') @ Matrix.Translation(-pivot)
# muzzles: front-most vertices of the turning parts, one or two clusters across the barrel axis
vs = [o.matrix_world @ v.co for o in H for v in o.data.vertices]
ymax = max(v.y for v in vs); front = [v for v in vs if v.y > ymax - 0.06]
xs = sorted(v.x for v in front); mz = []
if xs[-1] - xs[0] > 0.18:
    mid = (xs[0] + xs[-1]) / 2
    for side in (lambda v: v.x < mid, lambda v: v.x >= mid):
        c = [v for v in front if side(v)]; mz.append(sum(c, Vector()) / len(c))
else:
    mz.append(sum(front, Vector()) / len(front))
allv = vs + [o.matrix_world @ v.co for o in B for v in o.data.vertices]
R = max(math.hypot(v.x, v.y) for v in allv); Ht = max(v.z for v in allv); Hb = min(v.z for v in allv)
e = math.radians(elev)
# screen extent: width 2R; height = 2R*sin(e) + (Ht-Hb)*cos(e)
ortho = 1.08 * max(2 * R, 2 * R * math.sin(e) + (Ht - Hb) * math.cos(e))
px = int(math.ceil(ortho * ppm / 8.0)) * 8
ortho = px / ppm
cam = bpy.data.cameras.new("c"); cam.type = "ORTHO"; cam.ortho_scale = ortho; cam.clip_end = 400
co = bpy.data.objects.new("c", cam); sc.collection.objects.link(co); sc.camera = co
zc = (Ht + Hb) / 2; d = 60
co.location = (0, -d * math.cos(e), zc + d * math.sin(e)); co.rotation_euler = (math.pi / 2 - e, 0, 0)
def sun(en, rx, ry, rz):
    L = bpy.data.lights.new("s", "SUN"); L.energy = en; o = bpy.data.objects.new("s", L); sc.collection.objects.link(o)
    o.rotation_euler = (math.radians(rx), math.radians(ry), math.radians(rz))
sun(3.0, 40, -30, -20); sun(0.6, -30, 35, 0)
w = sc.world or bpy.data.worlds.new("w"); sc.world = w; w.use_nodes = True
bg = next(n for n in w.node_tree.nodes if n.type == "BACKGROUND"); bg.inputs[0].default_value = (1, 1, 1, 1); bg.inputs[1].default_value = 0.55
for eng in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE"):
    try: sc.render.engine = eng; break
    except TypeError: pass
sc.render.film_transparent = True; sc.render.resolution_x = sc.render.resolution_y = px
sc.render.image_settings.file_format = "PNG"; sc.render.image_settings.color_mode = "RGBA"
try: sc.view_settings.view_transform = "Standard"
except Exception: pass
nm = bpy.data.materials.new("nrm"); nm.use_nodes = True; nt = nm.node_tree; [nt.nodes.remove(x) for x in list(nt.nodes)]
g = nt.nodes.new("ShaderNodeNewGeometry"); vm = nt.nodes.new("ShaderNodeVectorMath"); vm.operation = "MULTIPLY_ADD"
vm.inputs[1].default_value = (0.5, 0.5, 0.5); vm.inputs[2].default_value = (0.5, 0.5, 0.5)
em = nt.nodes.new("ShaderNodeEmission"); oo = nt.nodes.new("ShaderNodeOutputMaterial")
nt.links.new(g.outputs["Normal"], vm.inputs[0]); nt.links.new(vm.outputs[0], em.inputs[0]); nt.links.new(em.outputs[0], oo.inputs[0])
orig = {n: list(o.data.materials) for n, o in meshes.items()}
# forward pass: grey = how far forward (along the firing direction) the visible surface is; used to find the muzzles
fm = bpy.data.materials.new("fwd"); fm.use_nodes = True; ft = fm.node_tree; [ft.nodes.remove(x) for x in list(ft.nodes)]
fg = ft.nodes.new("ShaderNodeNewGeometry"); fvec = ft.nodes.new("ShaderNodeCombineXYZ")
fdot = ft.nodes.new("ShaderNodeVectorMath"); fdot.operation = "DOT_PRODUCT"
fmap = ft.nodes.new("ShaderNodeMapRange"); fmap.inputs[1].default_value = -4.0; fmap.inputs[2].default_value = 4.0
fem = ft.nodes.new("ShaderNodeEmission"); fout = ft.nodes.new("ShaderNodeOutputMaterial")
ft.links.new(fg.outputs["Position"], fdot.inputs[0]); ft.links.new(fvec.outputs[0], fdot.inputs[1])
ft.links.new(fdot.outputs["Value"], fmap.inputs[0]); ft.links.new(fmap.outputs[0], fem.inputs[0]); ft.links.new(fem.outputs[0], fout.inputs[0])
# height pass: grey = height above the ground (0..8 m)
um = bpy.data.materials.new("up"); um.use_nodes = True; ut = um.node_tree; [ut.nodes.remove(x) for x in list(ut.nodes)]
ug = ut.nodes.new("ShaderNodeNewGeometry"); us = ut.nodes.new("ShaderNodeSeparateXYZ"); umap = ut.nodes.new("ShaderNodeMapRange")
umap.inputs[1].default_value = 0.0; umap.inputs[2].default_value = 8.0
uem = ut.nodes.new("ShaderNodeEmission"); uout = ut.nodes.new("ShaderNodeOutputMaterial")
ut.links.new(ug.outputs["Position"], us.inputs[0]); ut.links.new(us.outputs["Z"], umap.inputs[0]); ut.links.new(umap.outputs[0], uem.inputs[0]); ut.links.new(uem.outputs[0], uout.inputs[0])
def proj(p):
    v = world_to_camera_view(sc, co, p); return [v.x * px, (1 - v.y) * px]
def render(objs, name, modes=("rgb", "nrm")):
    for n, o in meshes.items(): o.hide_render = o not in objs
    for mode in modes:
        p = os.path.join(out, "%s_%s.png" % (name, mode))
        for n, o in meshes.items():
            o.data.materials.clear()
            for m in (orig[n] if mode == "rgb" else [{"nrm": nm, "fwd": fm, "up": um}[mode]] * max(1, len(orig[n]))): o.data.materials.append(m)
        sc.render.filepath = p; bpy.ops.render.render(write_still=True)
info = {"elev": elev, "ppm": ppm, "px": px, "ortho": ortho, "ground_origin": proj(Vector((0, 0, 0))),
        "recoil_px_per_m": [proj(Vector((0, -1, 0)))[i] - proj(Vector((0, 0, 0)))[i] for i in (0, 1)], "dirs": []}
if B: render(B, "base")
info["pitch"] = pitch_deg; info["pdirs"] = []
for j, deg in enumerate(pitch_deg):
    Pm = pitch_m(deg); rows = []
    for k in range(nd):
        yaw = -360.0 / nd * k
        Rk = Matrix.Rotation(math.radians(yaw), 4, 'Z')
        for o in H: o.matrix_world = Rk @ (Pm if o in P else Matrix.Identity(4)) @ mw0[o.name]
        bpy.context.view_layer.update()
        back = Rk @ Pm.to_3x3().to_4x4() @ Vector((0, -1, 0))
        name = "d%02d" % k if j == 0 else "d%02d_p%d" % (k, j)
        rows.append({"k": k, "name": name, "yaw": yaw, "pitch": deg, "muzzles": [proj(Rk @ ((Pm @ m) if P else m)) for m in mz],
                     "recoil_px_per_m": [proj(back)[i] - proj(Vector((0, 0, 0)))[i] for i in (0, 1)]})
        f3 = Rk @ Pm.to_3x3().to_4x4() @ Vector((0, 1, 0)); fvec.inputs[0].default_value = f3.x; fvec.inputs[1].default_value = f3.y; fvec.inputs[2].default_value = f3.z
        render(H, name, ("rgb", "nrm", "fwd", "up"))
        with open(os.path.join(out, "progress.txt"), "w") as f: f.write("%d/%d\n" % (j * nd + k + 1, nd * len(pitch_deg)))
    if j == 0: info["dirs"] = rows
    else: info["pdirs"].append(rows)
json.dump(info, open(os.path.join(out, "info.json"), "w"), indent=1)
print("DONE", out, px)
