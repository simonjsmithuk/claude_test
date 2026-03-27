#!/usr/bin/env python3
"""
Run a single task through the SDLC pipeline using Claude Code subagents.

This script uses Claude Code's Task tool to invoke custom subagents defined in .claude/agents/
These subagents run using your Claude Pro account, not API tokens.

Usage:
    python3 run_task_with_subagents.py TASK-015
"""
import json
import sys
from pathlib import Path


def load_task(task_id: str) -> dict:
    """Load task from taskplan.json"""
    taskplan_path = Path(".sdlc_runs/run_755492e6/taskplan.json")

    with open(taskplan_path) as f:
        data = json.load(f)
        taskplan = json.loads(data["value"].strip("```json\n").strip("\n```"))

    for task in taskplan["tasks"]:
        if task["id"] == task_id:
            return task

    raise ValueError(f"Task {task_id} not found")


def load_context(key: str) -> str:
    """Load context artifact"""
    context_path = Path(f".sdlc_runs/run_755492e6/context/{key}.json")

    if not context_path.exists():
        return ""

    with open(context_path) as f:
        data = json.load(f)
        return data.get("value", "")


def save_task_status(task_id: str, status: str):
    """Update task status in taskplan.json"""
    taskplan_path = Path(".sdlc_runs/run_755492e6/taskplan.json")

    with open(taskplan_path) as f:
        data = json.load(f)
        taskplan = json.loads(data["value"].strip("```json\n").strip("\n```"))

    for task in taskplan["tasks"]:
        if task["id"] == task_id:
            task["status"] = status
            break

    # Save back
    data["value"] = f"```json\n{json.dumps(taskplan, indent=2)}\n```"
    with open(taskplan_path, "w") as f:
        json.dump(data, f, indent=2)


def create_task_prompt(task: dict, spec: str, design: str, stage: str) -> str:
    """Create prompt for a specific stage"""

    base_context = f"""
# Task: {task['id']} - {task['title']}

## Description
{task['description']}

## Files to Create/Modify
{chr(10).join(f"- {f}" for f in task['files'])}

## Acceptance Criteria
{chr(10).join(f"{i+1}. {c}" for i, c in enumerate(task['acceptance_criteria']))}

## Project Specification (Context)
{spec[:5000]}  # Truncated for brevity

## Design Document (Context)
{design[:5000]}  # Truncated for brevity
"""

    if stage == "code":
        return f"""{base_context}

## Your Task: Implementation

Implement the code for this task following all acceptance criteria EXACTLY.

Critical Instructions:
1. Match acceptance criteria types/names EXACTLY (e.g., if it says DateTime, use DateTime not DateTimeOffset)
2. Create ALL required files listed above
3. Implement ALL acceptance criteria
4. Follow Clean Architecture (Domain → Application → Infrastructure → API)
5. Ensure code compiles without errors

Start implementing now. Create each file with complete, production-ready code.
"""

    elif stage == "test":
        return f"""{base_context}

## Your Task: Write Tests

Write comprehensive tests for the implemented code.

Requirements:
1. Use xUnit for .NET tests, Vitest for TypeScript tests
2. Test all acceptance criteria
3. Test happy path AND edge cases
4. Test error conditions
5. Use Moq for mocking (. NET) or vi.fn() (TypeScript)
6. Ensure tests run and pass

Start writing tests now.
"""

    elif stage == "review":
        return f"""{base_context}

## Your Task: Code Review

Review the implemented code and tests to ensure quality and spec compliance.

Review Checklist:
1. ✅ All acceptance criteria met EXACTLY
2. ✅ All required files created
3. ✅ Types match specification
4. ✅ Code compiles without errors
5. ✅ Tests exist and pass
6. ✅ No security issues

Respond with one of:
- "APPROVED" if all criteria met
- "APPROVED WITH SUGGESTIONS" if minor improvements possible
- "CHANGES REQUIRED" with specific issues to fix

Start review now.
"""

    else:
        raise ValueError(f"Unknown stage: {stage}")


def main():
    if len(sys.argv) < 2:
        print("Usage: python3 run_task_with_subagents.py TASK-XXX")
        sys.exit(1)

    task_id = sys.argv[1]

    print(f"Loading {task_id}...")
    task = load_task(task_id)

    print(f"Loading context (spec, design)...")
    spec = load_context("spec")
    design = load_context("design")

    print(f"\n{'='*80}")
    print(f"Running {task_id}: {task['title']}")
    print(f"{'='*80}\n")

    # Check if task is testable
    is_testable = task.get("testable", False)

    # Stage 1: Code
    print(f"\n📝 Stage 1: CODING")
    print(f"Invoking coder subagent...")
    print(f"\n💡 INSTRUCTIONS FOR YOU (Claude Code):")
    print(f"Please invoke the 'coder' subagent with the following prompt:\n")
    print(create_task_prompt(task, spec, design, "code"))
    print(f"\n⏳ Waiting for coder subagent to complete...")
    print(f"\n[User: After the coder subagent completes, check for compilation errors]")
    print(f"[User: If successful, proceed to next stage]")

    input("\n Press ENTER when coder stage is complete...")

    # Stage 2: Tests (if testable)
    if is_testable:
        print(f"\n🧪 Stage 2: TESTING")
        print(f"Invoking test-writer subagent...")
        print(f"\n💡 INSTRUCTIONS FOR YOU (Claude Code):")
        print(f"Please invoke the 'test-writer' subagent with the following prompt:\n")
        print(create_task_prompt(task, spec, design, "test"))
        print(f"\n⏳ Waiting for test-writer subagent to complete...")

        input("\nPress ENTER when test stage is complete...")
    else:
        print(f"\n⏭️  Stage 2: TESTING - SKIPPED (task not testable)")

    # Stage 3: Review
    print(f"\n👁️  Stage 3: REVIEW")
    print(f"Invoking reviewer subagent...")
    print(f"\n💡 INSTRUCTIONS FOR YOU (Claude Code):")
    print(f"Please invoke the 'reviewer' subagent with the following prompt:\n")
    print(create_task_prompt(task, spec, design, "review"))
    print(f"\n⏳ Waiting for reviewer subagent to complete...")

    input("\nPress ENTER when review stage is complete...")

    # Update status
    print(f"\n✅ All stages complete for {task_id}")
    response = input(f"Mark task as completed? (y/n): ")

    if response.lower() == 'y':
        save_task_status(task_id, "completed")
        print(f"✅ {task_id} marked as completed")
    else:
        save_task_status(task_id, "in_progress")
        print(f"⏸️  {task_id} marked as in_progress")

    print(f"\n{'='*80}")
    print(f"Task pipeline complete!")
    print(f"{'='*80}\n")


if __name__ == "__main__":
    main()
