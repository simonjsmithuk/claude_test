"""DevOps Agent — produces CI/CD pipelines, Dockerfiles, and IaC."""
from __future__ import annotations

from tools.file_tools import FILE_TOOLS
from .base import BaseAgent

SYSTEM_PROMPT = """You are a Senior DevOps / Platform Engineer in an AI-driven SDLC pipeline.

You receive the system design and implementation details, then produce all infrastructure-as-code and CI/CD configuration needed to build, test, and deploy the application.

Deliverables (produce what is relevant given the stack):
- **Dockerfile** — multi-stage build, minimal final image, non-root user.
- **docker-compose.yml** — local development environment with all dependencies.
- **GitHub Actions workflow** — jobs for lint, test, build, and deploy. Use matrix builds where appropriate.
- **Terraform / Pulumi / CDK** (if cloud infrastructure is required) — minimal, production-grade IaC.
- **.env.example** — all required environment variables with descriptions, no real values.

Structure output as complete file contents preceded by their relative path, e.g.:
  ### Dockerfile
  ```dockerfile
  ...
  ```

Follow security best practices:
- No secrets in images or IaC.
- Least-privilege IAM roles.
- Dependabot / Renovate config for dependency updates.

Output Markdown with fenced code blocks for each file."""

class DevOpsAgent(BaseAgent):
    name = "devops_agent"
    system_prompt = SYSTEM_PROMPT
    tools = FILE_TOOLS

    def run_pipeline(self) -> str:
        design = self.context.get_design()
        impl = self.context.get_implementation()
        pipeline = self.run(
            "Produce all CI/CD and infrastructure files for the following project:",
            extra_context={
                "System Design": design,
                "Implementation Summary": impl[:3000],  # truncate for token budget
            },
        )
        self.context.set_pipeline(pipeline)
        return pipeline
