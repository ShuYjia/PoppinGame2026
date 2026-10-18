# AI agent 工作约定

在本仓库开发前，先阅读 [`AI_AGENT_WORKFLOW.md`](AI_AGENT_WORKFLOW.md)，并遵守其中的检查点、验证和回退流程。

修改或测试任何 Unity 代码/场景/Prefab 前，必须先按 [`UNITY_MCP.md`](UNITY_MCP.md) 检测 Unity MCP 连接状态，检测结果决定后续工作方式。

所有改动的提交、推送、合并与回退，必须遵守 [`GIT_WORKFLOW.md`](GIT_WORKFLOW.md)（版本迭代与回退守则）。

- 先检查 `git status` 和相关 diff；已有未提交改动属于用户，禁止覆盖、清理、暂存或回退无关文件。只操作本次任务明确涉及的文件。
- 一次迭代只完成一个可描述、可验证的需求。先说明验收条件，再改动；完成后检查 diff、验证并报告结果。
- 默认在 `codex/<简短任务名>` 分支上开发；不要直接提交到 `main`。未经用户明确要求，不创建提交、推送或合并。
- Unity 资源的 `.meta` 文件必须与资源一起保留和审查；不要提交 `Library/`、`Temp/`、`Logs/`、`obj/` 等生成目录。
- 不用 `reset --hard`、`clean` 或全仓库 `restore` 回退。回退只允许针对已确认属于本任务的文件或提交。
- 修改脚本后检查 Unity 编译和 Console；改动场景、Prefab、动画或 UI 后，在 Editor 中验证并检查视觉/运行结果。无法运行 Unity 验证时，明确说明未验证项。
