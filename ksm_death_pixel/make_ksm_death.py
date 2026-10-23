#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
ksm 死亡动画帧生产脚本 v3
==========================
输入: ksm_death/frame_0NN.png  (82 张 720x720 白底 AI 生成连续帧)
输出: ksm_death_pixel/

关键结论与管线选择
------------------
1. 源图块宽 = 10px（双向周期检测确认，站立帧 power 0.5+），原生栅格 72x72。
   但倒地开始后 AI 让像素块跟随身体旋转重绘，网格轴向/相位不再固定
   （实测帧间相位 1.55 -> 6.71 -> 9.30 -> 0.65），所以**不能**用固定
   网格逐帧采样还原（会抖动且糊）。
2. ❌ 错误顺序：先 1/10「面积平均」降采样 → 采样窗跨块 → 颜色被平均糊掉
   （实测只剩 3 色）→ 再量化就成了剪影。
   ✅ 正确顺序：先在 720 分辨率上做**统一调色板量化**（消除抗锯齿渐变、
   使每个原生块内部趋于单色）→ 再用**众数**降采样（而非平均）取块代表色。
3. 水印「豆包AI生成」: x 604-703 / y 676-697，按 mask 精确抠除。
4. 平坦化: 删除过小 alpha 连通域（去飞溅噪点）+ 孤立像素并入邻域众数。
5. 对齐: 水平统一平移（基准=站立帧脚底中心 -> 内容区中心）；
        垂直逐帧底边贴内容区底部（游戏里原地倒地、不悬空）。
