#!/usr/bin/env python3
"""Reset failed tasks to pending status so they can be retried with auto-fix."""
import json
import re

taskplan_file = ".sdlc_runs/run_755492e6/taskplan.json"

# Load taskplan
with open(taskplan_file, 'r') as f:
    data = json.load(f)

taskplan_raw = data.get('value', data)
if isinstance(taskplan_raw, str):
    # Strip markdown fences
    taskplan_raw = re.sub(r'^```(?:json)?\s*\n', '', taskplan_raw)
    taskplan_raw = re.sub(r'\n```\s*$', '', taskplan_raw)
    taskplan = json.loads(taskplan_raw)
else:
    taskplan = taskplan_raw

# Find and reset failed tasks
failed_tasks = []
for task in taskplan['tasks']:
    if task.get('status') == 'failed':
        failed_tasks.append(task['id'])
        task['status'] = 'pending'
        print(f"Reset {task['id']}: {task['title']}")

# Save back
data['value'] = '```json\n' + json.dumps(taskplan, indent=2) + '\n```'
with open(taskplan_file, 'w') as f:
    json.dump(data, f, indent=2)

print(f"\n✅ Reset {len(failed_tasks)} failed tasks to pending:")
for task_id in failed_tasks:
    print(f"   {task_id}")
