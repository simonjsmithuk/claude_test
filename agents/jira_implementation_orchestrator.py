"""Jira Implementation Orchestrator — Coding → Testing → Review → Merge pipeline.

This orchestrator handles the implementation phase of the SDLC:
1. Transitions Jira ticket from "Open" to "In Progress"
2. Creates a Git branch with naming convention: <base_branch>_<ticket>_<description>
3. Runs Coder Agent to implement tasks from the design
4. Runs Test Writer Agent to create tests
5. Creates a GitHub PR
6. Runs Code Reviewer Agent to review the changes
7. On success: Adds comments to PR, merges, moves ticket to "Included In Build"
8. On failure: Adds review comments to PR, retries (max 3 iterations)

Usage:
    python -m agents.jira_implementation_orchestrator AIDAT-1 --interactive
    python -m agents.jira_implementation_orchestrator AIDAT-1 --base-branch sprint10
"""
from __future__ import annotations

import argparse
import json
import logging
import os
import subprocess
import sys
import time
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Optional

import anthropic
import yaml

from context.store import ContextStore
from .coder_agent import CoderAgent
from .test_writer_agent import TestWriterAgent
from .code_reviewer_agent import CodeReviewerAgent
from .jira_client import JiraClient

logger = logging.getLogger(__name__)


@dataclass
class ImplementationResult:
    """Result of the implementation orchestration."""
    run_id: str
    jira_key: str
    jira_summary: str
    base_branch: str
    feature_branch: str = ""
    pr_number: str = ""
    pr_url: str = ""
    iteration: int = 1
    max_iterations: int = 3
    review_passed: bool = False
    merged: bool = False
    timings: dict[str, float] = field(default_factory=dict)
    errors: dict[str, str] = field(default_factory=dict)

    def summary(self) -> str:
        """Generate a summary of the implementation run."""
        lines = [
            f"# Jira Implementation Run: {self.jira_key}",
            f"**Summary**: {self.jira_summary}",
            f"**Base Branch**: {self.base_branch}",
            f"**Feature Branch**: {self.feature_branch}",
            f"**PR**: {self.pr_url}",
            f"**Iteration**: {self.iteration}/{self.max_iterations}",
            f"**Review**: {'✓ Passed' if self.review_passed else '✗ Failed'}",
            f"**Merged**: {'✓ Yes' if self.merged else '✗ No'}",
            "",
        ]

        # Timings
        if self.timings:
            lines.append("## Timings")
            for step, duration in self.timings.items():
                lines.append(f"- **{step}**: {duration:.1f}s")
            lines.append("")

        # Errors
        if self.errors:
            lines.append("## Errors")
            for step, error in self.errors.items():
                lines.append(f"- **{step}**: {error}")
            lines.append("")

        return "\n".join(lines)


