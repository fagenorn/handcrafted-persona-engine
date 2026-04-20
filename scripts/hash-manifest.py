#!/usr/bin/env python3
"""
Updates install-manifest.json with SHA256 + size for each HuggingFace-sourced
asset by hashing the corresponding file under assets-source/.

Performs a *textual*, line-scoped substitution rather than a JSON round-trip so
the manifest's hand-curated formatting (inline single-line objects, key order,
indentation, trailing newline) survives intact. Only the two lines we own —
"sha256" and "sizeBytes" — change for each HuggingFace asset block.

NVIDIA assets are skipped — their hashes come from NVIDIA's redistrib_*.json
manifests and live as the literal string "FROM_NVIDIA_MANIFEST".
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from pathlib import Path

# Match an asset block opener through the matching closing brace at the same
# indent level. Manifest indentation is 2 spaces per level; assets are at depth
# 2 (4 spaces). Each block starts with `    {` and ends with `    }`.
_BLOCK_RE = re.compile(
    r"^( {4}\{\n)(.*?)^( {4}\},?\n)",
    re.MULTILINE | re.DOTALL,
)
_SHA_RE = re.compile(r'^(\s*"sha256":\s*)"[^"]*"', re.MULTILINE)
_SIZE_RE = re.compile(r'^(\s*"sizeBytes":\s*)\d+', re.MULTILINE)
_ID_RE = re.compile(r'^\s*"id":\s*"([^"]+)"', re.MULTILINE)


def sha256_of(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def main() -> int:
    repo_root = Path(__file__).resolve().parent.parent

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--manifest",
        type=Path,
        default=repo_root
        / "src"
        / "PersonaEngine"
        / "PersonaEngine.Lib"
        / "Assets"
        / "Manifest"
        / "install-manifest.json",
    )
    parser.add_argument(
        "--assets-root",
        type=Path,
        default=repo_root / "assets-source",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Preview the updates without writing the manifest.",
    )
    args = parser.parse_args()

    if not args.manifest.is_file():
        print(f"Manifest not found: {args.manifest}", file=sys.stderr)
        return 2
    if not args.assets_root.is_dir():
        print(f"Assets root not found: {args.assets_root}", file=sys.stderr)
        return 2

    # Parse to identify which assets need updating + compute hashes.
    text = args.manifest.read_text(encoding="utf-8")
    manifest = json.loads(text)

    # id -> (new_sha, new_size). Only HuggingFace entries land here.
    targets: dict[str, tuple[str, int]] = {}
    missing: list[str] = []
    total_bytes = 0

    for asset in manifest["assets"]:
        src = asset.get("source", {})
        if src.get("type") != "HuggingFace":
            continue

        local = args.assets_root / src["path"]
        if not local.is_file():
            missing.append(f"  - {asset['id']} -> {local}")
            continue

        digest = sha256_of(local)
        size = local.stat().st_size
        targets[asset["id"]] = (digest, size)
        total_bytes += size

    if missing:
        print(
            f"\nMissing local files for {len(missing)} HuggingFace asset(s):",
            file=sys.stderr,
        )
        for m in missing:
            print(m, file=sys.stderr)
        print(
            f"\nRefusing to write manifest while {len(missing)} HuggingFace "
            f"asset(s) are missing under {args.assets_root}.",
            file=sys.stderr,
        )
        return 1

    # Walk the file textually and rewrite sha256/sizeBytes inside each
    # HuggingFace asset block, preserving every other byte.
    updated = 0

    def rewrite_block(match: re.Match[str]) -> str:
        nonlocal updated
        head, body, tail = match.group(1), match.group(2), match.group(3)
        id_match = _ID_RE.search(body)
        if not id_match:
            return match.group(0)
        asset_id = id_match.group(1)
        if asset_id not in targets:
            return match.group(0)

        new_sha, new_size = targets[asset_id]
        new_body, n_sha = _SHA_RE.subn(rf'\g<1>"{new_sha}"', body, count=1)
        new_body, n_size = _SIZE_RE.subn(rf"\g<1>{new_size}", new_body, count=1)
        if n_sha != 1 or n_size != 1:
            print(
                f"WARN: asset {asset_id} block missing sha256/sizeBytes line",
                file=sys.stderr,
            )
            return match.group(0)
        if new_body != body:
            updated += 1
        return head + new_body + tail

    new_text = _BLOCK_RE.sub(rewrite_block, text)

    mb = total_bytes / (1024 * 1024)
    print(f"Hashed   : {len(targets)} asset(s) ({mb:.2f} MB total)")

    if args.dry_run:
        print(f"Would update : {updated} asset(s) in {args.manifest}")
        print("(--dry-run) No file was written.")
        return 0

    args.manifest.write_text(new_text, encoding="utf-8", newline="\n")
    print(f"Updated  : {updated} asset(s) in {args.manifest}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
