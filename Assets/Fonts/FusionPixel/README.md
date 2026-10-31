# Fusion Pixel Font（缝合像素字体）

- 来源：https://github.com/TakWolf/fusion-pixel-font ，版本 2026.10.25
- 文件：12px、比例宽度、简体中文（zh_hans）；官方发布的 `.ttf.woff2` 用 fontTools 还原成 `.ttf`，字形未改动
- 授权：SIL Open Font License 1.1（`OFL.txt`），可免费商用；分发游戏时需附带授权文本。
  字体内含 Ark Pixel / Cubic 11 / Galmuri 的字形，各自授权见 `LICENSES/`
- 用途：策划案 8.3 的「正文 / UI 像素字体」。`FusionPixel-12px SDF.asset` 是 TMP 动态字体（采样 48 = 12px × 4），
  已加进 TMP Settings 的全局回退列表，任何 TMP 文本缺字时都会用它补上中文

## 注意：动态字体资产会「自己变大」
编辑器里每显示一个新字，TMP 就把字形写进 `FusionPixel-12px SDF.asset` 的图集，git 里这个文件会出现改动（可能涨到几 MB）。
这些改动不用提交：在 Inspector 里选中这个字体资产 → 右上角齿轮 / 右键 → **Reset**，就清回空的初始状态；
或者直接丢弃这个文件的改动。打包时也会自动清空（已勾选 Clear Dynamic Data On Build），不影响游戏。
