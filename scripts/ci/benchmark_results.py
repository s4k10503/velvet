#!/usr/bin/env python3
"""Turns the benchmark run's NUnit results into github-action-benchmark's customSmallerIsBetter JSON.

Unity Performance Testing writes each benchmark's measurements into the test case's output as a line
starting `##performancetestresult2:` followed by one JSON object (the test's name and its sample groups,
each with a median), and the machine it ran on as one `##performancetestruninfo2:` line. Where that line
names a processor, every value carries it in its `extra` for benchmark_steps.py to compare runs by. The
same groups are written to a Markdown table for the job summary.

Usage: benchmark_results.py RESULTS_XML_OR_DIRECTORY --json OUT.json --summary OUT.md
"""

import argparse
import json
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

RESULT_PREFIX = "##performancetestresult2:"
RUN_INFO_PREFIX = "##performancetestruninfo2:"

# Unity.PerformanceTesting's SampleUnit, by the integer a result carries.
UNITS = ["ns", "μs", "ms", "s", "b", "kb", "mb", "gb"]


def unit_name(unit):
    if isinstance(unit, int) and 0 <= unit < len(UNITS):
        return UNITS[unit]
    return str(unit) if isinstance(unit, str) else ""


def read_results(xml_text):
    """Every performance result object found in any output element of an NUnit results document."""
    results = []
    for output in ET.fromstring(xml_text).iter("output"):
        for line in (output.text or "").splitlines():
            line = line.strip()
            if line.startswith(RESULT_PREFIX):
                results.append(json.loads(line[len(RESULT_PREFIX):]))
    return results


def read_processor(xml_text):
    """The processor an NUnit results document's run-info line names, or None where it has none."""
    for output in ET.fromstring(xml_text).iter("output"):
        for line in (output.text or "").splitlines():
            line = line.strip()
            if line.startswith(RUN_INFO_PREFIX):
                return json.loads(line[len(RUN_INFO_PREFIX):]).get("Hardware", {}).get("ProcessorType")
    return None


def entries(results):
    """One (name, unit, median) per sample group where a smaller median is better."""
    rows = []
    for result in results:
        for group in result.get("SampleGroups", []):
            if group.get("IncreaseIsBetter"):
                continue
            rows.append((f"{result['Name']} / {group['Name']}", unit_name(group.get("Unit")), group["Median"]))
    return rows


def to_benchmark_json(rows, processor=None):
    extra = {"extra": f"processor: {processor}"} if processor else {}
    return [{"name": name, "unit": unit, "value": value, **extra} for name, unit, value in rows]


def to_markdown(rows):
    lines = ["| Benchmark | Median | Unit |", "|---|---:|---|"]
    lines += [f"| {name} | {value:.4g} | {unit} |" for name, unit, value in rows]
    return "\n".join(lines) + "\n"


def main(argv):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("results", type=Path)
    parser.add_argument("--json", type=Path, required=True)
    parser.add_argument("--summary", type=Path, required=True)
    args = parser.parse_args(argv)

    documents = sorted(args.results.glob("*.xml")) if args.results.is_dir() else [args.results]
    texts = [path.read_text(encoding="utf-8") for path in documents]
    rows = entries([r for text in texts for r in read_results(text)])
    if not rows:
        print(f"{args.results} holds no performance result", file=sys.stderr)
        return 1
    processor = next(filter(None, map(read_processor, texts)), None)
    args.json.write_text(json.dumps(to_benchmark_json(rows, processor), indent=2) + "\n", encoding="utf-8")
    args.summary.write_text(to_markdown(rows), encoding="utf-8")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