class JiraImplementationOrchestrator:
    """Orchestrator for Coder → Test → Review → Merge pipeline.

    Workflow:
    1. Transition ticket: Open → In Progress
    2. Create Git branch
    3. Run Coder Agent
    4. Run Test Writer Agent
    5. Create GitHub PR
    6. Run Code Reviewer Agent
    7. Handle review results (merge or retry)
    """

    def __init__(
        self,
        api_key: str | None = None,
        jira_cloud_id: str | None = None,
        model: str = "claude-sonnet-4-6",
        interactive: bool = False,
        max_iterations: int = 3,
    ):
        """Initialize the orchestrator.

        Args:
            api_key: Anthropic API key (or from env ANTHROPIC_API_KEY)
            jira_cloud_id: Jira cloud ID (or will prompt if interactive)
            model: Claude model to use
            interactive: Whether to prompt for missing information
            max_iterations: Maximum retry iterations on review failure
        """
        self.client = anthropic.Anthropic(api_key=api_key)
        self.model = model
        self.interactive = interactive
        self.jira_cloud_id = jira_cloud_id
        self.max_iterations = max_iterations

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
            cloud_id = self._prompt_user("Enter Jira cloud ID (e.g., 'yoursite.atlassian.net')")
            self.jira_cloud_id = cloud_id
            return cloud_id

        raise ValueError("Jira cloud ID not configured. Add to config/agents.yml or use --interactive")

    def _run_git_command(self, command: list[str], check: bool = True) -> subprocess.CompletedProcess:
        """Run a git command and return the result."""
        logger.debug("Running git command: %s", " ".join(command))
        result = subprocess.run(
            command,
            capture_output=True,
            text=True,
            check=False
        )

        if check and result.returncode != 0:
            raise RuntimeError(f"Git command failed: {' '.join(command)}\n{result.stderr}")

        return result

    def _run_gh_command(self, command: list[str], check: bool = True) -> subprocess.CompletedProcess:
        """Run a gh CLI command and return the result."""
        logger.debug("Running gh command: %s", " ".join(command))
        result = subprocess.run(
            command,
            capture_output=True,
            text=True,
            check=False
        )

        if check and result.returncode != 0:
            raise RuntimeError(f"gh CLI command failed: {' '.join(command)}\n{result.stderr}")

        return result

    def run(self, jira_ticket_key: str, base_branch: str = "main") -> ImplementationResult:
        """Run the complete implementation pipeline.

        Args:
            jira_ticket_key: Jira ticket key (e.g., "AIDAT-1")
            base_branch: Base branch to branch from (default: "main")

        Returns:
            ImplementationResult with pipeline outcome
        """
        run_id = f"jira_{jira_ticket_key.lower()}_{int(time.time())}"

        result = ImplementationResult(
            run_id=run_id,
            jira_key=jira_ticket_key,
            jira_summary="",
            base_branch=base_branch,
            max_iterations=self.max_iterations
        )

        logger.info("=== Jira Implementation Pipeline starting for %s ===", jira_ticket_key)
        print(f"\n{'='*80}")
        print(f"Jira Implementation Pipeline: {jira_ticket_key}")
        print(f"Base Branch: {base_branch}")
        print(f"{'='*80}\n")

        try:
            # Create context store for this run
            ctx = ContextStore(run_id=run_id)

            # Step 1: Read Jira ticket and transition to In Progress
            self._read_and_transition_ticket(result)

            # Step 2: Create Git branch
            self._create_feature_branch(result)

            # Main implementation loop (max 3 iterations)
            while result.iteration <= result.max_iterations:
                logger.info("=== Iteration %d/%d ===", result.iteration, result.max_iterations)
                print(f"\n{'='*80}")
                print(f"Iteration {result.iteration}/{result.max_iterations}")
                print(f"{'='*80}\n")

                try:
                    # Step 3: Run Coder Agent
                    self._run_coder_agent(ctx, result)

                    # Step 4: Run Test Writer Agent
                    self._run_test_writer_agent(ctx, result)

                    # Step 5: Commit and push changes
                    self._commit_and_push(result)

                    # Step 6: Create or update GitHub PR
                    self._create_or_update_pr(result)

                    # Step 7: Run Code Reviewer Agent
                    self._run_code_reviewer_agent(ctx, result)

                    # Step 8: Handle review results
                    if result.review_passed:
                        self._merge_and_complete(result)
                        break
                    else:
                        if result.iteration < result.max_iterations:
                            logger.warning("Review failed, retrying iteration %d...", result.iteration + 1)
                            result.iteration += 1
                        else:
                            logger.error("Max iterations reached, implementation failed")
                            break

                except Exception as exc:
                    logger.error("Iteration %d failed: %s", result.iteration, exc)
                    result.errors[f"iteration_{result.iteration}"] = str(exc)

                    if result.iteration < result.max_iterations:
                        result.iteration += 1
                    else:
                        raise

        except Exception as exc:
            logger.error("Implementation pipeline failed: %s", exc)
            result.errors["pipeline"] = str(exc)
            raise

        finally:
            logger.info("=== Jira Implementation Pipeline complete for %s ===", jira_ticket_key)
            print(f"\n{'='*80}")
            print(result.summary())
            print(f"{'='*80}\n")

        return result

    def _read_and_transition_ticket(self, result: ImplementationResult) -> None:
        """Read Jira ticket and transition to In Progress."""
        t0 = time.time()
        try:
            cloud_id = self._get_jira_cloud_id()
            self.jira_client = JiraClient(cloud_id=cloud_id, interactive=self.interactive)

            logger.info("Reading Jira ticket %s", result.jira_key)
            ticket = self.jira_client.get_ticket(result.jira_key)
            result.jira_summary = ticket.summary

            logger.info("Transitioning %s to 'In Progress'", result.jira_key)
            transition_name = self.config.get("jira", {}).get("transitions", {}).get(
                "open_to_in_progress", "In Progress"
            )

            success = self.jira_client.transition_issue(result.jira_key, transition_name)
            if not success:
                logger.warning("Failed to transition ticket to '%s'", transition_name)

        except Exception as exc:
            logger.error("Failed to read/transition Jira ticket: %s", exc)
            result.errors["read_transition"] = str(exc)
            raise
        finally:
            result.timings["read_transition"] = time.time() - t0

    def _create_feature_branch(self, result: ImplementationResult) -> None:
        """Create Git feature branch with naming convention."""
        t0 = time.time()
        try:
            # Ensure we're on the base branch and up to date
            logger.info("Checking out base branch: %s", result.base_branch)
            self._run_git_command(["git", "checkout", result.base_branch])

            logger.info("Pulling latest changes from origin")
            self._run_git_command(["git", "pull", "origin", result.base_branch])

            # Generate branch name: <base_branch>_<ticket>_<short_description>
            # Use first 3 words of summary as short description
            summary_words = result.jira_summary.lower().replace(" ", "_").split("_")[:3]
            short_desc = "_".join(summary_words)

            branch_name = f"{result.base_branch}_{result.jira_key.lower()}_{short_desc}"
            result.feature_branch = branch_name

            logger.info("Creating feature branch: %s", branch_name)
            self._run_git_command(["git", "checkout", "-b", branch_name])

        except Exception as exc:
            logger.error("Failed to create feature branch: %s", exc)
            result.errors["create_branch"] = str(exc)
            raise
        finally:
            result.timings["create_branch"] = time.time() - t0

    def _run_coder_agent(self, ctx: ContextStore, result: ImplementationResult) -> None:
        """Run Coder Agent to implement the feature."""
        t0 = time.time()
        try:
            logger.info("Running Coder agent...")
            max_tokens = self._get_max_tokens_for_agent("coder_agent")
            agent = CoderAgent(self.client, ctx, self.model, max_tokens)

            # Coder agent reads spec and design from context store
            agent.run_coding()

            logger.info("Coder agent complete")

        except Exception as exc:
            logger.error("Coder agent failed: %s", exc)
            result.errors[f"coder_iteration_{result.iteration}"] = str(exc)
            raise
        finally:
            result.timings[f"coder_iteration_{result.iteration}"] = time.time() - t0

    def _run_test_writer_agent(self, ctx: ContextStore, result: ImplementationResult) -> None:
        """Run Test Writer Agent to create tests."""
        t0 = time.time()
        try:
            logger.info("Running Test Writer agent...")
            max_tokens = self._get_max_tokens_for_agent("test_writer_agent")
            agent = TestWriterAgent(self.client, ctx, self.model, max_tokens)

            agent.run_tests()

            logger.info("Test Writer agent complete")

        except Exception as exc:
            logger.error("Test Writer agent failed: %s", exc)
            result.errors[f"test_writer_iteration_{result.iteration}"] = str(exc)
            raise
        finally:
            result.timings[f"test_writer_iteration_{result.iteration}"] = time.time() - t0

    def _commit_and_push(self, result: ImplementationResult) -> None:
        """Commit all changes and push to remote."""
        t0 = time.time()
        try:
            logger.info("Committing changes...")

            # Stage all changes
            self._run_git_command(["git", "add", "."])

            # Create commit message
            commit_msg = f"{result.jira_key}: {result.jira_summary} (iteration {result.iteration})"
            self._run_git_command(["git", "commit", "-m", commit_msg])

            # Push to origin
            logger.info("Pushing to origin/%s", result.feature_branch)
            self._run_git_command(["git", "push", "-u", "origin", result.feature_branch])

        except Exception as exc:
            logger.error("Failed to commit and push: %s", exc)
            result.errors[f"commit_push_iteration_{result.iteration}"] = str(exc)
            raise
        finally:
            result.timings[f"commit_push_iteration_{result.iteration}"] = time.time() - t0

    def _create_or_update_pr(self, result: ImplementationResult) -> None:
        """Create a new PR or update existing one."""
        t0 = time.time()
        try:
            if not result.pr_number:
                # Create new PR
                logger.info("Creating GitHub PR...")

                pr_title = f"{result.jira_key}: {result.jira_summary}"
                pr_body = f"""## Jira Ticket
{result.jira_key}

## Summary
{result.jira_summary}

## Implementation
This PR implements the changes specified in the Jira ticket.

Iteration: {result.iteration}/{result.max_iterations}

🤖 Generated with AI-SDLC Pipeline
"""

                result_gh = self._run_gh_command([
                    "gh", "pr", "create",
                    "--base", result.base_branch,
                    "--head", result.feature_branch,
                    "--title", pr_title,
                    "--body", pr_body
                ])

                # Parse PR URL from output
                pr_url = result_gh.stdout.strip()
                result.pr_url = pr_url

                # Extract PR number from URL (e.g., https://github.com/owner/repo/pull/123)
                result.pr_number = pr_url.split("/")[-1]

                logger.info("Created PR #%s: %s", result.pr_number, pr_url)
            else:
                # PR already exists, just push updates
                logger.info("PR #%s already exists, changes pushed", result.pr_number)

        except Exception as exc:
            logger.error("Failed to create/update PR: %s", exc)
            result.errors[f"pr_iteration_{result.iteration}"] = str(exc)
            raise
        finally:
            result.timings[f"pr_iteration_{result.iteration}"] = time.time() - t0

    def _run_code_reviewer_agent(self, ctx: ContextStore, result: ImplementationResult) -> None:
        """Run Code Reviewer Agent to review the changes."""
        t0 = time.time()
        try:
            logger.info("Running Code Reviewer agent...")
            max_tokens = self._get_max_tokens_for_agent("code_reviewer_agent")
            agent = CodeReviewerAgent(self.client, ctx, self.model, max_tokens)

            review_result = agent.run_review()

            # Parse review result to determine pass/fail
            # Assuming review_result is a string with "APPROVED" or "CHANGES_REQUESTED"
            result.review_passed = "APPROVED" in review_result or "approved" in review_result.lower()

            logger.info("Code review complete: %s", "PASSED" if result.review_passed else "FAILED")

            # Add review as PR comment
            self._add_pr_comment(result, review_result)

        except Exception as exc:
            logger.error("Code Reviewer agent failed: %s", exc)
            result.errors[f"reviewer_iteration_{result.iteration}"] = str(exc)
            raise
        finally:
            result.timings[f"reviewer_iteration_{result.iteration}"] = time.time() - t0

    def _add_pr_comment(self, result: ImplementationResult, comment: str) -> None:
        """Add a comment to the PR."""
        try:
            logger.info("Adding review comment to PR #%s", result.pr_number)
            self._run_gh_command([
                "gh", "pr", "comment", result.pr_number,
                "--body", comment
            ])
        except Exception as exc:
            logger.warning("Failed to add PR comment: %s", exc)

    def _merge_and_complete(self, result: ImplementationResult) -> None:
        """Merge PR and transition ticket to 'Included In Build'."""
        t0 = time.time()
        try:
            # Merge PR
            logger.info("Merging PR #%s", result.pr_number)
            self._run_gh_command([
                "gh", "pr", "merge", result.pr_number,
                "--squash",  # Squash commits
                "--delete-branch"  # Delete feature branch after merge
            ])

            result.merged = True
            logger.info("PR #%s merged successfully", result.pr_number)

            # Transition Jira ticket
            logger.info("Transitioning %s to 'Included In Build'", result.jira_key)
            transition_name = self.config.get("jira", {}).get("transitions", {}).get(
                "in_progress_to_done", "Included In Build"
            )

            success = self.jira_client.transition_issue(result.jira_key, transition_name)
            if success:
                logger.info("Ticket transitioned to '%s'", transition_name)
            else:
                logger.warning("Failed to transition ticket to '%s'", transition_name)

        except Exception as exc:
            logger.error("Failed to merge and complete: %s", exc)
            result.errors["merge_complete"] = str(exc)
            raise
        finally:
            result.timings["merge_complete"] = time.time() - t0


