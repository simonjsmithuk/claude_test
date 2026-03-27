#!/usr/bin/env python3
import json
import re
import sys

with open('.sdlc_runs/run_755492e6/taskplan.json') as f:
    data = json.load(f)

taskplan_raw = data.get('value', data)
if isinstance(taskplan_raw, str):
    # Strip markdown fences
    taskplan_raw = re.sub(r'^```(?:json)?\s*\n', '', taskplan_raw)
    taskplan_raw = re.sub(r'\n```\s*$', '', taskplan_raw)
    taskplan = json.loads(taskplan_raw)
else:
    taskplan = taskplan_raw

task_id = sys.argv[1] if len(sys.argv) > 1 else 'TASK-002'
task = next((t for t in taskplan['tasks'] if t['id'] == task_id), None)

if task:
    print(f"ID: {task['id']}")
    print(f"Title: {task['title']}")
    print(f"Status: {task.get('status', 'pending')}")
    print(f"Dependencies: {task.get('dependencies', [])}")
    print(f"\nDescription:\n{task.get('description', 'N/A')}")
    print(f"\nFiles to create/modify:")
    for f in task.get('files', []):
        print(f"  - {f}")
    print(f"\nAcceptance Criteria:")
    for ac in task.get('acceptance_criteria', []):
        print(f"  - {ac}")
else:
    print(f"Task {task_id} not found")
