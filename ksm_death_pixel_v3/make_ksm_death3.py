#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
ksm 死亡动画 v2 生产脚本
========================
角色本体: 官方原生像素画 D:/bang/nfc/ksm/12.png (640x640 -> 原生 64x64, 块宽 10px, 21 色)
动作编排: ksm_death/frame_020-101.png (82 帧 AI 序列, 只取动作节奏, 不取其造型/配色)

与 v1 (ksm_death_pixel) 的区别:
  v1 = 把 AI 帧重新像素化 (配色映射官方 21 色, 但造型/比例跟 AI 图走)
  v2 = 官方角色的像素块重组: 头部块逐像素复用官方, 其余姿势以官方 21 色排布

分镜 (16 帧 @ 12fps, 一次性不循环, 约 1.33s):
  01        站立 (官方帧逐像素复用)
  02-04     下沉前倾 (块平移: 腿不动, 上身+头下移, 头前倾)
  05-07     蜷缩跪倒 (块平移 + 膝盖/鞋外翻手绘点; 07 头开始后甩)
  08        栽倒过渡A (空中斜向, 臀部将触地, 手绘)
  09        栽倒过渡B (撞击: 裙底触地 y63, 头仍差 1-2px, 脚仍翘, 手绘)
  10        躺平基准 (头着地, 脚放平, 吉他横压身上, 手绘)
  11-16     躺平 + 魂魄幽灵向左上飘并散成数颗 (身体完全静止)

