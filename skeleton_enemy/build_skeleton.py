# -*- coding: utf-8 -*-
"""骷髅小怪（Skeleton）像素动画生成器 —— 原创手绘，非还原。

规格：原生 28x32，每帧 32x36（四周 padding 2），12fps，pivot 脚底中心。
造型用一张字符网格 BASE 定义（28 列 x 32 行），改这一个常量就能改造型。

字符含义：
    .  透明        B  骨主色      L  骨亮部
    S  骨阴影      D  骨深（牙缝/缝隙）
    k  眼窝黑      f  鬼火蓝      F  焰心白
    #  自动描边（不手写，由 add_outline 生成）

分层（决定动画时谁跟谁动）：
    head  y1..13            arm_l / arm_r  y14..25 中 x<=8 / x>=19
    body  y14..25 中段       leg_l / leg_r  y26..31 中 x<=13 / x>=14

用法: python build_skeleton.py
"""
import json
import math
import os

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))

# ---------------------------------------------------------------- 基础参数
W, H = 28, 32          # 原生画布
PAD = 2                # 每帧四周留白
FW, FH = W + PAD * 2, H + PAD * 2   # 32 x 36
FPS = 12
SCALE = 8              # 预览放大倍数

# ---------------------------------------------------------------- 造型网格
BASE = [
    "............................",  # y0
    "..........BBBBBBBB..........",  # y1  颅顶
    "........BLLLLLLLLLLB........",  # y2
    ".......BLLLLLLLLLLLLB.......",  # y3
    ".......BBBkkBBBBkkBBB.......",  # y4  眼窝上缘（收窄成圆）
    ".......BBkFFkBBkFFkBB.......",  # y5  鬼火
    ".......BBfFFfBBfFFfBB.......",  # y6  鬼火
    ".......BBBkkBBBBkkBBB.......",  # y7  眼窝下缘
    ".......BBBBBkkkkBBBBB.......",  # y8  鼻腔（倒三角上沿）
    "........SBBBBkkBBBBS........",  # y9  鼻腔 + 颧骨阴影缝
    ".........BLLSLLSLLB.........",  # y10 上颌牙列（浅牙缝）
    ".........BBBSBBSBBB.........",  # y11 下颌牙列
    "..........BBBBBBBB..........",  # y12 下颌底
    "...........BBBBBB...........",  # y13 下巴
    ".............BB.............",  # y14 颈椎
    ".......BBBBBBBBBBBBBB.......",  # y15 肩（含肩关节球）
    "......BB.BBBBLLBBBB.BB......",  # y16 胸（脊柱高光）
    "......BB.BSSSLLSSSB.BB......",  # y17 肋骨 1
    "......BB.BBBBLLBBBB.BB......",  # y18
    "......BB.BSSSLLSSSB.BB......",  # y19 肋骨 2
    "......BB.BBBBLLBBBB.BB......",  # y20
    "......BB.BSSSLLSSSB.BB......",  # y21 肋骨 3
    "......BB..BBBLLBBB..BB......",  # y22 腰
    "......BB..BBBBBBBB..BB......",  # y23 骨盆上沿 + 前臂
    ".....BBBB.BBBBBBBB.BBBB.....",  # y24 手掌 + 骨盆
    ".....B..B..BBBBBB..B..B.....",  # y25 爪尖 + 骨盆底
    "..........BB....BB..........",  # y26 股骨
    "..........BB....BB..........",  # y27
    "..........BBB..BBB..........",  # y28 膝盖
    "..........BB....BB..........",  # y29 小腿
    "..........BB....BB..........",  # y30
    ".........BBBB..BBBB.........",  # y31 脚
]

assert len(BASE) == H, len(BASE)
for i, row in enumerate(BASE):
    assert len(row) == W, "row %d len=%d" % (i, len(row))

