# Pipeline Intervention Guide

Quick reference for intervening when tasks fail during pipeline execution.

---

## Monitor Pipeline

### Run the Intervention Monitor
```bash
python3 monitor_with_intervention.py
```

**What it shows:**
- Real-time task progress
- Fix attempt notifications
- Completed/failed counts
- Current task and stage

**To see summary:** Press `Ctrl+C`

---

## When a Task Fails

### Step 1: Investigate the Failure

#### View Task Details
```bash
python3 show_task.py TASK-XXX
```

Shows:
- Task description
- Files to create/modify
- Acceptance criteria
- Dependencies

#### Extract Review Feedback
```bash
python3 extract_review.py TASK-XXX
```

Shows:
- What the review found
- Why it failed
- Specific issues to fix

#### Check Generated Files
```bash
# List files in the task directory
ls -la src/DataViewer.Domain/Entities/

# View a specific file
cat src/DataViewer.Domain/Entities/User.cs

# Check git diff
git diff src/DataViewer.Domain/
```

---

## Step 2: Determine the Issue Type

### Type 1: Spec vs Best-Practice Conflict
**Example:** Used `DateTimeOffset` instead of `DateTime`

**Signs:**
- Review mentions "technically superior" or "better practice"
- Implementation is high quality
- Type mismatch with acceptance criteria

**Solution:** Change implementation to match spec exactly

### Type 2: Incomplete Implementation
**Example:** Created 1 of 6 required files

**Signs:**
- Review says "missing required files"
- Review mentions gaps in functionality
- Acceptance criteria not fully met

**Solution:** Complete the missing parts

### Type 3: Actual Bugs
**Example:** Missing required field, incorrect logic

**Signs:**
- Review mentions "critical bug" or "incorrect implementation"
- Logic errors or data inconsistencies

**Solution:** Fix the bugs

---

## Step 3: Apply the Fix

### Option A: Manual Fix (Recommended for Complex Issues)

#### 1. Make the changes
```bash
# Edit the files
code src/DataViewer.Domain/Entities/User.cs

# Or use sed for simple replacements
sed -i 's/DateTimeOffset/DateTime/g' src/DataViewer.Domain/Entities/*.cs
```

#### 2. Commit the changes
```bash
cd src/DataViewer.Domain/Entities
git add *.cs
git commit -m "fix(domain): Address TASK-XXX review feedback

- [describe changes]

TASK-XXX: [task title]"
```

#### 3. Mark task as completed
```bash
python3 -c "
import json, re

with open('.sdlc_runs/run_755492e6/taskplan.json', 'r') as f:
    data = json.load(f)

taskplan_raw = data.get('value', data)
if isinstance(taskplan_raw, str):
    taskplan_raw = re.sub(r'^\`\`\`(?:json)?\s*\n', '', taskplan_raw)
    taskplan_raw = re.sub(r'\n\`\`\`\s*$', '', taskplan_raw)
    taskplan = json.loads(taskplan_raw)
else:
    taskplan = taskplan_raw

for task in taskplan['tasks']:
    if task['id'] == 'TASK-XXX':
        task['status'] = 'completed'
        print(f\"Marked {task['id']} as completed\")
        break

data['value'] = '\`\`\`json\n' + json.dumps(taskplan, indent=2) + '\n\`\`\`'
with open('.sdlc_runs/run_755492e6/taskplan.json', 'w') as f:
    json.dump(data, f, indent=2)
"
```

### Option B: Reset and Retry with Enhanced Prompt

#### 1. Reset failed tasks
```bash
python3 reset_failed_tasks.py
```

This marks all failed tasks as "pending" so they can be retried.

#### 2. Stop current pipeline (if running)
```bash
# Find the process
ps aux | grep "python3 test_task_pipeline" | grep -v grep

# Kill it
kill <PID>
```

#### 3. Restart pipeline
```bash
source venv/bin/activate
nohup python3 test_task_pipeline.py > task_pipeline_retry.log 2>&1 &
```

The restarted pipeline will use the enhanced fix prompt.

### Option C: Skip and Continue

If a task is non-critical or blocking other work:

#### Mark as completed (skip)
```bash
python3 -c "
import json, re

with open('.sdlc_runs/run_755492e6/taskplan.json', 'r') as f:
    data = json.load(f)

taskplan_raw = data.get('value', data)
if isinstance(taskplan_raw, str):
    taskplan_raw = re.sub(r'^\`\`\`(?:json)?\s*\n', '', taskplan_raw)
    taskplan_raw = re.sub(r'\n\`\`\`\s*$', '', taskplan_raw)
    taskplan = json.loads(taskplan_raw)
else:
    taskplan = taskplan_raw

for task in taskplan['tasks']:
    if task['id'] == 'TASK-XXX':
        task['status'] = 'completed'
        print(f\"Skipped {task['id']} - marked as completed\")
        break

data['value'] = '\`\`\`json\n' + json.dumps(taskplan, indent=2) + '\n\`\`\`'
with open('.sdlc_runs/run_755492e6/taskplan.json', 'w') as f:
    json.dump(data, f, indent=2)
"
```

