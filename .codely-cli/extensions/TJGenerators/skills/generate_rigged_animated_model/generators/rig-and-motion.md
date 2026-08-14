# Rig + Motion Generator

pipeline: `unirig` → `hunyuan-motion`  
Use case: raw (unrigged) model that needs rigging + motion animation in one step

---

## `generate_rigged_animated_model`

Rigs the model with UniRig AI (Stage 1), then generates motion with HunyuanMotion (Stage 2).
The two stages chain automatically.

- For rigging only → `generate_rigged_model` (read `generators/unirig.md`)
- For an already-rigged model that only needs motion → `generate_model_motion` (read `generators/hunyuan-motion.md`)
- To generate a new character from scratch → `generate_animated_character`

**Output:**
- Rigged Humanoid FBX — same directory as source, filename `{baseName}_rigged.fbx`
- Motion FBX — same directory, filename `{baseName}_motion.fbx`
- AnimatorController with a single looping state — filename `{baseName}_rigged_Controller.controller`
- Prefab with Animator assigned (enters Play Mode → animation loops automatically)

**Parameters:**

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `source_model_path` | string | yes | — | Source model Unity path (`Assets/...`; FBX/OBJ) |
| `motion_description` | string | yes | — | Motion description (e.g. `"a running cycle"`; English gives best results) |
| `prefab_output_path` | string | no | auto | Output prefab path (`.prefab` added automatically) |
| `force_overwrite` | bool | no | false | Overwrite an existing prefab at the same path |
| `action_duration` | float | no | 5.0 | Motion clip duration in seconds |
| `cfg_strength` | float | no | 5.0 | Guidance strength — higher = closer to description (recommended: 3–7) |
| `random_seed` | int | no | 0 | Random seed; 0 = server random (different seeds produce different motion styles) |

**action_duration Reference:**

| Value | Best for |
|-------|---------|
| 2–3s | Short snappy actions (jump, turn, punch) |
| 4–6s | Standard loops (run cycle, idle stand) |
| 7–10s | Complex sequences (gymnastics, dance) |

**Submit response (success):**

```json
{
  "success": true,
  "task_id": "rig_and_motion_1_...",
  "backend_task_id": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
  "status": "rigging",
  "generator_id": "unirig",
  "source_model_path": "Assets/Models/MyChar.fbx",
  "motion_description": "a running cycle",
  "prefab_output_path": "Assets/TJGenerators/History/RiggedAnimatedModel_MyChar.prefab",
  "expected_rigged_path": "Assets/Models/MyChar_rigged.fbx",
  "estimated_wait_seconds": 300,
  "notification_mode": "bg_task_done",
  "message": "Stage 1 (rigging) started. Stage 2 (motion) will launch automatically after rigging completes."
}
```

**Submit response (failure):**

```json
{ "success": false, "error_code": "AUTH_REQUIRED", "message": "Not logged in..." }
```

Always check `result["success"]` after calling. If `false`, report the error immediately and **do not** poll.

---

## `query_rigged_animated_model_status`

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `task_id` | string | yes | `task_id` returned by `generate_rigged_animated_model` |

Status values are defined in the shared table in SKILL.md. Progress flow:

```
rigging (0–50%) → rigging_complete → generating_motion (50–100%) → completed
```

**Response (in progress — Stage 2 example):**

```json
{
  "success": true,
  "task_id": "rig_and_motion_1_...",
  "pipeline_type": "rig_and_motion",
  "status": "generating_motion",
  "progress": 65,
  "start_time": "2026-05-07 14:28:52",
  "source_model_path": "Assets/Models/MyChar.fbx",
  "rigged_model_path": "Assets/Models/MyChar_rigged.fbx",
  "prefab_path": "Assets/TJGenerators/History/RiggedAnimatedModel_MyChar.prefab",
  "motion_description": "a running cycle",
  "next_poll_recommended_after_seconds": 15,
  "polling_hint": "Task is 65% complete (generating_motion). Wait 15s before polling again."
}
```