# ---------------------------------------------------------------- 调色板
OUTLINE = (36, 31, 43)      # #241F2B 描边
COLORS = {
    '.': None,
    'B': (232, 226, 204),   # #E8E2CC 骨主色
    'L': (255, 253, 242),   # #FFFDF2 骨亮部
    'S': (185, 176, 154),   # #B9B09A 骨阴影
    'D': (126, 118, 99),    # #7E7663 骨深
    'k': (27, 24, 34),      # #1B1822 眼窝
    'f': (111, 211, 255),   # #6FD3FF 鬼火
    'F': (234, 251, 255),   # #EAFBFF 焰心
    'd': (42, 134, 200),    # #2A86C8 鬼火暗部
    '#': OUTLINE,           # 描边
    'w': (255, 255, 255),   # 闪白（受击）
    'x': (214, 214, 226),   # 闪白阴影
    'y': (150, 150, 170),   # 闪白深部
}

# 受击闪白映射（只替骨色，眼窝与描边保留）
WHITE_MAP = {'B': 'w', 'L': 'w', 'S': 'x', 'D': 'y', 'f': 'w', 'F': 'w', 'd': 'w'}
FLAME_LIT = {'f': 'f', 'F': 'F'}      # 旺
FLAME_MID = {'f': 'f', 'F': 'd'}      # 焰心转暗
FLAME_DIM = {'f': 'd', 'F': 'd'}      # 整体转暗
FLAME_OUT = {'f': 'k', 'F': 'k'}      # 熄灭

ORDER = ['leg_l', 'leg_r', 'body', 'arm_l', 'arm_r', 'head']


def classify(x, y):
    if y <= 13:
        return 'head'
    if y <= 25:
        if x <= 8:
            return 'arm_l'
        if x >= 19:
            return 'arm_r'
        return 'body'
    return 'leg_l' if x <= 13 else 'leg_r'


BASE_PIX = {k: [] for k in ORDER}
for _y, _row in enumerate(BASE):
    for _x, _c in enumerate(_row):
        if _c != '.':
            BASE_PIX[classify(_x, _y)].append(((_x, _y), _c))


# ---------------------------------------------------------------- 绘制
def line_points(p0, p1):
    """Bresenham 直线，返回整数点列表。"""
    x0, y0 = p0
    x1, y1 = p1
    pts = []
    dx, dy = abs(x1 - x0), -abs(y1 - y0)
    sx = 1 if x0 < x1 else -1
    sy = 1 if y0 < y1 else -1
    err = dx + dy
    while True:
        pts.append((x0, y0))
        if x0 == x1 and y0 == y1:
            break
        e2 = 2 * err
        if e2 >= dy:
            err += dy
            x0 += sx
        if e2 <= dx:
            err += dx
            y0 += sy
    return pts


def make_arm(hand):
    """程序化生成一条骨臂：肩(20,16) -> hand，2px 粗，末端 3 指爪。"""
    px = []
    for (x, y) in line_points((20, 16), hand):
        px.append((x, y, 'B'))
        px.append((x + 1, y, 'B'))
    hx, hy = hand
    for x in (hx - 1, hx, hx + 1):
        px.append((x, hy, 'B'))
        px.append((x, hy + 1, 'B'))
    px.append((hx - 1, hy + 2, 'L'))
    px.append((hx + 1, hy + 2, 'L'))
    return px


def add_outline(canvas):
    """对紧邻不透明像素的透明格补 1px 描边（8 邻域）。"""
    add = []
    for y in range(-1, H + 1):
        for x in range(-1, W + 1):
            if (x, y) in canvas:
                continue
            hit = False
            for dy in (-1, 0, 1):
                for dx in (-1, 0, 1):
                    if dx == 0 and dy == 0:
                        continue
                    if (x + dx, y + dy) in canvas:
                        hit = True
                        break
                if hit:
                    break
            if hit:
                add.append((x, y))
    for p in add:
        canvas[p] = '#'


