# Runs on Windows after tools/assets/make_assets.py. Draws the Workshop preview from the mod's own sprite files
# (local/gen/Sprites): every tower as it stands in the game (base + idle frame), on a dark ground (no tower names).
#   python tools/workshop/make_preview.py [direction 0-23]   ->  local/workshop/preview.png (under 1 MB)
import os, struct, sys
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SPR = os.path.join(ROOT, "local", "gen", "Sprites")
OUT = os.path.join(ROOT, "local", "workshop", "preview.png")
# build-menu order (tiers)
TOWERS = ["7101", "7102", "7103", "7201", "7202", "7203", "7204", "7205", "7301", "7302",
          "7303", "7304", "7305", "7306", "7307", "7401", "7404", "7402", "7403"]
ACTION_BASE, ACTION_IDLE = 1, 26


class R:
    def __init__(s, d): s.d, s.p = d, 0
    def g(s, f):
        f = ">" + f; v = struct.unpack_from(f, s.d, s.p); s.p += struct.calcsize(f)
        return v[0] if len(v) == 1 else v
    def utf(s):
        n = s.g("H"); v = s.d[s.p:s.p + n].decode("utf-8"); s.p += n; return v


def parse(data):
    """The format make_assets.Bin writes (free-rotate frames, 4-byte image index)."""
    r = R(data)
    images = [r.utf() for _ in range(r.g("i"))]
    mods = []
    for _ in range(r.g("h")):
        r.g("b"); x, y, w, h = r.g("hhhh"); r.g("i"); mods.append((x, y, w, h))
    frames = []
    for _ in range(r.g("h")):
        r.g("hhhfffhh"); parts = []
        for _ in range(r.g("h")):
            mid = r.g("h"); lx, ly, _, _, _ = r.g("hhhhh"); _, sx, sy = r.g("fff"); r.g("hh")
            parts.append((mid, lx, ly, sx))
        for _ in range(r.g("b")): r.g("hhhhh")
        frames.append(parts)
    acts = []
    for _ in range(r.g("h")):
        r.g("b"); acts.append([r.g("hh") for _ in range(r.g("h"))])
    assert r.p == len(data), "sprite file not fully read"
    return images, mods, frames, acts


def tower_image(tid, d):
    images, mods, frames, acts = parse(open(os.path.join(SPR, "ef_" + tid + ".bin"), "rb").read())
    atlas = Image.open(os.path.join(SPR, "ef_" + tid + ".png")).convert("RGBA")
    parts = []
    for a in (ACTION_BASE, ACTION_IDLE + d):
        for mid, lx, ly, sc in frames[acts[a][0][0]]:
            # the part is scaled by sc about its pivot (w/2, h/2), which sits at (lx + w/2, ly + h/2) in game units;
            # drawn here at module resolution (1/sc texels per unit)
            x, y, w, h = mods[mid]
            parts.append((atlas.crop((x, y, x + w, y + h)), round((lx + w / 2) / sc - w / 2), round((ly + h / 2) / sc - h / 2)))
    l = min(x for _, x, _ in parts); tp = min(y for _, _, y in parts)
    rr = max(x + i.width for i, x, _ in parts); b = max(y + i.height for i, _, y in parts)
    c = Image.new("RGBA", (rr - l, b - tp))
    for im, x, y in parts: c.alpha_composite(im, (x - l, y - tp))
    return c.crop(c.getbbox())


def font(size, bold=False):
    for f in (("msyhbd.ttc" if bold else "msyh.ttc"), "simhei.ttf"):
        p = os.path.join(os.environ.get("WINDIR", r"C:\Windows"), "Fonts", f)
        if os.path.exists(p): return ImageFont.truetype(p, size)
    return ImageFont.load_default()


def main():
    d = int(sys.argv[1]) if len(sys.argv) > 1 else 15
    W = 1024; cols = 5; top = 120; cell_w = W // cols; cell_h = (W - top - 16) // 4
    img = Image.new("RGBA", (W, W), (34, 40, 50, 255))
    g = ImageDraw.Draw(img)
    for y in range(W):                                    # slight vertical shading, darker at the bottom
        k = int(10 * y / W); g.line([(0, y), (W, y)], fill=(38 - k, 45 - k, 56 - k, 255))
    g.text((W // 2, 44), "工业防御塔", font=font(56, True), fill=(240, 236, 226), anchor="mm")
    g.text((W // 2, 94), "19 座防御塔 · 四级科技 · 原版特效", font=font(26), fill=(178, 190, 204), anchor="mm")
    for k, tid in enumerate(TOWERS):
        cx = (k % cols) * cell_w + cell_w // 2; y0 = top + (k // cols) * cell_h
        t = tower_image(tid, d)
        box_w, box_h = cell_w - 20, cell_h - 16
        s = min(box_w / t.width, box_h / t.height, 1.0)
        t = t.resize((max(1, round(t.width * s)), max(1, round(t.height * s))), Image.LANCZOS)
        sh = Image.new("RGBA", t.size, (0, 0, 0, 0)); sh.putalpha(t.getchannel("A").point(lambda a: a * 0.35))
        bx = cx - t.width // 2; by = y0 + 8 + (box_h - t.height)
        img.alpha_composite(sh, (bx + 4, by + 4)); img.alpha_composite(t, (bx, by))
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    rgb = img.convert("RGB")
    rgb.save(OUT, optimize=True)
    if os.path.getsize(OUT) >= 1000 * 1024:               # Steam wants the preview under 1 MB
        rgb.quantize(256).save(OUT, optimize=True)        # 256-colour PNG
    print("preview", OUT, os.path.getsize(OUT), "bytes, direction", d)


if __name__ == "__main__":
    main()
