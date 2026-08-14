---
name: unity-rigged-animated-model-generation
description: Rig an existing 3D model and/or generate motion animation using AI. Three tools available — (1) generate_rigged_model (rig any FBX/OBJ into a Humanoid skeleton using UniRig AI), (2) generate_model_motion (generate motion animation for an already-rigged Humanoid model using HunyuanMotion), (3) generate_rigged_animated_model (rig + motion in one shot). Use when the user wants to make an existing 3D model animatable, add rigging/skinning, or generate custom motion for a Humanoid character — e.g., "给这个模型绑骨", "rig my character", "让模型动起来", "add a walk animation", "给角色加跑步动作", "make it do a backflip", "绑骨并生成动画". DO NOT use for generating characters from scratch — use generate_animated_character instead.
---

> ⚠️ **执行约束**（**当前无专属子代理**）
> - **主 agent**：无 `execute_custom_tool` 权限。任选其一委托：
>   1. `task(subagent_name="general-purpose", ...)` 把任务委给通用子代理（其有 `execute_custom_tool` 权限）；
>   2. 或临时在 `agents/` 下新增 `rigged-animated-model-generator.toml`（allowed_tools 含 `execute_custom_tool`，allowed_skills 含 `unity-rigged-animated-model-generation`）后委托。
>   不要主 agent 直接 `activate_skill` 后调 `execute_custom_tool`，会报"自定义工具未注册"。
> - **子代理（本文档主要读者）**：有权限，按下方 `execute_custom_tool(...)` 示例执行。

