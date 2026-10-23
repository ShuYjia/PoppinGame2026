#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
ksm 死亡动画 v2 独立质检脚本 (从磁盘读成品, 不依赖生产脚本)
用法: python qc_ksm_death2.py
"""
import os, sys, json, glob, collections
import numpy as np
from PIL import Image
from scipy import ndimage

BASE = os.path.dirname(os.path.abspath(__file__))
OFFICIAL = 'D:/bang/nfc/ksm/12.png'
NATIVE, PAD, CELL = 64, 6, 76
N_OUT = 16

def official():
    im = Image.open(OFFICIAL).convert('RGBA')
    nat = np.asarray(im.resize((NATIVE, NATIVE), Image.NEAREST))
    cnt = collections.Counter()
    al = nat[:, :, 3] > 127
    for p in nat[al][:, :3].tolist():
        cnt[tuple(p)] += 1
    return nat, [c for c, _ in cnt.most_common()]

def main():
    nat, pal = official()
    palset = set(pal)
    paths = sorted(glob.glob(os.path.join(BASE, 'frames', 'ksm_death2_*.png')))
    if len(paths) != N_OUT:
        print('FAIL 帧数 %d != %d' % (len(paths), N_OUT)); sys.exit(1)
    ok = True
    print('=== 质检 %s (%d 帧) ===' % (BASE, len(paths)))
    for i, p in enumerate(paths):
        im = Image.open(p).convert('RGBA')
        a = np.asarray(im)
        if a.shape[:2] != (CELL, CELL):
            print('frame%02d FAIL 尺寸 %s' % (i, a.shape)); ok = False; continue
        sub = a[PAD:PAD + NATIVE, PAD:PAD + NATIVE]     # 内容区
        al = sub[:, :, 3] > 127
        # 1 调色板
        cols = set(map(tuple, sub[al][:, :3].tolist()))
        bad = cols - palset
        if bad:
            print('frame%02d FAIL 越界色 %s' % (i, bad)); ok = False
        # 2 alpha 二值
        if not set(np.unique(sub[:, :, 3]).tolist()) <= {0, 255}:
            print('frame%02d FAIL alpha 非二值' % i); ok = False
        # 3 padding 区必须全透明 (防内容溢出到相邻帧)
        over = a.copy(); over[PAD:PAD + NATIVE, PAD:PAD + NATIVE, 3] = 0
        if (over[:, :, 3] > 0).any():
            print('frame%02d FAIL 内容溢出 padding 区' % i); ok = False
        # 4 触地 (全部帧都贴地: AI 序列无腾空帧)
        if not al[NATIVE - 1].any():
            print('frame%02d FAIL 底行无触地内容' % i); ok = False
        # 5 连通性
        lab, n = ndimage.label(al)
        if i <= 9 and n != 1:
            print('frame%02d FAIL 身体连通域 %d 个' % (i, n)); ok = False
        if i >= 10 and n > 5:
            print('frame%02d FAIL 星星碎片 %d 个' % (i, n)); ok = False
        ys, xs = np.nonzero(al)
        print('frame%02d  bbox x%d-%d y%d-%d  px=%d  comps=%d' %
              (i, xs.min(), xs.max(), ys.min(), ys.max(), al.sum(), n))
        if i == 0:
            nal = nat[:, :, 3] > 127
            same = (al == nal).all() and (sub[:, :, :3][al] == nat[:, :, :3][nal]).all()
            print('frame00 == 官方帧(不透明区 RGB 全等): %s' % same)
            ok &= same
    # 6 帧 11-16 身体必须与帧 10 完全一致; 星星会移动, 故只允许白/浅紫像素自由增减
    STAR = {(255, 255, 255), (213, 156, 227)}
    base = np.asarray(Image.open(paths[9]).convert('RGBA'))[PAD:PAD + NATIVE, PAD:PAD + NATIVE]
    bb = base[:, :, 3] > 127
    for i in range(10, N_OUT):
        d = np.asarray(Image.open(paths[i]).convert('RGBA'))[PAD:PAD + NATIVE, PAD:PAD + NATIVE]
        db = d[:, :, 3] > 127
        m_del = bb & ~db
        if m_del.any():
            dc = set(map(tuple, base[m_del][:, :3].tolist()))
            if dc - STAR:
                print('frame%02d FAIL 身体像素消失了 %s' % (i, dc - STAR)); ok = False
        m_new = db & ~bb
        nc = set(map(tuple, d[m_new][:, :3].tolist())) if m_new.any() else set()
        if nc - STAR:
            print('frame%02d FAIL 新增像素非星星色 %s' % (i, nc)); ok = False
        print('frame%02d 星色增/减 %d/%d px' % (i, int(m_new.sum()), int(m_del.sum())))
    print('结果: %s' % ('PASS' if ok else 'FAIL'))
    return 0 if ok else 1

if __name__ == '__main__':
    sys.exit(main())
