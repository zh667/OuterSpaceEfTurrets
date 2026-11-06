# Runs ON WINDOWS. Makes the ground marks the special towers leave (acid pool, poison pool, ice sheet) from the
# game's own terrain pictures: the swamp ground (map_6_rainswamp) gives the liquid its grain, the frozen water
# (map_13_Water) gives the ice its marbling. Each mark is an irregular pool with a damp rim around it, a lighter
# edge where the liquid meets the ground, and a soft sheen from the top left (the light of the game's sprites).
#   python tools/assets/make_ground.py [preview]  -> local/gen/Textures/ef_ground_<kind>_<n>.png (256 x 256)
# 'preview' also lays them on a few of the game's grounds at game scale: local/shots/ground_preview.jpg
import sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageFilter, ImageDraw

ROOT = Path(__file__).resolve().parents[2]
GAME = Path(r"E:\SteamLibrary\steamapps\common\Outer Space\Outer Space_Data\StreamingAssets\img")
OUT = ROOT / "local" / "gen" / "Textures"
N = 256
VARIANTS = 3

KINDS = {
    #          grain texture            deep colour       edge colour       damp rim        alpha  bubbles
    "acid":   ("map_6_rainswamp",      (104, 124, 26),   (156, 172, 58),   (46, 50, 20),   0.86, 0),
    "poison": ("map_6_rainswamp",      (56, 30, 76),     (112, 74, 134),   (34, 24, 44),   0.86, 0),
    "frost":  ("map_13_Water",         (150, 196, 218),  (222, 240, 250), (112, 146, 168), 0.84, 0),
}


def noise(rng, cells, n=N):
    small = Image.fromarray((rng.random((cells, cells)) * 255).astype(np.uint8))
    return np.asarray(small.resize((n, n), Image.BICUBIC), dtype=np.float32) / 255.0


def fbm(rng, n=N):
    return sum(noise(rng, c, n) * w for c, w in ((4, .5), (8, .25), (16, .15), (32, .1)))


def blur(a, r):
    im = Image.fromarray(np.clip(a * 255, 0, 255).astype(np.uint8))
    return np.asarray(im.filter(ImageFilter.GaussianBlur(r)), dtype=np.float32) / 255.0


