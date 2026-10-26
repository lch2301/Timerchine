#!/usr/bin/env python3
"""Deterministically remove unused GPL interactive Python modules from the pinned AAR.

Preserves Unix symlink metadata required by the upstream Android ZipUtils loader.
No yt-dlp extractor, networking, EJS, cryptography or media-decoding code is removed.
"""
from __future__ import annotations
import copy
import hashlib
import io
from pathlib import Path
import shutil
import sys
import zipfile

EXPECTED_SHA256 = "579b5fb480892b1abc2b218c2089699d52759cc8d7ba256bf876453f0365faef"
FIXED_TIME = (2000, 1, 1, 0, 0, 0)


def excluded(name: str) -> bool:
    base = name.rsplit("/", 1)[-1]
    if name.startswith("usr/lib/") and base.startswith(("libreadline", "libhistory", "libgdbm")):
        return True
    if "/lib-dynload/" in name and base.startswith(("readline.", "_gdbm.", "_dbm.")):
        return True
    # The app does not use yt-dlp tag-writing postprocessors. Mutagen is not an extractor dependency.
    if "/site-packages/mutagen/" in name or name.endswith("/site-packages/mutagen/"):
        return True
    return False


def trim_python(data: bytes) -> bytes:
    result = io.BytesIO()
    with zipfile.ZipFile(io.BytesIO(data)) as source, zipfile.ZipFile(result, "w") as target:
        for original in source.infolist():
            if excluded(original.filename):
                continue
            info = copy.copy(original)
            info.date_time = FIXED_TIME
            target.writestr(info, source.read(original))
    return result.getvalue()


def transform(source_path: Path, destination: Path) -> None:
    destination.parent.mkdir(parents=True, exist_ok=True)
    if source_path.name != "library-0.18.1.aar":
        shutil.copyfile(source_path, destination)
        return
    actual = hashlib.sha256(source_path.read_bytes()).hexdigest()
    if actual != EXPECTED_SHA256:
        raise ValueError(f"The pinned youtubedl-android AAR hash differs: {actual}")
    with zipfile.ZipFile(source_path) as source, zipfile.ZipFile(destination, "w") as target:
        count = 0
        for original in source.infolist():
            info = copy.copy(original)
            info.date_time = FIXED_TIME
            data = source.read(original)
            if original.filename == "res/raw/ytdlp":
                # ExternalMedia installs its separately pinned engine before upstream initialization.
                # Keep the resource ID required by the wrapper, without shipping an unused older engine.
                data = b"Timerchine bundles the pinned engine in assets/media/yt-dlp.bin.\n"
            if original.filename.startswith("jni/") and original.filename.endswith("/libpython.zip.so"):
                data = trim_python(data)
                count += 1
            target.writestr(info, data)
    if count != 4:
        raise ValueError(f"Expected four Python ABI archives, found {count}")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        raise SystemExit("Usage: strip_python_runtime.py input.aar output.aar")
    transform(Path(sys.argv[1]), Path(sys.argv[2]))
