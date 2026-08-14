#!/usr/bin/env python3
"""Install .NET SDK into the Codely CLI user bin directory."""

from __future__ import annotations

import argparse
import json
import os
import platform
import shutil
import stat
import subprocess
import sys
import tarfile
import tempfile
import urllib.error
import urllib.request
import zipfile
from pathlib import Path

DEFAULT_SDK_VERSION = "8.0.404"
CODELY_CLI_DIR = ".codely-cli"
MARKER_FILENAME = ".codely-dotnet-install.json"
DOWNLOAD_BASE = "https://builds.dotnet.microsoft.com/dotnet/Sdk"


def codely_cli_home() -> Path:
    override = os.environ.get("CODELY_CLI_HOME", "").strip()
    if override:
        return Path(override).expanduser().resolve()
    return (Path.home() / CODELY_CLI_DIR).resolve()


def default_install_dir() -> Path:
    override = os.environ.get("CODELY_DOTNET_HOME", "").strip()
    if override:
        return Path(override).expanduser().resolve()
    return codely_cli_home() / "tmp" / "bin" / "dotnet"


def dotnet_executable(install_dir: Path) -> Path:
    name = "dotnet.exe" if platform.system().lower() == "windows" else "dotnet"
    return install_dir / name


def platform_rid() -> str:
    system = platform.system().lower()
    machine = platform.machine().lower()

    if system == "darwin":
        arch = "arm64" if machine in {"arm64", "aarch64"} else "x64"
        return f"osx-{arch}"
    if system == "linux":
        if machine in {"arm64", "aarch64"}:
            return "linux-arm64"
        if machine in {"arm", "armv7l"}:
            return "linux-arm"
        return "linux-x64"
    if system == "windows":
        arch = "arm64" if machine in {"arm64", "aarch64"} else "x64"
        return f"win-{arch}"

    raise RuntimeError(f"Unsupported platform: {system} {machine}")


def download_artifact(version: str, rid: str) -> tuple[str, str]:
    if rid.startswith("win-"):
        ext = "zip"
    else:
        ext = "tar.gz"
    filename = f"dotnet-sdk-{version}-{rid}.{ext}"
    url = f"{DOWNLOAD_BASE}/{version}/{filename}"
    return url, ext


def download_file(url: str, dest: Path) -> None:
    dest.parent.mkdir(parents=True, exist_ok=True)
    request = urllib.request.Request(url, headers={"User-Agent": "codely-install-codely-dotnet/1.0"})
    with urllib.request.urlopen(request, timeout=120) as response, dest.open("wb") as out:
        shutil.copyfileobj(response, out)


def extract_archive(archive: Path, install_dir: Path, ext: str) -> None:
    install_dir.mkdir(parents=True, exist_ok=True)
    if ext == "zip":
        with zipfile.ZipFile(archive) as zf:
            zf.extractall(install_dir)
        return

    with tarfile.open(archive, "r:gz") as tf:
        tf.extractall(install_dir)


def ensure_executable(path: Path) -> None:
    if not path.exists() or platform.system().lower() == "windows":
        return
    mode = path.stat().st_mode
    path.chmod(mode | stat.S_IXUSR | stat.S_IXGRP | stat.S_IXOTH)


def verify_dotnet(install_dir: Path) -> str:
    exe = dotnet_executable(install_dir)
    if not exe.exists():
        raise RuntimeError(f"dotnet executable not found at {exe}")

    ensure_executable(exe)
    result = subprocess.run(
        [str(exe), "--version"],
        capture_output=True,
        text=True,
        timeout=30,
        check=False,
    )
    if result.returncode != 0:
        detail = (result.stderr or result.stdout or "").strip()
        raise RuntimeError(f"dotnet --version failed: {detail}")

    return result.stdout.strip()


def read_marker(install_dir: Path) -> dict | None:
    marker = install_dir / MARKER_FILENAME
    if not marker.exists():
        return None
    try:
        return json.loads(marker.read_text(encoding="utf-8"))
    except json.JSONDecodeError:
        return None


def write_marker(install_dir: Path, version: str, rid: str) -> None:
    marker = install_dir / MARKER_FILENAME
    marker.write_text(
        json.dumps(
            {
                "sdkVersion": version,
                "rid": rid,
                "installDir": str(install_dir),
            },
            indent=2,
        )
        + "\n",
        encoding="utf-8",
    )


def install_sdk(install_dir: Path, version: str, force: bool) -> None:
    rid = platform_rid()
    marker = read_marker(install_dir)
    exe = dotnet_executable(install_dir)

    if not force and marker and marker.get("sdkVersion") == version and exe.exists():
        installed_version = verify_dotnet(install_dir)
        print(f"OK: dotnet {installed_version} already installed at {install_dir}")
        return

    url, ext = download_artifact(version, rid)
    print(f"Downloading .NET SDK {version} ({rid})...")
    print(f"Source: {url}")

    with tempfile.TemporaryDirectory(prefix="codely-dotnet-") as tmp:
        archive = Path(tmp) / f"dotnet-sdk.{ext}"
        try:
            download_file(url, archive)
        except urllib.error.HTTPError as exc:
            raise RuntimeError(f"Download failed ({exc.code}): {url}") from exc
        except urllib.error.URLError as exc:
            raise RuntimeError(f"Download failed: {exc.reason}") from exc

        if install_dir.exists() and force:
            shutil.rmtree(install_dir)
        install_dir.mkdir(parents=True, exist_ok=True)
        extract_archive(archive, install_dir, ext)

    installed_version = verify_dotnet(install_dir)
    write_marker(install_dir, version, rid)
    print(f"OK: installed dotnet SDK {installed_version} to {install_dir}")
    print(f"Executable: {exe}")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Install .NET SDK for codely-unity-lsp-server")
    parser.add_argument(
        "--install-dir",
        default=str(default_install_dir()),
        help="Target directory (default: ~/.codely-cli/tmp/bin/dotnet)",
    )
    parser.add_argument(
        "--version",
        default=DEFAULT_SDK_VERSION,
        help=f".NET SDK version (default: {DEFAULT_SDK_VERSION})",
    )
    parser.add_argument(
        "--force",
        action="store_true",
        help="Re-download even if already installed",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    install_dir = Path(args.install_dir).expanduser().resolve()

    try:
        install_sdk(install_dir, args.version.strip(), args.force)
    except Exception as exc:  # noqa: BLE001 - script CLI boundary
        print(f"ERROR: {exc}", file=sys.stderr)
        return 1

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
