# -*- coding: utf-8 -*-
"""
ksm 受击(hurt)动画生成 —— 基于 ksm_base_64x64.png（原生 64x64，无吸附重建）
铁律：不修改原美术 = 颜色原样保留 + 只做整块平移（无重绘）
    唯一例外：受击帧「闭眼」，且只用原图已有颜色（脸肤色 + 原眼黑 8）
分层（绘制顺序 = 先画的在下层）：腿(底层) -> 躯干+裙+吉他 -> 头+发(顶层)
"""
from PIL import Image
import os, json

BASE = r"D:\UnityProject\Poppin\ksm_hurt_pixel\ksm_base_64x64.png"
OUT  = r"D:\UnityProject\Poppin\ksm_hurt_pixel"
N    = 64          # 原生逻辑尺寸
PAD  = 6           # 四周留白（最大位移 5px + 余量）
FPS  = 12

# ---------- 分层 ----------
# 头层：行 23..41 全部 + 行 42 中的发丝/下巴色
HEAD_ROWS = range(23, 42)
ROW42_HEAD_COLORS = {(0,0,0), (118,68,68), (216,155,151)}  # 描边黑/深棕发/下巴影 9
# 腿层：行 57..63
LEG_ROWS = range(57, 64)
# 躯干层：其余全部（行 42 剩余 + 43..56）

# ---------- 眼部（闭眼用，坐标来自基础帧实测）----------
EYE_COLORS = {(0,0,0), (255,255,255), (145,87,189), (71,39,118), (213,156,227)}  # 8/4/c/h/d
EYE_BOXES  = [(26, 35, 31, 38), (36, 35, 38, 38)]        # (x0,y0,x1,y1) 左眼 / 右眼
SKIN       = (242, 210, 207, 255)                        # 脸肤色（原图已有色）
LASH       = (0, 0, 0, 255)                              # 原图眼黑
EYE_LINE   = [(x, 37) for x in range(27, 32)] + [(x, 37) for x in range(36, 39)]  # 闭眼 1px 线（中线偏下）

# ---------- 动画曲线（12 帧 @12fps = 1.0s）----------
# dx 为负 = 向左弹（可整镜 flipX 镜像到受击侧）
# 腿完全跟随躯干（DX_LEGS = DX_BODY）：整体被击退，身-腿零错位
DX_BODY = [0, -2, -3, -3, -3, -3, -2, -2, -1, -1, 0, 0]
DY_BODY = [0,  2,  3,  3,  3,  3,  2,  2,  1,  1, 0, 0]   # 屈膝下沉（裙摆盖腿=重叠=安全）
DX_LEGS = DX_BODY
DY_LEGS = [0]*12
# 向左转头：hold 期头比躯干再往左 1px（配合缩脖=扭头躲避），|相对| ≤1
DX_HEAD = [0, -2, -3, -4, -4, -4, -3, -2, -1, -1, 0, 0]
# 缩脖：冲击期头比躯干多沉 1px（只沉不浮，重叠安全）
DY_HEAD = [0,  2,  4,  4,  4,  4,  2,  2,  1,  1, 0, 0]
# 闭眼帧（受击期闭眼，最后两帧睁眼）
BLINK_FRAMES = [1, 2, 3, 4, 5, 6, 7, 8, 9]
NF = len(DX_BODY)
assert (len(DX_HEAD)==len(DY_HEAD)==len(DY_BODY)==len(DX_LEGS)==len(DY_LEGS)==NF)
# 安全约束：头只能比躯干多沉（不能上浮撕缝）
assert all(dyh >= dyb for dyh, dyb in zip(DY_HEAD, DY_BODY))
# 安全约束：头/躯干水平相对错位 ≤1px（脖子不撕缝）
assert all(abs(dxh - dxb) <= 1 for dxh, dxb in zip(DX_HEAD, DX_BODY))

CELL = N + 2*PAD

def load_layers():
    base = Image.open(BASE).convert('RGBA')
    assert base.size == (N, N)
    px = base.load()
    legs  = Image.new('RGBA', (N, N), (0,0,0,0))
    body  = Image.new('RGBA', (N, N), (0,0,0,0))
    head  = Image.new('RGBA', (N, N), (0,0,0,0))
    lp, bp, hp = legs.load(), body.load(), head.load()
    for y in range(N):
        for x in range(N):
            p = px[x, y]
            if p[3] == 0:
                continue
            if y in LEG_ROWS:
                lp[x, y] = p
            elif y in HEAD_ROWS or (y == 42 and p[:3] in ROW42_HEAD_COLORS):
                hp[x, y] = p
            else:
                bp[x, y] = p
    return base, legs, body, head

def make_closed_eye_head(head):
    """闭眼头层：眼部框内眼色→脸肤色，再画 1px 闭合眼线（颜色全取自原图）"""
    closed = head.copy()
    cp = closed.load()
    for (x0, y0, x1, y1) in EYE_BOXES:
        for y in range(y0, y1+1):
            for x in range(x0, x1+1):
                if cp[x, y][3] > 0 and cp[x, y][:3] in EYE_COLORS:
                    cp[x, y] = SKIN
    for (x, y) in EYE_LINE:
        cp[x, y] = LASH
    return closed

