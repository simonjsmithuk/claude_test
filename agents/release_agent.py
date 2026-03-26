"""Release Agent — drafts changelog, bumps version, and prepares release notes."""
from __future__ import annotations

from tools.file_tools import FILE_TOOLS
from tools.git_tools import GIT_TOOLS
from tools.github_tools import GITHUB_TOOLS
from .base import BaseAgent

SYSTEM_PROMPT = """You are a Release Manager in an AI-driven SDLC pipeline.

You receive the spec, review report, and pipeline status, then produce all release artefacts.

Deliverables:
1. **CHANGELOG entry** — in Keep a Changelog format (https://keepachangelog.com). Sections: Added, Changed, Fixed, Removed, Security.
2. **Release Notes** — human-readable summary suitable for a GitHub Release. Include migration steps if any.
3. **Version bump recommendation** — Semantic Versioning (major/minor/patch) with justification.
4. **Release checklist** — Markdown checklist of manual steps (smoke tests, feature flags, monitoring checks).

Be concise. Focus on what changed and why it matters to end users.
Output in Markdown."""

class ReleaseAgent(BaseAgent):
    name = "release_agent"
    system_prompt = SYSTEM_PROMPT
    tools = FILE_TOOLS + GIT_TOOLS + GITHUB_TOOLS

    def run_release(self) -> str:
        spec = self.context.get_spec()
        review = self.context.get_review()
        pipeline = self.context.get_pipeline()
        changelog = self.run(
            "Produce all release artefacts for this completed SDLC cycle:",
            extra_context={
                "Product Specification": spec,
                "Code Review": review,
                "Pipeline Config": pipeline[:2000],
            },
        )
        self.context.set_changelog(changelog)
        return changelog
