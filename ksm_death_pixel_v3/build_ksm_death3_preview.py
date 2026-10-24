#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""生成 ksm_death 自包含预览页（图集 base64 内联，单文件可直接打开）。"""
import json, base64, io, sys
from PIL import Image

TEMPLATE = r"""<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<title>__TITLE__</title>
<style>
  :root{ --bg:#1b1d21; --panel:#24272c; --fg:#e6e8eb; --muted:#9aa2ad; --accent:#5aa9ff; }
  *{box-sizing:border-box}
  body{margin:0;background:var(--bg);color:var(--fg);
       font:14px/1.5 "Segoe UI",system-ui,"Microsoft YaHei",sans-serif}
  .wrap{max-width:1180px;margin:0 auto;padding:20px}
  h1{font-size:20px;margin:0 0 4px}
  .sub{color:var(--muted);font-size:12px;margin-bottom:16px}
  .stage{position:relative;background:var(--panel);border:1px solid #33383f;
         border-radius:10px;overflow:hidden;display:flex;align-items:center;
         justify-content:center;height:__STAGEH__px}
  .stage.checker{background-image:linear-gradient(45deg,#2a2e34 25%,transparent 25%),
      linear-gradient(-45deg,#2a2e34 25%,transparent 25%),
      linear-gradient(45deg,transparent 75%,#2a2e34 75%),
      linear-gradient(-45deg,transparent 75%,#2a2e34 75%);
      background-size:16px 16px;background-position:0 0,0 8px,8px -8px,-8px 0}
  .stage.light{background:#f2f3f5}
  .stage.grid{background-image:linear-gradient(#3a4048 1px,transparent 1px),
      linear-gradient(90deg,#3a4048 1px,transparent 1px);
      background-size:__CELL__px __CELL__px}
  #sprite{image-rendering:pixelated;display:block}
  #ground{position:absolute;left:0;right:0;height:1px;background:#e0483b;opacity:.85;pointer-events:none}
  .bar{display:flex;gap:10px;align-items:center;flex-wrap:wrap;margin:12px 0}
  button,select{background:#2f333a;color:var(--fg);border:1px solid #3d434b;
      border-radius:6px;padding:6px 11px;cursor:pointer;font-size:13px}
  button:hover{border-color:var(--accent)}
  button.on{background:var(--accent);border-color:var(--accent);color:#08121e}
  input[type=range]{accent-color:var(--accent)}
  .mono{font-family:ui-monospace,Consolas,monospace}
  .thumbs{display:flex;gap:5px;flex-wrap:wrap;margin-top:10px}
  .thumbs img{image-rendering:pixelated;border:1px solid #3d434b;border-radius:4px;
      cursor:pointer;background:#1a1c20}
  .thumbs img.sel{border-color:var(--accent);box-shadow:0 0 0 1px var(--accent)}
  table{border-collapse:collapse;margin-top:14px;font-size:13px}
  td,th{border:1px solid #3d434b;padding:5px 10px;text-align:left}
  th{background:#2a2e34;color:var(--muted);font-weight:600}
  .sw{display:inline-block;width:15px;height:15px;border:1px solid #555;vertical-align:-3px}
</style>
</head>
<body>
<div class="wrap">
  <h1>__TITLE__</h1>
  <div class="sub">__SUB__</div>

  <div id="stage" class="stage checker">
    <canvas id="cv"></canvas>
    <div id="ground"></div>
  </div>

  <div class="bar">
    <button id="play" class="on">暂停</button>
    <button id="prev">◀ 上一帧</button>
    <button id="next">下一帧 ▶</button>
    <span class="mono" id="fno">0 / 0</span>
    <label>速度 <input type="range" id="spd" min="1" max="48" value="__FPS__"> <span class="mono" id="spdv">__FPS__</span> fps</label>
    <label>缩放 <input type="range" id="zoom" min="1" max="12" value="__ZOOM__"> <span class="mono" id="zoomv">__ZOOM__</span>x</label>
    <select id="bg">
      <option value="checker">棋盘</option>
      <option value="dark">深色</option>
      <option value="light">浅色</option>
      <option value="grid">网格</option>
    </select>
  </div>

  <div class="thumbs" id="thumbs"></div>

  <table>
    <tr><th>项目</th><th>值</th></tr>
    <tr><td>原生栅格</td><td class="mono">__NATIVE__ x __NATIVE__（源 720px / 块宽 10px）</td></tr>
    <tr><td>帧单元格</td><td class="mono">__CELL__ x __CELL__（padding __PAD__）</td></tr>
    <tr><td>帧数 / 帧率</td><td class="mono">__NFRAMES__ 帧 @ __FPS__fps（__DUR__s，一次性不循环）</td></tr>
    <tr><td>枢轴</td><td class="mono">Bottom Center（脚底中心，全部帧贴地）</td></tr>
    <tr><td>调色板</td><td class="mono">__NPAL__ 色（全序列统一）</td></tr>
  </table>
  <div class="sub" style="margin-top:10px">__PALSW__</div>
</body>
<script>
const CELL = __CELL__, N = __NFRAMES__;
const sheet = new Image();
sheet.src = "__SHEET__";
const cv = document.getElementById('cv');
const ctx = cv.getContext('2d');
let f = 0, playing = true, fps = __FPS__, zoom = __ZOOM__;
let last = 0, acc = 0;

function layout(){
  cv.width = CELL * zoom; cv.height = CELL * zoom;
  const y = CELL - (__PIVOTY__);      // 枢轴到底边的距离(源像素) -> 缩放后
  const g = document.getElementById('ground');
  g.style.bottom = (cv.getBoundingClientRect().height - y) + 'px';
  draw();
}
function draw(){
  ctx.clearRect(0,0,cv.width,cv.height);
  ctx.imageSmoothingEnabled = false;
  ctx.drawImage(sheet, f*CELL, 0, CELL, CELL, 0, 0, CELL*zoom, CELL*zoom);
  document.getElementById('fno').textContent = (f+1) + ' / ' + N;
  document.querySelectorAll('.thumbs img').forEach((im,i)=>im.classList.toggle('sel', i===f));
}
function tick(t){
  if(!last) last = t;
  const dt = t - last; last = t;
  if(playing){
    acc += dt;
    const step = 1000/fps;
    while(acc >= step){ acc -= step; f = (f+1) % N; }
    draw();
  }
  requestAnimationFrame(tick);
}
sheet.onload = ()=>{ layout(); };

document.getElementById('play').onclick = function(){
  playing = !playing;
  this.textContent = playing ? '暂停' : '播放';
  this.classList.toggle('on', playing);
};
document.getElementById('prev').onclick = ()=>{ f = (f-1+N)%N; playing=false;
  document.getElementById('play').textContent='播放';
  document.getElementById('play').classList.remove('on'); draw(); };
document.getElementById('next').onclick = ()=>{ f = (f+1)%N; playing=false;
  document.getElementById('play').textContent='播放';
  document.getElementById('play').classList.remove('on'); draw(); };
document.getElementById('spd').oninput = function(){ fps = +this.value;
  document.getElementById('spdv').textContent = fps; };
document.getElementById('zoom').oninput = function(){ zoom = +this.value;
  document.getElementById('zoomv').textContent = zoom; layout(); };
document.getElementById('bg').onchange = function(){
  const s = document.getElementById('stage');
  s.className = 'stage ' + this.value;
};

// 缩略图
(function(){
  const t = document.getElementById('thumbs');
  for(let i=0;i<N;i++){
    const im = new Image();
    im.width = __THUMB__; im.height = __THUMB__;
    im.src = "__SHEET__";
    im.style.width = __THUMB__+'px'; im.style.height = __THUMB__+'px';
    im.title = '帧 ' + (i+1);
    im.onload = ()=>{
      const c = document.createElement('canvas');
      c.width = __THUMB__; c.height = __THUMB__;
      const x = c.getContext('2d');
      x.imageSmoothingEnabled = false;
      x.drawImage(im, i*CELL, 0, CELL, CELL, 0, 0, __THUMB__, __THUMB__);
      im.src = c.toDataURL();
    };
    im.onclick = ()=>{ f = i; playing=false;
      document.getElementById('play').textContent='播放';
      document.getElementById('play').classList.remove('on'); draw(); };
    t.appendChild(im);
  }
})();
</script>
</html>
"""

