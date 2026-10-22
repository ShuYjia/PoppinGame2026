# -*- coding: utf-8 -*-
# 预览页文案 patch（idle → hurt 语义），字节级替换避免 sed/行尾坑
import io, sys

P = r"D:\UnityProject\Poppin\ksm_hurt_pixel\ksm_hurt_preview.html"
data = open(P, 'rb').read()

pairs = [
    ('<title>ksm · 像素待机动画预览</title>', '<title>ksm · 像素受击动画预览</title>'),
    ('<h1>ksm · 像素2D 待机动画 <span>Idle</span></h1>', '<h1>ksm · 像素2D 受击动画 <span>Hurt</span></h1>'),
    ('呼吸起伏 3px + 眨眼帧 [1, 2, 3, 4, 5, 6, 7, 8, 9]', '受击整体击退 3px · 闭眼帧 [1-9]'),
    ('呼吸 · 睁眼', '受击 · 闭眼'),
    ('(看 3px 起伏)', '(看 3px 受击位移)'),
    ('<em>呼吸</em>：上身下沉 0~3px + 水平重心摇摆 ±4px（头层垂直滞后 1 帧）；腿静止、绘制在最底层，身体下沉时下摆盖住腿的上沿，脚不抖。<br>',
     '<em>受击</em>：躯干+腿整体向左击退 0~3px（腿完全随动，身-腿零错位）+ 屈膝下沉 0~3px；受击 hold 期头向左转头 1px 并缩脖多沉 1px；受击帧 1-9 闭眼（只用原图已有色），末两帧睁眼回位。<br>'),
    ('<em>眨眼</em>：闭眼帧 [1, 2, 3, 4, 5, 6, 7, 8, 9]。<br>',
     '<em>闭眼</em>：受击帧 1-9 闭眼（只用原图已有色），末两帧睁眼。<br>'),
    ("'闭眼眨眼'", "'闭眼'"),
]
miss = 0
for old, new in pairs:
    ob, nb = old.encode('utf-8'), new.encode('utf-8')
    if ob not in data:
        print('MISS:', old[:24])
        miss += 1
        continue
    data = data.replace(ob, nb)
open(P, 'wb').write(data)
print('patched, miss =', miss)
