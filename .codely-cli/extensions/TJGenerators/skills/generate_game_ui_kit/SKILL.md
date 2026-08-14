---
name: unity-game-ui-kit-generation
description: Generate game UI asset kits in Unity using AI via a two-step workflow — Step 1 generates a game UI screenshot from text, Step 2 converts it into a UI cutout sheet on magenta background for element extraction. Use this skill whenever the user wants to create game UI assets such as HUD layouts, inventory screens, buttons, panels, health bars, skill icons — e.g. "帮我生成游戏UI", "生成背包界面", "make a game HUD", "create UI kit for my game". Trigger proactively for any game UI design or UI element extraction request in Unity. Do NOT use for standalone 2D sprites/icons (use generate_sprite) or general images (use generate_image).
---

> ⚠️ **执行约束**
> - **主 agent**：无 `execute_custom_tool` 权限，必须 `task(subagent_name="game-ui-kit-generator", ...)` 委托，不要 `activate_skill` 后自己调。
> - **子代理（本文档主要读者）**：有权限，按下方 `execute_custom_tool(...)` 示例执行。

> ⛔ **`place_assets_in_scene` 调用规则**（本 skill **无 placeholder**）
> - **调用方式**：`activate_skill("unity-place-assets-in-scene")` → 按 §4i Texture2D 模板用 `execute_csharp_script` 赋给 `Material.mainTexture` 或建 `RawImage`（**不是** `execute_custom_tool`）。
> - **子代理**：Step 2 的 `<bg_task_done>` 到达后，读 `image_path`，**调一次** `place_assets_in_scene` 把 cutout sheet 放到场景。Step 1 **不调**（中间产物，不需要放置）。
> - **主 agent**：报告里的 `image_path` 是"已放置"的证据，不是"请你放置"的指示，**不要再调**。
> - **例外**：用户明确要"换位置 / 再放一份"时才再次调用。详见 [async-pattern §5.1](../../experience/templates/generator-async-pattern.md#51-place_assets_in_scene-调用规则)。

# Generate Game UI Kit in Unity 🎮

通过两步 AI 工作流生成游戏 UI 资产套件。
Output: Step 1 产出 UI 截图 PNG（landscape_16_9）；Step 2 产出 UI 元素抠图 sheet PNG（square_hd，品红背景），自动保存到 `Assets/TJGenerators/History/`。

## 两步工作流概览

| 步骤 | 输入 | 输出 | 用途 |
|---|---|---|---|
| Step 1 | 文本描述（无 `screenshot_path`） | UI 截图 PNG（landscape_16_9） | 预览 UI 布局设计 |
| Step 2 | Step 1 截图本地路径（`screenshot_path`） | UI 抠图 sheet PNG（square_hd，品红背景） | 提取独立 UI 元素 |

> ⚠️ **两步是串行依赖**——Step 2 必须等 Step 1 完成后才能提交，因为需要 Step 1 的截图本地路径。

## 🚦 执行流程（不要跳读外链）

### Step 1：生成 UI 截图

1. 调 `generate_game_ui_kit`（**不传** `screenshot_path`）→ 拿 `task_id` + `placeholder_path`（1×1 灰色 PNG）
2. **跳过** `place_assets_in_scene`（中间产物，不放置）
3. **END RESPONSE TURN** — 不要 poll、不要 `query_game_ui_kit_status`、不要继续操作
4. 下一轮收到 `<bg_task_done>` → 读 `image_path`（截图本地路径）→ **立即提交 Step 2**

### Step 2：生成 UI 抠图 sheet

5. 调 `generate_game_ui_kit`（`screenshot_path` = Step 1 的 `image_path`）→ 拿 `task_id` + `placeholder_path`
6. **END RESPONSE TURN** — 不要 poll
7. 下一轮收到 `<bg_task_done>` → 读 `image_path`（cutout sheet 本地路径）→ **调一次** `place_assets_in_scene`（资产类型 `Texture2D`）→ 报告完成

**档位**：每步 30–90 秒；120 秒内无通知才允许 `query_game_ui_kit_status` 一次。完整 async 规则见 [generator-async-pattern](../../experience/templates/generator-async-pattern.md)。

## ⚠️ Skill 独有约束

1. **两步串行**——Step 2 依赖 Step 1 的 `image_path`，不能并发。
2. **有 placeholder**——两步都返回 `placeholder_path`（1×1 灰色 PNG），但中间产物不需要放置。
3. **`prompt` 在 Step 2 中被忽略**——Step 2 使用后端固定的 cutout prompt，`prompt` 参数必须传但内容不影响结果。
4. **Step 2 产出品红背景**——cutout sheet 使用纯品红 (#FF00FF) 背景，便于 chroma-key 抠图提取 UI 元素。
5. **`screenshot_path` 是本地路径**——C# host 自动上传到 CDN 并提交给后端，与 `generate_image` 的 `image_path` 模式一致。
6. **并发上限 5**——同时运行的 game_ui_kit 任务最多 5 个（但两步串行，实际每个套件占 1 个并发槽 × 2 次）。

## When to Use / NOT to Use

适用：游戏 HUD 设计、背包界面、技能栏、主菜单 UI、设置面板、对话框样式、游戏 UI 元素提取。

不适用：
- 独立 2D 精灵（图标、立绘、道具） → `generate_sprite`
- 通用图片 / 概念图 / 纹理 → `generate_image`
- 3D 模型 / 材质 / 天空盒 → 各自专属 skill
- 已有 UI 截图、只需抠图 → 仍可用本 skill 的 Step 2（传入 `screenshot_path`）

## 工具

所有工具通过 `execute_custom_tool` 调用。

### `generate_game_ui_kit` — Step 1（生成 UI 截图）

```python
execute_custom_tool(
  tool_name="generate_game_ui_kit",
  parameters={
    "prompt": "fantasy RPG inventory screen with health bars, item slots, skill buttons",  # Required
    # screenshot_path: 不传（Step 1）
    "quality": "medium",       # 可选："low" / "medium" / "high"，默认 "medium"
    "output_format": "png",    # 可选："png" / "jpeg" / "webp"，默认 "png"
    # output_path: 不建议指定，默认 Assets/TJGenerators/History/
  }
)
```

**后端 prompt 增强**：后端会自动在用户 prompt 后追加 UI 设计关键词（`complete game UI screen design, full HUD layout, health bars, mana bars, buttons, panels, inventory grid, skill icons, mini-map, dialogue box, score display, clean professional game interface` 等），无需自己写这些。

### `generate_game_ui_kit` — Step 2（生成 UI 抠图 sheet）

```python
execute_custom_tool(
  tool_name="generate_game_ui_kit",
  parameters={
    "prompt": "fantasy RPG inventory screen",  # 必传但被忽略，用 Step 1 的原 prompt 即可
    "screenshot_path": "Assets/TJGenerators/History/ui_screenshot_xxx.png",  # Required：Step 1 的 image_path
    "quality": "medium",
    "output_format": "png",
  }
)
```

**后端 prompt**：Step 2 使用固定 cutout prompt（提取所有 UI 元素为独立 cutout、网格排列、品红背景），用户 `prompt` 不影响结果。

### 返回字段

**Step 1 / Step 2 通用返回**：
- `task_id`
- `placeholder_path`：1×1 灰色占位 PNG，**立即可用**（但中间产物不需要放置）
- `step`：`1` 或 `2`
- `notification_mode: "bg_task_done"`

提交失败时 `result["success"] == false`，读 `error_code` / `message`，**不要**poll。

### `<bg_task_done>` 独有字段

通用字段见模板。本 skill 额外字段：

**Step 1 完成时**：

| 字段 | 说明 |
|---|---|
| `image_path` | UI 截图本地路径 — **传给 Step 2 的 `screenshot_path`** |
| `preview_url` | 预览 URL |

**Step 2 完成时**：

| 字段 | 说明 |
|---|---|
| `image_path` | cutout sheet 本地路径（品红背景 PNG） |
| `preview_url` | 预览 URL |

### `query_game_ui_kit_status` / `list_game_ui_kit_tasks`

`query_game_ui_kit_status` 仅作 fallback（120 秒后单次）。返回字段同 `<bg_task_done>` payload，外加 `placeholder_path`（仅 `generating` 时）。

`list_game_ui_kit_tasks` 返回当前 session 的所有 game_ui_kit 任务。

## 参数速查

| 参数 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `prompt` | string | **required** | Step 1: UI 描述；Step 2: 被忽略但必传 |
| `screenshot_path` | string | — | Step 2 only：Step 1 的 `image_path`。省略 = Step 1 |
| `quality` | string | `"medium"` | `"low"` / `"medium"` / `"high"` |
| `output_format` | string | `"png"` | `"png"` / `"jpeg"` / `"webp"` |
| `output_path` | string | — | 不建议指定，默认 `Assets/TJGenerators/History/` |

## 使用示例

### 完整两步流程

```python
# === Step 1: 生成 UI 截图 ===
result = execute_custom_tool(
    tool_name="generate_game_ui_kit",
    parameters={
        "prompt": "fantasy RPG inventory screen with health bars, item slots, skill buttons",
        "quality": "medium"
    }
)
if not result.get("success", True):
    raise RuntimeError(f"[{result['error_code']}] {result['message']}")

task_id = result["task_id"]
# ✅ END RESPONSE TURN — 等 bg_task_done
# 通知到达后读 image_path，提交 Step 2
```

```python
# === Step 2: 生成 UI 抠图 sheet ===
# （在 Step 1 的 <bg_task_done> 到达后执行）
result = execute_custom_tool(
    tool_name="generate_game_ui_kit",
    parameters={
        "prompt": "fantasy RPG inventory screen",  # 原始 prompt，Step 2 忽略
        "screenshot_path": screenshot_path,           # Step 1 的 image_path
        "quality": "medium"
    }
)
task_id = result["task_id"]
# ✅ END RESPONSE TURN — 等 bg_task_done
# 通知到达后读 image_path，调 place_assets_in_scene
```

### 跳过 Step 1（用户已有截图）

```python
# 用户提供了已有截图的本地路径
result = execute_custom_tool(
    tool_name="generate_game_ui_kit",
    parameters={
        "prompt": "existing UI",              # 必传但被忽略
        "screenshot_path": "Assets/UI/existing_screenshot.png",
    }
)
# 直接进入 Step 2
```

## 元素提取指南（Step 2 完成后向 caller 提供）

Step 2 产出的 cutout sheet 使用品红 (#FF00FF) 背景，可通过以下方式提取 UI 元素：

### 1. 去除品红背景（Chroma Key）

**ImageMagick**:
```bash
convert ui_cutout_sheet.png -fuzz 15% -transparent "#FF00FF" ui_cutout_transparent.png
```

**Python (PIL)**:
```python
from PIL import Image
img = Image.open("ui_cutout_sheet.png").convert("RGBA")
data = img.getdata()
newData = []
for item in data:
    if item[0] > 200 and item[1] < 55 and item[2] > 200:  # magenta
        newData.append((255, 255, 255, 0))
    else:
        newData.append(item)
img.putdata(newData)
img.save("ui_cutout_transparent.png")
```

### 2. Unity Sprite Editor 切割

1. 导入透明 PNG → Texture Type = **Sprite (2D and UI)**, Sprite Mode = **Multiple**
2. Sprite Editor → **Slice** → Type = **Automatic** 或 **Grid by Cell Size**
3. Apply → 每个 UI 元素成为独立 sub-sprite

### 3. 注意事项

- **细边框**：使用低 fuzz 容差 (5-10%) 避免吃掉边缘像素
- **抗锯齿边缘**：chroma key 后检查品红残留，用 `-channel RGBA -blur 0x1` 平滑
- **半透明元素**：玻璃/幽灵面板可能需要双背景提取
- **动态文字**：cutout 中的文字是栅格化的，可编辑文字请在引擎中单独渲染

## 放入场景

Step 2 的 cutout sheet 可作为 `Texture2D` 放入场景（用于预览或 Material 贴图）。

资产类型 **`Texture2D`**，路径用 `image_path`。

> 多数情况下用户需要的是提取后的独立 UI 元素（Sprite），而非整张 cutout sheet。放置整张 sheet 主要用于预览。规则见 [async-pattern §5 / §5.1](../../experience/templates/generator-async-pattern.md#5-placeholder-工作流适用于会返回-placeholder_path--prefab_output_path-的工具)。

## Prompt 写作指南

| 用途 | Prompt 示例 |
|---|---|
| RPG 背包 | `"fantasy RPG inventory screen with health bars, item slots, skill buttons"` |
| FPS HUD | `"first-person shooter HUD with ammo counter, minimap, crosshair, health bar"` |
| 主菜单 | `"medieval game main menu with ornate buttons, settings panel, character portrait"` |
| 对话框 | `"visual novel dialogue box with text area, character name plate, choice buttons"` |
| 技能树 | `"skill tree UI with branching nodes, connection lines, unlock buttons"` |

技巧：
- 描述 **UI 类型和包含的元素**（按钮、面板、血条、物品格）
- 提及 **游戏类型/风格**（fantasy RPG / sci-fi / medieval）
- 后端会自动增强 prompt，无需写 "HUD layout" 等关键词
- 英文 prompt 效果更佳

## 故障排查

### Skill 独有问题

> 通用故障（配置缺失 / 任务卡住 / 状态异常 / 未登录）见 [generator-async-pattern §10](../../experience/templates/generator-async-pattern.md#10-通用故障排查)。

| 问题 | 原因 | 解决 |
|---|---|---|
| Step 1 截图不像游戏 UI | prompt 太笼统 | 描述具体 UI 元素（血条、物品格、技能按钮）；后端会增强但基础描述仍重要 |
| Step 2 cutout sheet 为空/全品红 | `screenshot_path` 路径错误 | 确认使用 Step 1 `<bg_task_done>` 中的 `image_path` |
| Step 2 提交报错 | 缺少 `screenshot_path` | Step 2 必须传 `screenshot_path`，值为 Step 1 的 `image_path` |
| 元素提取有品红残留 | chroma key 容差太低 | 提高 fuzz 百分比到 15-20%；或用 `-channel RGBA -blur 0x1` 平滑边缘 |
| Step 1 和 Step 2 用了不同 prompt | Step 2 忽略 prompt | 这是正常的——Step 2 使用固定 cutout prompt |

### Domain reload 后 task 丢失

通用恢复流程见 [generator-async-pattern §6](../../experience/templates/generator-async-pattern.md#6-domain-reload-recovery)。本 skill 完成态阈值：

- PNG < 5 KB → 仍是 placeholder 或任务丢失
- PNG ≥ 50 KB → 真实图片已就绪

可用 `glob("Assets/TJGenerators/History/*.png")` + 文件大小恢复。注意区分 Step 1 截图和 Step 2 cutout sheet（按时间和尺寸判断）。

---

**Task ID Format**：`game_ui_kit_{counter}_{timestamp}`

**Notes**：
- 两步均使用 Frontier Game Design 模型
- Step 1 使用 `landscape_16_9` 尺寸；Step 2 使用 `square_hd` 尺寸
- Step 2 的 cutout prompt 是后端固定的，用户 prompt 不影响
- 自动应用 `TuanjieAI` 标签
- **并发上限 5**
- 需 Unity Editor 在线运行；消耗 AI 服务额度