def render(off, override=None, flame=None, white=False, outline=True):
    """合成一帧。off: {层名: (dx, dy)}；override: {层名: [(x,y,char)]}（绝对内容坐标）。"""
    canvas = {}
    override = override or {}
    for name in ORDER:
        dx, dy = off.get(name, (0, 0))
        if name in override:
            for (x, y, c) in override[name]:
                canvas[(x + dx, y + dy)] = c
        else:
            for (x, y), c in BASE_PIX[name]:
                canvas[(x + dx, y + dy)] = c
    if flame:
        for p, c in list(canvas.items()):
            if c in flame:
                canvas[p] = flame[c]
    if white:
        for p, c in list(canvas.items()):
            if c in WHITE_MAP:
                canvas[p] = WHITE_MAP[c]
    if outline:
        add_outline(canvas)

    img = Image.new('RGBA', (FW, FH), (0, 0, 0, 0))
    px = img.load()
    for (x, y), c in canvas.items():
        col = COLORS.get(c)
        if col is None:
            continue
        tx, ty = x + PAD, y + PAD
        if 0 <= tx < FW and 0 <= ty < FH:
            px[tx, ty] = col + (255,)
    return img


def zero_off():
    return {k: (0, 0) for k in ORDER}


# ---------------------------------------------------------------- 动画定义
def build_idle():
    """24 帧 / 2.0s：呼吸下沉，头滞后 1 帧，鬼火 8 帧一循环明灭。"""
    dy = [0] * 6 + [1] * 4 + [2] * 6 + [1] * 4 + [0] * 4
    assert len(dy) == 24
    flame_seq = [FLAME_LIT, FLAME_MID, FLAME_DIM, FLAME_MID,
                 FLAME_LIT, FLAME_MID, FLAME_DIM, FLAME_MID]
    frames = []
    for i in range(24):
        off = zero_off()
        off['body'] = (0, dy[i])
        off['arm_l'] = (0, dy[i])
        off['arm_r'] = (0, dy[i])
        # 头滞后 1 帧：取 max(当前, 前一帧)，保证下沉不比躯干少 -> 脖子不撕缝
        off['head'] = (0, max(dy[i], dy[i - 1]))
        frames.append(render(off, flame=flame_seq[i % 8]))
    return frames, {'body_vertical_offset_px': dy, 'head_lag_frames': 1,
                    'flame_period_frames': 8, 'legs': 'static',
                    'horizontal_offset_px': 0}


def build_walk():
    """8 帧 / 0.667s：双腿反相前后摆，抬脚 1px，躯干两步一 bob，手臂反相。"""
    n = 8
    leg_l_dx = [int(round(2 * math.sin(2 * math.pi * i / n))) for i in range(n)]
    leg_r_dx = [-v for v in leg_l_dx]
    leg_l_dy = [-1 if abs(v) <= 1 else 0 for v in leg_l_dx]
    leg_r_dy = [-1 if abs(v) <= 1 else 0 for v in leg_r_dx]
    bob = [1, 0, 0, 1, 1, 0, 0, 1]
    frames = []
    for i in range(n):
        off = zero_off()
        off['leg_l'] = (leg_l_dx[i], leg_l_dy[i])
        off['leg_r'] = (leg_r_dx[i], leg_r_dy[i])
        off['body'] = (0, bob[i])
        off['head'] = (0, max(bob[i], bob[i - 1]))
        off['arm_l'] = (0, bob[i] - leg_l_dy[i])
        off['arm_r'] = (0, bob[i] - leg_r_dy[i])
        frames.append(render(off, flame=FLAME_LIT))
    return frames, {'leg_dx': [leg_l_dx, leg_r_dx], 'leg_lift_px': [leg_l_dy, leg_r_dy],
                    'body_bob_px': bob, 'arm_counter_swing': True}


def build_attack():
    """6 帧 / 0.5s：右臂抬起 -> 后拉 -> 挥出 -> 收回；躯干配合前倾下沉。"""
    hands = [(21, 24), (22, 20), (23, 17), (24, 23), (22, 25), (21, 24)]
    body_dy = [0, -1, -1, 1, 1, 0]
    head_dy = [0, -1, -2, 1, 1, 0]
    frames = []
    for i in range(6):
        off = zero_off()
        off['body'] = (0, body_dy[i])
        off['arm_l'] = (0, body_dy[i])
        off['head'] = (0, head_dy[i])
        frames.append(render(off, override={'arm_r': make_arm(hands[i])}, flame=FLAME_LIT))
    return frames, {'hand_positions': hands, 'body_dy': body_dy, 'head_dy': head_dy,
                    'note': 'arm_r 为程序化绘制（Bresenham 2px 粗 + 3 指爪）'}