规格: 原生 64x64 | cell 76x76 (pad 6, 与 ksm_hurt_pixel 一致) | 枢轴 Bottom Center
"""
import os, sys, json, collections
import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

BASE = os.path.dirname(os.path.abspath(__file__))
OFFICIAL = 'D:/bang/nfc/ksm/12.png'
NATIVE = 64
PAD = 6
CELL = NATIVE + PAD * 2          # 76
FPS = 12
N_OUT = 16
NAME = 'ksm_death3'

# 官方 21 色 (按 12.png 像素频次; 字母仅用于脚本内栅格书写)
C = {
    'A': (170, 110, 102),   # 头发主色
    'B': (242, 210, 207),   # 浅肤色
    'C': (118, 68, 68),     # 头发暗部
    'D': (189, 15, 26),     # 红 (裙/吉他/蝴蝶结)
    'E': (255, 255, 255),   # 白
    'F': (210, 164, 148),   # 中间肤色 (手臂)
    'G': (52, 56, 76),      # 深蓝过膝袜
    'H': (151, 114, 100),   # 暗肤色描边
    'I': (0, 0, 0),         # 黑 (睫毛/琴弦)
    'J': (216, 155, 151),   # 腮红浅
    'K': (222, 87, 91),     # 腮红深
    'L': (109, 67, 57),     # 琴颈棕
    'M': (145, 87, 189),    # 紫瞳
    'N': (213, 156, 227),   # 浅紫星光
    'O': (194, 203, 208),   # 灰蓝 (琴身细节)
    'P': (255, 230, 74),    # 黄 (弦钮)
    'Q': (57, 27, 22),      # 鞋深棕
    'R': (71, 39, 118),     # 暗紫瞳
    'S': (193, 74, 255),    # 品红 (弦钮)
    'T': (170, 51, 54),     # 暗红 (蝴蝶结/裙缘)
    'U': (74, 178, 255),    # 蓝 (弦钮)
}
REV = {v: k for k, v in C.items()}
OOB = []          # 越界落点记录 (QC 要求为空)


def load_official():
    im = Image.open(OFFICIAL).convert('RGBA')
    nat = np.asarray(im.resize((NATIVE, NATIVE), Image.NEAREST))
    cnt = collections.Counter()
    al = nat[:, :, 3] > 127
    for p in nat[al][:, :3].tolist():
        cnt[tuple(p)] += 1
    pal = [c for c, _ in cnt.most_common()]
    return nat, pal


# ---------- 画布/像素工具 ----------
def new_frame():
    return np.zeros((NATIVE, NATIVE, 4), np.uint8)


def putc(f, x, y, col):
    if not (0 <= x < NATIVE and 0 <= y < NATIVE):
        OOB.append((x, y, col))
        return
    f[y, x, 0], f[y, x, 1], f[y, x, 2] = col
    f[y, x, 3] = 255


def px(f, x, y, ch):
    if ch in ('.', ' '):
        return
    putc(f, x, y, C[ch])


def paste(f, block, x, y, row_dx=None):
    """把官方像素块贴到 (x,y); row_dx(j) 给每行附加水平位移 (做轻微倾斜)."""
    h, w = block.shape[:2]
    for j in range(h):
        dx = row_dx(j) if row_dx else 0
        for i in range(w):
            if block[j, i, 3] > 127:
                putc(f, x + i + dx, y + j, tuple(int(v) for v in block[j, i, :3]))


def band(f, x0, y0, x1, y1, thick, ch):
    """水平行进的厚线: 沿 x0->x1, 每个 x 处竖排 thick 像素 (像素画斜线=阶梯)."""
    n = abs(x1 - x0) + 1
    sx = 1 if x1 >= x0 else -1
    for k in range(n):
        x = x0 + sx * k
        t = k / float(n - 1) if n > 1 else 0.0
        yc = int(round(y0 + (y1 - y0) * t))
        for d in range(thick):
            px(f, x, yc + d, ch)


def blob(f, rows):
    """rows: [(y, x, "AB.C"), ...] 字符栅格, '.'=跳过."""
    for y, x, s in rows:
        for i, ch in enumerate(s):
            px(f, x + i, y, ch)


def closed_eyes(f, hx, hy):
    """把贴好的头部块改成闭眼: 眼区(块行12-15/列7-19)填肤色, 再画两道 I 睫线."""
    for j in range(12, 16):
        for i in range(7, 20):
            if f[hy + j, hx + i, 3] > 127:
                px(f, hx + i, hy + j, 'B')
    for i in (9, 10, 11, 17, 18, 19):        # 块坐标 x28-30 与 x36-38
        px(f, hx + i, hy + 13, 'I')

# ---------- 官方块提取 ----------
def extract_blocks(nat):
    HEAD  = nat[23:42, 19:43].copy()   # 头发+脸: y23-41, x19-42 (19 行 x 24 列)
    UPPER = nat[42:57, 19:48].copy()    # 上身+吉他+手臂: y42-56, x19-47
    LEGS  = nat[57:64, 26:37].copy()    # 大腿+袜+鞋: y57-63, x26-36
    return HEAD, UPPER, LEGS


# ---------- 帧 02-05: 官方块整体平移 (腿不动, 上身下沉, 头持续往右下低) ----------
# 鬓角碎发桥: 官方 (24,42) 的 1px 发色在上身块里, 头块右移 2 格后会悬空,
# 在 (24, 41+upper_dy) 补 1px C 连回


def frame_shift(B, upper_dy, head_dx, head_dy, flares=(), splays=()):
    HEAD, UPPER, LEGS = B
    f = new_frame()
    paste(f, LEGS, 26, 57)
    paste(f, UPPER, 19, 42 + upper_dy)
    paste(f, HEAD, 19 + head_dx, 23 + head_dy)
    for (x, y, ch) in flares:
        px(f, x, y, ch)
    for (x, y, ch) in splays:
        px(f, x, y, ch)
    return f


def build_frames(nat):
    B = extract_blocks(nat)
    frames = []
    # 01 官方帧原样 (RGB 逐像素; alpha 二值化) —— AI f020
    f = new_frame()
    paste(f, nat, 0, 0)
    frames.append(f)
    # 02-05 渐进蜷缩 (AI f020-042): 头持续往右下低, 上身下沉, 腿不动, 无外翻
    frames.append(frame_shift(B, 1, +1, +1))
    frames.append(frame_shift(B, 2, +1, +2))
    frames.append(frame_shift(B, 2, +2, +3, flares=[(24, 43, 'C')]))
    frames.append(frame_shift(B, 3, +2, +5, flares=[(24, 44, 'C')]))
    # 06-07 伏倒定格 (AI f044-058 稳定 15 帧): 跪地伏身, 头深低, 长发披落, 吉他横抱
    frames.append(frame_prostrate(B))
    frames.append(frame_prostrate(B))
    # 08 翻身弓身 (AI f059: 贴地翻滚的唯一中间帧, 臀先着地, 脚仍弓着)
    frames.append(frame_roll(B))
    # 09-10 仰面躺平 (AI f060-068: 头左脚右, 全身平贴地)
    frames.append(frame_lie(B))
    frames.append(frame_lie(B))
    # 11-16 魂魄幽灵从身体处向左上飘 (AI f069-101)
    frames.extend(star_frames(frames[9]))
    return frames

# ---------- 帧 06-10: 手绘姿势 (头部块逐像素复用官方+闭眼, 其余以官方 21 色排布) ----------
def frame_prostrate(B):
    """伏倒定格 (AI f044-058): 正面跪伏头深低 —— 跪地, 上身下压, 头深低伏,
    官方头顶弧再贴一层表示披落的头发, 吉他随上身横抱身前."""
    HEAD, UPPER, LEGS = B
    f = new_frame()
    # 腿: 跪地原位 (小腿贴地, 与站立同位 —— AI 蜷缩/伏倒期脚基本不动)
    paste(f, LEGS, 26, 57)
    # 上身: 下沉 4 格压向腿 (y46-60), 吉他横抱身前
    paste(f, UPPER, 19, 46)
    # 头: 深低伏 (下移 11 格 + 右移 2 格 -> y34-53), 盖住上身顶部, 闭眼
    # 不做"披落头发层": 复制官方头顶弧会形成双头顶(已实测), 正面视角下头发披落
    # 无法用现有块表达, 按 Q 版简化只保留"头深低"
    paste(f, HEAD, 21, 34)
    closed_eyes(f, 21, 34)
    return f


def frame_roll(B):
    HEAD, UPPER, LEGS = B
    f = new_frame()
    # 腿: 大腿接裙, 袜斜向右上, 鞋仍翘起
    band(f, 50, 59, 52, 58, 2, 'B')
    band(f, 52, 58, 57, 54, 3, 'G')
    blob(f, [
        (52, 57, "QQ"),
        (53, 56, "QQQQ"),
        (54, 56, "QQQ"),
        (55, 56, "LLLL"),
    ])
    # 领口+衬衫带 (近水平: 肩(28,57)->髋(41,60)) + 蝴蝶结 (先画, 让裙盖住衬衫尾)
    blob(f, [(56, 26, "EEEE"), (57, 26, "EEE"), (58, 26, "EE")])
    band(f, 28, 57, 41, 60, 4, 'E')
    px(f, 30, 58, 'T'); px(f, 31, 58, 'K')
    px(f, 30, 59, 'D'); px(f, 31, 59, 'D')
    # 裙 (撞击帧: 底行 y63 触地, 臀部先落地; 盖住衬衫带尾部)
    blob(f, [
        (56, 41, "DDDDDDD"),
        (57, 39, "DDDDDDDDDDD"),
        (58, 38, "TDDDDDDDDDDDDT"),
        (59, 38, "TDDEEDDDDDDDDT"),
        (60, 38, "TDDDDDDDDDDDDT"),
        (61, 38, "TDDDDDDDDDDDDT"),
        (62, 39, ".TDDDDDDDDDDT."),
        (63, 40, "..TTTTTTT"),
    ])
    # 头: 闭眼, 底部 y61 距地 2px (尚未拍地)
    paste(f, HEAD, 3, 43)
    closed_eyes(f, 3, 43)
    # 脖子: 下颌(x24) -> 肩/领口(x29), 实心带 + 尾部与躯干/裙的补缝
    blob(f, [
        (56, 25, "FFF"),       # 补 y56 头与裙之间的 3px 缝
        (57, 24, "FFFF"),
        (58, 24, "FFFFF"),
        (59, 24, "FFFFF"),
        (60, 24, "FFFFF"),
        (61, 24, "HHHHH"),
        (61, 29, "FF"),        # 补 y61 脖尾 -> 躯干带(x31)
        (62, 29, "FFFFFF"),    # 补 y62 脖尾 -> 躯干带(x35)
        (63, 31, "FFFFFFFF"),  # 补 y63 触地行 -> 裙(x39)
        (59, 33, "EEEEE"),     # 补 y59 胸口与裙(x38)之间的腰缝
        (60, 37, "E"),         # 补 y60 腰缝 1px
    ])
    # 吉他: 琴身横贴胸口, 琴颈向右, 琴头+弦钮在脚前
    blob(f, [
        (49, 31, "DDDDDDDD"),
        (50, 29, "DDOEODODDD"),
        (51, 28, "DDEEDDDDDD"),
        (52, 27, "EIIDDDDDDD"),
        (53, 28, "DDDDDDDDD"),
        (54, 29, "DDDDDDD"),
        (55, 30, "DDDDD"),
        (56, 31, "DDDD"),
        (57, 31, "DDDD"),
    ])
    band(f, 40, 56, 53, 58, 2, 'L')
    blob(f, [
        (57, 54, "DDDD"),
        (58, 53, "DDDDDD"),
        (59, 53, "DPDDDD"),
        (60, 53, "DDSDDD"),
        (61, 54, "DUDD"),
    ])
    px(f, 58, 57, 'P'); px(f, 58, 59, 'S'); px(f, 58, 61, 'U')
    # 手臂+手 (画在吉他之上; 水平实线链, 起点接衬衫带终点接手)
    for (x, y) in ((34, 58), (35, 58), (36, 58), (37, 58), (38, 58), (39, 58)):
        px(f, x, y, 'F')
    blob(f, [(57, 40, "BB"), (58, 40, "BB")])
    return f


def frame_lie(B):
    HEAD, UPPER, LEGS = B
    f = new_frame()
    # 腿: 放平, 鞋底着地 (y63)
    band(f, 50, 60, 52, 60, 2, 'B')
    band(f, 52, 60, 58, 60, 3, 'G')
    blob(f, [
        (59, 58, "QQQ"),
        (60, 57, "QQQQ"),
        (61, 57, "QQQQ"),
        (62, 57, "QQQ"),
        (63, 58, "LLL"),
    ])
    # 领口+衬衫带+蝴蝶结 (先画, 让裙盖住衬衫尾)
    blob(f, [(58, 26, "EEEE"), (59, 26, "EEE"), (60, 26, "EE")])
    band(f, 28, 58, 38, 61, 3, 'E')
    px(f, 30, 59, 'T'); px(f, 31, 59, 'K')
    px(f, 30, 60, 'D'); px(f, 31, 60, 'D')
    # 裙 (与帧 09 同位: 已落地不再动; 盖住衬衫带尾部)
    blob(f, [
        (56, 41, "DDDDDDD"),
        (57, 39, "DDDDDDDDDDD"),
        (58, 38, "TDDDDDDDDDDDDT"),
        (59, 38, "TDDEEDDDDDDDDT"),
        (60, 38, "TDDDDDDDDDDDDT"),
        (61, 38, "TDDDDDDDDDDDDT"),
        (62, 39, ".TDDDDDDDDDDT."),
        (63, 40, "..TTTTTTT"),
    ])
    # 头: 闭眼, 后脑着地 (底行 y63)
    paste(f, HEAD, 2, 45)
    closed_eyes(f, 2, 45)
    # 脖子: 下颌(x24) -> 肩/领口(x29), 实心带 + 尾部与躯干/裙的补缝
    blob(f, [
        (56, 25, "FFF"),       # 补 y56 头-裙缝
        (57, 25, "FFFF"),      # 补 y57 头-裙缝
        (58, 24, "FFF"),
        (59, 24, "FFFFF"),
        (60, 24, "FFFFFF"),
        (61, 24, "FFFFFF"),
        (62, 24, "FFFFFFFFF"),  # 尾部延长到 x32 接躯干带(x33)
        (63, 24, "FFFFFF"),     # 触地行; 发丝稍后覆盖 x28-30
        (58, 30, "E"),          # 补 x30 接琴身(x31)
        (59, 33, "EEEEE"),     # 补 y59 腰缝
        (60, 37, "E"),          # 补 y60 腰缝 1px
        (62, 39, "E"),          # 补 y62 躯干-裙 1px
        (63, 31, "FFFFFF"),     # 补 y63 触地行 -> 躯干带(x37)
        (63, 39, "EEE"),        # 补 y63 -> 裙底(x42)
    ])
    # 散落发丝: 从脖子右下角沿地面向右散 (画在脖子之上, 盖住其右下 2px)
    for (x, y, ch) in ((27, 62, 'A'), (28, 62, 'C'),
                       (28, 63, 'A'), (29, 63, 'A'), (30, 63, 'A')):
        px(f, x, y, ch)
    # 吉他: 琴身横在胸口, 琴颈沿裙指向脚, 琴头+弦钮探在鞋前
    blob(f, [
        (52, 31, "DDDDDDDD"),
        (53, 29, "DDOEODODDD"),
        (54, 28, "DDEEDDDDDD"),
        (55, 27, "EIIDDDDDDD"),
        (56, 28, "DDDDDDDDD"),
        (57, 29, "DDDDDDD"),
        (58, 31, "DDDD"),
    ])
    band(f, 39, 57, 50, 58, 2, 'L')
    blob(f, [
        (58, 51, "DDDD"),
        (59, 50, "DPDDDD"),
        (60, 50, "DDSDDD"),
        (61, 51, "DUDD"),
        (62, 52, "DDD"),
    ])
    px(f, 55, 58, 'P'); px(f, 56, 60, 'S'); px(f, 55, 61, 'U')
    # 手臂搭在琴身上 + 手按琴颈 (画在吉他之上)
    for (x, y) in ((30, 57), (31, 57), (32, 57), (33, 57), (34, 58),
                   (35, 58), (36, 58), (37, 58), (38, 58)):
        px(f, x, y, 'F')
    blob(f, [(57, 39, "BB"), (58, 39, "BB")])
    return f

# ---------- 帧 11-16: 躺平不动 + 魂魄从身体处向左上飘 (AI f069-101) ----------
# AI 实测: 魂魄质心 (448,596) -> (216,272), 即原生 (40,53) -> (19,24), 左上方;
# 数量/面积由 1564 增到 4917 -> 魂群由一颗散成数颗
#
# ⚠️ 魂魄造型按 AI 实际像素还原(不是十字星): AI 的魂是「浅灰填充 + 深色描边 + 两只眼睛」的
#    小幽灵团 (实测 f082 放大图: 圆角菱形, 中间两个深色像素当眼睛)。
#    描边色用官方已有的 I(纯黑) + 填充用 O(灰蓝 194,203,208) —— 都取自 12.png 的调色板。
#    AI 魂块实测原生尺寸约 7x7 px (源 70x38px / 块宽10), 大魂 7x7, 小魂 5x5。
SOUL_BIG = [
    "..III..",
    ".IOOOI.",
    "IOOOOI.",
    "IOIOOI.",
    "IOOOOI.",
    ".IOOOI.",
    "..III..",
]
SOUL_SMALL = [
    "..II..",
    ".IOOI.",
    "IOIOI",
    "IOOOI",
    ".IOOI.",
    "..II..",
]


def add_soul(f, x, y, shape):
    blob(f, [(y + j, x, s) for j, s in enumerate(shape)])


def star_frames(base):
    # 魂魄轨迹按 AI 实测逐帧定位 (原生坐标): 由身体上方(40,53) 向左上散到 (3,2) 附近,
    # 数量由 1 颗增到 3~4 颗 —— 与 AI f069-101 的"一颗散成数颗"一致。
    spec = [
        # 帧 11: 1 大魂刚从身体升起 + 1 小魂
        [('BIG', 36, 47), ('SMALL', 44, 52)],
        # 帧 12: 1 大 + 1 小 + 1 点
        [('BIG', 32, 42), ('SMALL', 40, 48), ('dot', 37, 53)],
        # 帧 13: 1 大 + 1 小 + 2 点
        [('BIG', 28, 37), ('SMALL', 36, 43), ('dot', 33, 48), ('dot', 42, 51)],
        # 帧 14: 2 魂 (大+小) 上移
        [('BIG', 24, 32), ('SMALL', 33, 38), ('dot', 28, 43), ('dot', 39, 46)],
        # 帧 15: 2 小魂 + 2 点 (大魂继续上飘变淡)
        [('SMALL', 20, 27), ('SMALL', 29, 33), ('dot', 24, 37), ('dot', 35, 41)],
        # 帧 16: 魂群散到左上角, 最小最淡
        [('SMALL', 17, 23), ('dot', 21, 28), ('dot', 26, 33), ('wisp', 30, 38)],
    ]
    frames = []
    for items in spec:
        f = np.asarray(base).copy()
        for kind, x, y in items:
            if kind == 'BIG':
                add_soul(f, x, y, SOUL_BIG)
            elif kind == 'SMALL':
                add_soul(f, x, y, SOUL_SMALL)
            elif kind == 'dot':
                px(f, x, y, 'O')
            else:                       # wisp: 即将消散的余光 (官方眼里星光色 N), 十字火花形
                blob(f, [(y, x, 'N'), (y - 1, x, 'N'), (y + 1, x, 'N'),
                          (y, x - 1, 'N'), (y, x + 1, 'N')])
        frames.append(f)
    return frames


# ---------- QC ----------
def qc(frames, nat, pal):
    print('=== QC ===')
    ok = True
    palset = set(pal)
    a0 = np.asarray(frames[0])
    al0 = a0[:, :, 3] > 127
    nal = nat[:, :, 3] > 127
    same = (al0 == nal).all() and (a0[:, :, :3][al0] == nat[:, :, :3][nal]).all()
    print('frame01 == 官方帧(不透明区 RGB 全等, alpha 二值化): %s' % bool(same))
    ok &= bool(same)
    for i, fr in enumerate(frames):
        a = np.asarray(fr)
        al = a[:, :, 3] > 127
        cols = set(map(tuple, a[al][:, :3].tolist()))
        bad = cols - palset
        if bad:
            print('frame%02d 非法色: %s' % (i + 1, bad)); ok = False
        av = set(np.unique(a[:, :, 3]).tolist())
        if not av <= {0, 255}:
            print('frame%02d alpha 非二值: %s' % (i + 1, av)); ok = False
        if not al[63].any():
            print('frame%02d 底行 y63 无触地内容' % (i + 1)); ok = False
        lab, n = ndimage.label(al)
        if i <= 9 and n != 1:
            sizes = sorted(ndimage.sum(al, lab, range(1, n + 1)).tolist(), reverse=True)
            print('frame%02d 连通域 %d 个: %s' % (i + 1, n, sizes)); ok = False
        if i >= 10 and n > 5:
            print('frame%02d 魂魄碎片过多 (%d 个连通域)' % (i + 1, n)); ok = False
        ys, xs = np.nonzero(al)
        print('frame%02d bbox x%d-%d y%d-%d px=%d comps=%d' %
              (i + 1, xs.min(), xs.max(), ys.min(), ys.max(), al.sum(), n))
    # 魂魄帧: 身体部分一字不动, 只有魂魄色 (I 描边 / O 填充 / N 余光) 可自由增减(魂会移动)
    base = np.asarray(frames[9])
    bb = base[:, :, 3] > 127
    soul_cols = {C['I'], C['O'], C['N']}
    for i in range(10, 16):
        d = np.asarray(frames[i])
        db = d[:, :, 3] > 127
        m_del = bb & ~db
        if m_del.any():
            delcols = set(map(tuple, base[m_del][:, :3].tolist()))
            bad = delcols - soul_cols
            if bad:
                print('frame%02d 身体像素消失了: %s' % (i + 1, bad)); ok = False
        m_new = db & ~bb
        if m_new.any():
            newcols = set(map(tuple, d[m_new][:, :3].tolist()))
            bad = newcols - soul_cols
            if bad:
                print('frame%02d 新增像素用了非魂魄色: %s' % (i + 1, bad)); ok = False
    if OOB:
        print('越界落点 %d 个 (示例 %s)' % (len(OOB), OOB[:5])); ok = False
    else:
        print('无越界落点')
    print('QC 结果: %s' % ('PASS' if ok else 'FAIL'))
    return ok


def dump_ascii(frames, path):
    lines = []
    for i, fr in enumerate(frames):
        a = np.asarray(fr)
        lines.append('=== frame %02d ===' % (i + 1))
        for y in range(NATIVE):
            row = []
            for x in range(NATIVE):
                if a[y, x, 3] > 127:
                    row.append(REV[(a[y, x, 0], a[y, x, 1], a[y, x, 2])])
                else:
                    row.append('.')
            lines.append('%2d %s' % (y, ''.join(row)))
    with open(path, 'w', encoding='utf-8') as fh:
        fh.write('\n'.join(lines))
    print('字符栅格 dump -> %s' % path)


# ---------- 输出 ----------
def to_pil(fr):
    return Image.fromarray(np.asarray(fr).astype(np.uint8), 'RGBA')


def make_cell(fr):
    im = Image.new('RGBA', (CELL, CELL), (0, 0, 0, 0))
    p = to_pil(fr)
    im.paste(p, (PAD, PAD), p)
    return im


def make_gif(cells, path, fps=FPS, scale=6):
    ims = [c.resize((CELL * scale, CELL * scale), Image.NEAREST) for c in cells]
    used = set()
    for im in ims:
        a = np.asarray(im)
        used.update(map(tuple, a[:, :, :3][a[:, :, 3] > 127].tolist()))
    RESERVED = (255, 0, 255)
    assert RESERVED not in used
    pal = [RESERVED] + sorted(used)
    index = {c: i for i, c in enumerate(pal)}
    assert len(pal) <= 256, 'GIF 调色板溢出 %d' % len(pal)
    palflat = [v for c in pal for v in c]
    out = []
    for im in ims:
        a = np.asarray(im)
        al = a[:, :, 3] > 127
        p = Image.new('P', im.size)
        p.putpalette(palflat)
        idx = np.zeros(im.width * im.height, np.uint8)
        flat = a[:, :, :3].reshape(-1, 3).astype(np.int32)
        keys = np.array(list(index.keys()), np.int32)
        kvals = np.array([index[tuple(int(v) for v in k)] for k in keys], np.uint8)
        sel = np.nonzero(al.reshape(-1))[0]
        step = 8192
        for s in range(0, len(sel), step):
            blk = flat[sel[s:s + step]]
            dd = ((blk[:, None, :] - keys[None, :, :]) ** 2).sum(axis=2)
            idx[sel[s:s + step]] = kvals[dd.argmin(axis=1)]
        p.putdata(idx.tolist())
        out.append(p)
    dur = max(10, int(round(1000.0 / fps / 10)) * 10)
    out[0].save(path, save_all=True, append_images=out[1:], duration=dur,
                loop=0, transparency=0, disposal=2, optimize=False)
    print('GIF %d 帧 @ %dms -> %s' % (len(ims), dur, path))


def outputs(frames, pal):
    cells = [make_cell(fr) for fr in frames]
    fd = os.path.join(BASE, 'frames')
    os.makedirs(fd, exist_ok=True)
    for i, c in enumerate(cells):
        c.save(os.path.join(fd, '%s_%02d.png' % (NAME, i)))
    n = len(cells)
    sheet = Image.new('RGBA', (CELL * n, CELL), (0, 0, 0, 0))
    for i, c in enumerate(cells):
        sheet.paste(c, (i * CELL, 0))
    sheet.save(os.path.join(BASE, NAME + '_sheet.png'))
    sheet.resize((sheet.width * 4, sheet.height * 4), Image.NEAREST) \
        .save(os.path.join(BASE, NAME + '_sheet_x4.png'))
    grid = Image.new('RGBA', (CELL * 4, CELL * 4), (0, 0, 0, 0))
    for i, c in enumerate(cells):
        r, cc = divmod(i, 4)
        grid.paste(c, (cc * CELL, r * CELL))
    grid.save(os.path.join(BASE, NAME + '_grid.png'))
    grid.resize((grid.width * 4, grid.height * 4), Image.NEAREST) \
        .save(os.path.join(BASE, NAME + '_grid_x4.png'))
    grid.resize((grid.width * 6, grid.height * 6), Image.NEAREST) \
        .save(os.path.join(BASE, NAME + '_16frames_x6.png'))
    make_gif(cells, os.path.join(BASE, NAME + '.gif'))
    sw = 16
    pimg = Image.new('RGB', (len(pal) * sw, sw * 2), (255, 255, 255))
    for i, col in enumerate(pal):
        for y in range(sw * 2):
            for x in range(sw):
                pimg.putpixel((i * sw + x, y), col)
    pimg.save(os.path.join(BASE, NAME + '_palette.png'))
    meta = dict(
        name=NAME, display_name='ksm 死亡 v2 (官方角色重演)',
        source_motion='ksm_death/frame_020-101.png (82 帧 AI 序列, 仅取动作节奏)',
        source_art=OFFICIAL + ' (角色本体+配色权威来源, 21 色)',
        frame_count=n, fps=FPS, loop=False, duration_sec=round(n / FPS, 3),
        native_grid=[NATIVE, NATIVE], padding=PAD, cell=[CELL, CELL],
        frame_size=[CELL, CELL], blink_frames=[],
        pivot=dict(x=CELL / 2.0, y=PAD + NATIVE - 1,
                   note='脚底中心; 躺平帧身体底边贴地'),
        palette_count=len(pal), palette=pal,
        motion_map=[
            '01 站立(官方帧逐像素, 源f020)',
            '02-05 渐进蜷缩(块平移; 头持续往右下低 dx+1/1/2/2 dy+1/2/3/5, 腿不动, 源f024-042)',
            '06-07 伏倒定格(正面跪伏, 头下移11格右移2格盖住上身, 闭眼, 吉他横抱, 源f044-058 AI 稳定15帧)',
            '08 翻身弓身(臀先着地, 脚仍弓着, 手绘, 源f059 唯一侧躺中间帧)',
            '09-10 仰面躺平(头左脚右, 全身平贴地, 手绘, 源f060-068)',
            '11-16 躺平静止+魂魄幽灵向左上飘并散成数颗(源f069-101, 造型按AI像素还原:浅灰填充+深色描边+双眼)',
        ],
        eyes='01-05 官方原像素(睁眼); 06-16 闭眼(I 睫线, 仅用官方已有色)',
        guitar='全程在怀: 站立斜抱 -> 伏倒时横抱身前 -> 翻身/躺平时横压在身上(琴头探出脚边)',
        unity_import=dict(SpriteMode='Multiple', GridByCellSize=[CELL, CELL],
                          FilterMode='Point', Compression='None',
                          Pivot='BottomCenter',
                          PPU_note='与 ksm_hurt_pixel 同格(64 原生/块宽 10px/cell 76), 用同一 PPU'),
        notes='v3 动作严格照 AI 82 帧时间轴(无腾空/无后甩): 站立->渐进蜷缩->伏倒定格15帧->贴地翻身->仰躺->魂魄星左上飘。'
              '角色本体与 21 色全部取自 12.png, AI 序列仅提供动作。'
              '与 v1 的区别: v1 重像素化 AI 帧(造型跟 AI 走); v2/v3 官方角色块重组+手绘姿势',
    )
    with open(os.path.join(BASE, NAME + '.json'), 'w', encoding='utf-8') as fh:
        json.dump(meta, fh, ensure_ascii=False, indent=2)
    print('完成 -> %s/' % BASE)


def main():
    frames_only = '--frames-only' in sys.argv
    nat, pal = load_official()
    frames = build_frames(nat)
    ok = qc(frames, nat, pal)
    dump_ascii(frames, os.path.join(BASE, NAME + '_dump.txt'))
    if frames_only:
        print('--frames-only: 跳过产物输出')
        return
    outputs(frames, pal)
    print('帧数 %d' % len(frames))


if __name__ == '__main__':
    main()
