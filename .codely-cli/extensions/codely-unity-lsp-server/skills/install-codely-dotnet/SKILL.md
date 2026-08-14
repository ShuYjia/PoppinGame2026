---
name: install-codely-dotnet
description: Downloads and installs the .NET 8 SDK into the Codely CLI user directory (~/.codely-cli/tmp/bin/dotnet). Use when codely-unity-lsp-server reports missing dotnet runtime, bootstrap fails with dotnet errors, or the user asks to install dotnet for Unity LSP.
---

# Install Codely Dotnet

为 `codely-unity-lsp-server` 在 Codely 用户目录安装 .NET 8 SDK，供 LSP worker 启动与编译使用。

## 何时使用

- LSP 报错「未找到可用的 .NET 8 SDK」
- `~/.codely-cli/tmp/bin/dotnet` 下尚未安装 .NET 8 SDK
- 需要手动预装 dotnet 到 Codely 用户路径

## 安装目标

| 项 | 路径 |
|----|------|
| Codely 用户根目录 | `~/.codely-cli`（可用 `CODELY_CLI_HOME` 覆盖） |
| dotnet 安装目录 | `~/.codely-cli/tmp/bin/dotnet`（可用 `CODELY_DOTNET_HOME` 覆盖） |
| dotnet 可执行文件 | `<安装目录>/dotnet`（Windows 为 `dotnet.exe`） |

## 执行步骤

1. 确认 Python 3 可用：`python3 --version`
2. 从扩展根目录运行安装脚本：

```bash
python3 skills/install-codely-dotnet/scripts/install_dotnet.py
```

3. 验证安装：

```bash
~/.codely-cli/tmp/bin/dotnet/dotnet --info
```

4. 重启 LSP 或重新打开 Unity 工程，让 `codely-unity-lsp-server` 重新 bootstrap。

## 可选参数

```bash
python3 skills/install-codely-dotnet/scripts/install_dotnet.py \
  --install-dir ~/.codely-cli/tmp/bin/dotnet \
  --version 8.0.404
```

- `--install-dir`：dotnet 安装目录（默认 `~/.codely-cli/tmp/bin/dotnet`）
- `--version`：SDK 版本（默认 `8.0.404`，需满足 worker 的 net8.0）
- `--force`：即使已安装也重新下载

## 与 LSP 的集成

`codely-unity-lsp-server` bootstrap 在找不到 dotnet 时会报错并指向本 skill 文档；Codely Agent 应读取本 skill、执行安装脚本后重试 bootstrap。

dotnet 解析顺序（所有来源均须为 .NET 8，`dotnet --version` 主版本为 8）：

1. `CODELY_DOTNET`（显式指定 dotnet 可执行文件）
2. `CODELY_DOTNET_HOME` 或 `~/.codely-cli/tmp/bin/dotnet`
3. 系统 PATH（`which dotnet` / `where dotnet`），仅当版本为 .NET 8 时使用

## 故障排查

- **下载失败**：检查网络，或指定 `--version` 重试
- **无 python3**：安装 Python 3.8+ 后重试
- **权限错误**：确保对 `~/.codely-cli` 有写权限
- **已安装但仍报错**：运行 `dotnet --info` 确认 SDK 8.x 可用
