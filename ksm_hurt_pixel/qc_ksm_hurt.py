# -*- coding: utf-8 -*-
# ksm_hurt 质检：逐帧按层逆映射 + 调色板不变性 + 零位移帧与基础帧一致
from PIL import Image
import os
OUT = r'D:\UnityProject\Poppin\ksm_hurt_pixel'
import sys
sys.path.insert(0, OUT)
import make_ksm_hurt as M          # 曲线与分层以生成脚本为准，避免两边写死不同步
N=M.N; PAD=M.PAD; CELL=M.CELL; NF=M.NF
DX_BODY=M.DX_BODY; DX_HEAD=M.DX_HEAD; DY_BODY=M.DY_BODY
DX_LEGS=M.DX_LEGS; DY_LEGS=M.DY_LEGS; DY_HEAD=M.DY_HEAD
HEAD_ROWS=set(M.HEAD_ROWS); LEG_ROWS=set(M.LEG_ROWS); ROW42_HEAD=set(M.ROW42_HEAD_COLORS)
headimg=Image.open(os.path.join(OUT,'ksm_head_64x64.png')).convert('RGBA')
hbp=headimg.load()
headpix={(x,y):hbp[x,y] for y in range(N) for x in range(N)}
BLINK=set(M.BLINK_FRAMES)
base=Image.open(os.path.join(OUT,'ksm_base_64x64.png')).convert('RGBA')
headc=Image.open(os.path.join(OUT,'ksm_head_closed_64x64.png')).convert('RGBA')
bp=base.load(); hcp=headc.load()
basepix={(x,y):bp[x,y] for y in range(N) for x in range(N)}
basepal={p for p in basepix.values() if p[3]>0}
def head_src(x, y, p):
    """闭眼帧的头层源像素取自闭眼头层"""
    q = hcp[x, y]
    return q if q[3] > 0 else p
def layer_of(x,y,p):
    if y in LEG_ROWS: return 0
    if y in HEAD_ROWS or (y==42 and p[:3] in ROW42_HEAD): return 2
    return 1
LAY=[(DX_LEGS,DY_LEGS),(DX_BODY,DY_BODY),(DX_HEAD,DY_HEAD)]
err=0
for i in range(NF):
    fr=Image.open(os.path.join(OUT,'frames',f'ksm_hurt_{i:02d}.png')).convert('RGBA')
    assert fr.size==(CELL,CELL), f'frame {i} size {fr.size}'
    fp=fr.load()
    for ty in range(CELL):
        for tx in range(CELL):
            got=fp[tx,ty]
            exp=(0,0,0,0)
            for li in (2,1,0):                 # 上层先：head/body/legs
                dxs,dys = LAY[li]
                sx, sy = tx-PAD-dxs[i], ty-PAD-dys[i]
                if 0<=sx<N and 0<=sy<N:
                    sp=basepix[(sx,sy)]
                    if sp[3]>0 and layer_of(sx,sy,sp)==li:
                        exp = head_src(sx, sy, sp) if (li == 2 and i in BLINK) else sp
                        break
            if got!=exp:
                err+=1
                if err<=5: print(f'MISMATCH f{i} ({tx},{ty}) got={got} exp={exp}')
    frpix={(x,y):fp[x,y] for y in range(CELL) for x in range(CELL)}
    frpal={p for p in frpix.values() if p[3]>0}
    extra=frpal-basepal
    if extra: print(f'f{i} 引入了基础帧没有的颜色: {list(extra)[:3]}'); err+=1
print('layer-mapping errors:', err)

# 闭眼改动必须严格局限在眼部框内，且只能用原图已有色
allowed = set(M.EYE_COLORS) | {M.SKIN[:3], M.LASH[:3]}
diff = 0; out_of_box = 0; bad_color = 0
for y in range(N):
    for x in range(N):
        a, b = headpix[(x, y)], hcp[x, y]     # 与「头层」比，不是与完整基础帧比
        if a == b: continue
        diff += 1
        if not any(x0 <= x <= x1 and y0 <= y <= y1 for (x0, y0, x1, y1) in M.EYE_BOXES):
            out_of_box += 1
        if b[:3] not in allowed:
            bad_color += 1
print(f'闭眼改动像素 {diff} 个 | 越出眼部框 {out_of_box} 个 | 用了非原图色 {bad_color} 个')

# 逐帧眼睛开合验证
for i in range(NF):
    fr = Image.open(os.path.join(OUT, 'frames', f'ksm_hurt_{i:02d}.png')).convert('RGBA')
    fp = fr.load()
    lash_ok = all(fp[x + PAD + DX_HEAD[i], y + PAD + DY_HEAD[i]] == M.LASH for (x, y) in M.EYE_LINE)
    iris_visible = any(
        fp[x + PAD + DX_HEAD[i], y + PAD + DY_HEAD[i]][:3] == (145, 87, 189)
        for y in range(35, 39) for x in range(26, 39))
    print(f'f{i:02d} {"闭眼" if i in BLINK else "睁眼"}: 眼线到位={lash_ok} 虹膜可见={iris_visible}',
          '' if (lash_ok and not iris_visible) == (i in BLINK) else '  <<< 不符预期')
zero_frames = [i for i in range(NF)
               if DX_BODY[i]==DX_HEAD[i]==DX_LEGS[i]==DY_BODY[i]==DY_HEAD[i]==DY_LEGS[i]==0]
for i in zero_frames:
    fr=Image.open(os.path.join(OUT,'frames',f'ksm_hurt_{i:02d}.png')).convert('RGBA')
    fp=fr.load(); bad=0
    for y in range(N):
        for x in range(N):
            if fp[x+PAD,y+PAD]!=basepix[(x,y)]: bad+=1
    print(f'frame{i} vs base diff pixels:', bad)
