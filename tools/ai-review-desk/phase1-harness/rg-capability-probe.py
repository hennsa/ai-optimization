"""Explicit, no-account semantic probe for the inspected Windows CLI 1.0.91 build.

Requires Python 3.12+ and Node. Never executes a model or reads a saved profile.
The hash pins extraction offsets, not certificate trust. Only synthetic data is used.
"""
import argparse
import hashlib
import os
from pathlib import Path
import shutil
import stat
import subprocess
import tarfile
import tempfile
import io
import zlib

EXPECTED = "9db6bff0cf719556b0c8bf74488c2fc58adf1421ea2de240417d60ef7b7f529c"


def remove_owned(path):
    # Do not descend into Windows junctions or symlinks.
    if path.is_symlink() or path.is_junction():
        if path.is_dir():
            os.rmdir(path)
        else:
            path.unlink()
    elif path.is_dir():
        for child in path.iterdir():
            remove_owned(child)
        path.rmdir()
    else:
        path.chmod(stat.S_IWRITE)
        path.unlink()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cli", type=Path, required=True, help="Installed copilot.exe (not a wrapper)")
    parser.add_argument("--node", default="node")
    parser.add_argument("--tool", choices=["rg", "grep"], default="rg")
    args = parser.parse_args()
    binary = args.cli.read_bytes()
    if hashlib.sha256(binary).hexdigest() != EXPECTED:
        raise SystemExit("Uninspected CLI build: update the semantic proof before running this harness.")
    root = Path(tempfile.mkdtemp(prefix="ARD-ToolCapability-"))
    try:
        # Exact inspected embedded archive; do not extract arbitrary archive paths.
        archive = zlib.decompress(binary[94167123:], wbits=31)
        with tarfile.open(fileobj=io.BytesIO(archive)) as package:
            for name, destination in [
                ("package/prebuilds/win32-x64/runtime.node", "runtime.node"),
                ("package/prebuilds/win32-x64/cli-native.node", "cli-native.node"),
                ("package/ripgrep/bin/win32-x64/rg.exe", "rg.exe"),
            ]:
                member = package.extractfile(name)
                if member is None:
                    raise RuntimeError("Inspected native runtime is absent")
                (root / destination).write_bytes(member.read())
        shutil.copyfile(Path(__file__).with_name("rg-capability-probe.cjs"), root / "probe.cjs")
        # Exclude tool configuration, provider credentials, extensions and parent PATH overrides.
        environment = {k: os.environ[k] for k in ["SystemRoot", "WINDIR", "TEMP", "TMP", "LOCALAPPDATA"] if k in os.environ}
        environment.update({"PATH": str(root) + os.pathsep + str(Path(os.environ["SystemRoot"]) / "System32"), "COPILOT_AUTO_UPDATE": "false", "USE_TGREP": "false", "USE_BUILTIN_RIPGREP": "true"})
        node = shutil.which(args.node) or args.node
        result = subprocess.run([node, str(root / "probe.cjs"), args.tool], cwd=root, env=environment, timeout=90, check=False)
        return result.returncode
    finally:
        # root is a directly created absolute directory; all descendants are owned.
        remove_owned(root)


if __name__ == "__main__":
    raise SystemExit(main())