def build_hurt():
    """4 帧 / 0.333s：后退 1~2px + 前 2 帧闪白。"""
    dx = [-1, -2, -1, 0]
    frames = []
    for i in range(4):
        off = {k: (dx[i], 0) for k in ORDER}
        frames.append(render(off, flame=FLAME_LIT, white=(i < 2)))
    return frames, {'knockback_px': dx, 'flash_white_frames': [0, 1]}


def build_death():
    """10 帧 / 0.833s：弹起 -> 散架 -> 鬼火熄灭（第 3 帧起）。"""
    head_dx = [0, 0, 1, 1, 0, -1, -1, 0, 1, 1]
    head_dy = [0, -1, -2, -2, -1, 0, 1, 2, 4, 6]
    body_dx = [0, 0, 0, -1, -1, -1, -1, 0, 0, 0]
    body_dy = [0, 0, 1, 2, 3, 4, 5, 6, 7, 7]
    leg_dx = [0, 0, -1, -1, -2, -2, -3, -3, -3, -3]
    leg_dy = [0, 0, 0, 0, 0, 1, 1, 2, 2, 2]
    frames = []
    for i in range(10):
        off = zero_off()
        off['head'] = (head_dx[i], head_dy[i])
        off['body'] = (body_dx[i], body_dy[i])
        off['arm_l'] = (body_dx[i] - 1, body_dy[i] + 1)
        off['arm_r'] = (body_dx[i] + 1, body_dy[i] + 1)
        off['leg_l'] = (leg_dx[i], leg_dy[i])
        off['leg_r'] = (-leg_dx[i], leg_dy[i])
        frames.append(render(off, flame=FLAME_LIT if i < 3 else FLAME_OUT))
    return frames, {'head': [head_dx, head_dy], 'body': [body_dx, body_dy],
                    'legs_spread_px': leg_dx, 'flame_out_at_frame': 3}


ANIMS = [
    ('idle', build_idle),
    ('walk', build_walk),
    ('attack', build_attack),
    ('hurt', build_hurt),
    ('death', build_death),
]


# ---------------------------------------------------------------- 输出
def write_sheet(frames, path):
    sheet = Image.new('RGBA', (FW * len(frames), FH), (0, 0, 0, 0))
    for i, f in enumerate(frames):
        sheet.paste(f, (i * FW, 0))
    sheet.save(path)


def write_gif(frames, path, scale=6):
    """显式调色板，0 号索引留品红当透明色（PIL 会把 0 号当透明挖掉）。"""
    up = [f.resize((FW * scale, FH * scale), Image.NEAREST) for f in frames]
    used = sorted({c for f in up for c in f.getdata() if c[3]})
    reserved = (255, 0, 255, 255)
    assert reserved not in used
    lut = {c: i + 1 for i, c in enumerate(used)}
    pals = [v for i in range(len(used) + 1)
            for v in ((reserved if i == 0 else used[i - 1])[:3])]
    pals += [0] * (768 - len(pals))
    out = []
    for f in up:
        pm = f.convert('P')
        pm.putpalette(pals)
        pm.info['transparency'] = 0
        pl = pm.load()
        for y in range(f.height):
            for x in range(f.width):
                c = f.getpixel((x, y))
                pl[x, y] = lut[c] if c[3] else 0
        out.append(pm)
    out[0].save(path, save_all=True, append_images=out[1:],
                duration=int(1000 / FPS), loop=0, disposal=2,
                transparency=0, optimize=False)


