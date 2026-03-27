#!/usr/bin/env python3
"""Extract the final review report for a specific task."""
import re
import sys

task_id = sys.argv[1] if len(sys.argv) > 1 else "TASK-002"
log_file = "task_pipeline_with_autofix.log"

with open(log_file, 'r') as f:
    content = f.read()

# Find all review attempts for this task
pattern = rf'{task_id}.*?failed review \(attempt (\d+)/3\), running fix stage'
attempts = list(re.finditer(pattern, content))

print(f"Found {len(attempts)} failed review attempts for {task_id}\n")

# Find the final review (should be after attempt 3 or when task failed)
# Look for the last review output before task failed
task_failed_pos = content.find(f'{task_id}: tested → failed')
if task_failed_pos == -1:
    print(f"Task {task_id} has not completed yet or didn't fail")
    sys.exit(1)

# Get the last 20000 characters before the failure (should contain the review)
review_section = content[max(0, task_failed_pos - 20000):task_failed_pos]

# Try to find the review report
review_match = re.search(r'(# Code Review Report.*?)(?=\n\d{2}:\d{2}:\d{2}|\Z)', review_section, re.DOTALL)

if review_match:
    print("=" * 80)
    print("FINAL REVIEW REPORT (Attempt 3)")
    print("=" * 80)
    print(review_match.group(1))
else:
    print("Could not extract review report. Here's the raw section:")
    print(review_section[-5000:])  # Last 5000 chars
