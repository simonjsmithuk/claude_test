#!/usr/bin/env python3
"""Real-time pipeline progress monitor."""
import time
import re
from pathlib import Path

log_file = Path("task_pipeline_full_run_v2.log")
check_interval = 10  # seconds

print("\n" + "=" * 80)
print("TASK PIPELINE MONITOR")
print("=" * 80 + "\n")

completed = []
failed = []
in_progress = None
last_position = 0

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
                        in_progress = match_start.group(1)

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

        # Print summary
        print(f"\r[{time.strftime('%H:%M:%S')}] Completed: {len(completed)} | Failed: {len(failed)} | Current: {in_progress or 'None'}  ", end='', flush=True)

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