This allows dependent tasks to proceed.

---

## Step 4: Verify the Fix

### Check if dependencies are unblocked
```bash
python3 show_task.py TASK-XXX | grep -A 10 Dependencies
```

### Verify generated code compiles
```bash
cd src/DataViewer.Domain
dotnet build
```

### Check pipeline status
```bash
python3 pipeline_summary.py
```

---

## Common Scenarios

### Scenario 1: DateTime vs DateTimeOffset
**Issue:** Implementation uses DateTimeOffset but spec says DateTime

**Quick Fix:**
```bash
cd src/DataViewer.Domain/Entities
for file in *.cs; do
    sed -i 's/DateTimeOffset?/DateTime?/g' "$file"
    sed -i 's/DateTimeOffset /DateTime /g' "$file"
    sed -i 's/DateTimeOffset>/DateTime>/g' "$file"
done
git add *.cs && git commit -m "fix: Change DateTimeOffset to DateTime per spec"
```

### Scenario 2: Missing Files
**Issue:** Only created 2 of 6 required files

**Quick Fix:**
1. Check which files exist: `ls -la src/path/to/entities/`
2. Check which files are required: `python3 show_task.py TASK-XXX | grep "Files to"`
3. Create missing files manually or reset task and retry

### Scenario 3: Type Mismatch in Multiple Places
**Issue:** Using wrong types throughout

**Quick Fix:**
```bash
# Find all occurrences
grep -r "WrongType" src/

# Replace in all files
find src/ -name "*.cs" -exec sed -i 's/WrongType/CorrectType/g' {} +
```

---

## Batch Operations

### Reset All Failed Tasks
```bash
python3 reset_failed_tasks.py
```

### Mark Multiple Tasks as Completed
```python
# Edit reset_failed_tasks.py to mark specific tasks
task_ids = ['TASK-002', 'TASK-005', 'TASK-008']
for task_id in task_ids:
    # mark as completed
```

### Generate Full Status Report
```bash
python3 pipeline_summary.py > status_report.txt
cat status_report.txt
```

---

## Emergency Procedures

### Pipeline is Stuck
```bash
# Check if running
ps aux | grep test_task_pipeline

# Check last activity
tail -n 50 task_pipeline_with_autofix.log

# If stuck, kill and restart
kill <PID>
python3 test_task_pipeline.py
```

### Taskplan Corrupted
```bash
# Restore from backup
cp .sdlc_runs/run_755492e6/taskplan.json.bak .sdlc_runs/run_755492e6/taskplan.json

# Or regenerate from scratch (loses progress)
# [run TaskPlannerAgent again]
```

### API Rate Limiting
```bash
# Check recent API call rate
grep "HTTP Request" task_pipeline_with_autofix.log | tail -20

# If rate limited, wait 60 seconds and resume
```

---

## Monitoring Commands

### Watch Pipeline Live
```bash
tail -f task_pipeline_with_autofix.log
```

### Filter for Specific Task
```bash
grep "TASK-005" task_pipeline_with_autofix.log
```

### See Only Completions/Failures
```bash
grep -E "(tested → completed|tested → failed)" task_pipeline_with_autofix.log
```

### Count Completed vs Failed
```bash
echo "Completed: $(grep 'tested → completed' task_pipeline_with_autofix.log | wc -l)"
echo "Failed: $(grep 'tested → failed' task_pipeline_with_autofix.log | wc -l)"
```

---

## Best Practices

1. **Investigate Before Fixing**
   - Always understand why it failed
   - Check if it's a pattern (affects other tasks)

2. **Fix Similar Issues Together**
   - If TASK-002 has DateTime issue, check TASK-004 too
   - Batch fixes save time

3. **Document Manual Fixes**
   - Add comments in code
   - Update session notes
   - Track patterns for future improvement

4. **Verify Compilation**
   - Always run `dotnet build` after manual fixes
   - Catch issues early

5. **Track Metrics**
   - Note which tasks fail most often
   - Measure fix success rate
   - Identify problematic task types

---

## Useful File Locations

- **Taskplan:** `.sdlc_runs/run_755492e6/taskplan.json`
- **Pipeline Log:** `task_pipeline_with_autofix.log`
- **Monitor Script:** `monitor_with_intervention.py`
- **Task Viewer:** `show_task.py`
- **Review Extractor:** `extract_review.py`
- **Generated Code:** `src/DataViewer.*/**/*.cs`

---

## Get Help

If you're stuck:
1. Check [AUTO_FIX_IMPROVEMENTS.md](AUTO_FIX_IMPROVEMENTS.md) for common patterns
2. Review [SESSION_STATUS.md](SESSION_STATUS.md) for context
3. Check git history: `git log --oneline`
4. Review file contents before modifying
