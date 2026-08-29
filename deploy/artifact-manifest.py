#!/usr/bin/env python3
"""Create and verify immutable deployment artifact manifests.

The command deliberately performs no deployment.  A manifest is the evidence
that a staging artifact can be reproduced byte-for-byte during rollback.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
from datetime import datetime, timezone
from pathlib import Path


FORMAT_VERSION = 1


def fail(message: str) -> None:
    print(f"ERROR: {message}", file=sys.stderr)
    raise SystemExit(1)


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def load(path: Path) -> dict:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        fail(f"cannot read manifest {path}: {exc}")
    if not isinstance(value, dict):
        fail(f"manifest {path} must contain a JSON object")
    return value


def parse_schema(value: str, name: str) -> int:
    try:
        return int(value)
    except ValueError:
        fail(f"{name} must be an integer, got {value!r}")
    raise AssertionError


def files_for(root: Path, excluded: set[Path]) -> list[dict]:
    files: list[dict] = []
    for path in sorted(root.rglob("*")):
        if not path.is_file() or path in excluded:
            continue
        relative = path.relative_to(root).as_posix()
        files.append({"path": relative, "size": path.stat().st_size, "sha256": sha256(path)})
    return files


def create(args: argparse.Namespace) -> None:
    root = Path(args.artifact).resolve()
    if not root.is_dir():
        fail(f"artifact directory does not exist: {root}")
    output = Path(args.output).resolve()
    required_files = sorted(set(args.required_file))
    for required in required_files:
        if not (root / required).is_file():
            fail(f"required artifact file is missing: {required}")
    database_schema = parse_schema(args.database_schema, "--database-schema")
    manifest = {
        "formatVersion": FORMAT_VERSION,
        "artifactVersion": args.version,
        "commit": args.commit,
        "generatedAtUtc": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
        "entrypoint": args.entrypoint,
        "apiContractVersion": args.api_contract,
        "databaseSchemaVersion": database_schema,
        "requiredFiles": required_files,
        "requiredConfiguration": sorted(set(args.required_config)),
        "rollback": {
            "databaseSchemaMin": database_schema,
            "databaseSchemaMax": database_schema,
            "assetRetentionUntilUtc": args.asset_retention_until,
        },
    }
    if args.image_digest:
        manifest["imageDigest"] = args.image_digest
    manifest["files"] = files_for(root, {output} if output.parent == root else set())
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(manifest, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(f"Wrote {output} ({len(manifest['files'])} files)")


def verify(args: argparse.Namespace) -> None:
    root = Path(args.artifact).resolve()
    manifest_path = Path(args.manifest).resolve()
    if not root.is_dir():
        fail(f"artifact directory does not exist: {root}")
    manifest = load(manifest_path)
    if manifest.get("formatVersion") != FORMAT_VERSION:
        fail(f"unsupported manifest formatVersion: {manifest.get('formatVersion')!r}")
    for field in ("artifactVersion", "apiContractVersion", "databaseSchemaVersion", "files", "rollback"):
        if field not in manifest:
            fail(f"manifest is missing {field}")
    if not isinstance(manifest["rollback"], dict):
        fail("manifest rollback must be an object")
    if args.expected_version and manifest["artifactVersion"] != args.expected_version:
        fail(f"artifact version is {manifest['artifactVersion']!r}, expected {args.expected_version!r}")
    if not isinstance(manifest["files"], list):
        fail("manifest files must be an array")
    expected_files = {entry.get("path"): entry for entry in manifest["files"] if isinstance(entry, dict)}
    if len(expected_files) != len(manifest["files"]):
        fail("manifest contains duplicate or malformed file entries")
    actual_paths = {
        path.relative_to(root).as_posix(): path
        for path in root.rglob("*")
        if path.is_file() and path.resolve() != manifest_path
    }
    for relative, entry in expected_files.items():
        if not relative or relative.startswith("/") or ".." in Path(relative).parts:
            fail(f"unsafe manifest path: {relative!r}")
        path = actual_paths.get(relative)
        if path is None:
            fail(f"manifest file is missing from artifact: {relative}")
        if path.stat().st_size != entry.get("size") or sha256(path) != entry.get("sha256"):
            fail(f"artifact digest mismatch: {relative}")
    extras = sorted(set(actual_paths) - set(expected_files))
    if extras:
        fail(f"artifact contains unmanifested files: {', '.join(extras)}")
    for required in manifest.get("requiredFiles", []):
        if required not in actual_paths:
            fail(f"required artifact file is missing: {required}")
    if args.database_schema is not None:
        check_database_compatibility(manifest, parse_schema(args.database_schema, "--database-schema"))
    if args.config_file:
        check_configuration(manifest, Path(args.config_file))
    print(f"Verified {manifest_path} ({len(expected_files)} files, version {manifest['artifactVersion']})")


def check_database_compatibility(manifest: dict, database_schema: int) -> None:
    rollback = manifest.get("rollback", {})
    lower = rollback.get("databaseSchemaMin", manifest.get("databaseSchemaVersion"))
    upper = rollback.get("databaseSchemaMax", manifest.get("databaseSchemaVersion"))
    if not isinstance(lower, int) or not isinstance(upper, int) or not lower <= database_schema <= upper:
        fail(f"database schema {database_schema} is outside artifact rollback range [{lower}, {upper}]")


def check_configuration(manifest: dict, path: Path) -> None:
    if not path.is_file():
        fail(f"configuration file does not exist: {path}")
    available = set()
    for line in path.read_text(encoding="utf-8").splitlines():
        stripped = line.strip()
        if stripped and not stripped.startswith("#") and "=" in stripped:
            available.add(stripped.split("=", 1)[0].strip())
    missing = sorted(set(manifest.get("requiredConfiguration", [])) - available)
    if missing:
        fail(f"configuration is missing required keys: {', '.join(missing)}")


def compatibility(args: argparse.Namespace) -> None:
    previous = load(Path(args.previous))
    candidate = load(Path(args.candidate))
    if previous.get("formatVersion") != FORMAT_VERSION or candidate.get("formatVersion") != FORMAT_VERSION:
        fail("both manifests must use the supported formatVersion")
    if previous.get("apiContractVersion") != candidate.get("apiContractVersion"):
        fail("API contract versions differ; rollback would cross an incompatible API boundary")
    new_schema = parse_schema(args.database_schema, "--database-schema")
    check_database_compatibility(candidate, new_schema)
    check_database_compatibility(previous, new_schema)
    retention = previous.get("rollback", {}).get("assetRetentionUntilUtc")
    if not retention:
        fail("previous manifest does not declare fingerprinted-asset retention")
    if args.config_file:
        check_configuration(previous, Path(args.config_file))
        check_configuration(candidate, Path(args.config_file))
    print(f"Compatible: {previous.get('artifactVersion')} -> {candidate.get('artifactVersion')} against database schema {new_schema}")


def parser() -> argparse.ArgumentParser:
    root = argparse.ArgumentParser(description=__doc__)
    commands = root.add_subparsers(dest="command", required=True)
    create_parser = commands.add_parser("create")
    create_parser.add_argument("artifact")
    create_parser.add_argument("--output", required=True)
    create_parser.add_argument("--version", required=True)
    create_parser.add_argument("--commit", required=True)
    create_parser.add_argument("--api-contract", required=True)
    create_parser.add_argument("--database-schema", required=True)
    create_parser.add_argument("--entrypoint", default="BzsOIDC.Idp.dll")
    create_parser.add_argument("--image-digest")
    create_parser.add_argument("--asset-retention-until", required=True)
    create_parser.add_argument("--required-file", action="append", default=[])
    create_parser.add_argument("--required-config", action="append", default=[])
    create_parser.set_defaults(function=create)
    verify_parser = commands.add_parser("verify")
    verify_parser.add_argument("artifact")
    verify_parser.add_argument("manifest")
    verify_parser.add_argument("--expected-version")
    verify_parser.add_argument("--database-schema")
    verify_parser.add_argument("--config-file")
    verify_parser.set_defaults(function=verify)
    compatibility_parser = commands.add_parser("compatibility")
    compatibility_parser.add_argument("previous")
    compatibility_parser.add_argument("candidate")
    compatibility_parser.add_argument("--database-schema", required=True)
    compatibility_parser.add_argument("--config-file")
    compatibility_parser.set_defaults(function=compatibility)
    return root


if __name__ == "__main__":
    arguments = parser().parse_args()
    arguments.function(arguments)
