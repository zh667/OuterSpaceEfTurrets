# Runs on Windows after render_all.py. Turns local/render/<id>/ into the game's sprite format:
#   local/gen/Sprites/ef_<id>.bin + ef_<id>.png   (one atlas per tower; the image name inside the .bin is a placeholder
#                                                  the mod replaces at start-up with the real path to this png)
#   local/gen/Textures/ef_<id>.png                 (build-menu icon, BaseDef.RawIcon)
#   local/gen/review/<id>.png, overview.png        (24 directions on the footprint, muzzles marked, vanilla turret for scale)
# Frame/action layout every tower shares (the mod's turret class relies on it):
#   action 0 = idle, facing up (the def's ModuleId is 0)      action 1 = base (fixed parts) + click rectangle
#   action 2+d = firing towards direction d                    action 26+d = idle towards direction d
#   d = 0..23, 15 degrees apart, clockwise from screen-up.
import io, json, math, os, struct, sys
import numpy as np
from PIL import Image, ImageDraw
HERE = os.path.dirname(os.path.abspath(__file__)); ROOT = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, HERE)
from pixelize import pixelize
SPEC = json.load(open(os.path.join(HERE, "towers.json"), encoding="utf-8"))
PLACEHOLDER = "@EF_SPRITE_DIR@/"
P = dict(lo=0.10, hi=0.74, gamma=1.35, edge_thr=0.45, edge_dark=0.78, colors=0, outline_w=2)
# firing patterns: alternate = two muzzles taking turns (machine guns); salvo = every muzzle at once, then recoil
PATTERN = {"7101": "alternate", "7201": "alternate", "7301": "alternate", "7304": "beam", "7401": "beam"}
RECOIL_M = {"7101": 0.06, "7201": 0.06, "7301": 0.06, "7304": 0.03, "7401": 0.03, "7402": 0.25, "7403": 0.30}


def be(fmt, *v): return struct.pack(">" + fmt, *v)
def utf(s): b = s.encode("utf-8"); return be("H", len(b)) + b