> ⛔ **`place_assets_in_scene` 调用规则**
> - **调用方式**：`activate_skill("unity-place-assets-in-scene")` → 按 §4a Prefab 模板用 `execute_csharp_script` 跑 `PrefabUtility.InstantiatePrefab`（**不是** `execute_custom_tool`，**不要** `unity_gameobject` 放 Prefab）。
> - **子代理**：Tool A / C 提交后**立即调一次**放占位 Prefab（Capsule）；`<bg_task_done>` 后**不再调**（Capsule 自动替换为 rigged 模型，Tool C 还会自动绑定 Animator + Controller）。Tool B 无 placeholder，等通知后**也只调一次**把 `motion_fbx_path` 应用到现有 Humanoid 角色。
> - **主 agent**：报告里的 `rigged_model_path` / `motion_fbx_path` 是"已放置"的证据，不是"请你放置"的指示，**不要再调**。
> - **例外**：用户明确要"再放一个 / 换位置 / 改 scale"时才再次调用。详见 [async-pattern §5.1](../../experience/templates/generator-async-pattern.md#51-place_assets_in_scene-调用规则)。

# Rig / Motion Generation for Existing Models 🦴

为**已有**的 3D 模型加骨骼（rigging）和/或生成动作动画。本 skill 是**路由文档**：根据用户意图决定走 Tool A / B / C，再读对应 `generators/*.md` 子文档拿完整参数。

输入：现有 **FBX / OBJ** 模型（**不是 prompt**）。
输出：
- Tool A：rigged Humanoid FBX + Prefab
- Tool B：motion FBX
- Tool C：rigged Humanoid FBX + motion FBX + AnimatorController + Prefab（一站式）

> **注意区分**：从零生成新角色用 `generate_animated_character`；本地命令行 UniRig（不是云端）用 `fbx-humanoid-auto-rig`。

## 🚦 执行流程（按 Tool 不同，不要跳读外链）

**Tool A（rigging only）/ Tool C（rig + motion）**——有 placeholder：
1. 调对应工具 → 拿 `task_id` + `prefab_output_path`（Capsule 占位）
2. 立即 `place_assets_in_scene`（资产类型 `Prefab`）→ 场景出现 Capsule
3. **END RESPONSE TURN** — 不要 poll、不要 `query_*_status`
4. 下一轮收到 `<bg_task_done>` → 读 `rigged_model_path` /（Tool C 还有）`motion_fbx_path`（Capsule 已原地替换，Tool C 还自动绑 Animator + Controller，**不要再 place**）

**Tool B（motion only）**——无 placeholder：
1. 调 `generate_model_motion`（传现有 rigged 角色路径）→ 拿 `task_id`
2. **跳过** place（没有占位资产）
3. **END RESPONSE TURN**
4. 下一轮收到 `<bg_task_done>` → 读 `motion_fbx_path` → **此时调一次** `place_assets_in_scene`（应用到现有 Humanoid 角色）

**档位**：Tool A 1–3 分钟，Tool B 1–2 分钟，Tool C 2–5 分钟；300 秒内无通知才允许 `query_*_status` 一次。完整 async 规则见 [generator-async-pattern](../../experience/templates/generator-async-pattern.md)。

## ⚠️ Skill 独有约束

1. **三个独立工具，不要混用**——每个工具有自己的提交、查询、列表 3 个调用：
   - Tool A：`generate_rigged_model` / `query_rigged_model_status` / `list_rigged_model_tasks`
   - Tool B：`generate_model_motion` / `query_model_motion_status` / `list_model_motion_tasks`
   - Tool C：`generate_rigged_animated_model` / `query_rigged_animated_model_status` / `list_rigged_animated_model_tasks`
2. **必须先读子文档再调工具**——三个 generator 的输入参数和返回字段差异较大；强行调用前未读子文档会传错参数。
3. **输入是已有模型，不是 prompt**——本 skill 不接受 `prompt` 参数；需要 `source_model_path` 指向 **FBX / OBJ**。从零生成请用 `generate_animated_character`。
4. **Tool B 输入必须是 Humanoid rigged 模型**——HunyuanMotion 只接受已有标准 Humanoid 骨架的 FBX。如果模型未绑骨，必须先用 Tool A 或直接用 Tool C。
5. **Tool C 是复合任务**——内部串行执行 Stage 1（rigging，0–50%）→ Stage 2（motion，50–100%）。Stage 2 可能独立失败（见下文 `rigging_complete_motion_failed` 状态）。
6. **`force_overwrite` 仅 Tool A 和 Tool C 有**——Tool B 没有这个参数；Tool B 的 `interrupted` 只能重新提交。
7. **Tool B 没有占位 Prefab**——提交后只返回 `task_id`，需要等通知。

## 工具选择决策

```
用户对一个已有 3D 模型想要什么？
  ├── 只绑骨（不要动画）           → Tool A: generate_rigged_model
  │                                   读 generators/unirig.md
  ├── 已经是 Humanoid，只要动作    → Tool B: generate_model_motion
  │                                   读 generators/hunyuan-motion.md
  └── 一次完成绑骨 + 动作          → Tool C: generate_rigged_animated_model
                                        读 generators/rig-and-motion.md
```

| 用户意图关键词 | 工具 | 子文档 |
|---|---|---|
| "给模型绑骨" / "rig my character" / "add humanoid skeleton" | **Tool A** | [`unirig.md`](generators/unirig.md) |
| "让 humanoid 动起来" / "add walk animation" / "做个 backflip" / "给角色加跑步动作" | **Tool B** | [`hunyuan-motion.md`](generators/hunyuan-motion.md) |
| "绑骨并生成动画" / "rig + motion" / "一次完成" | **Tool C** | [`rig-and-motion.md`](generators/rig-and-motion.md) |
| "从零生成新角色" | ❌ 用 `generate_animated_character` | — |
| "本地 UniRig 命令行" | ❌ 用 `fbx-humanoid-auto-rig` | — |

## When to Use / NOT to Use

适用：已有静态模型（如 `generate_3d_model` 产出的 Humanoid 形态、或外部导入的 FBX）需要绑骨 / 加动作。

不适用：
- 从零生成新角色 → `generate_animated_character`
- 本地命令行 UniRig 工作流（无网络） → `fbx-humanoid-auto-rig`
- 通用 3D 物件生成 → `generate_3d_model`
- 不是 Humanoid 骨骼（四足、机械臂） → 本 skill 不支持

## `<bg_task_done>` 独有字段

通用字段见模板。本 skill **三个工具的 payload 不同**：

### Tool A（`generate_rigged_model`）

| 字段 | 说明 |
|---|---|
| `pipeline_type` | `"rig_only"` |
| `source_model_path` | 输入模型路径 |
| `rigged_model_path` | 绑骨后的 Humanoid FBX 路径 |
| `prefab_path` | 最终 Prefab 路径 |

### Tool B（`generate_model_motion`）

| 字段 | 说明 |
|---|---|
| `pipeline_type` | `"motion_only"` |
| `source_model_path` | 输入 rigged Humanoid FBX 路径 |
| `motion_fbx_path` | 生成的 motion FBX 路径 |
| `controller_path` | AnimatorController 路径（如有） |

> Tool B **没有 `prefab_path`**——它只产出动画文件，需要绑到现有 GameObject 上。

### Tool C（`generate_rigged_animated_model`）

| 字段 | 说明 |
|---|---|
| `pipeline_type` | `"rig_and_motion"` |
| `source_model_path` | 输入模型路径 |
| `rigged_model_path` | Stage 1 输出的 rigged FBX |
| `motion_fbx_path` | Stage 2 输出的 motion FBX |
| `controller_path` | AnimatorController 路径 |
| `prefab_path` | 最终 Prefab 路径 |

## 状态枚举（三工具共享）

| Status | 适用工具 | 含义 |
|---|---|---|
| `initializing` | A/B/C | 任务已创建，尚未提交后端 |
| `pending` | A/B/C | 已注册到后端，提交中 |
| `rigging` | A/C | UniRig Stage 1（A: 0–100%；C: 0–50%） |
| `rigging_complete` | C | Stage 1 完成，Stage 2 自动启动中 |
| `generating_motion` | B/C | HunyuanMotion 进行（B: 0–100%；C: 50–100%） |
| `completed` | A/B/C | 全部输出就绪 |
| `failed` | A/B/C | 失败（A: rigging 失败；B: motion 失败；C: Stage 1 失败） |
| **`rigging_complete_motion_failed`** | **C 独有** | **Stage 1 成功但 Stage 2 失败** |
| `recovering` | A/B/C | domain reload 后自动恢复中 |
| `interrupted` | A/B/C | 后端记录丢失，需重新提交 |

> **`rigging_complete_motion_failed` 是 Tool C 独有**——这种状态下 `rigged_model_path` 已就绪可用，只需用 Tool B 单独重试 motion。见下文恢复策略。

## Domain reload / 失败恢复

通用 domain reload 恢复见 [generator-async-pattern §6](../../experience/templates/generator-async-pattern.md#6-domain-reload-recovery)。本 skill 独有的失败恢复策略：

| 状态 | 工具 | 恢复方式 |
|---|---|---|
| `interrupted` | A | 用相同参数 + `force_overwrite=true` 重新提交 |
| `interrupted` | B | 用相同参数重新提交（Tool B 无 `force_overwrite`） |
| `interrupted` | C，且 `rigged_stage_completed: true` | **Stage 1 已完成**——直接用 `generate_model_motion`（Tool B）拿 `rigged_model_path` 跑动作即可，跳过重新绑骨 |
| `interrupted` | C，且 `rigged_stage_completed: false` | 用 `force_overwrite=true` 重跑整条管线 |
| `rigging_complete_motion_failed` | C | 同上：Stage 1 成果可复用，调 Tool B 单独重试 motion |
| `failed` | A/B/C | 看 `error` 字段；Tool C 看 `error` 是 Stage 1 还是 Stage 2 |

## 工具速查

> 完整参数表与响应见各 generator 子文档。

### Tool A：`generate_rigged_model`（详见 [unirig.md](generators/unirig.md)）

```python
execute_custom_tool(
  tool_name="generate_rigged_model",
  parameters={
    "source_model_path": "Assets/Models/character.fbx",  # 必填：源模型
    "prefab_output_path": "Assets/Characters/Rigged",    # 可选：Prefab 输出
    "force_overwrite": False,
    # 完整参数读 generators/unirig.md
  }
)
```

### Tool B：`generate_model_motion`（详见 [hunyuan-motion.md](generators/hunyuan-motion.md)）

```python
execute_custom_tool(
  tool_name="generate_model_motion",
  parameters={
    "source_model_path": "Assets/Models/rigged_humanoid.fbx",  # 必填：已绑骨的 Humanoid
    "motion_prompt": "walking forward",                         # 或 action_id（见子文档）
    # 完整参数读 generators/hunyuan-motion.md
  }
)
# ⚠️ 没有 force_overwrite 参数
```

### Tool C：`generate_rigged_animated_model`（详见 [rig-and-motion.md](generators/rig-and-motion.md)）

```python
execute_custom_tool(
  tool_name="generate_rigged_animated_model",
  parameters={
    "source_model_path": "Assets/Models/character.fbx",  # 必填：未绑骨的源模型
    "motion_prompt": "running forward",                   # 或 action_id
    "prefab_output_path": "Assets/Characters/Animated",
    "force_overwrite": False,
    # 完整参数读 generators/rig-and-motion.md
  }
)
```

## 放入场景

### Tool A / Tool C

资产类型 **`Prefab`**，路径用 `prefab_output_path`（占位是 Capsule）。生成完成后 Capsule 被替换为 rigged 模型；Tool C 还会自动绑定 Animator + Controller。规则见 [async-pattern §5 / §5.1](../../experience/templates/generator-async-pattern.md#5-placeholder-工作流适用于会返回-placeholder_path--prefab_output_path-的工具)。

### Tool B（无占位）

提交后**没有可立即放置的资产**，必须等通知拿到 `motion_fbx_path` 后再处理：把 `motion_fbx_path` 作为 AnimationClip 加到场景中已有 Humanoid 角色的 AnimatorController state 上（通过 `execute_csharp_script`）。

## 使用示例

### Tool A：仅绑骨

```python
result = execute_custom_tool(
    tool_name="generate_rigged_model",
    parameters={
        "source_model_path": "Assets/Models/character.fbx",
        "prefab_output_path": "Assets/Characters/CharacterRigged"
    }
)
if not result.get("success", True):
    raise RuntimeError(f"[{result['error_code']}] {result['message']}")

task_id = result["task_id"]
prefab_output_path = result["prefab_output_path"]

# ✅ 立即用 place_assets_in_scene 把 prefab_output_path 应用为 Prefab（Capsule 占位）
# 然后 END RESPONSE TURN，等 bg_task_done 通知（1–3 分钟）
```

### Tool C：绑骨 + 动作 一次完成

```python
result = execute_custom_tool(
    tool_name="generate_rigged_animated_model",
    parameters={
        "source_model_path": "Assets/Models/character.fbx",
        "motion_prompt": "doing a backflip",
        "prefab_output_path": "Assets/Characters/CharacterAnimated"
    }
)
# 同样立即放 placeholder，再 END RESPONSE TURN（2–5 分钟）
```

### Tool B：给已有 rigged 角色加动作

```python
result = execute_custom_tool(
    tool_name="generate_model_motion",
    parameters={
        "source_model_path": "Assets/Characters/CharacterRigged/character_rigged.fbx",
        "motion_prompt": "walking forward casually"
    }
)
# ⚠️ 没有 placeholder，直接 END RESPONSE TURN，等 bg_task_done（1–2 分钟）
# 通知到达后再把 motion_fbx_path 接到已有 Animator 上
```

### Tool C 失败到 `rigging_complete_motion_failed` 时的恢复

```python
# 假设 Tool C 提交后通知显示 status="rigging_complete_motion_failed"
# rigged_model_path 已在通知里
rigged_path = "Assets/Characters/CharacterAnimated/character_rigged.fbx"

# 用 Tool B 单独重试 motion，跳过昂贵的绑骨阶段
retry = execute_custom_tool(
    tool_name="generate_model_motion",
    parameters={
        "source_model_path": rigged_path,   # 复用 Stage 1 成果
        "motion_prompt": "doing a backflip"
    }
)
```

## 故障排查

### Skill 独有问题

> 通用故障（配置缺失 / 任务卡住 / 状态异常 / 未登录）见 [generator-async-pattern §10](../../experience/templates/generator-async-pattern.md#10-通用故障排查)。

| 问题 | 原因 | 解决 |
|---|---|---|
| Tool B 报错"input model is not Humanoid rigged" | 输入模型未绑骨 | 先用 Tool A 绑骨，或改用 Tool C 一次完成 |
| Tool B 想强制覆盖但没有 `force_overwrite` | Tool B 不支持此参数 | 删掉旧产物或用不同 `output_path` 重新提交 |
| Tool C 的 motion 阶段失败 | Stage 2 出错 | 看通知 status，若是 `rigging_complete_motion_failed`，复用 `rigged_model_path` 调 Tool B 重试 |
| 调用了错误的 query 工具 | 三工具的 query 不通用 | Tool A 用 `query_rigged_model_status`；Tool B 用 `query_model_motion_status`；Tool C 用 `query_rigged_animated_model_status` |
| Tool C 提交后想跳过 rigging | 不支持中途跳 | 想跳过 rigging 就用 Tool B；Tool C 是一站式 |
| 想从零生成新角色 | 用错 skill | 改用 `generate_animated_character` |
| 想用本地 UniRig 命令行 | 用错 skill | 改用 `fbx-humanoid-auto-rig` |

### Domain reload 后 task 丢失

通用恢复流程见 [generator-async-pattern §6](../../experience/templates/generator-async-pattern.md#6-domain-reload-recovery)。本 skill 完成态判定：

- Prefab 内 Capsule 子节点 → 仍是占位
- Prefab 内已绑定真实模型且 Animator 已绑 controller（Tool C） → 已生成完成
- 期望路径不存在 / 是 placeholder → 任务可能丢失，按上文「失败恢复」表处理

可用 `glob` 检查 `rigged_model_path` / `motion_fbx_path` 是否就绪。

---

**Task ID Format**：
- Tool A：`rigged_model_{counter}_{timestamp}`
- Tool B：`model_motion_{counter}_{timestamp}`
- Tool C：`rigged_animated_{counter}_{timestamp}`

**Notes**：
- 长任务（1–5 分钟），需 Unity Editor 一直在线
- 三工具的 task_id 不通用——`query_*` 必须配对
- 自动应用 `TuanjieAI` 标签
- 消耗 AI 服务额度