def main():
    """CLI entry point for Jira Implementation Orchestrator."""
    parser = argparse.ArgumentParser(
        description="Run Coder → Test → Review → Merge pipeline for a Jira ticket"
    )
    parser.add_argument(
        "ticket",
        help="Jira ticket key (e.g., AIDAT-1)"
    )
    parser.add_argument(
        "--base-branch",
        "-b",
        default="main",
        help="Base branch to branch from (default: main)"
    )
    parser.add_argument(
        "--interactive",
        "-i",
        action="store_true",
        help="Enable interactive prompts for missing information"
    )
    parser.add_argument(
        "--max-iterations",
        type=int,
        default=3,
        help="Maximum retry iterations on review failure (default: 3)"
    )
    parser.add_argument(
        "--log-level",
        choices=["DEBUG", "INFO", "WARNING", "ERROR"],
        default="INFO",
        help="Logging level (default: INFO)"
    )

    args = parser.parse_args()

    # Configure logging
    logging.basicConfig(
        level=getattr(logging, args.log_level),
        format="%(asctime)s [%(levelname)s] %(name)s: %(message)s",
        datefmt="%Y-%m-%d %H:%M:%S"
    )

    # Run orchestrator
    orchestrator = JiraImplementationOrchestrator(
        interactive=args.interactive,
        max_iterations=args.max_iterations
    )

    try:
        result = orchestrator.run(args.ticket, base_branch=args.base_branch)

        if result.merged:
            print("\n✓ Implementation pipeline completed successfully!")
            print(f"  PR #{result.pr_number} merged: {result.pr_url}")
            print(f"  Ticket {result.jira_key} moved to 'Included In Build'")
            sys.exit(0)
        else:
            print("\n✗ Implementation pipeline failed")
            print(f"  Max iterations reached without successful review")
            print(f"  PR #{result.pr_number}: {result.pr_url}")
            sys.exit(1)

    except Exception as exc:
        logger.exception("Implementation pipeline failed with exception")
        print(f"\n✗ Error: {exc}")
        sys.exit(1)


if __name__ == "__main__":
    main()
