# GIT_WORKFLOW.md — 版本迭代与回退守则（Git / GitHub）

> **目标：任何一次修改都有锚点、可验证、可退回。**
> 本守则在 `AGENTS.md` 总纲之下运行；与 `AGENTS.md` 冲突时以 `AGENTS.md` 为准。
> 修改/测试 Unity 代码前仍须先按 [`UNITY_MCP.md`](UNITY_MCP.md) 检测 MCP 连接。

---

## 0. 三级回退防线（心智模型）

```
第 3 层  远程（GitHub origin）──── 已推送的历史：只能 revert，不改写
            ▲ push
第 2 层  本地提交（commit）─────── 验证通过的锚点：可 revert / 可整支废弃
            ▲ commit
第 1 层  工作区（working tree）─── 未提交的改动：只能针对"本任务文件"restore
```

- 回退能力逐层增强：工作区只能单文件回退；提交后可以整点回退；推送后即使本机损坏也能从 GitHub 恢复。
- **核心动作：勤提交（第 2 层锚点，常态授权）；推送（第 3 层保险）按用户指令执行（见 4.1）。**

## 1. 任务开始前的基线检查（必做）

```bash
git status --short        # 工作区是否干净；哪些是用户的未提交改动
git branch --show-current # 当前分支
git log --oneline -5      # 最近的锚点在哪
```

- **已有未提交改动一律视为用户财产**：不提交、不覆盖、不清理、不回退，只操作本次任务明确涉及的文件（`AGENTS.md` 原文要求）。
- 若工作区里有与本任务文件冲突的用户改动，先停下来报告，由用户决定先整理还是并行。

## 2. 分支策略

| 分支 | 用途 | 规则 |
|---|---|---|
| `main` | 稳定基线 | **禁止直接提交**。只接受用户当场确认后的合并 |
| `codex/<简短任务名>` | AI 任务分支 | 每个任务一条分支；checkpoint 提交都发生在这里 |
| `hotfix/<问题名>` | 紧急修复 | 从 main 或某个 tag 拉出，修完同样经确认后合并 |

- 任务开始时：`git switch -c codex/<任务名>`（从 main 或用户指定的基点拉出）。
- 长任务期间 main 有更新：优先 `git merge main`（保留真实历史），不 rebase 已推送的分支。
- 分支合并完成后是否删除远程分支：默认**保留**，用户说删才删（历史审计需要）。

## 3. Checkpoint 提交制度（第 2 层锚点）

### 3.1 何时提交

**已确认：每个验证通过的验收点立即提交一次。**

一次"验收点" = `AGENTS.md` 定义的"一个可描述、可验证的需求"：改动完成 → Unity 编译通过 / Console 无错 / Play 验证通过 → diff 审查通过 → 立即 commit。

绝不允许：攒了一堆未验证的改动再一次大提交（出错时无法定位是哪个改动引入的）。

### 3.2 提交前检查清单（全部通过才允许 commit）

1. `git status --short`：改动范围外科级，**只含本任务文件**（不含用户的无关改动、不含生成目录）。
2. `git diff --cached` / `git diff`：逐文件过目，确认纯增量、无误删。
3. Unity 侧验证：脚本改动已过编译 + Console 无 error；场景/Prefab/UI 改动已在 Editor 验证（走 [`UNITY_MCP.md`](UNITY_MCP.md) 流程）。无法验证的在提交说明里写明"未验证项"。
4. Unity 资产的 `.meta` 与资产**同进同出**：新增资产必须连 `.meta` 一起 `git add`；删资产连 `.meta` 一起删。
5. `Library/`、`Temp/`、`Logs/`、`obj/`、`UserSettings/`、`.workbuddy/` 不出现在暂存区（`.gitignore` 已配置，提交前仍须核对）。

### 3.3 Commit message 规范

```
<type>(<scope>): <一句话摘要>

- 验收条件：<这次改动满足的验收条件>
- 验证证据：<编译结果 / Console 错误数 / Play 验证结论 / 未验证项>
```

- `type`：`feat`（新功能）/ `fix`（修错）/ `refactor`（重构）/ `assets`（美术/场景资产）/ `chore`（杂项）/ `docs`（文档）。
- 反面教材（本仓库历史里出现过，禁止再出现）：`"96"`、`"Update Unity project state"` 这类无信息提交。
- 中途必须存盘但尚未验证：允许 `WIP:` 前缀提交，**但 WIP 提交不得推送远程**；验证通过后接着提交正式锚点即可，不回头改历史。

## 4. GitHub 同步（第 3 层保险）

远程：`origin = https://github.com/ShuYjia/PoppinGame2026.git`

### 4.1 推送时机

**已确认：仅手动推送 —— 用户明确要求时才 `git push`，AI 不主动推送。**

影响：第 3 层远程保险按需启用，"本地提交 → 推送"之间存在风险窗口（本机故障时未推送的锚点会丢失）。缓解措施：

- 每个任务结束时，AI 在报告里提醒一次"当前分支有 N 个未推送锚点，是否需要推送备份"；
- 用户任何时候说"推送"即执行 `git push origin codex/<任务名>`；
- 推送前 AI 先检查分支上是否含 `WIP:` 提交，有则提醒用户确认后再推（WIP 原则上不上远程）。

### 4.2 合并 main 与发布

1. 任务分支验收完成 → 向用户报告 diff 摘要与验证证据。
2. **用户当场确认后**才合并 main。
3. **已确认合并方式：`git merge --no-ff codex/<任务名>`** —— main 上留下一个明确的合并节点，整个任务可作为一个整体被 `revert -m 1` 撤回。
4. 合并后立即打里程碑 tag（见第 6 节）；推送 main 与 tag（`git push origin main --tags`）在合并确认时一并授权。
5. 多人协作或需要评审时改用 GitHub Pull Request 流程（同等的"确认后才合并"语义）。