def compose(layers, i):
    fr = Image.new('RGBA', (CELL, CELL), (0,0,0,0))
    for img, dxs, dys in layers[i]:
        shifted = Image.new('RGBA', (N, N), (0,0,0,0))
        shifted.alpha_composite(img, (dxs[i], dys[i]))
        fr.alpha_composite(shifted, (PAD, PAD))
    return fr

def main():
    base, legs, body, head = load_layers()
    head_closed = make_closed_eye_head(head)

    frames_dir = os.path.join(OUT, 'frames')
    os.makedirs(frames_dir, exist_ok=True)
    frames = []
    for i in range(NF):
        h = head_closed if i in BLINK_FRAMES else head
        layer_pack = [(legs, DX_LEGS, DY_LEGS), (body, DX_BODY, DY_BODY), (h, DX_HEAD, DY_HEAD)]
        fr = compose({i: layer_pack}, i)
        fr.save(os.path.join(frames_dir, f'ksm_hurt_{i:02d}.png'))
        frames.append(fr)

    # 横条图集
    sheet = Image.new('RGBA', (CELL*NF, CELL), (0,0,0,0))
    for i, fr in enumerate(frames):
        sheet.alpha_composite(fr, (i*CELL, 0))
    sheet.save(os.path.join(OUT, 'ksm_hurt_sheet.png'))

    # 网格版 6x2
    grid = Image.new('RGBA', (CELL*6, CELL*2), (0,0,0,0))
    for i, fr in enumerate(frames):
        grid.alpha_composite(fr, ((i%6)*CELL, (i//6)*CELL))
    grid.save(os.path.join(OUT, 'ksm_hurt_grid.png'))

    # x8 放大图集（肉眼检查用）
    sheet.resize((sheet.width*8, sheet.height*8), Image.NEAREST).save(
        os.path.join(OUT, 'ksm_hurt_sheet_x8.png'))

    # 头层与闭眼头层（供质检/复核）
    head.save(os.path.join(OUT, 'ksm_head_64x64.png'))
    head_closed.save(os.path.join(OUT, 'ksm_head_closed_64x64.png'))

    # GIF（保留色 0 号 = 品红，防止透明索引吃色）
    used = {p[:3] for p in base.getdata() if p[3] > 0}
    assert (255,0,255) not in used
    pal_img = Image.new('P', (1,1))
    pal = [(255,0,255)] + sorted(used) + [(0,0,0)] * (256 - 1 - len(used))
    pal_img.putpalette([c for rgb in pal for c in rgb])
    S = 8
    gif_frames = []
    for fr in frames:
        big = fr.resize((CELL*S, CELL*S), Image.NEAREST).convert('RGBA')
        p = big.convert('P')
        pp = p.load()
        br = big.load()
        for y in range(big.height):
            for x in range(big.width):
                r,g,b,a = br[x,y]
                pp[x,y] = 0 if a < 128 else (pal.index((r,g,b)))
        p.info['transparency'] = 0
        gif_frames.append(p)
    gif_frames[0].save(os.path.join(OUT, 'ksm_hurt.gif'), save_all=True,
                       append_images=gif_frames[1:], duration=int(1000/FPS), loop=0,
                       transparency=0, disposal=2)

    # 元数据
    pal_colors = sorted({p[:3] for p in base.getdata() if p[3] > 0})
    meta = {
        'name': 'ksm_hurt', 'display_name': 'ksm',
        'frames': NF, 'frame_count': NF, 'fps': FPS,
        'loop_duration_sec': NF / FPS,
        'cell': [CELL, CELL], 'frame_size': [CELL, CELL], 'padding': PAD,
        'blink_frames': BLINK_FRAMES, 'palette': [list(c) for c in pal_colors],
        'native_grid': [N, N],
        'motion': {
            'body_vertical_offset_px': DY_BODY,
            'layers': [
                {'name': 'legs',  'dx': DX_LEGS, 'dy': DY_LEGS},
                {'name': 'body',  'dx': DX_BODY, 'dy': DY_BODY},
                {'name': 'head',  'dx': DX_HEAD, 'dy': DY_HEAD},
            ],
        },
        'source_base': 'ksm_base_64x64.png',
        'native_grid_note': '64x64 (source 640x640, block 10px)',
        'palette_locked': True, 'pixel_edits': 'none (translation only)',
        'layers_note': ['legs(static)', 'body(dx_body,dy_body)', 'head(dx_head lag 1f, dy_head=dy_body)'],
        'curves': {'dx_body': DX_BODY, 'dx_head': DX_HEAD, 'dy_body': DY_BODY},
        'unity_import': {
            'SpriteMode': 'Multiple',
            'GridByCellSize': [CELL, CELL],
            'FilterMode': 'Point', 'Compression': 'None', 'Pivot': 'BottomCenter',
            'PPU_note': '与项目其他 ksm 动画保持同一 PPU 换算',
        },
    }
    with open(os.path.join(OUT, 'ksm_hurt.json'), 'w', encoding='utf-8') as f:
        json.dump(meta, f, ensure_ascii=False, indent=2)
    print(f'OK: {NF} frames, cell {CELL}x{CELL}, sheet {sheet.width}x{sheet.height}')

if __name__ == '__main__':
    main()
