#!/usr/bin/env python3
"""Entry point for the AI-SDLC multi-agent framework."""
from __future__ import annotations

import argparse
import json
import logging
import os
import sys
from pathlib import Path


def configure_logging(verbose: bool) -> None:
    level = logging.DEBUG if verbose else logging.INFO
    logging.basicConfig(
        level=level,
        format="%(asctime)s [%(levelname)s] %(name)s: %(message)s",
        datefmt="%H:%M:%S",
    )


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="AI-SDLC: Multi-agent software development lifecycle pipeline powered by Claude.",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Examples:
  python main.py "Build a REST API for a task manager"
  python main.py --stages spec,design,code "Build a CLI weather app in Python"
  python main.py --run-id my-run-001 --output results.json "Build a blog platform"
""",
    )
    parser.add_argument("request", nargs="?", help="Feature request or project description.")
    parser.add_argument("--stages", default="spec,design,code,tests,review,devops,release",
                        help="Comma-separated list of stages to run (default: all).")
    parser.add_argument("--model", default="claude-sonnet-4-6",
                        help="Claude model ID to use.")
    parser.add_argument("--run-id", dest="run_id", default=None,
                        help="Custom run ID (used for context store directory).")
    parser.add_argument("--output", default=None,
                        help="Write final results as JSON to this file path.")
    parser.add_argument("--verbose", "-v", action="store_true",
                        help="Enable debug logging.")
    parser.add_argument("--list-stages", action="store_true",
                        help="Print available stages and exit.")
    return parser.parse_args()


AVAILABLE_STAGES = {
    "spec": "PM Agent — convert request to structured spec",
    "design": "Architect Agent — produce system design from spec",
    "code": "Coder Agent — implement features from spec + design",
    "tests": "Test Writer Agent — generate test suite",
    "review": "Code Reviewer Agent — review implementation + tests",
    "devops": "DevOps Agent — produce CI/CD and IaC",
    "release": "Release Agent — draft changelog and release notes",
}


def main() -> int:
    args = parse_args()
    configure_logging(args.verbose)

    if args.list_stages:
        print("Available stages:")
        for stage, desc in AVAILABLE_STAGES.items():
            print(f"  {stage:10s} — {desc}")
        return 0

    if not args.request:
        request = input("Enter your feature request or project description:\n> ").strip()
        if not request:
            print("Error: no request provided.", file=sys.stderr)
            return 1
    else:
        request = args.request

    api_key = os.environ.get("ANTHROPIC_API_KEY")
    if not api_key:
        print("Error: ANTHROPIC_API_KEY environment variable not set.", file=sys.stderr)
        return 1

    stages = [s.strip() for s in args.stages.split(",") if s.strip()]

    # Import here so module-level imports don't fail before env check
    from agents.orchestrator import OrchestratorAgent

    orchestrator = OrchestratorAgent(
        api_key=api_key,
        model=args.model,
        run_id=args.run_id,
        stages=stages,
    )

    print(f"\nStarting SDLC pipeline (run_id={orchestrator.run_id})")
    print(f"Stages: {' → '.join(stages)}\n")

    result = orchestrator.run(request)

    print("\n" + "=" * 60)
    print(result.summary())
    print("=" * 60 + "\n")

    if args.output:
        out_path = Path(args.output)
        out_path.write_text(json.dumps({
            "run_id": result.run_id,
            "spec": result.spec,
            "design": result.design,
            "implementation": result.implementation,
            "tests": result.tests,
            "review": result.review,
            "pipeline": result.pipeline,
            "changelog": result.changelog,
            "timings": result.timings,
            "errors": result.errors,
        }, indent=2))
        print(f"Results written to: {out_path}")

    return 1 if result.errors else 0


if __name__ == "__main__":
    sys.exit(main())