class Bin:
    def __init__(self, image):
        self.image, self.modules, self.frames, self.actions = image, [], [], []
    def module(self, x, y, w, h): self.modules.append((x, y, w, h)); return len(self.modules) - 1
    def frame(self, parts, coll=()):
        self.frames.append((parts, list(coll))); return len(self.frames) - 1
    def action(self, steps): self.actions.append(steps); return len(self.actions) - 1
    def bytes(self):
        o = be("i", 1) + utf(self.image)
        o += be("h", len(self.modules))
        for x, y, w, h in self.modules: o += be("b", 0) + be("hhhh", x, y, w, h) + be("i", 0)
        o += be("h", len(self.frames))
        for parts, coll in self.frames:
            o += be("hhhfffhh", 0, 0, 0, 1.0, 1.0, 1.0, 0, 0) + be("h", len(parts))
            for mid, lx, ly, sc in parts:
                w, h = self.modules[mid][2:]
                o += be("h", mid) + be("hhhhh", lx, ly, 0, w // 2, h // 2) + be("fff", 1.0, sc, sc) + be("hh", lx, ly)
            o += be("b", len(coll))
            for c in coll: o += be("hhhhh", *c)
        o += be("h", len(self.actions))
        for steps in self.actions:
            o += be("b", 0) + be("h", len(steps))
            for f, ms in steps: o += be("hh", f, ms)
        return o


def crop_aligned(img, ax, ay, t):
    """Crop to content with the left/top edge an exact number of game units from the anchor (ax, ay texels) and the
    size a multiple of 2t texels, so the module can be placed with whole-unit coordinates at scale 1/t."""
    bb = img.getbbox()
    if bb is None: return None
    x0 = ax + t * math.floor((bb[0] - ax) / t); y0 = ay + t * math.floor((bb[1] - ay) / t)
    w = int(math.ceil((bb[2] - x0) / (2 * t)) * 2 * t); h = int(math.ceil((bb[3] - y0) / (2 * t)) * 2 * t)
    c = Image.new("RGBA", (w, h)); c.alpha_composite(img.crop((x0, y0, min(img.width, x0 + w), min(img.height, y0 + h))), (0, 0))
    return c, (x0 - ax) // t, (y0 - ay) // t     # image, left and top in units relative to the anchor


def pack(images, maxw=2048, pad=2):
    order = sorted(range(len(images)), key=lambda i: -images[i].height)
    x = y = rowh = 0; pos = [None] * len(images); W = 0
    for i in order:
        im = images[i]
        if x + im.width + pad > maxw: x = 0; y += rowh + pad; rowh = 0
        pos[i] = (x, y); x += im.width + pad; rowh = max(rowh, im.height); W = max(W, x)
    H = y + rowh
    W2 = 1 << (W - 1).bit_length(); H2 = 1 << (H - 1).bit_length()
    atlas = Image.new("RGBA", (W2, H2))
    for i, im in enumerate(images): atlas.alpha_composite(im, pos[i])
    return atlas, pos


def find_muzzles(rd, d, expect, mode="front"):
    """Muzzles from the forward pass: the visible surface farthest along the firing direction, split across the barrel
    axis into the expected number of barrels. Returns render-pixel coordinates."""
    rgb = np.asarray(Image.open(os.path.join(rd, "d%02d_rgb.png" % d["k"])).convert("RGBA"))
    fw = np.asarray(Image.open(os.path.join(rd, "d%02d_fwd.png" % d["k"])).convert("L")).astype(np.float32) / 255
    dec = lambda g: np.where(g > 0.04045, ((g + 0.055) / 1.055) ** 2.4, g / 12.92)
    fwd = dec(fw) * 8 - 4                     # metres along the firing direction
    upi = np.asarray(Image.open(os.path.join(rd, "d%02d_up.png" % d["k"])).convert("L")).astype(np.float32) / 255
    up = dec(upi) * 8                         # metres above the ground
    dot = {"front": fwd, "top": up, "topfront": fwd + up}[mode]
    vis = rgb[..., 3] > 128
    if not vis.any(): return [(0, 0)] * expect
    dmax = dot[vis].max(); tip = vis & (dot >= dmax - 0.06)
    ys, xs = np.nonzero(tip)
    bx, by = d["recoil_px_per_m"]; n = math.hypot(bx, by) or 1; fx, fy = -bx / n, -by / n; lx, ly = -fy, fx
    lat = xs * lx + ys * ly; order = np.argsort(lat); lat_s = lat[order]
    if expect >= 2 and len(lat_s) > 1:
        gaps = np.diff(lat_s); g = int(np.argmax(gaps))
        if gaps[g] >= 6:
            a = order[:g + 1]; b2 = order[g + 1:]
            return [(xs[a].mean(), ys[a].mean()), (xs[b2].mean(), ys[b2].mean())]
        c = (xs.mean(), ys.mean()); return [c, c]
    return [(xs.mean(), ys.mean())]


def build(t):
    tid = t["id"]; rd = os.path.join(ROOT, "local", "render", tid); info = json.load(open(os.path.join(rd, "info.json")))
    tu = SPEC["texel_per_unit"]; k = SPEC["render_ppm"] / (t["upm"] * tu)
    gx, gy = info["ground_origin"]; ax, ay = int(round(gx / k)), int(round(gy / k))
    crops = []   # (image, left_u, top_u)
    base = None
    if t["base"]:
        im = pixelize(os.path.join(rd, "base_rgb.png"), os.path.join(rd, "base_nrm.png"), SPEC["render_ppm"], SPEC["render_ppm"] / k, crop=False, **P)
        base = crop_aligned(im, ax, ay, tu)
    heads = []
    dirs = info["dirs"]
    if t.get("static"):
        dirs = [dict(dirs[0], k=0) for _ in range(SPEC["dirs"])]
    for d in dirs[:1] if t.get("static") else dirs:
        im = pixelize(os.path.join(rd, "d%02d_rgb.png" % d["k"]), os.path.join(rd, "d%02d_nrm.png" % d["k"]), SPEC["render_ppm"], SPEC["render_ppm"] / k, crop=False, **P)
        heads.append(crop_aligned(im, ax, ay, tu))
    if t.get("static"): heads = heads * SPEC["dirs"]
    uniq = []                                   # one atlas entry per distinct picture (static towers repeat it)
    for h in heads:
        if not any(h is u for u in uniq): uniq.append(h)
    imgs = ([base[0]] if base else []) + [h[0] for h in uniq]
    atlas, pos = pack(imgs)
    head_index = [next(i for i, u in enumerate(uniq) if u is h) for h in heads]
    b = Bin(PLACEHOLDER + "ef_" + tid)
    def place(i, entry):    # module + part placement for image i (left/top in units)
        im, lu, tu_ = entry; x, y = pos[i]; mid = b.module(x, y, im.width, im.height)
        w, h = im.width, im.height
        return mid, int(lu - w * (tu - 1) / (2 * tu)), int(tu_ - h * (tu - 1) / (2 * tu))
    half = t["size"] * 16
    click = (0, -half, -half, 2 * half, 2 * half)
    pattern = PATTERN.get(tid, "salvo"); rec = RECOIL_M.get(tid, 0.12)
    base_part = []
    if base:
        mid, lx, ly = place(0, base); base_part = [(mid, lx, ly, 1.0 / tu)]
    f_base = b.frame(base_part, [click])
    off = 1 if base else 0
    rest, fire_seq, muzzles_u = [], [], []
    mod_cache = {}
    for i, (d, h) in enumerate(zip(dirs, heads)):
        hi = head_index[i]
        if hi not in mod_cache: mod_cache[hi] = place(off + hi, h)
        mid, lx, ly = mod_cache[hi]
        rx, ry = d["recoil_px_per_m"]; sx = rx * rec / k / tu; sy = ry * rec / k / tu
        if t.get("static"): sx, sy = 0.0, -0.6      # whole tower stands still: just a small kick upwards
        dx, dy = int(round(sx)), int(round(sy))
        if dx == 0 and dy == 0: dx, dy = (int(math.copysign(1, sx)) if abs(sx) >= abs(sy) else 0), (int(math.copysign(1, sy)) if abs(sy) > abs(sx) else 0)
        mpx = find_muzzles(rd, d, t.get("muzzles", 1), t.get("muzzle_mode", "front"))
        mu = [((mx / k - ax) / tu, (my / k - ay) / tu) for mx, my in mpx]
        muzzles_u.append(mu)
        coll = [(0, int(round(x)) - 2, int(round(y)) - 2, 4, 4) for x, y in mu]
        part = lambda ox=0, oy=0: [(mid, lx + ox, ly + oy, 1.0 / tu)]
        R = b.frame(part()); K = b.frame(part(dx, dy)); M = b.frame(part(int(round(dx / 2)), int(round(dy / 2))))
        rest.append(R)
        if pattern == "alternate" and len(coll) >= 2:
            F1 = b.frame(part(), [coll[0]]); F2 = b.frame(part(), [coll[1]])
            seq = [(F1, 50), (K, 50), (F2, 50), (K, 50)]
        elif pattern == "beam":
            F = b.frame(part(), coll); seq = [(F, 60), (M, 120), (R, 200)]
        else:
            F = b.frame(part(), coll); seq = [(F, 60), (K, 120), (M, 120), (R, 160)]
        fire_seq.append(seq)
    b.action([(rest[0], 100)])              # 0 idle up (ModuleId 0)
    b.action([(f_base, 100)])               # 1 base
    for s in fire_seq: b.action(s)          # 2..25
    for r in rest: b.action([(r, 100)])     # 26..49
    gen = os.path.join(ROOT, "local", "gen"); os.makedirs(os.path.join(gen, "Sprites"), exist_ok=True)
    os.makedirs(os.path.join(gen, "Textures"), exist_ok=True); os.makedirs(os.path.join(gen, "review"), exist_ok=True)
    atlas.save(os.path.join(gen, "Sprites", "ef_%s.png" % tid))
    open(os.path.join(gen, "Sprites", "ef_%s.bin" % tid), "wb").write(b.bytes())
    # composite at display size (units) for review and icon
    def composite(d, show_muzzle=False, scale=1):
        W = 6 * half; cv = Image.new("RGBA", (W, W)); cx, cy = W // 2, int(W * 0.62)
        if base:
            im, lu, tu_ = base; s = im.resize((im.width // tu, im.height // tu), Image.LANCZOS); cv.alpha_composite(s, (cx + lu, cy + tu_))
        im, lu, tu_ = heads[d]; s = im.resize((im.width // tu, im.height // tu), Image.LANCZOS); cv.alpha_composite(s, (cx + lu, cy + tu_))
        return cv, (cx, cy)
    icon_cv, _ = composite(9); bb = icon_cv.getbbox(); ic = icon_cv.crop(bb); ic.thumbnail((96, 96), Image.LANCZOS)
    icon = Image.new("RGBA", (96, 96)); icon.alpha_composite(ic, ((96 - ic.width) // 2, (96 - ic.height) // 2))
    icon.save(os.path.join(gen, "Textures", "ef_%s.png" % tid))
    # review: 24 directions at 2x, footprint square, muzzles
    S = 2; cells = []
    for d in range(24):
        cv, (cx, cy) = composite(d); dr = ImageDraw.Draw(cv)
        dr.rectangle([cx - half, cy - half, cx + half, cy + half], outline=(255, 210, 0, 160))
        for x, y in muzzles_u[d]: dr.rectangle([cx + x - 1, cy + y - 1, cx + x + 1, cy + y + 1], fill=(255, 40, 40, 255))
        bg = Image.new("RGBA", cv.size, (74, 70, 57, 255)); bg.alpha_composite(cv); cells.append(bg.resize((cv.width * S, cv.height * S), Image.NEAREST))
    cw, ch = cells[0].size; sheet = Image.new("RGB", (cw * 6, ch * 4))
    for i, c in enumerate(cells): sheet.paste(c.convert("RGB"), ((i % 6) * cw, (i // 6) * ch))
    sheet.save(os.path.join(gen, "review", "%s.png" % tid))
    return {"id": tid, "atlas": atlas.size, "modules": len(b.modules), "frames": len(b.frames), "actions": len(b.actions),
            "head_px": heads[0][0].size, "muzzles": len(muzzles_u[0]), "pattern": pattern}


if __name__ == "__main__":
    only = sys.argv[1:]; res = []
    for t in SPEC["towers"]:
        if only and t["id"] not in only: continue
        if not os.path.exists(os.path.join(ROOT, "local", "render", t["id"], "info.json")): print("not rendered", t["id"]); continue
        r = build(t); res.append(r); print(json.dumps(r), flush=True)
    json.dump(res, open(os.path.join(ROOT, "local", "gen", "assets.json"), "w"), indent=1)
