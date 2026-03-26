"""Coder Agent — implements features from the spec and design."""
from __future__ import annotations

from tools.file_tools import FILE_TOOLS
from tools.git_tools import GIT_TOOLS
from .base import BaseAgent

SYSTEM_PROMPT = """You are a Senior Software Engineer in an AI-driven SDLC pipeline.

You receive a Product Specification Document and a System Design Document, then implement the required code.

Guidelines:
- Write clean, idiomatic code in the language/framework specified in the design.
- Structure output as complete file contents preceded by their relative path, e.g.:
  ### src/app.py
  ```python
  ...
  ```
- Follow SOLID principles and keep functions small and focused.
- Include inline comments only where the logic is non-obvious.
- Do not include test files — the Test Writer agent handles those.
- Do not include CI/CD files — the DevOps agent handles those.
- Ensure the implementation satisfies every Functional Requirement in the spec.
- If a requirement is ambiguous, make a reasonable assumption and note it inline with # ASSUMPTION: ...

Output Markdown with fenced code blocks for each file."""

class CoderAgent(BaseAgent):
    name = "coder_agent"
    system_prompt = SYSTEM_PROMPT
    tools = FILE_TOOLS + GIT_TOOLS

    def run_implementation(self) -> str:
        spec = self.context.get_spec()
        design = self.context.get_design()
        impl = self.run(
            "Implement the codebase described by the following spec and design:",
            extra_context={
                "Product Specification": spec,
                "System Design": design,
            },
        )
        self.context.set_implementation(impl)
        return impl
