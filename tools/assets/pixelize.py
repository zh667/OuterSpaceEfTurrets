"""高分辨率俯视渲染 -> 秩序：新曙光 风格像素图。
pixelize(rgb_path, nrm_path, ppm_render, ppm_target, tone=dict) -> PIL RGBA
- 按块平均缩小（不插值），alpha 过半才算实心
- 明度压到游戏金属灰区间（游戏炮塔主色 #52504e/#6a6763/#72716f，高光 #cacaca，暗部 #232323）
- 法线突变处画暗线（游戏的内部结构线），外轮廓 1 像素近黑描边
- 中值切分量化到 n 色
"""
import numpy as np
from PIL import Image
def _lum(c): return 0.299*c[...,0]+0.587*c[...,1]+0.114*c[...,2]
def _outline(img, color, w=1):
    o = np.asarray(img).copy()
    for _ in range(w):
        s = o[..., 3] > 0
        p = np.pad(s, 1); nb = p[:-2, 1:-1] | p[2:, 1:-1] | p[1:-1, :-2] | p[1:-1, 2:]
        o[nb & ~s] = (*color, 255)
    return Image.fromarray(o, 'RGBA')

def pixelize(rgb_path, nrm_path, ppm_render, ppm_target, colors=28, lo=0.13, hi=0.80, gamma=1.25, sat=0.85,
             edge_thr=0.35, edge_dark=0.62, outline=(20,21,20), crop=True, outline_w=1):
    im = np.asarray(Image.open(rgb_path).convert('RGBA')).astype(np.float32)/255
    nm = np.asarray(Image.open(nrm_path).convert('RGBA')).astype(np.float32)/255
    f = ppm_render/ppm_target; k = int(round(f)); H, W = im.shape[:2]
    h, w = H//k, W//k; im = im[:h*k, :w*k]; nm = nm[:h*k, :w*k]
    # 法线边：相邻像素法线差
    n = nm[..., :3]*2-1; a = im[..., 3] > 0.5
    d = np.zeros((h*k, w*k), np.float32)
    dx = np.linalg.norm(n[:, 1:]-n[:, :-1], axis=-1) * (a[:, 1:] & a[:, :-1]); d[:, 1:] = np.maximum(d[:, 1:], dx)
    dy = np.linalg.norm(n[1:]-n[:-1], axis=-1) * (a[1:] & a[:-1]); d[1:] = np.maximum(d[1:], dy)
    edge = (d > edge_thr).reshape(h, k, w, k).mean(axis=(1, 3)) > (1.2/k)
    A = im[..., 3].reshape(h, k, w, k).mean(axis=(1, 3))
    pre = im[..., :3]*im[..., 3:4]
    C = pre.reshape(h, k, w, k, 3).sum(axis=(1, 3)) / np.maximum(im[..., 3].reshape(h, k, w, k).sum(axis=(1, 3)), 1e-6)[..., None]
    solid = A > 0.5
    # 明度映射 + 降饱和（灰色更灰，彩色保留）
    L = _lum(C)[..., None]; chroma = C - L
    L2 = lo + (hi-lo)*np.clip(L, 0, 1)**gamma
    C2 = np.clip(L2 + chroma*sat*(L2/np.maximum(L, 1e-3))**0.5, 0, 1)
    C2[edge & solid] *= edge_dark
    out = np.zeros((h, w, 4), np.uint8); out[..., :3] = (C2*255).astype(np.uint8); out[..., 3] = solid*255
    img = Image.fromarray(out, 'RGBA')
    if colors:
        rgb = img.convert('RGB').quantize(colors=colors, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE).convert('RGB')
        img = Image.merge('RGBA', (*rgb.split(), img.getchannel('A')))
    # 外描边
    img = _outline(img, outline, outline_w)
    if crop: img = img, img.getbbox()
    return img

