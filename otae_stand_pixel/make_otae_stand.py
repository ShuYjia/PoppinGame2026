# -*- coding: utf-8 -*-
"""
otae_stand_pixel: 将 otae/ 下 55 帧 Dola AI 待机序列转为透明背景 PNG。
处理原则: RGB 颜色原样, 只做 alpha 二值化 (0/255); 连通域去背; 清除固定位置水印。
"""
import os, json
import numpy as np
from PIL import Image
from scipy import ndimage

SRC = r"D:/UnityProject/Poppin/otae"
DST = r"D:/UnityProject/Poppin/otae_stand_pixel"
os.makedirs(DST, exist_ok=True)

# watermark zone (measured: text bbox x407-467, y612-628, fixed across frames)
WM_X0, WM_Y0 = 395, 605
BG_TOL = 35  # sum of abs channel diffs vs bg color

frames = sorted(f for f in os.listdir(SRC) if f.startswith("frame_") and f.endswith(".png"))
assert len(frames) == 55, f"expect 55 frames, got {len(frames)}"

processed = []
min_x0 = min_y0 = 10**9
max_x1 = max_y1 = -1

for name in frames:
    im = np.array(Image.open(os.path.join(SRC, name)).convert("RGB")).astype(int)
    H, W, _ = im.shape

    # 1) estimate bg color from four 40x40 corners (median)
    corners = np.concatenate([
        im[0:40, 0:40].reshape(-1, 3), im[0:40, W-40:W].reshape(-1, 3),
        im[H-40:0+H-40 if False else H-40:H, 0:40].reshape(-1, 3),
        im[H-40:H, W-40:W].reshape(-1, 3)])
    # corner samples include character in some frames (ears/feet reach bottom?), use median which is robust
    bg = np.median(corners, axis=0)

    # 2) candidate foreground
    dist = np.abs(im - bg).sum(axis=2)
    fg = dist > BG_TOL

    # 3) clear watermark zone
    fg[WM_Y0:, WM_X0:] = False

    # 4) connected-component background removal:
    #    background pixels connected to image border -> transparent
    bgmask = ~fg
    labels, n = ndimage.label(bgmask)
    border_labels = set(np.unique(np.concatenate([
        labels[0, :], labels[-1, :], labels[:, 0], labels[:, -1]])))
    border_labels.discard(0)
    transparent = np.isin(labels, list(border_labels))
    rgba = np.dstack([im.astype(np.uint8), np.where(transparent, 0, 255).astype(np.uint8)])

    # 5) track union content bbox (for unified crop)
    ys, xs = np.where(~transparent)
    min_x0, min_y0 = min(min_x0, xs.min()), min(min_y0, ys.min())
    max_x1, max_y1 = max(max_x1, xs.max()), max(max_y1, ys.max())

    processed.append((name, rgba))

# 6) unified crop across all frames (frames are globally aligned, dx=dy=0 verified)
print("union bbox:", min_x0, min_y0, max_x1, max_y1)
meta = {"source": SRC, "frames": [], "bbox": [int(min_x0), int(min_y0), int(max_x1 + 1), int(max_y1 + 1)],
        "block_px": 12, "fps": 12, "watermark_zone": [WM_X0, WM_Y0]}

out_size = None
for i, (name, rgba) in enumerate(processed, 1):
    crop = rgba[min_y0:max_y1 + 1, min_x0:max_x1 + 1]
    if out_size is None:
        out_size = crop.shape
    assert crop.shape == out_size
    out_name = f"otae_stand_{i:02d}.png"
    Image.fromarray(crop, "RGBA").save(os.path.join(DST, out_name), optimize=False)
    meta["frames"].append(out_name)

# 7) preview gif (12 fps) on checkerboard-free flat light bg for visibility
gif_frames = []
for fn in meta["frames"]:
    a = Image.open(os.path.join(DST, fn))
    g = Image.new("RGBA", a.size, (235, 235, 235, 255))
    g.alpha_composite(a)
    gif_frames.append(g.convert("P", palette=Image.ADAPTIVE))
gif_frames[0].save(os.path.join(DST, "otae_stand_preview.gif"), save_all=True,
                   append_images=gif_frames[1:], duration=1000 // meta["fps"], loop=0)

with open(os.path.join(DST, "otae_stand.json"), "w", encoding="utf-8") as f:
    json.dump(meta, f, ensure_ascii=False, indent=2)

print("frames:", len(meta["frames"]), "size:", out_size[1], "x", out_size[0])
print("done ->", DST)
