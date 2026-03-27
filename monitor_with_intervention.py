#!/usr/bin/env python3
"""Monitor pipeline and track failures for intervention."""
import time
import re
import json
from pathlib import Path

log_file = Path("task_pipeline_with_autofix.log")
check_interval = 10  # seconds

print("\n" + "=" * 80)
print("PIPELINE MONITOR WITH FAILURE TRACKING")
print("=" * 80 + "\n")

# Load task info
def load_task_info():
    try:
        with open(".sdlc_runs/run_755492e6/taskplan.json", 'r') as f:
            data = json.load(f)
        taskplan_raw = data.get('value', data)
        if isinstance(taskplan_raw, str):
            taskplan_raw = re.sub(r'^```(?:json)?\s*\n', '', taskplan_raw)
            taskplan_raw = re.sub(r'\n```\s*$', '', taskplan_raw)
            taskplan = json.loads(taskplan_raw)
        else:
            taskplan = taskplan_raw

        task_info = {}
        for task in taskplan.get('tasks', []):
            task_info[task['id']] = {
                'title': task.get('title', ''),
                'description': task.get('description', '')[:100],
                'files': task.get('files', [])
            }
        return task_info
    except Exception as e:
        print(f"Warning: Could not load taskplan: {e}")
        return {}

task_info = load_task_info()

# Tracking
completed = []
failed = []
failed_details = {}
in_progress = None
current_stage = None
last_position = 0
last_displayed = None

print("Monitoring started. Press Ctrl+C to see failure summary and intervention options.\n")

try:
    while True:
        if log_file.exists():
            with open(log_file, 'r') as f:
                f.seek(last_position)
                new_lines = f.read()
                last_position = f.tell()

                for line in new_lines.split('\n'):
                    # Task start
                    match_start = re.search(r'=== Starting (TASK-\d+) ===', line)
                    if match_start:
                        task_id = match_start.group(1)
                        in_progress = task_id
                        current_stage = "starting"

                        if task_id != last_displayed and task_id in task_info:
                            print(f"\n{'─' * 80}")
                            print(f"📋 {task_id}: {task_info[task_id]['title']}")
                            print(f"   {task_info[task_id]['description']}")
                            print(f"{'─' * 80}")
                            last_displayed = task_id

                    # Stage transitions
                    match_stage = re.search(r'Running (code|test|review|fix) stage for (TASK-\d+)', line)
                    if match_stage:
                        current_stage = match_stage.group(1)
                        in_progress = match_stage.group(2)

                    # Fix attempts
                    match_fix = re.search(r'(TASK-\d+) failed review \(attempt (\d+)/3\), running fix stage', line)
                    if match_fix:
                        task_id = match_fix.group(1)
                        attempt = match_fix.group(2)
                        print(f"🔧 {task_id} - Fix attempt {attempt} (review failed)")

                    # Review passes after fix
                    match_pass = re.search(r'(TASK-\d+) passed review on attempt (\d+)', line)
                    if match_pass:
                        task_id = match_pass.group(1)
                        attempt = match_pass.group(2)
                        print(f"✅ {task_id} - Passed on attempt {attempt} after fix!")

                    # Task completion
                    match_complete = re.search(r'(TASK-\d+): tested → completed', line)
                    if match_complete:
                        task_id = match_complete.group(1)
                        completed.append(task_id)
                        print(f"✅ {task_id} COMPLETED")
                        in_progress = None
                        current_stage = None

                    # Task failure
                    match_fail = re.search(r'(TASK-\d+): tested → failed', line)
                    if match_fail:
                        task_id = match_fail.group(1)
                        failed.append(task_id)
                        failed_details[task_id] = {
                            'title': task_info.get(task_id, {}).get('title', 'Unknown'),
                            'files': task_info.get(task_id, {}).get('files', [])
                        }
                        print(f"❌ {task_id} FAILED after all attempts")
                        in_progress = None
                        current_stage = None

                    # Final review failure
                    match_final_fail = re.search(r'(TASK-\d+) failed review after 3 attempts', line)
                    if match_final_fail:
                        task_id = match_final_fail.group(1)
                        print(f"⚠️  {task_id} exhausted all fix attempts")

        # Status line
        status = f"[{time.strftime('%H:%M:%S')}] ✅ {len(completed)} | ❌ {len(failed)}"
        if in_progress and current_stage:
            status += f" | 🔄 {in_progress} ({current_stage})"
        elif in_progress:
            status += f" | 🔄 {in_progress}"

        print(f"\r{status}  ", end='', flush=True)
        time.sleep(check_interval)

except KeyboardInterrupt:
    print("\n\n" + "=" * 80)
    print("PIPELINE SUMMARY & INTERVENTION OPTIONS")
    print("=" * 80)

    print(f"\n✅ Completed: {len(completed)}")
    for task in completed:
        print(f"   {task}: {task_info.get(task, {}).get('title', 'Unknown')}")

    print(f"\n❌ Failed: {len(failed)}")
    for task in failed:
        details = failed_details.get(task, {})
        print(f"   {task}: {details.get('title', 'Unknown')}")
        print(f"      Files: {', '.join(details.get('files', [])[:3])}")

    if failed:
        print("\n" + "=" * 80)
        print("INTERVENTION OPTIONS")
        print("=" * 80)
        print("\n1. Review Failed Tasks:")
        print("   python3 show_task.py <TASK-ID>")
        print("   python3 extract_review.py <TASK-ID>")

        print("\n2. Manual Fix:")
        print("   # Review the generated files")
        print("   # Make necessary changes")
        print("   # Mark as completed:")
        print("   python3 -c \"...update taskplan...\"")

        print("\n3. Reset & Retry with Enhanced Prompt:")
        print("   python3 reset_failed_tasks.py")
        print("   # Restart pipeline (will use enhanced prompt)")

        print("\n4. Check Generated Files:")
        for task_id in failed[:3]:  # Show first 3
            if task_id in failed_details:
                print(f"\n   {task_id} files:")
                for f in failed_details[task_id].get('files', [])[:5]:
                    print(f"      ls -la {f}")

    print("\n" + "=" * 80 + "\n")
