#!/usr/bin/env python3
"""Package a locally published Windows x64 app; no upload or installation."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import struct
import zipfile

WINDOWS = Path(__file__).resolve().parents[1]
REPO = WINDOWS.parent
VERSION = "0.1.4-temperature-preview"
SOURCES = {
    "LibreHardwareMonitor.NET.10.zip": "29739c4959b01b348fddad87664066634bcfd4f46e9bf41e4e916c318bcfdb99",
    "LibreHardwareMonitor-0.9.6-source.zip": "28100b87fcd7a9a213c58fabe1174e4f3064975b5ab11799224766993ca1fbfc",
    "PawnIO.Modules-0.1.6.zip": "665b2f7a028f8a317bc35cd6fbf64986115909239019ff2d30d19260d7dd0982",
}


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("publish_dir", nargs="?", type=Path, default=WINDOWS / "artifacts/win-x64")
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    output = args.publish_dir.resolve()
    archive = (args.output or WINDOWS / "artifacts" / f"thermal-kun-windows-{VERSION}-x64.zip").resolve()
    if archive.is_relative_to(output):
        raise SystemExit("Put the ZIP outside the publish directory.")
    exe = (output / "ThermalKun.exe").read_bytes()
    offset = struct.unpack_from("<I", exe, 0x3C)[0]
    machine = struct.unpack_from("<H", exe, offset + 4)[0]
    if exe[offset:offset+4] != b"PE\0\0" or machine != 0x8664:
        raise SystemExit("Expected a Windows x64 PE executable.")
    if b"asInvoker" not in exe or b"requireAdministrator" in exe:
        raise SystemExit("The GUI must request asInvoker execution.")
    if digest(output / "drivers/PawnIO_setup.exe") != "1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032":
        raise SystemExit("Unexpected PawnIO installer.")
    for name, expected in SOURCES.items():
        if digest(REPO / "upstream" / name) != expected:
            raise SystemExit(f"Source/dependency hash mismatch: {name}")
    shutil.copytree(WINDOWS / "third-party-notices", output / "licenses", dirs_exist_ok=True)
    shutil.copytree(WINDOWS / "docs", output / "docs", dirs_exist_ok=True)
    (output / "scripts").mkdir(exist_ok=True)
    shutil.copy2(WINDOWS / "scripts/diagnose.ps1", output / "scripts/diagnose.ps1")
    shutil.copy2(WINDOWS / "docs/START-HERE.ja.md", output / "README.ja.txt")
    sources = output / "sources"
    sources.mkdir(exist_ok=True)
    for name in SOURCES:
        shutil.copy2(REPO / "upstream" / name, sources / name)
    shutil.copy2(WINDOWS / "README.md", sources / "BUILD.md")
    source_files = [p for p in sorted(WINDOWS.rglob("*")) if p.is_file()
                    and not {"bin", "obj", "artifacts", ".git"}.intersection(p.relative_to(WINDOWS).parts)]
    with zipfile.ZipFile(sources / "SOURCE-Windows.zip", "w", zipfile.ZIP_DEFLATED) as source_zip:
        for path in source_files:
            source_zip.write(path, "Windows/" + path.relative_to(WINDOWS).as_posix())
        source_zip.write(REPO / "LICENSE", "LICENSE")
    runtime_config = json.loads((output / "ThermalKun.runtimeconfig.json").read_text(encoding="utf-8"))
    manifest = {
        "product": "thermal kun for Windows", "version": VERSION,
        "rid": "win-x64", "selfContained": True, "executionLevel": "asInvoker",
        "includedFrameworks": runtime_config["runtimeOptions"]["includedFrameworks"],
        "buildProvenance": json.loads((WINDOWS / "docs/BUILD-PROVENANCE.json").read_text(encoding="utf-8")),
        "verification": "See docs/VERIFICATION.md for tested version and limitations.", "files": {},
    }
    for path in sorted(output.rglob("*")):
        if path.is_file() and path.name != "MANIFEST.json":
            manifest["files"][path.relative_to(output).as_posix()] = {"sha256": digest(path), "bytes": path.stat().st_size}
    (output / "MANIFEST.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    archive.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED, compresslevel=5) as package:
        for path in sorted(output.rglob("*")):
            if path.is_file():
                package.write(path, "ThermalKun/" + path.relative_to(output).as_posix())
    with zipfile.ZipFile(archive) as package:
        if package.testzip():
            raise SystemExit("ZIP integrity verification failed.")
    sha = digest(archive)
    archive.with_suffix(archive.suffix + ".sha256").write_text(f"{sha}  {archive.name}\n", encoding="utf-8")
    print(json.dumps({"file": str(archive), "sha256": sha, "bytes": archive.stat().st_size, "files": len(manifest["files"])+1}))


if __name__ == "__main__":
    main()
