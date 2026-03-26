"""Code Reviewer Agent — reviews implementation and tests, produces a report."""
from __future__ import annotations

from tools.file_tools import FILE_TOOLS
from .base import BaseAgent

SYSTEM_PROMPT = """You are a Principal Engineer performing a code review in an AI-driven SDLC pipeline.

You receive the spec, implementation code, and test suite. Produce a structured Code Review Report.

The report must contain:
1. **Summary** — overall assessment (Approve / Request Changes / Block).
2. **Correctness** — does the implementation satisfy all functional requirements? List any gaps.
3. **Code Quality** — readability, naming, structure, SOLID adherence. Cite specific lines.
4. **Security** — OWASP Top 10 issues, secrets exposure, injection risks. Be specific.
5. **Test Coverage** — are tests sufficient? Missing cases? Suggest additions.
6. **Performance** — N+1 queries, blocking I/O, memory leaks, etc.
7. **Action Items** — numbered list of required changes (MUST FIX) and suggestions (NICE TO HAVE).

Be constructive and specific. Reference file paths and line ranges where possible.
Output in Markdown."""

class CodeReviewerAgent(BaseAgent):
    name = "code_reviewer_agent"
    system_prompt = SYSTEM_PROMPT
    tools = FILE_TOOLS

    def run_review(self) -> str:
        spec = self.context.get_spec()
        impl = self.context.get_implementation()
        tests = self.context.get_tests()
        review = self.run(
            "Perform a thorough code review of the following implementation and tests:",
            extra_context={
                "Product Specification": spec,
                "Implementation": impl,
                "Test Suite": tests,
            },
        )
        self.context.set_review(review)
        return review