def write_preview(meta, path):
    """自包含交互预览页：动画切换 / 播放暂停 / 逐帧 / 背景切换 / 倍率。"""
    import html as _html
    anims_js = json.dumps(
        [{'name': k, 'sheet': v['file'], 'frames': v['frames'],
          'dur': round(v['frames'] / FPS * 1000)} for k, v in meta['anims'].items()],
        ensure_ascii=False)
    doc = """<!DOCTYPE html>
<html lang="zh-CN"><head><meta charset="utf-8">
<title>骷髅小怪 - 动画预览</title>
<style>
 body{margin:0;font:14px/1.6 system-ui,sans-serif;background:#1d1a24;color:#e8e2cc}
 header{padding:12px 20px;border-bottom:1px solid #3a3346}
 header h1{margin:0;font-size:17px} header p{margin:2px 0 0;color:#9a9284;font-size:12px}
 #stage{display:flex;flex-direction:column;align-items:center;gap:14px;padding:26px}
 #view{image-rendering:pixelated;background-size:18px 18px;
   background-image:linear-gradient(45deg,#2a2634 25%,transparent 25%),
   linear-gradient(-45deg,#2a2634 25%,transparent 25%),
   linear-gradient(45deg,transparent 75%,#2a2634 75%),
   linear-gradient(-45deg,transparent 75%,#2a2634 75%);
   background-position:0 0,0 9px,9px -9px,-9px 0}
 #view.bg-dark{background:#141218;background-image:none}
 #view.bg-light{background:#d8d2c0;background-image:none}
 #view{width:calc(var(--s)*32px);height:calc(var(--s)*36px);
   background-repeat:no-repeat;background-position:center center}
 .row{display:flex;gap:8px;flex-wrap:wrap;justify-content:center}
 button{background:#2a2634;color:#e8e2cc;border:1px solid #4a4356;border-radius:6px;
   padding:5px 14px;cursor:pointer;font-size:13px}
 button.on{background:#6fd3ff;color:#141218;border-color:#6fd3ff;font-weight:600}
 #info{color:#9a9284;font-size:12px}
</style></head><body>
<header><h1>骷髅小怪 (skeleton_enemy)</h1>
<p>28x32 原生 / 32x36 帧 / 12fps / pivot 脚底中心 / PPU 15 建议</p></header>
<div id="stage">
 <div id="view" class="bg-checker"></div>
 <div class="row" id="anims"></div>
 <div class="row">
  <button id="play">暂停</button>
  <button id="prev">上一帧</button><button id="next">下一帧</button>
  <button id="b1">深底</button><button id="b2">棋盘</button><button id="b3">浅底</button>
 </div>
 <div class="row"><span id="info"></span></div>
</div>
<script>
const ANIMS = __ANIMS__;
let cur=0, frame=0, playing=true, timer=null;
const view=document.getElementById('view'), info=document.getElementById('info');
function cssScale(){ return matchMedia('(max-width:640px)').matches?4:8; }
function setup(a){
  const s=cssScale();
  view.style.setProperty('--s',s);
  view.style.backgroundImage='url('+encodeURIComponent(a.sheet)+')';
  view.style.backgroundSize=(a.frames*32*s)+'px '+(36*s)+'px';
  view.style.backgroundRepeat='no-repeat'; view.style.backgroundPosition='0 0';
  document.querySelectorAll('#anims button').forEach((b,i)=>b.classList.toggle('on',i===cur));
  draw();
}
function draw(){
  const a=ANIMS[cur], s=cssScale();
  view.style.backgroundPosition=(-frame*32*s)+'px 0';
  info.textContent=a.name+'  第 '+String(frame).padStart(2,'0')+' / '+a.frames+' 帧  ('+a.dur+'ms/循环)';
}
function tick(){
  if(!playing) return;
  frame=(frame+1)%ANIMS[cur].frames; draw();
}
ANIMS.forEach((a,i)=>{
  const b=document.createElement('button'); b.textContent=a.name;
  b.onclick=()=>{cur=i;frame=0;setup(a)}; document.getElementById('anims').appendChild(b);
});
document.getElementById('play').onclick=e=>{playing=!playing;e.target.textContent=playing?'暂停':'播放'};
document.getElementById('prev').onclick=()=>{playing=false;document.getElementById('play').textContent='播放';
  frame=(frame-1+ANIMS[cur].frames)%ANIMS[cur].frames;draw()};
document.getElementById('next').onclick=()=>{playing=false;document.getElementById('play').textContent='播放';
  frame=(frame+1)%ANIMS[cur].frames;draw()};
document.getElementById('b1').onclick=()=>view.className='bg-dark';
document.getElementById('b2').onclick=()=>view.className='bg-checker';
document.getElementById('b3').onclick=()=>view.className='bg-light';
setup(ANIMS[0]); timer=setInterval(tick, Math.round(1000/12));
</script></body></html>"""
    doc = doc.replace('__ANIMS__', _html.escape(anims_js, quote=False))
    with open(path, 'w', encoding='utf-8') as fh:
        fh.write(doc)


