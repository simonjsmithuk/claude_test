"""Orchestrator Agent — routes tasks through the SDLC pipeline."""
from __future__ import annotations

import logging
import time
import uuid
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

import anthropic
import yaml

from context.store import ContextStore
from .pm_agent import PMAgent
from .architect_agent import ArchitectAgent
from .taskplanner_agent import TaskPlannerAgent
from .coder_agent import CoderAgent
from .test_writer_agent import TestWriterAgent
from .code_reviewer_agent import CodeReviewerAgent
from .devops_agent import DevOpsAgent
from .release_agent import ReleaseAgent

logger = logging.getLogger(__name__)


@dataclass
class PipelineResult:
    run_id: str
    spec: str = ""
    design: str = ""
    implementation: str = ""
    tests: str = ""
    review: str = ""
    pipeline: str = ""
    changelog: str = ""
    timings: dict[str, float] = field(default_factory=dict)
    errors: dict[str, str] = field(default_factory=dict)

    def summary(self) -> str:
        lines = [f"# SDLC Run: {self.run_id}", ""]
        for stage in ("spec", "design", "implementation", "tests", "review", "pipeline", "changelog"):
            val = getattr(self, stage)
            status = "✓" if val and stage not in self.errors else ("✗" if stage in self.errors else "—")
            t = self.timings.get(stage, 0)
            lines.append(f"- {status} **{stage}** ({t:.1f}s)")
        if self.errors:
            lines += ["", "## Errors"]
            for stage, err in self.errors.items():
                lines.append(f"- **{stage}**: {err}")
        return "\n".join(lines)


class OrchestratorAgent:
    """Runs the full SDLC pipeline, delegating each phase to a specialist agent.

    Usage::

        orchestrator = OrchestratorAgent(api_key="sk-...")
        result = orchestrator.run("Build a REST API for a task manager")
        print(result.summary())
    """

    def __init__(
        self,
        api_key: str | None = None,
        model: str = "claude-sonnet-4-6",
        run_id: str | None = None,
        stages: list[str] | None = None,
    ):
        self.client = anthropic.Anthropic(api_key=api_key)
        self.model = model
        self.run_id = run_id or f"run_{uuid.uuid4().hex[:8]}"
        # Which stages to execute. Default: all (including new taskplan stage).
        self.stages = stages or ["spec", "design", "taskplan", "code", "tests", "review", "devops", "release"]

        # Load agent configuration for max_tokens overrides
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

    def run(self, user_request: str) -> PipelineResult:
        """Execute the full pipeline for *user_request*."""
        logger.info("=== SDLC Pipeline starting: %s ===", self.run_id)
        context = ContextStore(self.run_id)
        result = PipelineResult(run_id=self.run_id)

        stage_map = {
            "spec": self._run_spec,
            "design": self._run_design,
            "taskplan": self._run_taskplan,
            "code": self._run_code,
            "tests": self._run_tests,
            "review": self._run_review,
            "devops": self._run_devops,
            "release": self._run_release,
        }

        for stage in self.stages:
            fn = stage_map.get(stage)
            if not fn:
                logger.warning("Unknown stage: %s — skipping.", stage)
                continue
            t0 = time.time()
            try:
                fn(context, result, user_request)
            except Exception as exc:
                logger.error("Stage '%s' failed: %s", stage, exc, exc_info=True)
                result.errors[stage] = str(exc)
            result.timings[stage] = time.time() - t0

        logger.info("=== SDLC Pipeline complete: %s ===", self.run_id)
        return result

    # ------------------------------------------------------------------
    # Stage runners
    # ------------------------------------------------------------------

    def _run_spec(self, ctx: ContextStore, result: PipelineResult, request: str) -> None:
        max_tokens = self._get_max_tokens_for_agent("pm_agent")
        agent = PMAgent(self.client, ctx, self.model, max_tokens)
        result.spec = agent.run_spec(request)
        logger.info("[orchestrator] spec complete (%d chars)", len(result.spec))

    def _run_design(self, ctx: ContextStore, result: PipelineResult, _: Any) -> None:
        max_tokens = self._get_max_tokens_for_agent("architect_agent")
        agent = ArchitectAgent(self.client, ctx, self.model, max_tokens)
        result.design = agent.run_design()
        logger.info("[orchestrator] design complete (%d chars)", len(result.design))

    def _run_taskplan(self, ctx: ContextStore, result: PipelineResult, _: Any) -> None:
        max_tokens = self._get_max_tokens_for_agent("taskplanner_agent")
        agent = TaskPlannerAgent(self.client, ctx, self.model, max_tokens)
        taskplan = agent.run_taskplan()
        # Store in context for CoderAgent to use
        ctx.set("taskplan", taskplan)
        logger.info("[orchestrator] taskplan complete (%d chars)", len(taskplan))

    def _run_code(self, ctx: ContextStore, result: PipelineResult, _: Any) -> None:
        max_tokens = self._get_max_tokens_for_agent("coder_agent")
        agent = CoderAgent(self.client, ctx, self.model, max_tokens)
        result.implementation = agent.run_implementation()
        logger.info("[orchestrator] code complete (%d chars)", len(result.implementation))

    def _run_tests(self, ctx: ContextStore, result: PipelineResult, _: Any) -> None:
        max_tokens = self._get_max_tokens_for_agent("test_writer_agent")
        agent = TestWriterAgent(self.client, ctx, self.model, max_tokens)
        result.tests = agent.run_tests()
        logger.info("[orchestrator] tests complete (%d chars)", len(result.tests))

    def _run_review(self, ctx: ContextStore, result: PipelineResult, _: Any) -> None:
        max_tokens = self._get_max_tokens_for_agent("code_reviewer_agent")
        agent = CodeReviewerAgent(self.client, ctx, self.model, max_tokens)
        result.review = agent.run_review()
        logger.info("[orchestrator] review complete (%d chars)", len(result.review))

    def _run_devops(self, ctx: ContextStore, result: PipelineResult, _: Any) -> None:
        max_tokens = self._get_max_tokens_for_agent("devops_agent")
        agent = DevOpsAgent(self.client, ctx, self.model, max_tokens)
        result.pipeline = agent.run_pipeline()
        logger.info("[orchestrator] devops complete (%d chars)", len(result.pipeline))

    def _run_release(self, ctx: ContextStore, result: PipelineResult, _: Any) -> None:
        max_tokens = self._get_max_tokens_for_agent("release_agent")
        agent = ReleaseAgent(self.client, ctx, self.model, max_tokens)
        result.changelog = agent.run_release()
        logger.info("[orchestrator] release complete (%d chars)", len(result.changelog))