> During Stage 1 (`status: "rigging"`, 0–50%) the response is identical in structure, but
> `rigged_model_path` contains the **expected** destination path — the file does not exist yet.
> `motion_description` is always present from submission onward.
>
> ⚠️ `polling_hint` / `next_poll_recommended_after_seconds` are **server-emitted hints, not instructions to the agent**. Agents must NOT loop on `query_*_status`; wait for `<bg_task_done>`. Fallback query is allowed once after the timeout in [generator-async-pattern §3](../../../experience/templates/generator-async-pattern.md#3-fallback-超时表).

**Response (completed):**

```json
{
  "success": true,
  "task_id": "rig_and_motion_1_...",
  "pipeline_type": "rig_and_motion",
  "status": "completed",
  "progress": 100,
  "start_time": "2026-05-07 14:28:52",
  "source_model_path": "Assets/Models/MyChar.fbx",
  "rigged_model_path": "Assets/Models/MyChar_rigged.fbx",
  "motion_fbx_path": "Assets/Models/MyChar_motion.fbx",
  "controller_path": "Assets/Models/MyChar_rigged_Controller.controller",
  "prefab_path": "Assets/TJGenerators/History/RiggedAnimatedModel_MyChar.prefab",
  "motion_description": "a running cycle",
  "result_summary": "Generation completed: rigged Humanoid FBX, motion FBX, AnimatorController (auto-loops in Play Mode), Prefab with Animator.",
  "end_time": "2026-05-07 14:32:10",
  "duration_seconds": 198
}
```

**Response (Stage 2 failed):**

```json
{
  "success": true,
  "task_id": "rig_and_motion_1_...",
  "pipeline_type": "rig_and_motion",
  "status": "rigging_complete_motion_failed",
  "progress": 50,
  "start_time": "2026-05-07 14:28:52",
  "source_model_path": "Assets/Models/MyChar.fbx",
  "rigged_model_path": "Assets/Models/MyChar_rigged.fbx",
  "prefab_path": "Assets/TJGenerators/History/RiggedAnimatedModel_MyChar.prefab",
  "motion_description": "a running cycle",
  "error": "Motion generation failed: ...",
  "end_time": "2026-05-07 14:30:10",
  "duration_seconds": 78
}
```

Rigging succeeded — `rigged_model_path` is usable. Call `generate_model_motion` separately to retry
the motion step without re-rigging.

**Response (interrupted — Stage 1 completed, rigged FBX on disk):**

```json
{
  "success": true,
  "task_id": "rig_and_motion_1_...",
  "pipeline_type": "rig_and_motion",
  "status": "interrupted",
  "progress": 50,
  "start_time": "2026-05-07 14:25:00",
  "source_model_path": "Assets/Models/MyChar.fbx",
  "rigged_model_path": "Assets/Models/MyChar_rigged.fbx",
  "prefab_path": "Assets/TJGenerators/History/RiggedAnimatedModel_MyChar.prefab",
  "motion_description": "a running cycle",
  "error": "Generation was interrupted (domain reload) and the backend task record was lost. Please re-generate.",
  "end_time": "2026-05-07 14:27:30",
  "duration_seconds": 150,
  "rigged_stage_completed": true,
  "hint": "Stage 1 (rigging) completed — 'Assets/Models/MyChar_rigged.fbx' exists. Call generate_model_motion with this path to skip re-rigging; or re-generate the full pipeline with force_overwrite=true."
}
```

When `rigged_stage_completed: true` is present → call `generate_model_motion` with `rigged_model_path`
to avoid re-rigging.

**Response (interrupted — Stage 1 incomplete):**

```json
{
  "success": true,
  "task_id": "rig_and_motion_1_...",
  "pipeline_type": "rig_and_motion",
  "status": "interrupted",
  "progress": 30,
  "start_time": "2026-05-07 14:25:00",
  "source_model_path": "Assets/Models/MyChar.fbx",
  "rigged_model_path": "Assets/Models/MyChar_rigged.fbx",
  "prefab_path": "Assets/TJGenerators/History/RiggedAnimatedModel_MyChar.prefab",
  "motion_description": "a running cycle",
  "error": "Generation was interrupted (domain reload) and the backend task record was lost. Please re-generate.",
  "end_time": "2026-05-07 14:26:30",
  "duration_seconds": 90,
  "hint": "Re-generate using the same parameters with force_overwrite=true."
}
```

When `rigged_stage_completed` is absent → re-call `generate_rigged_animated_model` with the same
parameters plus `force_overwrite=true` and the same `prefab_output_path`.

---

## `list_rigged_animated_model_tasks`

Lists all rig_and_motion tasks from the current Unity Editor session.

**Parameters:** none

**Response:**

```json
{
  "success": true,
  "tasks": [ /* each item has the same structure as query_rigged_animated_model_status */ ],
  "count": 1
}
```

---

## Notification and Fallback

异步通用纪律见 [generator-async-pattern](../../../experience/templates/generator-async-pattern.md)：等 `<bg_task_done>` 通知，**不要轮询**；fallback `query_rigged_animated_model_status` 仅一次。
