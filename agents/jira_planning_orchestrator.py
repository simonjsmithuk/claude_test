"""Jira Planning Orchestrator — PM → Architect → Task Planner with Jira integration.

This orchestrator handles the planning phase of the SDLC:
1. Reads a Jira ticket (bug, task, or user story)
2. Runs PM Agent to create/refine specification
3. Updates Jira ticket with specification
4. Runs Architect Agent to create system design
5. Updates Jira ticket with design
6. Runs Task Planner Agent to break down into tasks
7. Creates Jira subtasks from task plan
8. Transitions ticket from "Awaiting Estimate" to "Open"

Usage:
    python -m agents.jira_planning_orchestrator AIDAT-1 --interactive
"""
from __future__ import annotations

import argparse
import json
import logging
import sys
import time
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

import anthropic
import yaml

from context.store import ContextStore
from .pm_agent import PMAgent
from .architect_agent import ArchitectAgent
from .taskplanner_agent import TaskPlannerAgent
from .jira_client import JiraClient, JiraTicket

logger = logging.getLogger(__name__)


@dataclass
class PlanningResult:
    """Result of the planning orchestration."""
    run_id: str
    jira_key: str
    jira_summary: str
    jira_description: str
    jira_type: str  # Bug, Task, Story
    spec: str = ""
    design: str = ""
    taskplan: str = ""
    subtasks_created: list[str] = field(default_factory=list)
    timings: dict[str, float] = field(default_factory=dict)
    errors: dict[str, str] = field(default_factory=dict)

    def summary(self) -> str:
        """Generate a summary of the planning run."""
        lines = [
            f"# Jira Planning Run: {self.jira_key}",
            f"**Type**: {self.jira_type}",
            f"**Summary**: {self.jira_summary}",
            "",
        ]
        for stage in ("spec", "design", "taskplan"):
            val = getattr(self, stage)
            status = "✓" if val and stage not in self.errors else ("✗" if stage in self.errors else "—")
            t = self.timings.get(stage, 0)
            lines.append(f"- {status} **{stage}** ({t:.1f}s)")

        if self.subtasks_created:
            lines += ["", f"## Subtasks Created ({len(self.subtasks_created)})"]
            for key in self.subtasks_created:
                lines.append(f"- {key}")

        if self.errors:
            lines += ["", "## Errors"]
            for stage, err in self.errors.items():
                lines.append(f"- **{stage}**: {err}")

        return "\n".join(lines)


