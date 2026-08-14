# codely-unity-lsp-server

内置（embedded）Unity C# LSP（stdio）服务，供 Codely `/lsp` 使用。

## 目标

- **不依赖在线下载 LSP server 二进制**：服务端代码就在本项目 `src/`。
- 运行时直接启动本地构建产物：`node dist/server.js`。

## 安装与构建

```bash
cd codely-unity-lsp-server
npm install
npm run build
```

## 本地运行

```bash
npm start
```

```bash
npm ci && npm run build
```

## 正式发布（推荐命令）

**对外发布 / 给用户安装的 zip，请使用：**

```bash
npm run package:runtime
```

说明：会在打包前执行 `dotnet build -c Release`，预编译产物位于 `worker/runtime/release/`；zip 内**不包含** worker 的 `*.cs` / `*.csproj`，体积更小，安装后一般**无需**在目标机再编译 worker。zip 内的 `dist/**/*.js` 会经 **esbuild 压缩（minify）** 后再打入包；仓库里的本地 `dist/` 仍为 `tsc` 明文输出，便于开发与调试。

若你希望 zip 内**自带完整 `node_modules`**（用户解压后不必再跑 `npm ci`），且可接受包含 worker 源码目录，可用：

```bash
npm run package
```

---

## 其它打包命令

- **`npm run package`**：全量（含 `node_modules`）+ 预编译 worker，zip 内含完整 `worker/UnityRoslynWorker` 源码与 `worker/runtime/release/` 等。
- **`npm run package:slim`**：不含 `node_modules`（安装后需在扩展目录执行 `npm ci --omit=dev`）。
- **`npm run package:runtime`**：见上文「正式发布」；依赖与预编译流程与 `package` 一致，仅排除 worker 源码/csproj；**仅在此命令**下 zip 内的 `dist` 为压缩后的 JS（其它 `package` / `package:slim` 仍为明文 `tsc` 产物）。

若曾出现 `~/.codely-cli/extensions/worker/...` 这类错误路径，请更新到已修复 `bootstrap` 路径解析的版本并重新安装扩展。

## 扩展清单

`gemini-extension.json` 已内置 `lspServers` 配置：

- command: `node`
- args: `${extensionPath}/dist/server.js`

即安装扩展后直接使用本地 `dist` 启动 LSP，不需要在线拉取其它 LSP server。

## Unity 工程文件自动生成

当 LSP 初始化到一个 Unity 工程时，如果项目根目录缺少 `.sln` / `.csproj`，服务端会先尝试：

- 通过当前已连接的 Unity/Tuanjie TCP 编辑器实例执行 `execute_csharp_script`，在已打开项目的编辑器内生成工程文件
- 复制捆绑的临时 Editor 脚本到 `Assets/Editor/`，通过 batchmode `-executeMethod` 生成工程文件
- 若仍失败，再回退到 `UnityEditor.SyncVS.SyncSolution`

相关日志会写到工作区的 `.codely/` 目录下。若你想禁用这一步，可设置环境变量 `CODELY_UNITY_LSP_SKIP_INIT_SLN=1`。

## Worker 位置

- 源码：`worker/UnityRoslynWorker/`
- **预编译输出（Release）**：`worker/runtime/release/UnityRoslynWorker.dll`（由 `UnityRoslynWorker.csproj` 的 `OutputPath` 指定，比默认 `bin/Release/net8.0` 更浅）
- 仍兼容旧路径：`worker/UnityRoslynWorker/bin/Release/net8.0/`（若本地仍有旧构建）

## LSP 能力支持测试脚本

新增脚本：`test/test_codely_lsp_support.py`，用于批量检查 `/lsp` 操作在 **worker RPC** 是否被支持（纯 worker 直连，不经过 codely CLI）。

示例：

```bash
python3 test/test_codely_lsp_support.py \
  --project-dir /path/to/your/unity-project
```

可选参数：

- `--cases`：自定义 case 文件（默认 `test/lsp_support_cases.json`）
- `--out-dir`：报告输出目录（默认 `test/lsp-support-results/`）

报告输出：

- `lsp_support_report.json`
- `lsp_support_report.md`
- `logs/*.log`（每条 case 的原始输出）

### Release 打包门禁

`scripts/package-extension.sh` 已接入强制门禁：

- 每次打包都会先运行 `test/test_codely_lsp_support.py`
- 使用仓库内置测试项目：`test/UnityMiniProject`
- 使用门禁用例：`test/lsp_support_cases.json`
- 只有测试全部通过，才会继续生成 zip

快速打包（跳过测试）：

```bash
bash scripts/package-extension.sh --runtime-worker --skip-test
```