def main():
    all_meta = {}
    counts = {}
    for name, builder in ANIMS:
        frames, info = builder()
        d = os.path.join(HERE, name)
        fd = os.path.join(d, 'frames')
        os.makedirs(fd, exist_ok=True)
        for i, f in enumerate(frames):
            f.save(os.path.join(fd, 'skeleton_%s_%02d.png' % (name, i)))
        write_sheet(frames, os.path.join(d, 'skeleton_%s_sheet.png' % name))
        write_gif(frames, os.path.join(d, 'skeleton_%s.gif' % name))
        all_meta[name] = {
            'file': 'skeleton_%s_sheet.png' % name,
            'frames': len(frames),
            'fps': FPS,
            'loop_duration_sec': round(len(frames) / FPS, 3),
            'motion': info,
        }
        counts[name] = len(frames)
        print('%-7s %2d 帧' % (name, len(frames)))

    # 基础帧（idle 第 0 帧，无位移）
    base = render(zero_off(), flame=FLAME_LIT)
    base.save(os.path.join(HERE, 'skeleton_base.png'))
    base.resize((FW * SCALE, FH * SCALE), Image.NEAREST).save(
        os.path.join(HERE, 'skeleton_base_x8.png'))

    # 调色板
    order = ['#', 'L', 'B', 'S', 'D', 'k', 'F', 'f', 'd']
    pal = Image.new('RGBA', (len(order) * 24, 24), (0, 0, 0, 0))
    for i, c in enumerate(order):
        for y in range(24):
            for x in range(24):
                pal.putpixel((i * 24 + x, y), COLORS[c] + (255,))
    pal.resize((len(order) * 24 * 3, 72), Image.NEAREST).save(
        os.path.join(HERE, 'skeleton_palette.png'))

    meta = {
        'name': 'skeleton_enemy',
        'display_name': '骷髅小怪 (Skeleton)',
        'type': 'enemy',
        'authoring': 'original pixel art (procedural, not upscaled from reference)',
        'native_size': [W, H],
        'frame_size': [FW, FH],
        'padding': PAD,
        'fps': FPS,
        'pivot': {'x': FW / 2.0, 'y': FH - PAD,
                  'note': '脚底中心，向上为 -y；内容底边在帧内 y=%d' % (PAD + H)},
        'palette_count': len(COLORS) - 1,
        'palette': {k: list(v) for k, v in COLORS.items() if v is not None},
        'layers': {k: len(v) for k, v in BASE_PIX.items()},
        'anims': all_meta,
        'unity_recommended': {
            'sprite_mode': 'Multiple (按 %dx%d 网格切)' % (FW, FH),
            'pixels_per_unit': 15,
            'filter_mode': 'Point (no filter)',
            'compression': 'None',
            'pivot': 'Bottom Center',
            'note': 'PPU 15 与 ksm 一致 -> 世界高约 2.13 单位（ksm 为 2.4）',
        },
    }
    with open(os.path.join(HERE, 'skeleton_enemy.json'), 'w', encoding='utf-8') as fh:
        json.dump(meta, fh, ensure_ascii=False, indent=2)
    write_preview(meta, os.path.join(HERE, 'preview.html'))
    print('基础帧/调色板/meta/预览页 已写出，总帧数', sum(counts.values()))


if __name__ == '__main__':
    main()
