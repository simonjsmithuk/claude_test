"""Task-Level Orchestrator — runs each task through code → tests → review pipeline."""
from __future__ import annotations

import json
import logging
import re
import time
from typing import Any

import anthropic

from context.store import ContextStore
from .coder_agent import CoderAgent
from .test_writer_agent import TestWriterAgent
from .code_reviewer_agent import CodeReviewerAgent

logger = logging.getLogger(__name__)


class TaskLevelOrchestrator:
    """Orchestrates individual tasks through the complete development pipeline.

    For each task:
    1. Mark as in_progress
    2. Run CoderAgent to implement (status: coded)
    3. Run TestWriterAgent to create tests (status: tested)
    4. Run ReviewerAgent to review (status: reviewed)
    5. If review passes, mark as completed
    6. If review fails, mark as failed and stop

    Persists task status back to taskplan.json after each stage.
    """

    def __init__(
        self,
        client: anthropic.Anthropic,
        run_id: str,
        model: str = "claude-sonnet-4-6",
        max_tokens: int = 8096,
    ):
        self.client = client
        self.run_id = run_id
        self.model = model
        self.max_tokens = max_tokens
        self.context = ContextStore(run_id)

    def run_all_tasks(self) -> dict[str, Any]:
        """Execute all pending tasks sequentially through the pipeline.

        Returns:
            Dictionary with execution summary and results per task.
        """
        logger.info("[TaskLevelOrchestrator] Starting task-level pipeline for run %s", self.run_id)

        # Load taskplan
        taskplan = self._load_taskplan()
        if not taskplan:
            logger.error("[TaskLevelOrchestrator] No taskplan found")
            return {"error": "No taskplan found"}

        tasks = taskplan.get("tasks", [])
        results = {
            "total_tasks": len(tasks),
            "completed": 0,
            "failed": 0,
            "skipped": 0,
            "task_results": []
        }

        # Get spec and design once (shared context for all tasks)
        spec = self.context.get_spec()
        design = self.context.get_design()

        for task in tasks:
            task_id = task.get("id", "UNKNOWN")
            status = task.get("status", "pending")

            # Skip if already completed or not pending
            if status == "completed":
                logger.info("[TaskLevelOrchestrator] %s already completed, skipping", task_id)
                results["completed"] += 1
                continue

            if status not in ["pending", "in_progress"]:
                logger.info("[TaskLevelOrchestrator] %s has status '%s', skipping", task_id, status)
                results["skipped"] += 1
                continue

            # Check dependencies
            deps = task.get("dependencies", [])
            if not self._check_dependencies(deps, tasks):
                logger.warning("[TaskLevelOrchestrator] %s dependencies not met, skipping", task_id)
                results["skipped"] += 1
                continue

            # Execute task through pipeline
            logger.info("[TaskLevelOrchestrator] === Starting %s ===", task_id)
            task_result = self._execute_task_pipeline(task, spec, design)

            results["task_results"].append(task_result)

            if task_result["final_status"] == "completed":
                results["completed"] += 1
            elif task_result["final_status"] == "failed":
                results["failed"] += 1
                # Log failure but continue processing other tasks
                logger.warning("[TaskLevelOrchestrator] %s failed, continuing with remaining tasks", task_id)

        logger.info("[TaskLevelOrchestrator] Pipeline complete: %d completed, %d failed, %d skipped",
                   results["completed"], results["failed"], results["skipped"])

        return results

    def _execute_task_pipeline(self, task: dict, spec: str, design: str) -> dict[str, Any]:
        """Execute a single task through code → test → review stages.

        Returns:
            Dictionary with timing and status for each stage.
        """
        task_id = task.get("id", "UNKNOWN")
        task_result = {
            "task_id": task_id,
            "title": task.get("title", ""),
            "stages": {},
            "final_status": "failed"
        }

        try:
            # Stage 1: Code
            self._update_task_status(task_id, "in_progress")
            t0 = time.time()
            code_result = self._run_code_stage(task, spec, design)
            task_result["stages"]["code"] = {
                "duration": time.time() - t0,
                "success": code_result["success"],
                "message": code_result.get("message", "")
            }

            if not code_result["success"]:
                self._update_task_status(task_id, "failed")
                return task_result

            self._update_task_status(task_id, "coded")

            # Stage 2: Tests (only if task is testable)
            if task.get("testable", False):
                t0 = time.time()
                test_result = self._run_test_stage(task, spec, design)
                task_result["stages"]["tests"] = {
                    "duration": time.time() - t0,
                    "success": test_result["success"],
                    "message": test_result.get("message", "")
                }

                if not test_result["success"]:
                    self._update_task_status(task_id, "failed")
                    return task_result

                self._update_task_status(task_id, "tested")
            else:
                task_result["stages"]["tests"] = {
                    "duration": 0,
                    "success": True,
                    "message": "Task not testable, skipping tests"
                }
                self._update_task_status(task_id, "tested")

            # Stage 3: Review (with auto-fix retry loop)
            max_fix_attempts = 2  # Allow 2 fix attempts (3 total reviews)
            fix_attempt = 0
            review_success = False

            while fix_attempt <= max_fix_attempts and not review_success:
                attempt_label = f"review_attempt_{fix_attempt + 1}" if fix_attempt > 0 else "review"

                t0 = time.time()
                review_result = self._run_review_stage(task)
                task_result["stages"][attempt_label] = {
                    "duration": time.time() - t0,
                    "success": review_result["success"],
                    "message": review_result.get("message", "")
                }

                if review_result["success"]:
                    review_success = True
                    self._update_task_status(task_id, "completed")
                    task_result["final_status"] = "completed"
                    logger.info("[TaskLevelOrchestrator] %s passed review on attempt %d", task_id, fix_attempt + 1)
                elif fix_attempt < max_fix_attempts:
                    # Review failed but we have retries left - run fix stage
                    logger.info("[TaskLevelOrchestrator] %s failed review (attempt %d/%d), running fix stage",
                               task_id, fix_attempt + 1, max_fix_attempts + 1)

                    t0 = time.time()
                    fix_result = self._run_fix_stage(task, spec, design, review_result.get("message", ""))
                    task_result["stages"][f"fix_attempt_{fix_attempt + 1}"] = {
                        "duration": time.time() - t0,
                        "success": fix_result["success"],
                        "message": fix_result.get("message", "")
                    }

                    if not fix_result["success"]:
                        logger.warning("[TaskLevelOrchestrator] %s fix stage failed, stopping retries", task_id)
                        break

                    fix_attempt += 1
                else:
                    # No more retries left
                    logger.warning("[TaskLevelOrchestrator] %s failed review after %d attempts",
                                 task_id, max_fix_attempts + 1)
                    self._update_task_status(task_id, "failed")

        except Exception as exc:
            logger.error("[TaskLevelOrchestrator] Task %s failed with exception: %s", task_id, exc, exc_info=True)
            task_result["stages"]["error"] = {
                "duration": 0,
                "success": False,
                "message": str(exc)
            }
            self._update_task_status(task_id, "failed")

        return task_result

    def _run_code_stage(self, task: dict, spec: str, design: str) -> dict[str, Any]:
        """Run CoderAgent for a single task."""
        task_id = task.get("id", "UNKNOWN")
        logger.info("[TaskLevelOrchestrator] Running code stage for %s", task_id)

        try:
            agent = CoderAgent(self.client, self.context, self.model, self.max_tokens)

            # Build task-specific prompt
            result = agent.run(
                f"Implement {task_id}: {task.get('title', '')}",
                extra_context={
                    "Task ID": task_id,
                    "Task Description": task.get("description", ""),
                    "Files to Create/Modify": "\n".join(task.get("files", [])),
                    "Acceptance Criteria": "\n".join(f"- {ac}" for ac in task.get("acceptance_criteria", [])),
                    "Product Specification (for reference)": spec[:2000],  # Truncated
                    "System Design (for reference)": design[:2000],  # Truncated
                },
            )

            return {"success": True, "message": result}

        except Exception as exc:
            logger.error("[TaskLevelOrchestrator] Code stage failed for %s: %s", task_id, exc)
            return {"success": False, "message": str(exc)}

    def _run_test_stage(self, task: dict, spec: str, design: str) -> dict[str, Any]:
        """Run TestWriterAgent for a single task."""
        task_id = task.get("id", "UNKNOWN")
        logger.info("[TaskLevelOrchestrator] Running test stage for %s", task_id)

        try:
            agent = TestWriterAgent(self.client, self.context, self.model, self.max_tokens)

            # Build task-specific test prompt
            result = agent.run(
                f"Write tests for {task_id}: {task.get('title', '')}",
                extra_context={
                    "Task ID": task_id,
                    "Files Implemented": "\n".join(task.get("files", [])),
                    "Acceptance Criteria": "\n".join(f"- {ac}" for ac in task.get("acceptance_criteria", [])),
                    "Product Specification (for reference)": spec[:1000],
                },
            )

            return {"success": True, "message": result}

        except Exception as exc:
            logger.error("[TaskLevelOrchestrator] Test stage failed for %s: %s", task_id, exc)
            return {"success": False, "message": str(exc)}

    def _run_review_stage(self, task: dict) -> dict[str, Any]:
        """Run ReviewerAgent for a single task."""
        task_id = task.get("id", "UNKNOWN")
        logger.info("[TaskLevelOrchestrator] Running review stage for %s", task_id)

        try:
            agent = CodeReviewerAgent(self.client, self.context, self.model, self.max_tokens)

            # Build task-specific review prompt
            result = agent.run(
                f"Review implementation of {task_id}: {task.get('title', '')}",
                extra_context={
                    "Task ID": task_id,
                    "Files to Review": "\n".join(task.get("files", [])),
                    "Acceptance Criteria to Verify": "\n".join(f"- {ac}" for ac in task.get("acceptance_criteria", [])),
                },
            )

            # Parse review result to determine pass/fail
            # Simple heuristic: if review contains "APPROVED" or "PASSED", consider it successful
            result_lower = result.lower()
            if "approved" in result_lower or "passed" in result_lower or "looks good" in result_lower:
                return {"success": True, "message": result}
            elif "failed" in result_lower or "rejected" in result_lower or "critical" in result_lower:
                return {"success": False, "message": result}
            else:
                # Default to success if ambiguous
                return {"success": True, "message": result}

        except Exception as exc:
            logger.error("[TaskLevelOrchestrator] Review stage failed for %s: %s", task_id, exc)
            return {"success": False, "message": str(exc)}

    def _run_fix_stage(self, task: dict, spec: str, design: str, review_feedback: str) -> dict[str, Any]:
        """Run CoderAgent again to fix issues identified in review.

        Args:
            task: Task definition
            spec: Product specification
            design: System design
            review_feedback: The review report containing issues to fix

        Returns:
            Dictionary with success status and message
        """
        task_id = task.get("id", "UNKNOWN")
        logger.info("[TaskLevelOrchestrator] Running fix stage for %s", task_id)

        try:
            agent = CoderAgent(self.client, self.context, self.model, self.max_tokens)

            # Build fix prompt with review feedback
            fix_prompt = f"""Fix the issues identified in the code review for {task_id}: {task.get('title', '')}

REVIEW FEEDBACK:
{review_feedback}

Please address all the issues mentioned in the review feedback above.
Focus on:
1. Fixing any bugs or gaps identified
2. Addressing naming inconsistencies
3. Improving type safety where suggested
4. Adding missing functionality

The files to fix are:
{chr(10).join('- ' + f for f in task.get('files', []))}

Please read the current implementation, understand the issues, and make the necessary fixes."""

            result = agent.run(
                fix_prompt,
                extra_context={
                    "Task ID": task_id,
                    "Files to Fix": "\n".join(task.get("files", [])),
                    "Original Acceptance Criteria": "\n".join(f"- {ac}" for ac in task.get("acceptance_criteria", [])),
                    "Review Feedback": review_feedback[:3000],  # Truncate if very long
                    "Product Specification (for reference)": spec[:2000],
                    "System Design (for reference)": design[:2000],
                },
            )

            return {"success": True, "message": result}

        except Exception as exc:
            logger.error("[TaskLevelOrchestrator] Fix stage failed for %s: %s", task_id, exc)
            return {"success": False, "message": str(exc)}

    def _load_taskplan(self) -> dict | None:
        """Load and parse the taskplan from context store."""
        try:
            taskplan_raw = self.context.get("taskplan", default=None)
            if not taskplan_raw:
                return None

            # Strip markdown fences
            if taskplan_raw.startswith('```'):
                taskplan_raw = re.sub(r'^```(?:json)?\s*\n', '', taskplan_raw)
                taskplan_raw = re.sub(r'\n```\s*$', '', taskplan_raw)

            return json.loads(taskplan_raw)

        except Exception as exc:
            logger.error("[TaskLevelOrchestrator] Failed to load taskplan: %s", exc)
            return None

    def _update_task_status(self, task_id: str, new_status: str) -> None:
        """Update a task's status in the taskplan and persist to disk."""
        try:
            taskplan = self._load_taskplan()
            if not taskplan:
                logger.error("[TaskLevelOrchestrator] Cannot update status: taskplan not found")
                return

            # Find and update the task
            for task in taskplan.get("tasks", []):
                if task.get("id") == task_id:
                    old_status = task.get("status", "unknown")
                    task["status"] = new_status
                    logger.info("[TaskLevelOrchestrator] %s: %s → %s", task_id, old_status, new_status)
                    break

            # Save back to context store
            taskplan_json = json.dumps(taskplan, indent=2)
            self.context.set("taskplan", f"```json\n{taskplan_json}\n```")

        except Exception as exc:
            logger.error("[TaskLevelOrchestrator] Failed to update task status: %s", exc)

    def _check_dependencies(self, dep_ids: list[str], all_tasks: list[dict]) -> bool:
        """Check if all dependency tasks are completed.

        Note: Reloads the taskplan from disk to get the most current status.
        """
        if not dep_ids:
            return True

        # Reload taskplan to get current status (not the stale all_tasks parameter)
        current_taskplan = self._load_taskplan()
        if not current_taskplan:
            logger.error("[TaskLevelOrchestrator] Cannot check dependencies: taskplan not found")
            return False

        current_tasks = current_taskplan.get("tasks", [])

        for dep_id in dep_ids:
            dep_task = next((t for t in current_tasks if t.get("id") == dep_id), None)
            if not dep_task:
                logger.warning("[TaskLevelOrchestrator] Dependency %s not found in taskplan", dep_id)
                return False

            if dep_task.get("status") != "completed":
                logger.warning("[TaskLevelOrchestrator] Dependency %s not completed (status: %s)",
                             dep_id, dep_task.get("status"))
                return False

        return True