6. 抽帧: 等运动量取样 + 相邻过近帧去重，避免静止段浪费帧。
"""
import os, glob, json, collections
import numpy as np
from PIL import Image
from scipy import ndimage

# ============ CONFIG ============
SRC_DIR   = 'ksm_death'
OUT_DIR   = 'ksm_death_pixel'
PALETTE_SRC = 'D:/bang/nfc/ksm/12.png'   # 官方原生像素画，配色权威来源
BLOCK     = 10
NATIVE    = 720 // BLOCK          # 72
PAD       = 6
CELL      = NATIVE + PAD * 2      # 84
FPS       = 12
N_OUT     = 16
WHITE_THR = 238
WM_BOX    = (600, 670, 720, 720)
ALPHA_CUT = 0.5
MIN_ALPHA_BLOB = 6
COLOR_MIGRATE = 1.0      # LAB 色彩迁移强度：1=完全对齐官方分布，0=纯最近邻
# ================================

def load_rgb(path):
    return np.asarray(Image.open(path).convert('RGB')).astype(np.int16)

def build_watermark_mask(paths):
    """首帧(frame_020)角色 bbox 为 x222-523 / y126-552，
    故右下角任何非白像素只可能是水印。
    （不能用 82 帧中位数：41 帧静止躺姿占多数，会污染水印区）"""
    a = load_rgb(paths[0])
    inv = ~np.all(a > WHITE_THR, axis=2)
    reg = np.zeros((720, 720), bool)
    reg[WM_BOX[1]:, :] = True
    reg[:, WM_BOX[0]:] = True
    return inv & reg

def flood_bg(near_white):
    h, w = near_white.shape
    bg = np.zeros((h, w), bool)
    from collections import deque
    dq = deque()
    for x in range(w):
        for y in (0, h - 1):
            if near_white[y, x] and not bg[y, x]:
                bg[y, x] = True; dq.append((y, x))
    for y in range(h):
        for x in (0, w - 1):
            if near_white[y, x] and not bg[y, x]:
                bg[y, x] = True; dq.append((y, x))
    while dq:
        y, x = dq.popleft()
        for dy, dx in ((1,0),(-1,0),(0,1),(0,-1)):
            ny, nx = y + dy, x + dx
            if 0 <= ny < h and 0 <= nx < w and near_white[ny, nx] and not bg[ny, nx]:
                bg[ny, nx] = True; dq.append((ny, nx))
    return bg

def cutout(a, wm_mask):
    """抠白底：边界 flood-fill，保留角色内部白色（眼白/裙边/幽灵白身）。"""
    a = a.copy()
    a[wm_mask] = 255
    near_white = np.all(a > WHITE_THR, axis=2)
    bg = flood_bg(near_white)
    alpha = np.where(bg, 0, 255).astype(np.uint8)
    rgba = np.dstack([a.astype(np.uint8), alpha])
    return Image.fromarray(rgba, 'RGBA')

def load_official_palette(path=PALETTE_SRC):
    """从官方原生像素画提取精确调色板。
    12.png 是干净的原生像素画（640x640 / 块宽 10px / 原生 64x64 / 已带 alpha），
    共 21 色 —— 直接用其全部实际用色作为权威配色，不做任何吸附或重采样。"""
    a = np.asarray(Image.open(path).convert('RGBA'))
    al = a[:, :, 3] > 127
    c = collections.Counter(map(tuple, a[:, :, :3][al].tolist()))
    pal = [col for col, _ in c.most_common()]
    return pal, c


# ---------- LAB 色彩迁移（解决 AI 图整体偏暗 13.6%，避免头发全落到暗部色） ----------
def _rgb_to_lab(rgb):
    """rgb: (N,3) float 0..1 -> Lab (N,3)"""
    c = np.clip(rgb, 0, 1).astype(np.float64)
    m = c > 0.04045
    lin = np.where(m, ((c + 0.055) / 1.055) ** 2.4, c / 12.92)
    M = np.array([[0.4124, 0.3576, 0.1805],
                  [0.2126, 0.7152, 0.0722],
                  [0.0193, 0.1192, 0.9505]])
    xyz = lin @ M.T / np.array([0.95047, 1.00000, 1.08883])
    e, k = 0.008856, 903.3
    f = np.where(xyz > e, np.cbrt(xyz), (k * xyz + 16.0) / 116.0)
    return np.stack([116.0 * f[:, 1] - 16.0,
                     500.0 * (f[:, 0] - f[:, 1]),
                     200.0 * (f[:, 1] - f[:, 2])], axis=1)

def _lab_to_rgb(lab):
    L, a, b = lab[:, 0], lab[:, 1], lab[:, 2]
    fy = (L + 16.0) / 116.0
    fx = fy + a / 500.0
    fz = fy - b / 200.0
    def finv(t):
        return np.where(t ** 3 > 0.008856, t ** 3, (116.0 * t - 16.0) / 903.3)
    xyz = np.stack([finv(fx), finv(fy), finv(fz)], axis=1) * np.array([0.95047, 1.00000, 1.08883])
    Minv = np.array([[3.2406, -1.5372, -0.4986],
                     [-0.9689, 1.8758, 0.0415],
                     [0.0557, -0.2040, 1.0570]])
    c = xyz @ Minv.T
    m = c > 0.0031308
    out = np.where(m, 1.055 * np.power(np.maximum(c, 1e-9), 1 / 2.4) - 0.055, 12.92 * c)
    return np.clip(out, 0, 1)


def build_color_map(sample_rgb_u8, pal, dom_src, dom_tgt, strength=1.0):
    """构建「源色 -> 官方色」查找表。

    用「主导色配对」做亮度/色调校正，而不是全局统计量对齐：
    全局均值/标准差会被 AI 图里海量的抗锯齿过渡色带偏（实测会把红/蓝稀释、
    大面积推成纯白）。而两张图的**主导色**都对应同一部位（ksm 的头发/皮肤），
    用它算逐通道比例最稳。

    strength=0 时退化为纯最近邻映射（可用于对比验证）。
    """
    src = sample_rgb_u8.reshape(-1, 3).astype(np.float64) / 255.0
    if strength > 0:
        # 用 Lab 的 L 做「平移」（而非 RGB 线性比例）：
        # 线性比例会把亮部推出 255 变成纯白（实测白色占比 19.9%，官方仅 5.1%，
        # 浅肤色被吃掉）。平移只提亮暗部，亮部到 L=100 自然收敛。
        src_lab = _rgb_to_lab(src)
        l_src = _rgb_to_lab(np.array([dom_src], dtype=np.float64) / 255.0)[0, 0]
        l_tgt = _rgb_to_lab(np.array([dom_tgt], dtype=np.float64) / 255.0)[0, 0]
        src_lab[:, 0] = np.clip(src_lab[:, 0] + (l_tgt - l_src) * strength, 0, 100)
        moved = _lab_to_rgb(src_lab) * 255.0
    else:
        moved = src * 255.0
    # 必须用 int32：(255-0)^2*3 = 195075 会溢出 int16(32767)，
    # 导致高饱和色（红/蓝）距离算错、被错误映射（实测红色像素全丢）。
    m8 = np.clip(moved, 0, 255).round().astype(np.int32)
    p = np.array(pal, dtype=np.int32)
    d = ((m8[:, None, :] - p[None, :, :]) ** 2).sum(axis=2)
    return np.array(pal, dtype=np.uint8)[d.argmin(axis=1)]

def map_to_palette(rgb_u8, al, pal):
    """最近色映射，返回 RGBA uint8。"""
    flat = rgb_u8.reshape(-1, 3).astype(np.int32)
    p = np.array(pal, dtype=np.int32)
    out_idx = np.empty(len(flat), dtype=np.int32)
    step = 8192
    for s in range(0, len(flat), step):
        blk = flat[s:s + step]
        d = ((blk[:, None, :] - p[None, :, :]) ** 2).sum(axis=2)
        out_idx[s:s + step] = d.argmin(axis=1)
    mapped = p[out_idx].reshape(rgb_u8.shape).astype(np.uint8)
    return mapped

def downsample_mode(rgb_u8, al, block=BLOCK):
    """众数降采样：每个 block×block 窗口取不透明像素的众数色；
    不透明占比 < ALPHA_CUT 的窗口判为透明。"""
    h, w, _ = rgb_u8.shape
    nh, nw = h // block, w // block
    rgb = rgb_u8[:nh * block, :nw * block]
    mask = al[:nh * block, :nw * block]
    key = (rgb[:, :, 0].astype(np.int32) << 16) | (rgb[:, :, 1].astype(np.int32) << 8) | rgb[:, :, 2].astype(np.int32)
    key = np.where(mask, key, -1)
    # 注意：必须先 transpose(0,2,1,3) 把 (行组,列组) 提到前面，再 reshape。
    # 直接 reshape(nh,block,nw,block) -> (nh,nw,block*block) 会错位重组组内行列索引。
    blocks = key.reshape(nh, block, nw, block).transpose(0, 2, 1, 3).reshape(nh, nw, block * block)
    cover = mask.reshape(nh, block, nw, block).mean(axis=(1, 3))
    out_rgb = np.zeros((nh, nw, 3), np.uint8)
    out_al = (cover >= ALPHA_CUT)
    for j in range(nh):
        for i in range(nw):
            if not out_al[j, i]:
                continue
            vals = blocks[j, i]
            vals = vals[vals >= 0]
            if len(vals) == 0:
                out_al[j, i] = False
                continue
            c = collections.Counter(vals.tolist()).most_common(1)[0][0]
            out_rgb[j, i] = ((c >> 16) & 255, (c >> 8) & 255, c & 255)
    a = np.dstack([out_rgb, (out_al * 255).astype(np.uint8)])
    a[~out_al] = 0
    return Image.fromarray(a, 'RGBA')

def clean_artifacts(im, min_alpha=MIN_ALPHA_BLOB):
    """删除过小 alpha 连通域（飞溅噪点）+ 孤立像素并入邻域众数。单轮、保守。"""
    a = np.asarray(im).astype(np.uint8)
    al = a[:, :, 3] > 127
    if not al.any():
        return im
    lab, n = ndimage.label(al)
    if n:
        sizes = ndimage.sum(al, lab, range(1, n + 1))
        kill = np.isin(lab, np.nonzero(sizes < min_alpha)[0] + 1)
        if kill.any():
            al = al & ~kill
            a[~al] = 0
    if not al.any():
        return Image.fromarray(a, 'RGBA')
    rgb = a[:, :, :3].astype(np.int32)
    h, w = al.shape
    AL = np.pad(al, 1, constant_values=False)
    RGB = np.pad(rgb, ((1, 1), (1, 1), (0, 0)), constant_values=-9999)
    has_same = np.zeros((h, w), bool)
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            if dy == 0 and dx == 0:
                continue
            has_same |= al & AL[1 + dy:1 + dy + h, 1 + dx:1 + dx + w] & \
                       np.all(RGB[1 + dy:1 + dy + h, 1 + dx:1 + dx + w] == rgb, axis=2)
    ys, xs = np.nonzero(al & ~has_same)
    for y, x in zip(ys, xs):
        cnt = collections.Counter()
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                if dy == 0 and dx == 0:
                    continue
                ny, nx = y + dy, x + dx
                if 0 <= ny < h and 0 <= nx < w and al[ny, nx]:
                    cnt[tuple(int(v) for v in rgb[ny, nx])] += 1
        if cnt:
            a[y, x, :3] = cnt.most_common(1)[0][0]
    return Image.fromarray(a, 'RGBA')

def bbox_of_alpha(im):
    a = np.asarray(im)
    ys, xs = np.nonzero(a[:, :, 3] > 127)
    if len(ys) == 0: return None
    return int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1

def motion(a, b):
    m0 = a[:, :, 3] > 127
    m1 = b[:, :, 3] > 127
    both = m0 & m1
    return float(np.abs(a[:, :, :3].astype(int) - b[:, :, :3].astype(int)).max(axis=2)[both].mean()) if both.any() else 0.0

def pick_frames(arrs, n):
    """分段配额均匀取样。

    不用「等运动量取样」：那适合循环动画，对一次性动画会压缩静止段、
    放大运动段，破坏时间节奏（实测把 20->45 抽成相邻帧，丢掉整个失衡过程）。

    做法：先用运动量曲线找出「动作结束点」S（尾部持续低运动的起点），
    再把预算按两段时长比例分配，段内等间隔取样且各自保首尾帧。
    """
    N = len(arrs)
    if n >= N:
        return list(range(N))
    e = np.array([0.0] + [motion(arrs[i - 1], arrs[i]) for i in range(1, N)])
    mx = e.max()
    if mx <= 1e-6:
        return list(np.linspace(0, N - 1, n).round().astype(int))
    thr = mx * 0.30
    # 从后往前找第一个“运动量重新变高”的位置，其后为静止段
    S = N - 1
    i = N - 1
    run = 0
    while i >= 1:
        if e[i] < thr:
            run += 1
        else:
            run = 0
        if run >= 8:      # 连续 8 帧低运动 -> 认为动作已结束
            S = i
            break
        i -= 1
    S = max(S, 1)
    lenA, lenB = S + 1, N - S          # [0..S] 动作段, (S..N-1] 静止段
    nA = max(2, min(n - 2, int(round(n * lenA / float(N)))))
    nB = n - nA
    def seg(lo, hi, cnt):
        if cnt <= 1: return [lo]
        return [int(round(lo + (hi - lo) * k / float(cnt - 1))) for k in range(cnt)]
    out = seg(0, S, nA) + seg(S + 1, N - 1, nB)
    # 去重保序
    seen = set(); res = []
    for v in out:
        if v not in seen:
            seen.add(v); res.append(v)
    return res

def make_sheet(frames, cell, cols=None):
    n = len(frames)
    if cols is None: cols = n
    rows = (n + cols - 1) // cols
    sheet = Image.new('RGBA', (cols * cell, rows * cell), (0, 0, 0, 0))
    for i, im in enumerate(frames):
        r, c = divmod(i, cols)
        sheet.paste(im, (c * cell, r * cell))
    return sheet

def make_gif(frames, path, fps=FPS, scale=6):
    ims = [im.resize((im.width * scale, im.height * scale), Image.NEAREST) for im in frames]
    used = set()
    for im in ims:
        a = np.asarray(im); al = a[:, :, 3] > 127
        used.update(map(tuple, a[:, :, :3][al]))
    RESERVED = (255, 0, 255)
    assert RESERVED not in used, '保留色冲突'
    pal = [RESERVED] + sorted(used)
    index = {c: i for i, c in enumerate(pal)}
    assert len(pal) <= 256, f'调色板溢出 {len(pal)}'
    palflat = [v for c in pal for v in c]
    out = []
    for im in ims:
        a = np.asarray(im); al = a[:, :, 3] > 127
        p = Image.new('P', (im.width, im.height))
        p.putpalette(palflat)
        flat = a[:, :, :3].reshape(-1, 3).astype(np.int32)
        keys = np.array(list(index.keys()), dtype=np.int32)
        kvals = np.array([index[tuple(int(v) for v in k)] for k in keys], dtype=np.uint8)
        idx = np.zeros(len(flat), dtype=np.uint8)
        sel = np.nonzero(al.reshape(-1))[0]
        step = 8192
        for s in range(0, len(sel), step):
            blk = flat[sel[s:s + step]]
            d = ((blk[:, None, :] - keys[None, :, :]) ** 2).sum(axis=2)
            idx[sel[s:s + step]] = kvals[d.argmin(axis=1)]
        p.putdata(idx)
        out.append(p)
    dur = max(10, int(round(1000.0 / fps / 10)) * 10)
    out[0].save(path, save_all=True, append_images=out[1:], duration=dur,
                loop=0, transparency=0, disposal=2, optimize=False)
    return len(ims), dur

def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    paths = sorted(glob.glob(os.path.join(SRC_DIR, '*.png')))
    assert paths, 'no source frames'
    print(f'source frames: {len(paths)}')

    print('[1/8] 定位水印...')
    wm = build_watermark_mask(paths)
    ys, xs = np.nonzero(wm)
    print(f'      水印像素 {wm.sum()}  bbox x{xs.min()}-{xs.max()} y{ys.min()}-{ys.max()}')

    print('[2/8] 抠白底...')
    cuts = [cutout(load_rgb(p), wm) for p in paths]

    print('[3/8] 载入官方配色（12.png）...')
    pal, pal_cnt = load_official_palette()
    print(f'      官方调色板 {len(pal)} 色（来自 {PALETTE_SRC}）')
    for c_, n_ in pal_cnt.most_common(8):
        print(f'        {c_}  x{n_}')

    print('[4/8] 构建 AI色→官方色 查找表（LAB 迁移 + 最近邻）...')
    uniq = set()
    for im in cuts:
        a = np.asarray(im)
        al = a[:, :, 3] > 127
        uniq.update(map(tuple, a[:, :, :3][al].tolist()))
    uniq = np.array(sorted(uniq), dtype=np.uint8)
    # 主导色配对：两张图的最大频次色都对应同一部位（头发/皮肤）
    ai_cnt = collections.Counter()
    for im in cuts:
        a = np.asarray(im)
        al = a[:, :, 3] > 127
        ai_cnt.update(map(tuple, (((a[:, :, :3][al] // 8) * 8)).tolist()))
    dom_src = ai_cnt.most_common(1)[0][0]
    dom_tgt = pal_cnt.most_common(1)[0][0]
    print(f'      主导色配对: AI {dom_src} -> 官方 {dom_tgt}')
    mapped_colors = build_color_map(uniq, pal, dom_src, dom_tgt, strength=COLOR_MIGRATE)
    print(f'      源色 unique={len(uniq)}  ->  映射到官方 {len(pal)} 色（迁移强度 {COLOR_MIGRATE}）')
    uk = (uniq[:, 0].astype(np.int32) << 16) | (uniq[:, 1].astype(np.int32) << 8) | uniq[:, 2].astype(np.int32)
    order = np.argsort(uk)
    sk, sc = uk[order], mapped_colors[order]

    quants = []
    for im in cuts:
        a = np.asarray(im).astype(np.uint8)
        al = a[:, :, 3] > 127
        k = (a[:, :, 0].astype(np.int32) << 16) | (a[:, :, 1].astype(np.int32) << 8) | a[:, :, 2].astype(np.int32)
        pos = np.clip(np.searchsorted(sk, k), 0, len(sk) - 1)
        mapped = sc[pos]
        out = np.dstack([mapped, (al * 255).astype(np.uint8)])
        out[~al] = 0
        quants.append(Image.fromarray(out, 'RGBA'))

    print('[5/8] 众数降采样 1/10 -> 原生 72x72...')
    downs = []
    for im in quants:
        a = np.asarray(im).astype(np.uint8)
        downs.append(downsample_mode(a[:, :, :3], a[:, :, 3] > 127))
    print(f'      原生尺寸 {downs[0].size}')

    print('[6/8] 平坦化（清孤立像素 / 小连通域）...')
    cleaned = [clean_artifacts(im) for im in downs]

    print('[7/8] 抽帧 + 对齐...')
    arrs = [np.asarray(im) for im in cleaned]
    idxs = pick_frames(arrs, N_OUT)
    print(f'      选中源帧: {[i + 20 for i in idxs]}')

    a0 = arrs[0]
    al0 = a0[:, :, 3] > 127
    foot_rows = np.nonzero(al0.any(axis=1))[0]
    band = al0[foot_rows.max() - 5: foot_rows.max() + 1]
    xs_b = np.nonzero(band.any(axis=0))[0]
    cx_foot = float(xs_b.mean())
    tx = int(round(NATIVE / 2.0 - cx_foot))
    print(f'      脚底中心 x={cx_foot:.2f} -> tx={tx}')

    frames = []
    for i in idxs:
        im = cleaned[i]
        bb = bbox_of_alpha(im)
        canvas = Image.new('RGBA', (CELL, CELL), (0, 0, 0, 0))
        if bb:
            # 底边贴内容区底部：粘贴后内容底边索引 = PAD + ty + bb[3] - 1 = PAD + NATIVE - 1
            ty = NATIVE - bb[3]
            canvas.paste(im, (PAD + tx, PAD + ty), im)
        frames.append(canvas)

    print('[8/8] 输出...')
    sheet = make_sheet(frames, CELL)
    sheet.save(f'{OUT_DIR}/ksm_death_sheet.png')
    sheet.resize((sheet.width * 4, sheet.height * 4), Image.NEAREST).save(f'{OUT_DIR}/ksm_death_sheet_x4.png')
    grid = make_sheet(frames, CELL, cols=4)
    grid.save(f'{OUT_DIR}/ksm_death_grid.png')
    grid.resize((grid.width * 4, grid.height * 4), Image.NEAREST).save(f'{OUT_DIR}/ksm_death_grid_x4.png')
    # 主展示图：16 帧 4x4、放大 6 倍、透明背景
    grid.resize((grid.width * 6, grid.height * 6), Image.NEAREST).save(f'{OUT_DIR}/ksm_death_16frames_x6.png')
    fd = f'{OUT_DIR}/frames'; os.makedirs(fd, exist_ok=True)
    for i, im in enumerate(frames):
        im.save(f'{fd}/ksm_death_{i:02d}.png')
    cleaned[0].save(f'{OUT_DIR}/ksm_death_base_72x72.png')
    cleaned[0].resize((NATIVE * 8, NATIVE * 8), Image.NEAREST).save(f'{OUT_DIR}/ksm_death_base_72x72_x8.png')
    sw = 16
    pimg = Image.new('RGB', (len(pal) * sw, sw * 2), (255, 255, 255))
    for i, c in enumerate(pal):
        for y in range(sw * 2):
            for x in range(sw):
                pimg.putpixel((i * sw + x, y), c)
    pimg.save(f'{OUT_DIR}/ksm_death_palette.png')
    n, dur = make_gif(frames, f'{OUT_DIR}/ksm_death.gif')
    print(f'      GIF {n} 帧 @ {dur}ms')

    meta = dict(
        name='ksm_death', display_name='ksm 死亡',
        source=f'{SRC_DIR}/frame_0[20-101].png', source_frames=len(paths),
        frame_count=len(frames), fps=FPS, loop=False,
        duration_sec=round(len(frames) / FPS, 3),
        native_grid=[NATIVE, NATIVE], padding=PAD, cell=[CELL, CELL],
        frame_size=[CELL, CELL],   # 兼容预览模板
        blink_frames=[],
        pivot=dict(x=CELL / 2.0, y=NATIVE - 1 + PAD, note='脚底中心；所有帧底边贴地'),
        palette_count=len(pal), palette=pal,
        palette_source=dict(
            file=PALETTE_SRC,
            note='官方原生像素画（640x640 / 块宽 10px / 原生 64x64 / 已带 alpha / 21 色）',
            mapping='主导色配对算 Lab 明度平移，再最近邻映射到官方 21 色',
            correction='AI 源图整体比官方图暗约 13.6%，仅平移明度、保留色相与饱和度'),
        alignment=dict(horizontal='统一平移，基准=站立帧脚底中心',
                       vertical='逐帧底边贴内容区底部（原地倒地，不悬空）'),
        processing=dict(
            watermark_removed=True, watermark_bbox=list(WM_BOX),
            order='先统一调色板量化（消抗锯齿渐变）-> 再众数降采样（非平均）',
            downsample=f'1/{BLOCK} 众数',
            quantize='全序列统一调色板最近色映射',
            flatten='清理孤立像素与过小 alpha 连通域',
            alpha='格内覆盖度<0.5 丢弃，输出二值 alpha'),
        selected_source_frames=[i + 20 for i in idxs],
        unity_import=dict(SpriteMode='Multiple', GridByCellSize=[CELL, CELL],
                          FilterMode='Point', Compression='None',
                          Pivot='BottomCenter',
                          PPU_note='与 ksm_hurt_pixel 同一 PPU（同为 10px/block 原生格）'),
    )
    with open(f'{OUT_DIR}/ksm_death.json', 'w', encoding='utf-8') as f:
        json.dump(meta, f, ensure_ascii=False, indent=2)
    print(f'\n完成 -> {OUT_DIR}/')

if __name__ == '__main__':
    main()
