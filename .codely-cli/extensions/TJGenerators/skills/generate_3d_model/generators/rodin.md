# Rodin Gen-2.5 生成器文档

generator_id: `rodin`  
适用场景：高精度 / hero 资产 / PBR 材质 / **FBX** 输出 / 高质量 3D 模型  
默认 tier: `Gen-2.5-Extreme-High`（极高精度）

---

## 何时选择 Rodin Gen-2.5

- 高精度 hero 资产（武器主角、场景标志物）
- 需要 PBR 材质纹理（`material: "PBR"`）
- 需要 FBX 格式输出（含材质与模型）
- 需要精细控制质量等级（`tier` 从 Extreme-Low 到 Extreme-High）
- 需要四边形网格（`mesh_mode: "Quad"`）

---

## 工具

### `generate_3d_model_by_rodin`

启动 Rodin Gen-2.5 生成任务。

**参数：**

| 参数 | 类型 | 必填 | 默认 | 说明 |
|------|------|------|------|------|
| `prompt` | string | 无图时必填 | — | 文本描述（≤1000字符） |
| `image_path` | string | 无提示词时必填 | — | Unity 资产路径（`Assets/...`）或绝对路径 |
| `prefab_output_path` | string | 否 | 自动生成 | 输出 prefab 路径（`.prefab` 自动添加） |
| `force_overwrite` | bool | 否 | false | 覆盖同路径已有 prefab |
| `tier` | string | 否 | `Gen-2.5-Extreme-High` | 模型层级（见下表） |
| `quality` | string | 否 | `medium` | 质量等级：`extra-low`/`low`/`medium`/`high` |
| `material` | string | 否 | `PBR` | 材质类型：`PBR`/`Shaded` |
| `mesh_mode` | string | 否 | `Quad` | 网格模式：`Quad`（四边形）/`Raw`（三角形） |
| `ta_pose` | bool | 否 | false | 生成 T/A 姿势的模型 |
| `geometry_format` | string | 否 | `fbx` | 输出格式 |
| `session_id` | string | 否 | — | 为占位符 prefab 添加 Session 标签 |

**tier 选项：**

| 值 | 描述 |
|----|------|
| `Gen-2.5-Extreme-Low` | 超低（最快，适合快速预览） |
| `Gen-2.5-Low` | 低（快，适合简单物体） |
| `Gen-2.5-Medium` | 中（平衡） |
| `Gen-2.5-High` | 高（精细） |
| `Gen-2.5-Extreme-High` | 极高（**默认**，最精细，适合 hero 资产） |

**返回（成功）：**

```json
{
  "success": true,
  "task_id": "static_model_1_...",
  "generator_id": "rodin",
  "prompt": "wooden chair",
  "prefab_output_path": "Assets/TJGenerators/History/Model3D.prefab",
  "estimated_wait_seconds": 600,
  "notification_mode": "bg_task_done"
}
```

**返回（失败）：**

```json
{ "success": false, "error_code": "AUTH_REQUIRED", "message": "Not logged in..." }
```

调用前检查 `result["success"]`。若 `false`，立即上报错误，**不要**继续轮询。

---

### `query_3d_model_status_by_rodin`

查询任务状态（**fallback only，仅一次**——见 [generator-async-pattern §2](../../../experience/templates/generator-async-pattern.md#2--polling-is-strictly-forbidden)）。

**参数：**

| 参数 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `task_id` | string | 是 | `generate_3d_model_by_rodin` 返回的 `task_id` |

**状态值：**

| Status | 含义 |
|--------|------|
| `initializing` | 任务已创建，等待后端任务 ID |
| `generating` | 后端生成中（进度 0–100%） |
| `recovering` | domain reload 后自动恢复中——**等 `<bg_task_done>` 通知**,不要重复 query |
| `completed` | 完成，模型已下载并绑定到 prefab |
| `failed` | 生成失败（查看 `error` 字段） |
| `interrupted` | domain reload 后丢失后端记录，需重新生成 |

> `interrupted` 处理：用 `generate_3d_model_by_rodin` 加 `force_overwrite=true` 和相同 `prefab_output_path` 重新生成。

**返回（完成）：**

```json
{
  "success": true,
  "task_id": "static_model_1_...",
  "status": "completed",
  "progress": 100,
  "prefab_path": "Assets/TJGenerators/History/Model3D.prefab",
  "model_path": "Assets/TJGenerators/History/Model3D_model/Model3D_model.fbx",
  "preview_url": "https://example.com/preview.png",
  "result_summary": "Generation completed. Model: Assets/...fbx. Prefab: Assets/...prefab.",
  "end_time": "2026-03-13 10:35:22",
  "duration_seconds": 487
}
```

> **`preview_url`** — 渲染预览缩略图 URL，需直接展示给用户预览生成效果，可能为空。

**返回（进行中）：**

> ⚠️ Fallback 查询规则见 [generator-async-pattern §3](../../../experience/templates/generator-async-pattern.md#3-fallback-超时表)：长任务 300 秒后才允许调用，仅一次。

```json
{
  "success": true,
  "status": "generating",
  "progress": 45
}
```

---

### `list_3d_model_tasks_by_rodin`

列出当前 Unity Editor session 内的所有 Rodin Gen-2.5 任务。

**参数：** 无

---

## 输入模式

| 模式 | 参数 | 适用场景 |
|------|------|---------|
| 文生3D | `prompt` | 文字描述生成 |
| 图生3D | `image_path` | 从参考图生成 |
| 文+图 | `prompt` + `image_path` | 带文字指导的参考图生成 |

---

## quality 参考值

| 值 | 描述 |
|----|------|
| `extra-low` | 超低质量（最快） |
| `low` | 低质量 |
| `medium` | 中等（默认，平衡） |
| `high` | 高质量（更精细纹理） |

---

## Domain Reload 与通知

通用 domain reload 恢复流程与异步纪律见 [generator-async-pattern](../../../experience/templates/generator-async-pattern.md)：

- 任务状态持久化到 `Library/AI.TJGenerators/InterruptedTasks.json`
- reload 后状态短暂显示 `recovering`，C# host 会自动恢复并重发 `<bg_task_done>` 通知
- **`interrupted` 状态独有处理**：用 `generate_3d_model_by_rodin` + `force_overwrite=true` + 相同 `prefab_output_path` 重新提交（Tripo P1 一般不会出 `interrupted`）
- 写代码时使用 `execute_csharp_script`，**不要**把 `.cs` 文件写入磁盘（会触发不必要的 domain reload）
