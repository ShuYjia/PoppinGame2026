# UNITY_MCP.md — 修改/测试代码前的 Unity MCP 连接检测规程

> **硬性规则：任何涉及修改、编译、测试 Unity 代码 / 场景 / Prefab 的任务，第一步（在读文件、写文件、跑命令之前）必须先检测 Unity MCP 是否连接。检测结果决定后续工作方式。**
>
> 原因：连接不通时盲改文件 = 改动无法验证，直接违反 `AGENTS.md` 中"无法运行 Unity 验证时，明确说明未验证项"的约定。

---

## 1. 连接信息

| 项 | 值 |
|---|---|
| 服务 | MCPForUnity（Unity 编辑器内置窗口 "MCP for Unity"） |
| 端点 | `http://127.0.0.1:8081/mcp` |
| WorkBuddy 侧注册 | `~/.workbuddy/mcp.json` → `unityMCP`（`type: http`，url 指向上面的端点） |
| 协议 | MCP over HTTP，SSE 响应；会话号在响应头 `mcp-session-id` |

**端口以 Unity 编辑器 "MCP for Unity" 窗口里显示的 HTTP URL 为准**（本项目 "My project" 当前为 8081；2026-10-16 实测 8080 已无人监听）。端口变了要同步更新 `~/.workbuddy/mcp.json` 和本文件。

注意：注册了 ≠ 本会话能用。会话工具列表里有没有 `mcp__unityMCP__*` 工具，取决于连接器管理页是否点了 **Trust**。没暴露时用第 3 节的裸 JSON-RPC 客户端兜底。

---

## 2. 检测流程（按顺序执行，任一步成功即停）

### Step 1 — 看会话工具列表

工具列表里已有 unityMCP 工具（如 `mcp__unityMCP__execute_code`、`manage_scene`、`read_console` 等）→ **已连接**，直接调用。

### Step 2 — 探端口（工具未暴露时）

```bash
curl -s -m 5 -o /dev/null -w "%{http_code}\n" http://127.0.0.1:8081/mcp
```

| 返回 | 含义 | 处置 |
|---|---|---|
| `406` | 服务在线（只是 curl 没带 Accept 头） | 进入 Step 3 握手，之后用裸客户端 |
| `000` / 连接失败 | Unity 没开 / MCPForUnity 没启用 / 端口变了 | **停止一切改动**，按第 4 节处置 |

### Step 3 — 完整握手验证（跑 Play 模式测试前必做）

`initialize` 拿到 `mcp-session-id`，再 `tools/list`，能列出工具才算真通。一键脚本（managed Python 运行）：

```python
# check_unity_mcp.py —— 退出码 0 且输出 count>=1 即连接正常
import json, sys, urllib.request, urllib.error

URL = "http://127.0.0.1:8081/mcp"

def post(payload, sid=None):
    req = urllib.request.Request(URL, data=json.dumps(payload).encode(), method="POST")
    req.add_header("Content-Type", "application/json")
    req.add_header("Accept", "application/json, text/event-stream")
    if sid:
        req.add_header("mcp-session-id", sid)
    try:
        r = urllib.request.urlopen(req, timeout=15)
        return r.status, dict(r.headers), r.read().decode(errors="replace")
    except urllib.error.HTTPError as e:
        return e.code, dict(e.headers), e.read().decode(errors="replace")

def sse(body):
    out = []
    for line in body.splitlines():
        if line.startswith("data: ") and line[6:].strip().startswith("{"):
            try:
                out.append(json.loads(line[6:]))
            except Exception:
                pass
    return out

code, h, _ = post({"jsonrpc": "2.0", "id": 1, "method": "initialize",
                   "params": {"protocolVersion": "2025-03-26", "capabilities": {},
                              "clientInfo": {"name": "wb-check", "version": "1"}}})
sid = h.get("mcp-session-id")
print("init:", code, "sid:", sid)
if code != 200 or not sid:
    sys.exit(1)
post({"jsonrpc": "2.0", "method": "notifications/initialized"}, sid)
code2, _, b2 = post({"jsonrpc": "2.0", "id": 2, "method": "tools/list"}, sid)
tools = [t["name"] for m in sse(b2) for t in m.get("result", {}).get("tools", [])]
print("tools/list:", code2, "count:", len(tools))
print("tools:", ", ".join(sorted(tools)))
sys.exit(0 if code2 == 200 and tools else 1)
```

实测基线（2026-10-16）：`init: 200` / `tools/list: 200` / **49 个工具**，含
`execute_code, manage_scene, manage_editor, read_console, refresh_unity, manage_script, run_tests, unity_reflect, manage_asset, manage_gameobject, manage_components, manage_prefabs, manage_ui, manage_shader, manage_material, find_gameobjects, get_sha, get_test_job` 等。

---

## 3. 连接状态 → 工作方式决策表

| 检测结果 | 工作方式 |
|---|---|
| 会话内有 unityMCP 工具 | 直接调用工具 |
| 406 + 握手成功，但工具未暴露 | 用裸 JSON-RPC 客户端直连（规则见下） |
| `000` / 握手失败 | **先不写文件**：报告检测结果、引导检查，并按第 4 节询问用户是否在无 MCP 情况下继续；经用户明确确认后才允许离线修改 |

### 裸 JSON-RPC 客户端调用规则（踩过的坑，必须遵守）

