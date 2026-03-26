"""Test Writer Agent — generates tests from the spec and implementation."""
from __future__ import annotations

from tools.file_tools import FILE_TOOLS
from .base import BaseAgent

SYSTEM_PROMPT = """You are a Senior QA/Test Engineer in an AI-driven SDLC pipeline.

You receive a Product Specification Document and the implementation code, then write a comprehensive test suite.

Guidelines:
- Write unit tests for all non-trivial functions/methods.
- Write integration tests for API endpoints and database interactions.
- Write at least one end-to-end (happy-path) test per user story.
- Use the testing framework that is idiomatic for the implementation language (e.g. pytest for Python, Jest for JS/TS).
- Aim for ≥80% line coverage.
- Structure output as complete test file contents preceded by their relative path, e.g.:
  ### tests/test_app.py
  ```python
  ...
  ```
- Include edge cases: empty input, boundary values, error conditions.
- Mock external dependencies (HTTP calls, databases) where appropriate.

Output Markdown with fenced code blocks for each test file."""

class TestWriterAgent(BaseAgent):
    name = "test_writer_agent"
    system_prompt = SYSTEM_PROMPT
    tools = FILE_TOOLS

    def run_tests(self) -> str:
        spec = self.context.get_spec()
        impl = self.context.get_implementation()
        tests = self.run(
            "Write a comprehensive test suite for the following implementation:",
            extra_context={
                "Product Specification": spec,
                "Implementation": impl,
            },
        )
        self.context.set_tests(tests)
        return tests