def stylize_sprite(img, scale_x, scale_y, colors=20, lo=0.12, hi=0.78, gamma=1.2, sat=0.7, hue_to=None, hue_mix=0.0,
                   edge_dark=0.7, edge_thr=0.22, outline=(20,21,20), outline_w=1, blur=0.0):
    """已是像素图的精灵（如另一款游戏的斜视精灵）→ 本游戏画风：缩放/拉伸、明度压缩、降饱和、亮度边缘加暗线、量化、外描边。"""
    k = 8
    W = max(1, round(img.width*scale_x)); H = max(1, round(img.height*scale_y))
    big = img.convert('RGBA').resize((W*k, H*k), Image.LANCZOS)
    if blur:
        from PIL import ImageFilter
        pre_ = np.asarray(big).astype(np.float32); al = pre_[..., 3:4]/255
        rgbp = Image.fromarray(np.clip(pre_[..., :3]*al, 0, 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(blur*k))
        alb = Image.fromarray((al[..., 0]*255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(blur*k))
        r_ = np.asarray(rgbp).astype(np.float32); a_ = np.asarray(alb).astype(np.float32)[..., None]
        rgb_ = np.clip(r_/np.maximum(a_/255, 1e-3), 0, 255); a0 = pre_[..., 3:4]
        big = Image.fromarray(np.concatenate([rgb_, a0], -1).astype(np.uint8), 'RGBA')
    a = np.asarray(big).astype(np.float32)/255
    A = a[..., 3].reshape(H, k, W, k).mean(axis=(1, 3))
    pre = (a[..., :3]*a[..., 3:4]).reshape(H, k, W, k, 3).sum(axis=(1, 3))
    C = pre/np.maximum(a[..., 3].reshape(H, k, W, k).sum(axis=(1, 3)), 1e-6)[..., None]
    solid = A > 0.5
    L = _lum(C)[..., None]; chroma = C-L
    if hue_to is not None:
        t = np.array(hue_to, np.float32)/255; chroma = chroma*(1-hue_mix) + (t-_lum(t))*hue_mix*np.clip(np.abs(chroma).sum(-1, keepdims=True)*4, 0, 1)
    L2 = lo+(hi-lo)*np.clip(L, 0, 1)**gamma
    C2 = np.clip(L2+chroma*sat, 0, 1)
    # 亮度边缘（向下/向右的暗台阶）画暗线
    l2 = L2[..., 0]; e = np.zeros_like(l2, bool)
    e[:-1] |= (l2[:-1]-l2[1:] > edge_thr) & solid[1:]; e[:, :-1] |= (l2[:, :-1]-l2[:, 1:] > edge_thr) & solid[:, 1:]
    C2[e & solid] *= edge_dark
    out = np.zeros((H, W, 4), np.uint8); out[..., :3] = (C2*255).astype(np.uint8); out[..., 3] = solid*255
    im = Image.fromarray(out, 'RGBA')
    if colors:
        rgb = im.convert('RGB').quantize(colors=colors, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE).convert('RGB')
        im = Image.merge('RGBA', (*rgb.split(), im.getchannel('A')))
    return _outline(im, outline, outline_w)

# ---------- 加重“新曙光”画风：调色板映射 + 去噪 + 亮/暗边 ----------
def game_palette(atlas_path, k=40, seed=1, iters=25):
    """从游戏原版精灵图集取 k 色调色板（简单 k-means，按出现次数加权）。"""
    a = np.asarray(Image.open(atlas_path).convert('RGBA'))
    px = a[a[..., 3] > 200][:, :3].astype(np.float32)
    u, cnt = np.unique(px, axis=0, return_counts=True)
    rng = np.random.default_rng(seed); w = cnt.astype(np.float32)
    C = u[rng.choice(len(u), k, replace=False, p=w/w.sum())]
    for _ in range(iters):
        d = ((u[:, None, :]-C[None])**2).sum(-1); lab = d.argmin(1)
        for j in range(k):
            m = lab == j
            if m.any(): C[j] = (u[m]*w[m, None]).sum(0)/w[m].sum()
    return C.astype(np.uint8)

def _to_lab(c):
    c = c.astype(np.float32)/255
    c = np.where(c > 0.04045, ((c+0.055)/1.055)**2.4, c/12.92)
    M = np.array([[0.4124, 0.3576, 0.1805], [0.2126, 0.7152, 0.0722], [0.0193, 0.1192, 0.9505]], np.float32)
    xyz = c@M.T/np.array([0.9505, 1.0, 1.089], np.float32)
    f = np.where(xyz > 0.008856, np.cbrt(xyz), 7.787*xyz+16/116)
    return np.stack([116*f[..., 1]-16, 500*(f[..., 0]-f[..., 1]), 200*(f[..., 1]-f[..., 2])], -1)

def stylize_game(img, palette, lo=0.10, hi=0.86, gamma=1.0, desat_below=0.22, keep_sat=1.15, denoise=True,
                 rim=1.22, shade=0.72, outline=(10, 10, 10)):
    """已是像素尺寸的精灵 → 新曙光画风（尺寸不变）。
    低饱和颜色（锈、脏）先去色成金属灰，高饱和（红管、黄条、蓝灯）保留；明度拉到游戏范围；
    3×3 众数去噪；映射到游戏调色板；左上亮边、右下暗边；黑描边。"""
    a = np.asarray(img.convert('RGBA')).astype(np.float32)/255
    solid = a[..., 3] > 0.5; C = a[..., :3].copy()
    L = _lum(C)[..., None]; ch = C-L; sat = np.abs(ch).max(-1, keepdims=True)
    f = np.clip((sat-desat_below)/0.15, 0, 1)            # 0=去色, 1=保留
    ch = ch*f*keep_sat
    L2 = lo+(hi-lo)*np.clip(L, 0, 1)**gamma
    C2 = np.clip(L2+ch, 0, 1)
    # 映射到调色板
    P = palette.astype(np.uint8); Pl = _to_lab(P)
    lab = _to_lab((C2*255).astype(np.uint8))
    idx = ((lab[..., None, :]-Pl[None, None])**2).sum(-1).argmin(-1)
    if denoise:   # 3×3 众数（只在实心像素里），孤立杂色并到周围
        H, W = idx.shape; out = idx.copy()
        pad = np.pad(idx, 1, constant_values=-1); sp = np.pad(solid, 1)
        for y in range(H):
            for x in range(W):
                if not solid[y, x]: continue
                win = pad[y:y+3, x:x+3][sp[y:y+3, x:x+3]]
                vals, cn = np.unique(win, return_counts=True)
                if cn.max() >= 5 and (idx[y, x] not in vals[cn >= 2]): out[y, x] = vals[cn.argmax()]
        idx = out
    rgb = P[idx].astype(np.float32)
    # 亮边/暗边：上或左是空 → 亮；下或右是空 → 暗
    pS = np.pad(solid, 1)
    up, left = ~pS[:-2, 1:-1], ~pS[1:-1, :-2]; dn, rt = ~pS[2:, 1:-1], ~pS[1:-1, 2:]
    rgb[solid & (up | left)] *= rim; rgb[solid & (dn | rt) & ~(up | left)] *= shade
    o = np.zeros(a.shape, np.uint8); o[..., :3] = np.clip(rgb, 0, 255).astype(np.uint8); o[..., 3] = solid*255
    ring = (pS[:-2, 1:-1] | pS[2:, 1:-1] | pS[1:-1, :-2] | pS[1:-1, 2:]) & ~solid
    o[ring] = (*outline, 255)
    return Image.fromarray(o, 'RGBA')
