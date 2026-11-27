"""Re-download the unused sample candidates into SampleLibrary/.

SampleLibrary/ is git-ignored. Every file in it is listed in
FMOD/ChordPulse/sample-manifest.json with "location": "SampleLibrary",
its original download URL and SHA-256, so it can be rebuilt at any time:

    python Tools/restore_sample_library.py            # restore missing files
    python Tools/restore_sample_library.py --check    # only report what is missing or wrong

Sources handled:
  - single files on GitHub (download_url points at the file, pinned to a commit)
  - files inside a ZIP pack (zip_sha256 + source_path inside the ZIP)
  - generated files (generator = script that recreates them)
  - manual files (e.g. Freesound needs a login): reported, not downloaded
"""
import argparse
import hashlib
import io
import json
import subprocess
import sys
import urllib.request
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
MANIFEST = ROOT / "FMOD" / "ChordPulse" / "sample-manifest.json"
LIBRARY = ROOT / "SampleLibrary"


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def fetch(url: str) -> bytes:
    req = urllib.request.Request(url, headers={"User-Agent": "sample-restore"})
    with urllib.request.urlopen(req, timeout=120) as r:
        return r.read()


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true", help="report only, download nothing")
    args = ap.parse_args()

    entries = [e for e in json.loads(MANIFEST.read_text(encoding="utf-8"))
               if e.get("location") == "SampleLibrary"]
    todo = []
    for e in entries:
        p = LIBRARY / e["file"]
        if not p.exists() or sha256(p.read_bytes()) != e["sha256"]:
            todo.append(e)
    print(f"{len(entries)} library files in manifest, {len(todo)} missing or changed")
    if args.check or not todo:
        for e in todo:
            print("  missing:", e["file"])
        return 1 if todo else 0

    zips: dict[str, zipfile.ZipFile] = {}
    generators: set[str] = set()
    failed = []
    for e in todo:
        if e.get("generator"):
            generators.add(e["generator"])
            continue
        if e.get("manual_download"):
            failed.append((e["file"], "manual download: " + e["source_url"]))
            continue
        try:
            if e.get("zip_sha256"):
                url = e["download_url"]
                if url not in zips:
                    blob = fetch(url)
                    if sha256(blob) != e["zip_sha256"]:
                        raise ValueError("ZIP hash differs from manifest (pack changed upstream?)")
                    zips[url] = zipfile.ZipFile(io.BytesIO(blob))
                data = zips[url].read(e["source_path"])
            else:
                data = fetch(e["download_url"])
            if sha256(data) != e["sha256"]:
                raise ValueError("file hash differs from manifest")
            out = LIBRARY / e["file"]
            out.parent.mkdir(parents=True, exist_ok=True)
            out.write_bytes(data)
            print("  restored:", e["file"])
        except Exception as ex:  # keep going, report at the end
            failed.append((e["file"], str(ex)))

    for g in sorted(generators):
        print("  running generator:", g)
        subprocess.run([sys.executable, str(ROOT / g)], check=True)

    for f, why in failed:
        print("  FAILED:", f, "-", why)
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