class JiraPlanningOrchestrator:
    """Orchestrates PM → Architect → Task Planner with Jira integration.

    Workflow:
    1. Read Jira ticket details
    2. Optionally run PM agent (for tasks/stories, not bugs)
    3. Update Jira with PM spec
    4. Run Architect agent
    5. Update Jira with architecture design
    6. Run Task Planner agent
    7. Create Jira subtasks from plan
    8. Transition ticket to "Open"
    """

    def __init__(
        self,
        api_key: str | None = None,
        jira_cloud_id: str | None = None,
        model: str = "claude-sonnet-4-6",
        interactive: bool = False,
    ):
        """Initialize the orchestrator.

        Args:
            api_key: Anthropic API key (or from env ANTHROPIC_API_KEY)
            jira_cloud_id: Jira cloud ID (or will prompt if interactive)
            model: Claude model to use
            interactive: Whether to prompt for missing information
        """
        self.client = anthropic.Anthropic(api_key=api_key)
        self.model = model
        self.interactive = interactive
        self.jira_cloud_id = jira_cloud_id

        # Load agent configuration
        self.config = self._load_config()
        self.default_max_tokens = self.config.get("max_tokens", 8096)
        self.agent_max_tokens = self.config.get("agent_max_tokens", {})

    def _load_config(self) -> dict:
        """Load configuration from config/agents.yml"""
        config_path = Path(__file__).parent.parent / "config" / "agents.yml"
        if config_path.exists():
            with open(config_path, 'r') as f:
                return yaml.safe_load(f) or {}
        return {}

    def _get_max_tokens_for_agent(self, agent_name: str) -> int:
        """Get max_tokens for specific agent, falling back to default"""
        return self.agent_max_tokens.get(agent_name, self.default_max_tokens)

    def _prompt_user(self, prompt: str, default: str | None = None) -> str:
        """Prompt user for input if in interactive mode."""
        if not self.interactive:
            if default:
                return default
            raise ValueError(f"Missing required input: {prompt} (use --interactive to prompt)")

        full_prompt = prompt
        if default:
            full_prompt += f" [{default}]"
        full_prompt += ": "

        response = input(full_prompt).strip()
        return response if response else (default or "")

    def _get_jira_cloud_id(self) -> str:
        """Get Jira cloud ID, prompting if needed in interactive mode."""
        if self.jira_cloud_id:
            return self.jira_cloud_id

        # Try to load from config
        jira_config = self.config.get("jira", {})
        cloud_id = jira_config.get("cloud_id")

        if cloud_id:
            self.jira_cloud_id = cloud_id
            return cloud_id

        # Prompt if interactive
        if self.interactive:
            print("\nJira cloud ID not found in config.")
            print("You can find it by calling the Atlassian API or from your Jira URL.")
            cloud_id = self._prompt_user("Enter Jira cloud ID (or site URL like 'yoursite.atlassian.net')")
            self.jira_cloud_id = cloud_id
            return cloud_id

        raise ValueError("Jira cloud ID not configured. Add to config/agents.yml or use --interactive")

    def run(self, jira_ticket_key: str) -> PlanningResult:
        """Execute the planning pipeline for a Jira ticket.

        Args:
            jira_ticket_key: Jira ticket key (e.g., "AIDAT-1")

        Returns:
            PlanningResult with spec, design, taskplan, and subtasks
        """
        logger.info("=== Jira Planning Pipeline starting for %s ===", jira_ticket_key)

        run_id = f"jira_{jira_ticket_key.lower()}_{int(time.time())}"
        context = ContextStore(run_id)

        # Initialize result
        result = PlanningResult(
            run_id=run_id,
            jira_key=jira_ticket_key,
            jira_summary="",
            jira_description="",
            jira_type="",
        )

        try:
            # Step 1: Read Jira ticket
            self._read_jira_ticket(result)

            # Step 2: Run PM agent (if needed)
            self._run_pm_agent(context, result)

            # Step 3: Update Jira with spec
            self._update_jira_with_spec(result)

            # Step 4: Run Architect agent
            self._run_architect_agent(context, result)

            # Step 5: Update Jira with design
            self._update_jira_with_design(result)

            # Step 6: Run Task Planner agent
            self._run_taskplanner_agent(context, result)

            # Step 7: Create Jira subtasks
            self._create_jira_subtasks(result)

            # Step 8: Transition ticket to Open
            self._transition_jira_ticket(result)

        except Exception as exc:
            logger.error("Planning pipeline failed: %s", exc, exc_info=True)
            result.errors["pipeline"] = str(exc)

        logger.info("=== Jira Planning Pipeline complete for %s ===", jira_ticket_key)
        return result

    def _read_jira_ticket(self, result: PlanningResult) -> None:
        """Read Jira ticket details using JiraClient."""
        t0 = time.time()
        try:
            cloud_id = self._get_jira_cloud_id()

            logger.info("Reading Jira ticket %s from %s", result.jira_key, cloud_id)
            print(f"\n=== Reading Jira Ticket: {result.jira_key} ===")

            # Use JiraClient to fetch ticket (tries MCP, API, then interactive)
            self.jira_client = JiraClient(cloud_id=cloud_id, interactive=self.interactive)
            ticket = self.jira_client.get_ticket(result.jira_key)

            # Update result with ticket data
            result.jira_summary = ticket.summary
            result.jira_description = ticket.description
            result.jira_type = ticket.issue_type

            logger.info("Read Jira ticket %s: %s [%s]", result.jira_key, result.jira_summary, result.jira_type)
            print(f"Type: {result.jira_type}")
            print(f"Summary: {result.jira_summary}")
            if len(result.jira_description) > 100:
                print(f"Description: {result.jira_description[:100]}...")
            else:
                print(f"Description: {result.jira_description}")

        except Exception as exc:
            logger.error("Failed to read Jira ticket: %s", exc)
            result.errors["read_jira"] = str(exc)
            raise
        finally:
            result.timings["read_jira"] = time.time() - t0

    def _run_pm_agent(self, ctx: ContextStore, result: PlanningResult) -> None:
        """Run PM agent to create specification.

        For bugs, we may skip this and just use the bug description.
        For tasks/stories, we create a proper spec.
        """
        t0 = time.time()
        try:
            # Decide if we need PM agent
            skip_pm = False
            if result.jira_type.lower() == "bug":
                if self.interactive:
                    response = self._prompt_user(
                        "This is a bug. Skip PM agent and use bug description as spec? (y/n)",
                        "y"
                    )
                    skip_pm = response.lower() in ("y", "yes")
                else:
                    skip_pm = True  # Default: skip PM for bugs

            if skip_pm:
                # Use bug description as spec
                result.spec = f"# Bug Report: {result.jira_summary}\n\n{result.jira_description}"
                ctx.set_spec(result.spec)
                logger.info("Skipped PM agent for bug, using description as spec")
            else:
                # Run PM agent
                logger.info("Running PM agent...")
                max_tokens = self._get_max_tokens_for_agent("pm_agent")
                agent = PMAgent(self.client, ctx, self.model, max_tokens)

                # Combine Jira ticket info into request
                request = f"""
# Jira Ticket: {result.jira_key}
**Type**: {result.jira_type}
**Summary**: {result.jira_summary}

## Description
{result.jira_description}

Please create a detailed Product Specification Document for this {result.jira_type}.
"""
                result.spec = agent.run_spec(request)
                logger.info("PM agent complete (%d chars)", len(result.spec))

        except Exception as exc:
            logger.error("PM agent failed: %s", exc)
            result.errors["spec"] = str(exc)
            raise
        finally:
            result.timings["spec"] = time.time() - t0

    def _update_jira_with_spec(self, result: PlanningResult) -> None:
        """Update Jira ticket with PM specification."""
        t0 = time.time()
        try:
            if not result.spec:
                logger.info("No spec to update Jira with")
                return

            # Format spec as a comment
            comment_text = f"Specification (generated by AI planning orchestrator):\n\n{result.spec}"

            logger.info("Updating Jira %s with spec (%d chars)", result.jira_key, len(result.spec))

            if self.interactive:
                print(f"\n=== Spec Generated ({len(result.spec)} chars) ===")
                print(result.spec[:500] + "..." if len(result.spec) > 500 else result.spec)
                response = self._prompt_user("Update Jira ticket with spec? (y/n)", "y")
                if response.lower() not in ("y", "yes"):
                    logger.info("Skipping Jira spec update (user declined)")
                    return

            # Add comment to Jira
            success = self.jira_client.add_comment(result.jira_key, comment_text)
            if success:
                logger.info("Successfully added spec to Jira ticket %s", result.jira_key)
            else:
                logger.warning("Failed to add spec comment to Jira (check logs)")

        except Exception as exc:
            logger.error("Failed to update Jira with spec: %s", exc)
            result.errors["update_jira_spec"] = str(exc)
        finally:
            result.timings["update_jira_spec"] = time.time() - t0

    def _run_architect_agent(self, ctx: ContextStore, result: PlanningResult) -> None:
        """Run Architect agent to create system design."""
        t0 = time.time()
        try:
            logger.info("Running Architect agent...")
            max_tokens = self._get_max_tokens_for_agent("architect_agent")
            agent = ArchitectAgent(self.client, ctx, self.model, max_tokens)
            result.design = agent.run_design()
            logger.info("Architect agent complete (%d chars)", len(result.design))

        except Exception as exc:
            logger.error("Architect agent failed: %s", exc)
            result.errors["design"] = str(exc)
            raise
        finally:
            result.timings["design"] = time.time() - t0

    def _update_jira_with_design(self, result: PlanningResult) -> None:
        """Update Jira ticket with architecture design."""
        t0 = time.time()
        try:
            if not result.design:
                logger.info("No design to update Jira with")
                return

            # Format design as a comment
            comment_text = f"System Design (generated by AI planning orchestrator):\n\n{result.design}"

            logger.info("Updating Jira %s with design (%d chars)", result.jira_key, len(result.design))

            if self.interactive:
                print(f"\n=== Design Generated ({len(result.design)} chars) ===")
                print(result.design[:500] + "..." if len(result.design) > 500 else result.design)
                response = self._prompt_user("Update Jira ticket with design? (y/n)", "y")
                if response.lower() not in ("y", "yes"):
                    logger.info("Skipping Jira design update (user declined)")
                    return

            # Add comment to Jira
            success = self.jira_client.add_comment(result.jira_key, comment_text)
            if success:
                logger.info("Successfully added design to Jira ticket %s", result.jira_key)
            else:
                logger.warning("Failed to add design comment to Jira (check logs)")

        except Exception as exc:
            logger.error("Failed to update Jira with design: %s", exc)
            result.errors["update_jira_design"] = str(exc)
        finally:
            result.timings["update_jira_design"] = time.time() - t0

    def _run_taskplanner_agent(self, ctx: ContextStore, result: PlanningResult) -> None:
        """Run Task Planner agent to create task breakdown."""
        t0 = time.time()
        try:
            logger.info("Running Task Planner agent...")
            max_tokens = self._get_max_tokens_for_agent("taskplanner_agent")
            agent = TaskPlannerAgent(self.client, ctx, self.model, max_tokens)
            result.taskplan = agent.run_taskplan()
            logger.info("Task Planner agent complete (%d chars)", len(result.taskplan))

        except Exception as exc:
            logger.error("Task Planner agent failed: %s", exc)
            result.errors["taskplan"] = str(exc)
            raise
        finally:
            result.timings["taskplan"] = time.time() - t0

    def _create_jira_subtasks(self, result: PlanningResult) -> None:
        """Create Jira subtasks from task plan."""
        t0 = time.time()
        try:
            if not result.taskplan:
                logger.info("No taskplan to create subtasks from")
                return

            # Parse taskplan JSON
            try:
                # Log the first 200 chars for debugging
                logger.debug("Taskplan preview (first 200 chars): %s", result.taskplan[:200] if result.taskplan else "(empty)")

                # Strip markdown code fences if present at the string level
                taskplan_str = result.taskplan.strip()
                if taskplan_str.startswith("```json"):
                    taskplan_str = taskplan_str[7:]  # Remove ```json
                elif taskplan_str.startswith("```"):
                    taskplan_str = taskplan_str[3:]  # Remove ```

                if taskplan_str.endswith("```"):
                    taskplan_str = taskplan_str[:-3]  # Remove trailing ```

                taskplan_str = taskplan_str.strip()

                # Now parse
                taskplan_data = json.loads(taskplan_str)

                # Check if it's wrapped in a context store format {"key": "taskplan", "value": "..."}
                if isinstance(taskplan_data, dict) and "value" in taskplan_data:
                    inner_value = taskplan_data["value"]

                    # Strip markdown code fences again if present in the value
                    if inner_value.startswith("```json"):
                        inner_value = inner_value[7:]
                    elif inner_value.startswith("```"):
                        inner_value = inner_value[3:]

                    if inner_value.endswith("```"):
                        inner_value = inner_value[:-3]

                    inner_value = inner_value.strip()
                    taskplan_data = json.loads(inner_value)

                tasks = taskplan_data.get("tasks", [])
            except json.JSONDecodeError as e:
                logger.error("Failed to parse taskplan JSON: %s", e)
                logger.error("Taskplan type: %s, length: %d", type(result.taskplan), len(result.taskplan) if result.taskplan else 0)
                logger.error("First 500 chars: %s", result.taskplan[:500] if result.taskplan else "(empty)")
                result.errors["parse_taskplan"] = str(e)
                return

            logger.info("Found %d tasks in taskplan", len(tasks))

            if self.interactive:
                print(f"\n=== Task Plan Generated: {len(tasks)} tasks ===")
                for i, task in enumerate(tasks[:5], 1):  # Show first 5
                    print(f"{i}. [{task.get('id')}] {task.get('title')} (Priority {task.get('priority')})")
                if len(tasks) > 5:
                    print(f"... and {len(tasks) - 5} more tasks")

                response = self._prompt_user(f"Create {len(tasks)} Jira subtasks? (y/n)", "y")
                if response.lower() not in ("y", "yes"):
                    logger.info("Skipping subtask creation (user declined)")
                    return

            # Create subtasks in Jira
            logger.info("Creating %d subtasks in Jira...", len(tasks))
            for task in tasks:
                task_id = task.get('id', 'unknown')
                title = task.get('title', 'Untitled task')
                description = task.get('description', '')
                priority = task.get('priority', 'medium')

                # Format description with additional details
                full_description = f"Priority: {priority}\n\n{description}"

                subtask_key = self.jira_client.create_subtask(
                    parent_key=result.jira_key,
                    summary=title,
                    description=full_description
                )

                if subtask_key:
                    result.subtasks_created.append(subtask_key)
                    logger.info("Created subtask %s: %s", subtask_key, title)
                else:
                    logger.warning("Failed to create subtask for task %s: %s", task_id, title)

        except Exception as exc:
            logger.error("Failed to create Jira subtasks: %s", exc)
            result.errors["create_subtasks"] = str(exc)
        finally:
            result.timings["create_subtasks"] = time.time() - t0

    def _transition_jira_ticket(self, result: PlanningResult) -> None:
        """Transition Jira ticket from 'Awaiting Estimate' to 'Open'."""
        t0 = time.time()
        try:
            transition_name = self.config.get("jira", {}).get("transitions", {}).get(
                "awaiting_estimate_to_open", "Open"
            )

            logger.info("Transitioning %s to '%s'", result.jira_key, transition_name)

            if self.interactive:
                response = self._prompt_user(
                    f"Transition {result.jira_key} to '{transition_name}' status? (y/n)",
                    "y"
                )
                if response.lower() not in ("y", "yes"):
                    logger.info("Skipping ticket transition (user declined)")
                    return

            # Perform the transition
            success = self.jira_client.transition_issue(result.jira_key, transition_name)
            if success:
                logger.info("Successfully transitioned %s to '%s'", result.jira_key, transition_name)
            else:
                logger.warning("Failed to transition ticket (check logs for details)")

        except Exception as exc:
            logger.error("Failed to transition Jira ticket: %s", exc)
            result.errors["transition_jira"] = str(exc)
        finally:
            result.timings["transition_jira"] = time.time() - t0


