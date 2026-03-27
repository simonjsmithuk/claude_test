#!/usr/bin/env python3
"""Test script to run TASK-001 through the task-level pipeline (code → test → review)."""

import os
import sys
import logging
from pathlib import Path

# Configure logging
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(name)s: %(message)s",
    datefmt="%H:%M:%S",
)

# Check API key
api_key = os.environ.get("ANTHROPIC_API_KEY")
if not api_key:
    print("Error: ANTHROPIC_API_KEY environment variable not set.", file=sys.stderr)
    sys.exit(1)

# Import TaskLevelOrchestrator
from agents.task_level_orchestrator import TaskLevelOrchestrator
import anthropic

# Run the task-level pipeline
client = anthropic.Anthropic(api_key=api_key)
orchestrator = TaskLevelOrchestrator(
    client=client,
    run_id="run_755492e6",  # Use existing run with taskplan
    model="claude-sonnet-4-6",
    max_tokens=8096
)

print("\n" + "=" * 80)
print("TASK-LEVEL PIPELINE TEST")
print("Running TASK-001 through: code → tests → review")
print("=" * 80 + "\n")

results = orchestrator.run_all_tasks()

print("\n" + "=" * 80)
print("PIPELINE RESULTS")
print("=" * 80)
print(f"Total tasks: {results['total_tasks']}")
print(f"Completed: {results['completed']}")
print(f"Failed: {results['failed']}")
print(f"Skipped: {results['skipped']}")

if results.get("task_results"):
    print("\nPer-Task Results:")
    for task_result in results["task_results"]:
        print(f"\n{task_result['task_id']}: {task_result['title']}")
        print(f"  Final Status: {task_result['final_status']}")
        for stage_name, stage_info in task_result.get("stages", {}).items():
            success_icon = "✅" if stage_info["success"] else "❌"
            print(f"  {success_icon} {stage_name}: {stage_info['duration']:.1f}s")
            if not stage_info["success"] and stage_name == "review":
                # Print full review message if review failed
                print(f"\n    REVIEW FAILURE DETAILS:\n{stage_info['message']}\n")

print("\n" + "=" * 80 + "\n")
