#!/usr/bin/env python3
"""Fail if line coverage of the core assemblies is below the threshold (RNF-03).

Usage: python3 scripts/check_coverage.py <merged Cobertura.xml> [threshold=80]
"""
import sys
import xml.etree.ElementTree as ET

CORE = ("Leaderboard.Domain", "Leaderboard.Application")


def main() -> int:
    report = sys.argv[1] if len(sys.argv) > 1 else "TestResults/coverage/Cobertura.xml"
    threshold = float(sys.argv[2]) if len(sys.argv) > 2 else 80.0

    packages = {p.get("name"): float(p.get("line-rate")) * 100 for p in ET.parse(report).getroot().iter("package")}
    failed = False
    for name in CORE:
        rate = packages.get(name)
        if rate is None:
            print(f"::error::No coverage data for {name}")
            failed = True
            continue
        status = "ok" if rate >= threshold else "BELOW THRESHOLD"
        print(f"{name}: {rate:.1f}% (threshold {threshold:.0f}%) {status}")
        failed |= rate < threshold

    if failed:
        print(f"::error::Core line coverage must be >= {threshold:.0f}%")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