def main():
    """CLI entry point for Jira Planning Orchestrator."""
    parser = argparse.ArgumentParser(
        description="Run PM → Architect → Task Planner pipeline for a Jira ticket"
    )
    parser.add_argument(
        "ticket",
        help="Jira ticket key (e.g., AIDAT-1)"
    )
    parser.add_argument(
        "--interactive",
        "-i",
        action="store_true",
        help="Enable interactive mode to prompt for missing information"
    )
    parser.add_argument(
        "--cloud-id",
        help="Jira cloud ID (or site URL)"
    )
    parser.add_argument(
        "--model",
        default="claude-sonnet-4-6",
        help="Claude model to use (default: claude-sonnet-4-6)"
    )
    parser.add_argument(
        "--log-level",
        default="INFO",
        choices=["DEBUG", "INFO", "WARNING", "ERROR"],
        help="Logging level"
    )

    args = parser.parse_args()

    # Configure logging
    logging.basicConfig(
        level=getattr(logging, args.log_level),
        format="%(asctime)s [%(levelname)s] %(name)s: %(message)s"
    )

    # Run orchestrator
    try:
        orchestrator = JiraPlanningOrchestrator(
            jira_cloud_id=args.cloud_id,
            model=args.model,
            interactive=args.interactive,
        )
        result = orchestrator.run(args.ticket)

        # Print summary
        print("\n" + "=" * 80)
        print(result.summary())
        print("=" * 80)

        # Exit with error code if there were errors
        if result.errors:
            sys.exit(1)

    except KeyboardInterrupt:
        print("\n\nInterrupted by user")
        sys.exit(130)
    except Exception as e:
        logger.error("Fatal error: %s", e, exc_info=True)
        sys.exit(1)


if __name__ == "__main__":
    main()