## 5. 回退规程（核心，按场景对号入座）

**总原则：已共享的历史（推送过的）只准 `revert`（新增反向提交），绝不改写；未推送的本地历史也优先 `revert`，保持一致习惯。**

| # | 场景 | 命令 | 说明 |
|---|---|---|---|
| 1 | 改错了，**还没提交**，要丢弃本任务的改动 | `git restore -- <本任务文件>` | 只允许针对本任务明确涉及的文件；**禁止全仓 `restore .`** |
| 2 | 改到一半要切换任务 | `git stash push -m "<任务>:<说明>"` | 回来用 `git stash pop`；stash 不是长期仓库，及时清理 |
| 3 | **刚提交的锚点**发现是错的 | `git revert HEAD` | 生成一个反向提交，历史保留完整审计轨迹 |
| 4 | 撤销**连续多个**提交 | `git revert --no-commit <最旧>^..<最新>` 然后一次提交 | 一次反向提交撤掉一段，比逐个 revert 干净 |
| 5 | 已**推送到远程**的提交要撤 | `git revert <sha>` + `git push` | 公开历史不改写；协作者无感知 |
| 6 | main 上的**合并提交**有问题 | `git revert -m 1 <merge-sha>` + push | 整个任务分支的改动一次性撤回 |
| 7 | 只想**看**某个历史版本的文件 | `git show <sha>:<路径>` | 只读，不动工作区 |
| 8 | 想从某个 tag/提交**拉实验分支** | `git switch -c experiment/<名> <tag>` | 在新分支上试，不动 main |
| 9 | 未推送的锚点提交说明写错了 | `git commit --amend` | **仅限未推送**且属于本任务的提交 |
| 10 | 整个任务方向作废 | 直接丢弃 `codex/<任务名>` 分支（不合并） | 分支即隔离舱，作废成本为零 |

### 5.1 绝对禁令（任何情况下不执行，除非用户逐字明确要求）

- `git reset --hard`、`git clean -fd(x)`、全仓 `git restore .` / `git checkout .`
- `git push --force` / `--force-with-lease`（改写远程历史）
- 对已推送分支做 `git rebase` / 交互式 rebase
- 删除远程 `main` 或他人分支

### 5.2 "错误修改后退回修改前状态"的标准动作

> 用户最常见的需求：**这次修改引入了 bug，退回修改前的状态。**

```
已提交：git revert <那次提交的sha>   → 验证（编译/Play）→ push
未提交：git restore -- <本任务文件>   → 重新验证
```

回退本身也是一次改动，同样要过第 3.2 节的验证清单后才算完成。

## 6. 里程碑 tag 制度

**已确认：每次合并 main 后立即打 tag。**

- 命名：发布版本用 `v0.2.0` 语义化版本；普通里程碑用 `checkpoint/2026-10-16-<任务名>`。
- 打 tag 时附一句说明：`git tag -a <名> -m "<一句话>"`。
- tag 是回退的"快速路标"：`git switch -c hotfix/xxx <tag>` 可秒回任何里程碑。

## 7. Unity 项目 Git 专项

1. `.gitignore` 已按官方 Unity 模板配置（含 `Library/ Temp/ obj/ Logs/ UserSettings/ *.csproj *.sln .workbuddy/` 等），**新增生成目录时同步补充**。
2. `.meta` 与资产同进同出；移动/重命名资产用 `git mv` 或在 Unity 编辑器内操作，避免 GUID 断链。
3. 场景 / Prefab（`.unity` / `.prefab`）是 YAML：
   - 冲突时**不要手改冲突标记**，用 Unity 自带 `UnityYAMLMerge`（`mergetool`）或放弃一方后重做；
   - 同一场景尽量单人单任务修改，从流程上避免冲突。
4. 大二进制（高分辨率贴图、音频、视频、大型 `.unity`）：单文件超过 ~50MB 或仓库明显膨胀时引入 Git LFS 迁移（引入前先在分支上试点，LFS 会改写历史，属"待用户确认"操作）。

## 8. 每个任务的标准闭环（Checklist）

1. ☐ 检测 Unity MCP 连接（[`UNITY_MCP.md`](UNITY_MCP.md)）
2. ☐ 基线检查：`git status` / 分支 / 最近锚点（第 1 节）
3. ☐ 说明验收条件，开/确认 `codex/<任务名>` 分支
4. ☐ 改动 + Unity 验证（编译 / Console / Editor / Play）
5. ☐ 过第 3.2 节提交前检查清单 → checkpoint 提交
6. ☐ （用户要求时才推送）任务结束报告里提醒未推送锚点数量，询问是否需要推送备份
7. ☐ 向用户报告：diff 摘要 + 验证证据 + 未验证项
8. ☐ 用户确认 → 合并 main（`--no-ff`）→ 打 tag → 推送

## 9. 授权边界（与 AGENTS.md 的关系）

- `AGENTS.md` 原文："未经用户明确要求，不创建提交、推送或合并。"
- **本守则经用户确认后，视为对以下动作的常态授权**：`codex/<任务名>` 分支上的 checkpoint 提交（仅提交，不含推送）。
- **以下动作每次仍需用户当场要求/确认**：推送到 origin、合并进 `main`、删远程分支、引入 Git LFS、任何第 5.1 节禁令中的操作。（合并 main 确认时，当次里程碑 tag 与 main 推送随合并一并授权执行。）
