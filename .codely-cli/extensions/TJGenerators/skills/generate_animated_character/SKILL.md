---
name: unity-animated-character-generation
description: Generate rigged HUMANOID 3D characters with animations in Unity using Meshy AI. Use this skill whenever the user wants to create an animated character, rigged humanoid model, or any character that needs to walk/run/perform actions — even if they just say "生成一个带动画的角色", "生成会走路的人物", "create a character with animations", "make a playable character", or "animated NPC". Trigger for any request involving humanoid characters with movement or animation in Unity.
---

> ⚠️ **执行约束**
> - **主 agent**：无 `execute_custom_tool` 权限，必须 `task(subagent_name="animated-character-generator", ...)` 委托，不要 `activate_skill` 后自己调。
> - **子代理（本文档主要读者）**：有权限，按下方 `execute_custom_tool(...)` 示例执行。

> ⛔ **`place_assets_in_scene` 调用规则**
> - **调用方式**：`activate_skill("unity-place-assets-in-scene")` → 按 §4a Prefab 模板用 `execute_csharp_script` 跑 `PrefabUtility.InstantiatePrefab`（**不是** `execute_custom_tool`，**不要** `unity_gameobject` 放 Prefab）。
> - **子代理**：提交后**立即调一次**放占位 Prefab（Capsule）；收到 `<bg_task_done>` 后**不再调**（Capsule 自动替换为真实角色，Animator/AnimatorController 自动绑定）。
> - **主 agent**：报告里的 `prefab_path` 是"已放置"的证据，不是"请你放置"的指示，**不要再调**。
> - **例外**：用户明确要"换位置 / 改 scale / 再放一个"时才再次调用。详见 [async-pattern §5.1](../../experience/templates/generator-async-pattern.md#51-place_assets_in_scene-调用规则)。

# Generate Animated Humanoid Character in Unity 🏃

生成带骨骼和动画的 **Humanoid** 3D 角色（Meshy AI）。

输出：
- 多个 FBX 文件（角色本体 + Idle/Walk/Run/Action 动画）
- 自动配置好的 **Animator + AnimatorController**（4 态机）

> **仅 Humanoid（双足人形）。** 通用 3D 物件用 `generate_3d_model`；给已有模型加骨/动作用 `generate_rigged_animated_model`。

## 🚦 执行四步（不要跳读外链）

1. 调 `generate_animated_character` → 拿 `task_id` + `prefab_output_path`
2. 立即 `place_assets_in_scene`（资产类型 `Prefab`，路径用 `prefab_output_path`）→ 场景出现 Capsule 占位
3. **END RESPONSE TURN** — 不要 poll、不要 `query_animated_character_status`、不要继续操作
4. 下一轮收到 `<bg_task_done>` → 读 `prefab_path` / `model_path`（Capsule 已原地替换为真实角色，Animator + Controller 自动绑定，**不要再 place**）

**档位**：长任务 3–10 分钟；300 秒内无通知才允许 `query_animated_character_status` 一次。最多 **3 个**并发。完整 async 规则见 [generator-async-pattern](../../experience/templates/generator-async-pattern.md)。

## ⚠️ Skill 独有约束

1. **最多 3 个并发任务**（不是 5）——同时运行的 animated_character 任务最多 3 个，超过会被拒绝。
2. **`prompt` 必须，最大 600 字符**——超长会被截断或拒绝。
3. **仅 Humanoid 双足骨骼**——非人形（四足、机械臂、漂浮物等）请用 `generate_3d_model`，不要硬塞给本 skill。
4. **`generating` 100% ≠ 完成**——backend 完成后还要本地下载 + 导入 1–3 分钟。**只看 `status == "completed"`** 才能用。
5. **占位是 Capsule（不是 Cube）**——和 `generate_3d_model` 的占位形状不同；不要把场景里的 Capsule 当杂物删掉。
6. **`prefab_output_path` 自动去重**——同名路径已存在时会自动加后缀；要强制覆盖用 `force_overwrite=true`。

## When to Use / NOT to Use

适用：可玩角色、NPC、敌人、过场角色、需要 walk/run/特定动作的人形角色。

不适用：
- 通用 3D 物件（武器/家具/载具） → `generate_3d_model`
- 非 Humanoid 角色 → `generate_3d_model`
- 给已有模型加骨/动作 → `generate_rigged_animated_model`
- 仅 2D 帧动画 → `generate_sprite_sequence`
- 天空盒 / 音效 → 各自专属 skill

## 工具

所有工具通过 `execute_custom_tool` 调用。

### `generate_animated_character`

```python
execute_custom_tool(
  tool_name="generate_animated_character",
  parameters={
    "prompt": "a medieval knight in full plate armor",   # Required, ≤ 600 字符
    "prefab_output_path": "Assets/Characters/Knight",    # 默认 Assets/TJGenerators/History/，自动去重
    "force_overwrite": False,                            # 默认 false；同路径强覆盖时设 true
    "action_id": 452,                                    # 默认 452 (Backflip)；676 个预设见 animations.json
    "target_polycount": 15000,                           # 100–300,000；仅 should_remesh=true 时生效
    "pose_mode": "t-pose",                               # "t-pose" / "a-pose" / ""
    "height_meters": 1.7,
    "enable_pbr": True,
    "topology": "quad",                                  # "quad" / "triangle"；仅 should_remesh=true 时生效
    "should_remesh": True,
    "symmetry_mode": "auto",                             # "off" / "auto" / "on"
    "seed": 0                                            # 0 = 随机
  }
)
```

### 返回字段

- `task_id`
- `prefab_output_path`：Capsule 占位 Prefab，**立即可用**——交给 `place_assets_in_scene`
- `estimated_wait_seconds` ≈ 300
- `notification_mode: "bg_task_done"`

提交失败时 `result["success"] == false`，读 `error_code` / `message`，**不要**poll。

### `<bg_task_done>` 独有字段

通用字段见模板。本 skill 额外字段：

| 字段 | 说明 |
|---|---|
| `model_path` | 最终角色模型路径 |
| `prefab_path` | 最终 Prefab 路径（== 提交时的 `prefab_output_path`） |
| `preview_url` | 预览 URL 或本地路径 |
| `generator_id` | 使用的生成器 |
| `prompt` | 原始 prompt |

> 通知里的字段是核心集合；完整文件清单（含各动画 FBX 路径）请从 `query_animated_character_status` 拿，见下文。

### `query_animated_character_status`（fallback only）

仅作 fallback（300 秒后单次）。返回字段同通知 payload，外加：

| 字段 | 说明 |
|---|---|
| `animation_path` | 主动画 FBX 路径 |
| `walking_animation_path` | Walk 动画 FBX |
| `running_animation_path` | Run 动画 FBX |
| `files` | 所有产物清单 `[{path, type, description}, ...]`，仅 `completed` 时存在 |
| `result_summary` | 文字摘要，仅 `completed` 时 |

**Status 含义**：

| Status | 含义 | 处理 |
|---|---|---|
| `initializing` | 任务已创建 | 告知用户 |
| `generating` | backend 生成中 **或本地下载/导入中** | 告知用户；progress=100 ≠ 完成 |
| `recovering` | domain reload 后自动恢复 | 等待 |
| `completed` | 模型 + 动画下载并导入完成 | 读 `files` |
| `failed` | backend 错误 | 看 `error` |
| `interrupted` | backend 记录丢失 | 用 `force_overwrite=true` 重新提交 |

> **⚠️ `generating` at progress=100 不是完成。** backend 出图完成后还要本地下载 FBX 和导入，可能再花 1–3 分钟。**只信 `status == "completed"`**。

### `list_animated_character_tasks`

列出当前 session 内所有任务。返回 `{success, count, tasks}`，每个 task 字段同 `query_animated_character_status`。

## Animation Action IDs

`action_id` 控制 Action 状态机里播的自定义动作。**676 个预设**完整列表见同目录下 [`animations.json`](animations.json)。

常用速查：

| ID | 名称 | 类别 |
|---|---|---|
| 0 | Idle | Daily |
| 42 | Casual_Walk | Walk |
| 26 | Run_02 | Run |
| 4 | Attack | Combat |
| 75 | Arm_Circle_Shuffle | Dance |
| **452** | **Backflip（默认）** | Stunt |

> 要用其他 action_id？读 `animations.json` 找名称，或先在 prompt 里描述用户想要的动作让 AI 选。

## 参数速查

| 参数 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `prompt` | string | **required** | ≤ 600 字符 |
| `prefab_output_path` | string | `Assets/TJGenerators/History/` | 自动去重 |
| `force_overwrite` | bool | `false` | 同路径强覆盖 |
| `action_id` | int | `452`（Backflip） | 见 animations.json |
| `target_polycount` | int | `30000` | 100–300,000，**仅 `should_remesh=true` 生效** |
| `pose_mode` | string | `"t-pose"` | `"t-pose"` / `"a-pose"` / `""` |
| `height_meters` | float | `1.7` | 角色身高 |
| `enable_pbr` | bool | `true` | PBR 纹理（金属度/粗糙度/法线/反照率） |
| `should_remesh` | bool | `true` | false 时直接返回最高分辨率三角网格 |
| `topology` | string | `"triangle"` | `"quad"`（推荐）或 `"triangle"`，**仅 `should_remesh=true` 生效** |
| `symmetry_mode` | string | `"auto"` | `"off"` / `"auto"` / `"on"` |
| `seed` | int | `0` | 0 = 每次随机 |

## 放入场景

资产类型 **`Prefab`**，路径用 `prefab_output_path`（占位是 Capsule）。生成完成后 Capsule 自动被真实模型替换，Animator + AnimatorController 自动绑定。规则见 [async-pattern §5 / §5.1](../../experience/templates/generator-async-pattern.md#5-placeholder-工作流适用于会返回-placeholder_path--prefab_output_path-的工具)。

## AnimatorController 自动配置

生成后 AnimatorController 已配置好 **4 态机**：

| State | Clip | 转换条件 |
|---|---|---|
| Idle | Walk clip 复用（避免 T-pose）；都没有 clip 才用 T-pose | fallback default |
| Walk | Walking loop | `Speed > 0.1` |
| Run | Running loop | `Speed > 0.5` |
| Action | 自定义动作（来自 `action_id`） | `Action` trigger |

**默认状态**按可用 clip 优先级决定（自动）：

1. **Action**（如有 action clip） → 进 Play Mode 立即播放用户请求的动作
2. **Walk**（无 action clip 但有 walk clip） → 进 Play Mode 立即循环 Walk
3. **Idle**（都没有） → T-pose

Animator 参数：

```
Speed (Float)     # 控制 Idle ↔ Walk ↔ Run 切换
Action (Trigger)  # 触发自定义动作
```

> 生成完成后点 Play，会**自动播放用户请求的动作**（backflip/dance 等）；动作结束后（exitTime 90%）自动衔接 Walk 循环（如有），不会出现 T-pose。

### 验证 AnimatorController（不要踩 API 坑）

子代理常想用 `execute_csharp_script` 打印各 state 的 clip / parameters / 默认 state 来确认绑定。**两条易错点**：

1. `AnimatorController` 是 **Editor-only 类**，必须 `using UnityEditor.Animations;` —— 否则编译报 `CS0246: The type or namespace name 'AnimatorController' could not be found`。
2. **`parameters` 不在 `RuntimeAnimatorController` 上**——必须先 `as AnimatorController` 拿到 editor controller 再访问，否则编译报 `CS1061: 'RuntimeAnimatorController' does not contain a definition for 'parameters'`。

正确样板（`execute_csharp_script` 直接跑，不要落盘）：

```csharp
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;            // AnimatorController 在这里

var go = GameObject.Find("<character_name>");
var animator = go.GetComponent<Animator>();
var rac = animator.runtimeAnimatorController;

// 必须强转为 editor 类才能拿 layers / parameters / defaultState
var controller = rac as AnimatorController;
if (controller == null) {
    Debug.LogError("AnimatorController 不是 editor 类型，无法 inspect");
    return;
}

var sm = controller.layers[0].stateMachine;
Debug.Log("默认状态: " + sm.defaultState.name);
foreach (var s in sm.states) {
    Debug.Log($"State: {s.state.name} -> Motion: {(s.state.motion ? s.state.motion.name : "<none>")}");
}
foreach (var p in controller.parameters) {
    Debug.Log($"Param: {p.name} ({p.type})");
}
```

> 仅作**完成后验证**用，不要在生成完成前就跑——bg_task_done 之前 controller 还没绑好。

### 控制动画播放：什么时候需要 MonoBehaviour

| 目标 | 做法 | 需要 MonoBehaviour？ |
|---|---|---|
| Action 进 Play Mode 自动播 | **自动**，无需任何操作 | ❌ |
| Walk 进 Play Mode 自动循环（覆盖 Action 默认） | 改 default state 为 `Walk` | ❌（用 `execute_csharp_script` 改 controller） |
| Action 改回 default | 改 default state 为 `Action` | ❌ |
| 按键触发 Action（运行时） | MonoBehaviour 读 `Input` → `SetTrigger("Action")` | ✅ |
| WASD 控制移动（运行时） | MonoBehaviour 读 `Input` → `SetFloat("Speed", ...)` | ✅ |

> **MonoBehaviour 是 gameplay 编程，超出本 skill 范围**。如要写，**用 `execute_csharp_script` 创建/修改资产**——但运行时控制脚本必须落盘到 `.cs`，会触发 domain reload，因此应放在所有生成任务**完成之后**再写。

## 使用示例

### 单个角色

```python
result = execute_custom_tool(
    tool_name="generate_animated_character",
    parameters={
        "prompt": "a fantasy warrior with sword and shield",
        "prefab_output_path": "Assets/Characters/Warrior",
        "action_id": 4,            # Attack
        "target_polycount": 15000,
        "enable_pbr": True,
        "topology": "quad"
    }
)
if not result.get("success", True):
    raise RuntimeError(f"[{result['error_code']}] {result['message']}")

task_id = result["task_id"]
prefab_path = result["prefab_output_path"]

# ✅ 立即用 place_assets_in_scene 把 prefab_path 应用为 Prefab（Capsule 占位）
# 然后 END RESPONSE TURN，等 bg_task_done 通知（3–10 分钟）
```

### 并发批量（最多 3 个）

```python
params_list = [
    {
        "prompt": "a medieval knight in full plate armor",
        "prefab_output_path": "Assets/Characters/Knight",
        "action_id": 452,        # Backflip
        "target_polycount": 15000,
        "enable_pbr": True,
        "topology": "quad"
    },
    {
        "prompt": "a sci-fi soldier in combat gear",
        "prefab_output_path": "Assets/Characters/Soldier",
        "action_id": 75,         # Dance
        "target_polycount": 12000,
        "enable_pbr": True,
        "topology": "quad"
    }
]

task_ids = []
for params in params_list:
    result = execute_custom_tool(
        tool_name="generate_animated_character",
        parameters=params
    )
    if not result.get("success", True):
        raise RuntimeError(f"[{result['error_code']}] {result['message']}")
    task_ids.append(result["task_id"])
    # ✅ 每个 prefab 立即用 place_assets_in_scene 放置，再继续提交下一个
    # 不要 poll！

# END RESPONSE TURN — 每个 task 各自发 bg_task_done 通知
```

> ⚠️ **同时不超过 3 个**——超过会被拒绝。

## 故障排查

### Skill 独有问题

> 通用故障（配置缺失 / 任务卡住 / 状态异常 / 未登录）见 [generator-async-pattern §10](../../experience/templates/generator-async-pattern.md#10-通用故障排查)。

| 问题 | 原因 | 解决 |
|---|---|---|
| 状态停在 `generating` 但 `progress=100` | backend 完成，本地仍在下载/导入 | 等 1–3 分钟，**不要重新提交**；只信 `status == "completed"` |
| Unity 编辑器 TJGenerators 面板一直显示 `Processing...` / 灰色 Cube 占位图 | TJGenerators UPM 包的面板 UI 没刷新（独立 bug） | **以 `<bg_task_done>` 通知和 Prefab 实际内容为准**；面板 UI 不影响生成是否完成，可忽略 |
| 动画不播 | FBX Animation Type 不是 Humanoid，或 Animator 未绑定 | 检查 FBX 导入设置；正常情况下 `place_assets_in_scene` 已自动绑定 |
| 角色卡在 T-pose | Idle 默认状态没设 / Speed 参数没在驱动 | 检查 AnimatorController 默认 state；按"控制动画播放"节方案设置 |
| `execute_csharp_script` 报 `CS0246: AnimatorController not found` | 缺 `using UnityEditor.Animations;` | 见"验证 AnimatorController"小节 |
| `execute_csharp_script` 报 `CS1061: RuntimeAnimatorController.parameters` | 没把 `runtimeAnimatorController` 强转为 `AnimatorController` | 见"验证 AnimatorController"小节 |
| 同时提交 4+ 任务被拒 | 并发上限 3 | 排队等已有任务完成 |
| `prompt` 报错"too long" | 超 600 字符 | 精简描述 |
| 角色不是人形（漂浮、四足） | 用错 skill | 改用 `generate_3d_model` |

### Domain reload 后 task 丢失

通用恢复流程见 [generator-async-pattern §6](../../experience/templates/generator-async-pattern.md#6-domain-reload-recovery)。本 skill 完成态判定：

- Prefab 内 Capsule 子节点 → 仍是占位
- Prefab 内已绑定真实 model 子节点 + Animator 已绑 controller → 已生成完成
- `Assets/.../<name>_model/*.fbx` 多个 FBX 文件存在 → 已生成完成

可用 `glob("Assets/**/*_model/*.fbx")` 找已完成任务。

---

**Task ID Format**：`animated_character_{counter}_{timestamp}`

**Notes**：
- 长任务（3–10 分钟，含本地下载导入）
- backend 任务由 server 端管理；Unity 端 domain reload 不影响 server 任务
- 任务状态持久化到 `Library/AI.TJGenerators/InterruptedTasks.json`
- 自动应用 `TuanjieAI` 标签
- **并发上限 3**——超过会被拒绝
- 需 Unity Editor 在线运行；消耗 AI 服务额度
