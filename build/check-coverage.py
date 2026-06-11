#!/usr/bin/env python3
"""Aggregate Cobertura coverage across test projects and enforce a minimum line rate.

Usage: check-coverage.py <coverage-dir> <min-percent>
Exits non-zero if the aggregate line coverage is below <min-percent>.
"""
import glob
import os
import sys
import xml.etree.ElementTree as ET


def main() -> int:
    coverage_dir = sys.argv[1] if len(sys.argv) > 1 else "coverage"
    minimum = float(sys.argv[2]) if len(sys.argv) > 2 else 85.0

    files = glob.glob(os.path.join(coverage_dir, "**", "coverage.cobertura.xml"), recursive=True)
    files += glob.glob(os.path.join(coverage_dir, "**", "Cobertura.xml"), recursive=True)
    if not files:
        print(f"No coverage files found under '{coverage_dir}'.")
        return 1

    covered = 0
    valid = 0
    for path in files:
        root = ET.parse(path).getroot()
        covered += int(root.get("lines-covered", 0))
        valid += int(root.get("lines-valid", 0))

    if valid == 0:
        print("No coverable lines were found.")
        return 1

    rate = 100.0 * covered / valid
    print(f"Aggregate line coverage: {rate:.2f}% ({covered}/{valid}) across {len(files)} report(s).")

    if rate + 1e-9 < minimum:
        print(f"FAIL: coverage {rate:.2f}% is below the required {minimum:.2f}%.")
        return 1

    print(f"PASS: coverage {rate:.2f}% meets the required {minimum:.2f}%.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
