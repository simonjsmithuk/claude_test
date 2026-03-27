#!/usr/bin/env python3
"""Generate comprehensive pipeline summary."""
import re
import json

log_file = "task_pipeline_full_run_v2.log"

completed = []
failed = []
skipped = []

with open(log_file, 'r') as f:
    content = f.read()

    # Find all task results
    for match in re.finditer(r'(TASK-\d+): tested → (completed|failed)', content):
        task_id = match.group(1)
        status = match.group(2)
        if status == "completed":
            completed.append(task_id)
        else:
            failed.append(task_id)

    # Find skipped tasks
    for match in re.finditer(r'(TASK-\d+) dependencies not met, skipping', content):
        task_id = match.group(1)
        if task_id not in skipped:
            skipped.append(task_id)

print("=" * 80)
print("PIPELINE EXECUTION SUMMARY")
print("=" * 80)
print(f"\n✅ Completed: {len(completed)} tasks")
for task in sorted(completed):
    print(f"   {task}")

print(f"\n❌ Failed: {len(failed)} tasks")
for task in sorted(failed):
    print(f"   {task}")

print(f"\n⏭️  Skipped: {len(skipped)} tasks (due to failed dependencies)")
for task in sorted(skipped, key=lambda x: int(x.split('-')[1])):
    print(f"   {task}")

print(f"\n📊 Total: {len(completed) + len(failed) + len(skipped) + 1}/44 tasks processed")
print("   (TASK-001 was already completed)")

# Analyze dependency chain
print("\n" + "=" * 80)
print("CRITICAL PATH ANALYSIS")
print("=" * 80)
print("\nFailed tasks that blocked others:")
for task in sorted(failed):
    blocked = [t for t in skipped if int(t.split('-')[1]) > int(task.split('-')[1])]
    if blocked:
        print(f"\n{task} likely blocked: {len([t for t in skipped if int(t.split('-')[1]) > int(task.split('-')[1])])} downstream tasks")

print("\n" + "=" * 80)
