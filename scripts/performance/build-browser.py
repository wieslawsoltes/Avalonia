#!/usr/bin/env python3
"""Publish the identical browser harness against exact baseline/head revisions."""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile

BASE = "a9429a328057befa287ffb5e981f58b86a86eda0"
HARNESS = Path("samples/FerroUi.Browser.Performance")


def run(command, cwd):
    subprocess.run(command, cwd=cwd, check=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--aot", action="store_true")
    parser.add_argument("--symbols", action="store_true")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    head = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=root, text=True).strip()
    remote = subprocess.check_output(["git", "remote", "get-url", "origin"], cwd=root, text=True).strip()
    run(["git", "diff", "--exit-code", "HEAD", "--", "src", "build", "samples", "Directory.Build.props", "Directory.Build.targets"], root)
    sources = {str(p.relative_to(root)): hashlib.sha256(p.read_bytes()).hexdigest()
               for p in (root / HARNESS).rglob("*") if p.is_file() and not {"obj", "bin"}.intersection(p.parts)}
    with tempfile.TemporaryDirectory(prefix="avalonia-browser-base-") as directory:
        base = Path(directory)
        run(["git", "init", "--quiet"], base)
        run(["git", "remote", "add", "origin", remote], base)
        run(["git", "fetch", "--depth=1", "origin", BASE], base)
        run(["git", "checkout", "--detach", BASE], base)
        run(["git", "submodule", "update", "--init", "--recursive"], base)
        shutil.copytree(root / HARNESS, base / HARNESS, ignore=shutil.ignore_patterns("bin", "obj"))
        for label, checkout in (("base", base), ("head", root)):
            run(["dotnet", "publish", str(HARNESS / "FerroUi.Browser.Performance.csproj"), "-c", "Release",
                 "-p:AvsSkipBuildingLegacyTargetFrameworks=True",
                 f"-p:FerroUiBrowserAot={str(args.aot).lower()}",
                 f"-p:FerroUiBrowserSymbols={str(args.symbols).lower()}"], checkout)
            candidates = list((checkout / HARNESS / "bin" / "Release").glob("**/publish/wwwroot/index.html"))
            if len(candidates) != 1:
                raise RuntimeError(f"Expected one published browser wwwroot, found {candidates}")
            shutil.copytree(candidates[0].parent, output / label, dirs_exist_ok=True)
    (output / "provenance.json").write_text(json.dumps({"base": BASE, "head": head, "aot": args.aot,
        "symbols": args.symbols, "harness_sha256": sources}, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