1. 每个请求都要带 `Accept: application/json, text/event-stream`；响应是 SSE，正文在 `data: {...}` 行，按行剥前缀再 `json.loads`。
2. 除 `initialize` 外，所有请求必须带响应头 `mcp-session-id` 里的会话号，否则报 `Missing session ID`。
3. `initialize` 之后、发其他请求之前，先发 `notifications/initialized`。
4. **同一会话禁止并发发请求**（`resources/read` 与 `tools/call` 同时发会挂住约 2 分钟不返回）。一次一发，严格串行。
5. 会话过期的表现是 **HTTP 404**（不是空响应）→ 删掉旧会话号，重新 `initialize` 再重试一次。
6. 域重载会打断会话，MCPForUnity 有重连兜底（结果里带 `recovered_from_disconnect: true`）→ 调用失败原样重试一次。
7. **路径坑**：Git Bash 的 `/tmp` 与 Windows Python 眼中的 `/tmp`（解析为 `C:\tmp`）不是同一个目录。会话号文件和 `.cs` 脚本一律用显式 Windows 路径（如 `C:/Users/garfi/AppData/Local/Temp/...`）。
8. 调用前先 `tools/list` 核对 schema：服务器版本会变（2026-10-16 实测 mcp-for-unity-server 3.4.8：`refresh_unity` 无 `action` 包装；`read_console` 过滤用 `types` 数组而非 `log_type`）。

---

## 4. 连不上时怎么办

1. **不要盲改文件。** 先向用户报告检测结果（失败步骤、返回码），再引导依次检查：
   - Unity 编辑器是否开着、项目是否就是本仓库（Poppin）；
   - 编辑器里 **MCP for Unity** 窗口是否打开，HTTP URL 端口是否仍是 8081；
   - 需要时点窗口里的 Refresh / 重新启用。
2. 端口若变了：以编辑器窗口显示的 URL 为准，同步更新 `~/.workbuddy/mcp.json` 中 `unityMCP.url` 与本文件第 1 节。
3. 想在本会话直接使用工具（而非裸客户端）：连接器管理页 → 对 `unityMCP` 点 **Trust**。不 Trust 也能干活，只是走第 3 节兜底通道。
4. **询问用户是否要在没有 MCP 的情况下修改**（二选一，必须拿到明确答复后才能动手）：
   - **继续（无 MCP 修改）**：允许离线改动，但本次交付必须按 `AGENTS.md` 逐条标注"未验证项"（哪些编译 / Console / Play 验证没做）；改动只限本任务文件；离线改动**不得作为验收 checkpoint 提交**，确需存盘只能用 `WIP:` 前缀且不得推送远程。
   - **暂停**：停在只读检查状态，等工作区恢复 MCP 连接后再继续；不写任何项目文件。

---

## 5. 修改与测试代码时的固定检查点

### 改 `.cs` 脚本

1. 先完成第 2 节连接检测。
2. 改文件 → 调 `refresh_unity`（`mode=force, scope=all, compile=request, wait_for_ready=true`）。
3. `read_console` 查编译错误（过滤参数用 `types` 数组）。
4. `unity_reflect`（`action=search`）搜新类型名，**count=1 才算编译真的生效**——只看 console 会误判，refresh 可能清空 console。
5. 新写的 `.cs` 不会自动编译进域，第 2 步的 `refresh_unity` 不能省。

### 改场景 / Prefab / 资产

1. 先完成第 2 节连接检测。
2. 动用户场景前，用 `execute_code` 检查 `SceneManager.GetActiveScene().isDirty`，**dirty 就中止并报告**，不要 `OpenScene(Single)` 直接覆盖。
3. 改动前后各跑一次 `git status --short`，确认改动范围是外科级的（没有误改用户文件）。

### Play 模式运行期测试

- 每次 MCP 调用是一次往返，**两次调用之间 Unity 的 Update 循环照常跑**——用 Python 侧 `sleep` 当"等待若干帧"。
- 标准序列：
  1. `execute_code`：dirty 检查 → `OpenScene(..., Single)` → `EditorApplication.isPlaying = true`；
  2. Python `sleep 12~15`（域重载 + 场景初始化）；
  3. `execute_code`：读 `Application.isPlaying`、`FindObjectOfType<...>()` 确认就绪；
  4. `execute_code`：驱动逻辑（启动服务、改状态、发请求）；
  5. Python `sleep` 让逻辑跑几帧；
  6. `execute_code`：读回状态/结果做断言；
  7. `execute_code`：收尾 → `EditorApplication.isPlaying = false`；
  8. `read_console` 复查运行期 error。
- **不要在同一脚本里"改状态 + 立刻断言"**：状态变化通常要一帧才被订阅者/轮询者观察到，要么拆成两次调用中间 sleep，要么把断言放到下一次调用。

### 交付前

- `git status --short` 确认外科级改动；`git diff <关键文件>` 确认是纯增量。
- 在回复里列出验证证据（`execute_code` 返回值、console 错误数、`unity_reflect` count 等），不允许只说"改完了"。

---

## 6. 更多细节

- 完整工作流（资产批建、`SerializedObject` 私有字段接线、`execute_code` 方法体语法约束、Unity 2021.3 + NGO 1.5.2 实测坑位清单）见用户级 skill：
  `~/.workbuddy/skills/unity-mcp-asset-and-playmode-validation/SKILL.md`（含可直接复用的 `scripts/mcp_client.py`，支持 `UNITY_MCP_PORT` / `UNITY_MCP_URL` 环境变量覆盖）。
- `execute_code` 的 code 是**方法体**：不能写 `using`、不能声明类（可用局部函数），必须有 `return`；`compiler` 传 `roslyn`，不可用时退 `codedom`（CodeDom 不支持局部函数）。
