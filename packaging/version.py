#!/usr/bin/env python3
"""Produce deterministic MSIX and display versions for GitHub Actions."""

from __future__ import annotations

import argparse
import re


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--ref-type", required=True)
    parser.add_argument("--ref-name", required=True)
    parser.add_argument("--run-number", required=True, type=int)
    return parser.parse_args()


def numeric_tag_version(ref_name: str) -> list[int] | None:
    match = re.fullmatch(r"v(\d+)\.(\d+)\.(\d+)", ref_name)
    if match is None:
        return None
    parts = [int(value) for value in match.groups()]
    if any(value > 65535 for value in parts):
        raise ValueError("Tag version components must be between 0 and 65535")
    return parts


def main() -> None:
    args = parse_args()
    if args.ref_type == "tag":
        tag_parts = numeric_tag_version(args.ref_name)
        if tag_parts is None:
            raise ValueError("Release tags must match v<major>.<minor>.<patch> exactly")
        parts = tag_parts
    else:
        parts = [0, 1, min(max(args.run_number, 0), 65535)]

    display_version = ".".join(str(value) for value in parts)
    package_version = f"{display_version}.0"
    print(f"display_version={display_version}")
    print(f"package_version={package_version}")


if __name__ == "__main__":
    main()
