#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""ksm_death 质检：不依赖目视，全部用指标判定。"""
import os, glob, json, collections
import numpy as np
from PIL import Image
from scipy import ndimage

OUT = 'ksm_death_pixel'
SRC = 'ksm_death'

def alpha_report(im):
    a = np.asarray(im)
    al = a[:, :, 3]
    u = collections.Counter(al.ravel().tolist())
    return u

def motion(a, b):
    m0 = a[:, :, 3] > 127
    m1 = b[:, :, 3] > 127
    both = m0 & m1
    d_rgb = np.abs(a[:, :, :3].astype(int) - b[:, :, :3].astype(int)).max(axis=2)[both].mean() if both.any() else 0.0
    return float(d_rgb), int((m0 ^ m1).sum())

def flatness(a):
    """内部平坦度：与右/下邻居完全同色的像素占比（越高越像素化）"""
    al = a[:, :, 3] > 127
    rgb = a[:, :, :3]
    same = np.zeros(al.shape, bool)
    same[:-1, :] |= (al[:-1, :] & al[1:, :] & np.all(rgb[:-1, :] == rgb[1:, :], axis=2))
    same[:, :-1] |= (al[:, :-1] & al[:, 1:] & np.all(rgb[:, :-1] == rgb[:, 1:], axis=2))
    return float(same.sum()) / max(int(al.sum()), 1)

def main():
    frames = sorted(glob.glob(f'{OUT}/frames/*.png'))
    print(f'输出帧数: {len(frames)}')
    arrs = [np.asarray(Image.open(f).convert('RGBA')).astype(np.uint8) for f in frames]

    print('\n[QC1] alpha 二值性（应只有 0 与 255）')
    ok = True
    for i, (f, a) in enumerate(zip(frames, arrs)):
        u = alpha_report(Image.fromarray(a, 'RGBA'))
        bad = {k: v for k, v in u.items() if k not in (0, 255)}
        if bad:
            ok = False
            print(f'  帧{i:02d} 非二值 alpha: {bad}')
    print('  全部二值 ✓' if ok else '  存在半透明 alpha ✗')

    print('\n[QC2] 是否溢出 84x84 单元格')
    for i, a in enumerate(arrs):
        ys, xs = np.nonzero(a[:, :, 3] > 127)
        if len(ys):
            if ys.min() < 0 or ys.max() >= a.shape[0] or xs.min() < 0 or xs.max() >= a.shape[1]:
                print(f'  帧{i:02d} 内容贴边/溢出: y {ys.min()}-{ys.max()}, x {xs.min()}-{xs.max()}')
    print('  检查完成')

    print('\n[QC3] 帧间运动曲线（RGB 差 / 掩码异或像素数）')
    ms = []
    for i in range(1, len(arrs)):
        d, xr = motion(arrs[i - 1], arrs[i])
        ms.append((d, xr))
        print(f'  f{i-1:02d}->f{i:02d}  dRGB={d:6.2f}  mask_xor={xr:5d}')
    ds = [m[0] for m in ms]
    if ds:
        print(f'  dRGB 均值={np.mean(ds):.2f} 最大={np.max(ds):.2f} 最小={np.min(ds):.2f}')
        # 跳变检测：超过 2.5 倍中位数视为跳变
        med = np.median(ds)
        for i, d in enumerate(ds):
            if med > 0 and d > max(2.5 * med, med + 6):
                print(f'  ⚠ 帧 {i}->{i+1} 运动跳变 dRGB={d:.2f} (中位 {med:.2f})')

    print('\n[QC4] 每帧不透明像素数 / 内部平坦度')
    for i, a in enumerate(arrs):
        al = a[:, :, 3] > 127
        print(f'  帧{i:02d}  不透明={int(al.sum()):5d}  平坦度={flatness(a):.3f}')

    print('\n[QC5] 颜色数（量化后应 <= 调色板色数）')
    with open(f'{OUT}/ksm_death.json', encoding='utf-8') as fp:
        meta = json.load(fp)
    pal = set(tuple(c) for c in meta['palette'])
    print(f'  调色板 {len(pal)} 色')
    for i, a in enumerate(arrs):
        al = a[:, :, 3] > 127
        used = set(map(tuple, a[:, :, :3][al].tolist()))
        extra = used - pal
        print(f'  帧{i:02d} 用色 {len(used):3d}' + (f'  ⚠ 越界 {len(extra)}' if extra else '  ✓'))

    print('\n[QC6] 水印残留检查（输出帧右下角 x>=65,y>=70 是否有前景）')
    for i, a in enumerate(arrs):
        al = a[:, :, 3] > 127
        # 单元格 84，padding 6，内容 72；右下角对应原生格 ~58-72
        sub = al[70:84, 65:84]
        if sub.any():
            print(f'  帧{i:02d} 右下角仍有 {int(sub.sum())} 个前景像素')
    print('  检查完成')

    print('\n[QC7] 底边贴地一致性')
    for i, a in enumerate(arrs):
        ys, xs = np.nonzero(a[:, :, 3] > 127)
        if len(ys):
            print(f'  帧{i:02d}  底边 y={ys.max()}  左右 x={xs.min()}-{xs.max()}')

if __name__ == '__main__':
    main()