def main():
    out_dir = sys.argv[1] if len(sys.argv) > 1 else 'ksm_death_pixel_v3'
    with open(f'{out_dir}/ksm_death3.json', encoding='utf-8') as f:
        meta = json.load(f)
    sheet = Image.open(f'{out_dir}/ksm_death3_sheet.png')
    buf = io.BytesIO()
    sheet.save(buf, format='PNG')
    b64 = base64.b64encode(buf.getvalue()).decode()

    pal = meta['palette']
    swatches = ''.join(
        f'<span class="sw" style="background:rgb({c[0]},{c[1]},{c[2]})"></span>'
        for c in pal)

    zoom = 5 if sheet.height >= sheet.width else 4
    stage_h = meta['cell'][1] * zoom + 20

    html = (TEMPLATE
            .replace('__TITLE__', meta['display_name'] + ' 动画帧')
            .replace('__SUB__', f"源 {meta['source_motion']} 帧抽成 {meta['frame_count']} 帧；"
                                f"统一调色板；底边贴地；用于像素 2D 游戏")
            .replace('__STAGEH__', str(stage_h))
            .replace('__SHEET__', 'data:image/png;base64,' + b64)
            .replace('__CELL__', str(meta['cell'][0]))
            .replace('__NFRAMES__', str(meta['frame_count']))
            .replace('__FPS__', str(meta['fps']))
            .replace('__ZOOM__', str(zoom))
            .replace('__PIVOTY__', str(meta['pivot']['y']))
            .replace('__NATIVE__', str(meta['native_grid'][0]))
            .replace('__PAD__', str(meta['padding']))
            .replace('__DUR__', str(meta['duration_sec']))
            .replace('__NPAL__', str(meta['palette_count']))
            .replace('__THUMB__', '72')
            .replace('__PALSW__', swatches))
    p = f'{out_dir}/ksm_death3_preview.html'
    with open(p, 'w', encoding='utf-8') as f:
        f.write(html)
    print('preview ->', p)

if __name__ == '__main__':
    main()
