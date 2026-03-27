#!/usr/bin/env python3
"""Real-time pipeline progress monitor."""
import time
import re
import json
from pathlib import Path
import sys

# Use the latest log file
log_file = Path("task_pipeline_with_autofix.log")
if not log_file.exists():
    log_file = Path("task_pipeline_full_run_v2.log")

check_interval = 5  # seconds

# Load taskplan to get task details
def load_taskplan():
    """Load taskplan to get task titles and descriptions."""
    try:
        with open(".sdlc_runs/run_755492e6/taskplan.json", 'r') as f:
            data = json.load(f)

        taskplan_raw = data.get('value', data)
        if isinstance(taskplan_raw, str):
            # Strip markdown fences
            taskplan_raw = re.sub(r'^```(?:json)?\s*\n', '', taskplan_raw)
            taskplan_raw = re.sub(r'\n```\s*$', '', taskplan_raw)
            taskplan = json.loads(taskplan_raw)
        else:
            taskplan = taskplan_raw

        # Create lookup dict
        task_info = {}
        for task in taskplan.get('tasks', []):
            task_info[task['id']] = {
                'title': task.get('title', ''),
                'description': task.get('description', '')[:150]  # First 150 chars
            }
        return task_info
    except Exception as e:
        print(f"Warning: Could not load taskplan: {e}")
        return {}

task_info = load_taskplan()

print("\n" + "=" * 80)
print("TASK PIPELINE MONITOR")
print(f"Monitoring: {log_file}")
print("=" * 80 + "\n")

completed = []
failed = []
in_progress = None
current_stage = None
last_position = 0
last_task_displayed = None

try:
    while True:
        if log_file.exists():
            with open(log_file, 'r') as f:
                f.seek(last_position)
                new_lines = f.read()
                last_position = f.tell()

                for line in new_lines.split('\n'):
                    # Check for task start
                    match_start = re.search(r'=== Starting (TASK-\d+) ===', line)
                    if match_start:
                        task_id = match_start.group(1)
                        in_progress = task_id
                        current_stage = "starting"

                        # Display task details when starting
                        if task_id != last_task_displayed and task_id in task_info:
                            print(f"\n\n{'─' * 80}")
                            print(f"📋 {task_id}: {task_info[task_id]['title']}")
                            print(f"   {task_info[task_id]['description']}")
                            print(f"{'─' * 80}\n")
                            last_task_displayed = task_id

                    # Check for stage transitions
                    match_stage = re.search(r'Running (code|test|review|fix) stage for (TASK-\d+)', line)
                    if match_stage:
                        current_stage = match_stage.group(1)
                        task_id = match_stage.group(2)
                        in_progress = task_id

                        # Display task details if not already shown
                        if task_id != last_task_displayed and task_id in task_info:
                            print(f"\n\n{'─' * 80}")
                            print(f"📋 {task_id}: {task_info[task_id]['title']}")
                            print(f"   {task_info[task_id]['description']}")
                            print(f"{'─' * 80}\n")
                            last_task_displayed = task_id

                    # Check for fix attempts
                    match_fix = re.search(r'(TASK-\d+) failed review .* running fix stage', line)
                    if match_fix:
                        in_progress = match_fix.group(1)
                        current_stage = "fix"
                        print(f"🔧 {in_progress} - Applying fixes from review feedback")

                    # Check for review passes after fix
                    match_pass = re.search(r'(TASK-\d+) passed review on attempt (\d+)', line)
                    if match_pass:
                        task_id = match_pass.group(1)
                        attempt = match_pass.group(2)
                        print(f"✅ {task_id} - Passed review on attempt {attempt}")

                    # Check for task completion/failure
                    match_result = re.search(r'(TASK-\d+): tested → (completed|failed)', line)
                    if match_result:
                        task_id = match_result.group(1)
                        status = match_result.group(2)
                        if status == "completed":
                            completed.append(task_id)
                            print(f"✅ {task_id} COMPLETED")
                        else:
                            failed.append(task_id)
                            print(f"❌ {task_id} FAILED")
                        in_progress = None
                        current_stage = None

        # Print summary with stage info
        status_line = f"[{time.strftime('%H:%M:%S')}] Completed: {len(completed)} | Failed: {len(failed)}"
        if in_progress and current_stage:
            status_line += f" | Current: {in_progress} ({current_stage})"
        elif in_progress:
            status_line += f" | Current: {in_progress}"
        else:
            status_line += " | Current: None"

        print(f"\r{status_line}  ", end='', flush=True)

        time.sleep(check_interval)

except KeyboardInterrupt:
    print("\n\n" + "=" * 80)
    print("PIPELINE SUMMARY")
    print("=" * 80)
    print(f"\nCompleted: {len(completed)}")
    if completed:
        for task in completed:
            print(f"  ✅ {task}")
    print(f"\nFailed: {len(failed)}")
    if failed:
        for task in failed:
            print(f"  ❌ {task}")
    print(f"\nCurrent: {in_progress or 'None'}")
    print("\n" + "=" * 80 + "\n")