def smooth(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def pool_shape(rng, jagged):
    yy, xx = np.mgrid[0:N, 0:N].astype(np.float32)
    xx = xx / (N - 1) * 2 - 1; yy = yy / (N - 1) * 2 - 1
    field = np.zeros((N, N), np.float32)
    for _ in range(rng.integers(5, 8)):
        a = rng.uniform(0, 6.283); r = rng.uniform(0, .32)
        cx, cy = r * np.cos(a), r * np.sin(a)
        sx, sy = rng.uniform(.22, .42), rng.uniform(.16, .34); rot = rng.uniform(0, 3.14)
        u = (xx - cx) * np.cos(rot) + (yy - cy) * np.sin(rot); v = -(xx - cx) * np.sin(rot) + (yy - cy) * np.cos(rot)
        field = np.maximum(field, np.exp(-((u / sx) ** 2 + (v / sy) ** 2)))
    edge = fbm(rng) - .5
    if jagged: edge = edge + (noise(rng, 48) - .5) * .5
    field = field + edge * .35
    field *= smooth(1.0, .82, np.hypot(xx, yy))          # keep it inside the square
    return field


def make(kind, seed):
    tex, deep, rim, damp, alpha, bubbles = KINDS[kind]
    rng = np.random.default_rng(seed)
    field = pool_shape(rng, kind == "frost")
    inside = smooth(.40, .46, field)                       # the pool, a soft but clear edge
    depth = blur(inside, 10) * inside                      # 0 at the edge, 1 in the middle
    halo = np.clip(blur(smooth(.30, .40, field), 6) - inside, 0, 1)   # damp ground just outside

    g = Image.open(GAME / (tex + ".png")).convert("L")
    off = rng.integers(0, g.width - N); grain = np.asarray(g.crop((off, off, off + N, off + N)), np.float32) / 255.0
    grain = grain - blur(grain, 8)                         # keep the detail, not the tone
    grain = np.clip(grain * (2.2 if kind == "frost" else 1.6), -.5, .5)

    t = depth[..., None] ** .6
    col = np.array(rim, np.float32) * (1 - t) + np.array(deep, np.float32) * t
    col = col * (1 + grain[..., None] * (1.1 if kind == "frost" else 1.0))
    if kind != "frost":   # murky: darker and lighter clouds in the liquid
        murk = fbm(rng) - .5
        col = col * (1 + murk[..., None] * .55)
    # meniscus: a lighter line just inside the edge
    edge_line = np.clip(inside - blur(inside, 2.5), 0, 1) * (1.6 if kind == "frost" else .9)
    col = col + edge_line[..., None] * (np.array(rim, np.float32) * .35 + 18)
    # sheen from the top left
    yy, xx = np.mgrid[0:N, 0:N].astype(np.float32) / N
    for _ in range(2):
        cx, cy = rng.uniform(.3, .45), rng.uniform(.3, .45)
        s = np.exp(-(((xx - cx) / .16) ** 2 + ((yy - cy) / .05) ** 2))
        col = col + (s * depth * (55 if kind == "frost" else 30))[..., None]
    # thin and see-through at the edge, thicker in the middle
    a = inside * alpha * (.55 + .45 * smooth(0, .5, depth)) + halo * .30
    rgb = col * inside[..., None] + np.array(damp, np.float32) * (1 - inside[..., None])
    img = Image.fromarray(np.dstack([np.clip(rgb, 0, 255), np.clip(a * 255, 0, 255)]).astype(np.uint8), "RGBA")

    d = ImageDraw.Draw(img)
    placed = 0
    while placed < bubbles:
        x, y = rng.integers(40, N - 40, 2)
        if depth[y, x] < .45: continue
        r = rng.uniform(2.5, 5)
        dark = tuple(int(c * .55) for c in deep) + (200,)
        d.ellipse((x - r, y - r, x + r, y + r), outline=dark, width=1)
        d.ellipse((x - r * .45 - 1, y - r * .45 - 1, x - r * .45 + 1, y - r * .45 + 1), fill=(255, 255, 255, 150))
        placed += 1
    return img


def preview(marks):
    grounds = ["map_8_snowground", "map_0_ground", "map_6_rainswamp", "map_0_alienground"]
    tile = 720
    sheet = Image.new("RGB", (tile * len(grounds), tile * 2))
    for gi, gname in enumerate(grounds):
        g = Image.open(GAME / (gname + ".png")).convert("RGBA").resize((tile, tile))
        for row in range(2):
            bg = g.copy()
            for k, kind in enumerate(KINDS):
                m = marks[kind][row % VARIANTS]
                # the mark fills ~75% of its square; radius 2.5-3 cells of 32 units, shown at the game's usual zoom (1 unit ~ 1 px)
                size = int(2 * (2.5 if kind != "poison" else 3.0) * 32 / .75)
                m = m.resize((size, size), Image.BILINEAR)
                bg.alpha_composite(m, (10 + k * 235, 60 + (k % 2) * 300))
            sheet.paste(bg.convert("RGB"), (gi * tile, row * tile))
    out = ROOT / "local" / "shots"; out.mkdir(parents=True, exist_ok=True)
    sheet.save(out / "ground_preview.jpg", quality=88)
    print("preview", out / "ground_preview.jpg")


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    marks = {}
    for ki, kind in enumerate(KINDS):
        marks[kind] = []
        for v in range(VARIANTS):
            img = make(kind, 1000 * ki + 17 * v + 3)
            img.save(OUT / ("ef_ground_%s_%d.png" % (kind, v)))
            marks[kind].append(img)
    print("ground marks", len(KINDS) * VARIANTS, "->", OUT)
    if "preview" in sys.argv[1:]: preview(marks)


if __name__ == "__main__":
    main()
