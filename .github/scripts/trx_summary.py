#!/usr/bin/env python3
"""Render TRX test results as a GitHub job-summary Markdown table.

Usage: trx_summary.py <results-dir>   (prints Markdown to stdout)
"""
import pathlib
import sys
import xml.etree.ElementTree as ET

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def main(root: str) -> int:
    files = sorted(pathlib.Path(root).rglob("*.trx"))
    print("## Test results\n")
    if not files:
        print("No TRX files found.")
        return 0

    print("| Test assembly | Total | Passed | Failed | Skipped |")
    print("|---|---:|---:|---:|---:|")
    failures = []
    for path in files:
        tree = ET.parse(path)
        counters = tree.find(".//t:ResultSummary/t:Counters", NS)
        if counters is None:
            continue
        total = int(counters.get("total", 0))
        passed = int(counters.get("passed", 0))
        failed = int(counters.get("failed", 0)) + int(counters.get("error", 0))
        skipped = total - passed - failed
        storage = tree.find(".//t:UnitTest", NS)
        name = pathlib.Path(storage.get("storage", path.stem)).stem if storage is not None else path.stem
        icon = "❌" if failed else "✅"
        print(f"| {icon} {name} | {total} | {passed} | {failed} | {skipped} |")
        for result in tree.iterfind(".//t:UnitTestResult[@outcome='Failed']", NS):
            message = result.findtext(".//t:Message", default="", namespaces=NS).strip().splitlines()
            failures.append((result.get("testName"), message[0] if message else ""))

    if failures:
        print("\n### Failed tests\n")
        for test, message in failures:
            print(f"- `{test}`: {message}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1] if len(sys.argv) > 1 else "TestResults"))
