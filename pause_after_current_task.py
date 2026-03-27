#!/usr/bin/env python3
"""Monitor the pipeline log and kill the process after current task completes."""
import time
import subprocess
import re
import sys

log_file = "task_pipeline_full_run_v2.log"
check_interval = 5  # seconds

print("Monitoring pipeline to pause after current task completes...")
print("Press Ctrl+C to cancel and let pipeline continue running.\n")

last_position = 0
current_task = None

try:
    while True:
        with open(log_file, 'r') as f:
            f.seek(last_position)
            new_lines = f.read()
            last_position = f.tell()

            for line in new_lines.split('\n'):
                # Check for task start
                match_start = re.search(r'=== Starting (TASK-\d+) ===', line)
                if match_start:
                    current_task = match_start.group(1)
                    print(f"Current task: {current_task}")

                # Check for task completion
                match_complete = re.search(r'(TASK-\d+): \w+ → (completed|failed)', line)
                if match_complete:
                    completed_task = match_complete.group(1)
                    status = match_complete.group(2)
                    print(f"{completed_task} finished with status: {status}")

                    if completed_task == current_task:
                        print(f"\n{current_task} is complete. Stopping pipeline...")
                        # Find and kill the python process
                        result = subprocess.run(
                            ["ps", "aux"],
                            capture_output=True,
                            text=True
                        )
                        for proc_line in result.stdout.split('\n'):
                            if 'python3 test_task_pipeline.py' in proc_line and 'grep' not in proc_line:
                                pid = proc_line.split()[1]
                                print(f"Killing process {pid}...")
                                subprocess.run(["kill", pid])
                                print("Pipeline paused successfully!")
                                sys.exit(0)

        time.sleep(check_interval)

except KeyboardInterrupt:
    print("\nMonitoring cancelled. Pipeline will continue running.")
    sys.exit(0)
