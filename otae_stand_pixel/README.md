# otae_stand_pixel — 兔耳少女待机动画帧（Dola AI 序列透明化版）

## 来源
- 源图：`D:/UnityProject/Poppin/otae/frame_001..055.png`（Dola AI 生成待机序列，480×640，不透明近白底）
- 生成脚本：`make_otae_stand.py`（可重复运行）

## 产物
- `otae_stand_01.png` ~ `otae_stand_55.png`：透明背景 RGBA，统一画布 **383×640**（55 帧联合包围盒裁剪，帧间已验证无整体平移，裁剪后天然对齐）
- `otae_stand_preview.gif`：12fps 预览
- `otae_stand.json`：元数据（源路径、bbox、帧列表）

## 处理方式（符合「不改原美术」惯例）
- RGB 颜色原样，未做调色板吸附；仅 alpha 二值化（0/255）
- 去背 = 连通域法：仅与图像边缘连通的近背景色区域置透明，角色内部白色区域（耳内、发箍）不与边缘连通，全部保留
- 右下角固定位置 "Dola AI" 水印整块清除（实测 55 帧水印均固定于 x407-467/y612-628，该矩形内无角色像素）
- 栅格：水平同色段台阶 12/24/36/72 → 块 12px；因 AI 伪像素画块内自带渐变，保持 480 系原分辨率未做强制量化（避免帧间抖动）

## Unity 接入建议
- Texture Import: **Point (no filter)**，关闭 mipmaps
- PPU：块 12px；与 ksm（idle 175px）同场景时按目标身高折算，例如角色期望 4 单位高 → PPU≈160
- Sprite pivot：**Bottom Center (0.5, 0)**
- Animation：帧序列 12fps 循环（AI 生成序列，帧间为轻微呼吸/发丝晃动）
