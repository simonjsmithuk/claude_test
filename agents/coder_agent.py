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
  ### src/MyProject.API/Controllers/UserController.cs
  ```csharp
  ...
  ```
- Follow SOLID principles and keep functions small and focused.
- Include inline comments only where the logic is non-obvious.
- Do not include test files — the Test Writer agent handles those.
- Do not include CI/CD files — the DevOps agent handles those.
- Ensure the implementation satisfies every Functional Requirement in the spec.
- If a requirement is ambiguous, make a reasonable assumption and note it inline with // ASSUMPTION: ...

**.NET Core Coding Standards:**
- **Language**: C# 12+ with .NET 8.0 LTS or later
- **Style**: Follow Microsoft C# Coding Conventions
- **Nullable Reference Types**: Enable and use properly
- **Async/Await**: Use async/await for I/O operations, suffix async methods with "Async"
- **Dependency Injection**: Constructor injection for all dependencies
- **Configuration**: Use IOptions<T> pattern for strongly-typed configuration
- **Logging**: Use ILogger<T> for logging, not Console.WriteLine
- **Exception Handling**: Use specific exception types, avoid catching generic Exception
- **LINQ**: Use LINQ for collections; prefer method syntax over query syntax
- **Naming Conventions**:
  - PascalCase for classes, methods, properties, constants
  - camelCase for local variables and parameters
  - Prefix private fields with underscore: _fieldName
  - Prefix interfaces with "I" (IUserService)
  - Async methods suffix with "Async"

**Project Structure:**
- Use .NET solution with multiple projects:
  - {ProjectName}.API - ASP.NET Core Web API (Controllers, Program.cs, Startup)
  - {ProjectName}.Core - Domain models, interfaces, business logic
  - {ProjectName}.Infrastructure - Data access, external services, EF Core DbContext
- Use namespace matching folder structure
- Place DTOs in separate folder from domain models

**Framework Preferences:**
- **Web API**: ASP.NET Core Web API with minimal APIs or controllers
- **ORM**: Entity Framework Core with migrations
- **Validation**: FluentValidation or built-in DataAnnotations
- **Serialization**: System.Text.Json (not Newtonsoft.Json)
- **HTTP Client**: HttpClient with IHttpClientFactory
- **Caching**: IMemoryCache or IDistributedCache
- **Background Tasks**: IHostedService or BackgroundService

**Package Guidelines:**
- Prefer Microsoft.* and System.* packages from the framework
- Only use external NuGet packages with permissive licenses (MIT, Apache 2.0, BSD)
- Document license in comments when using external packages

**React/Redux Frontend Standards:**
- **Language**: TypeScript for type safety
- **Components**: Functional components with hooks (useState, useEffect, etc.)
- **State Management**: Redux Toolkit (createSlice, createAsyncThunk)
- **Folder Structure**:
  - /src/components - Reusable UI components
  - /src/containers - Connected container components
  - /src/redux - Store, slices, actions, selectors
  - /src/services - API client functions
  - /src/types - TypeScript interfaces and types
- **Naming**: PascalCase for components, camelCase for functions/variables
- **Styling**: CSS Modules or styled-components
- **API Calls**: Use RTK Query or axios with async thunks

**Configuration Files to Include:**
- *.csproj files for each project
- appsettings.json and appsettings.Development.json
- .editorconfig for code style
- For React: package.json, tsconfig.json

**Target Platform:**
- Linux (Ubuntu) runtime
- Dockerfile should use mcr.microsoft.com/dotnet/aspnet:8.0 base image

Output Markdown with fenced code blocks for each file."""

class CoderAgent(BaseAgent):
    name = "coder_agent"
    system_prompt = SYSTEM_PROMPT
    tools = FILE_TOOLS + GIT_TOOLS

    def run_implementation(self) -> str:
        """Run implementation - supports both full implementation and task-by-task mode."""
        spec = self.context.get_spec()
        design = self.context.get_design()
        taskplan = self.context.get("taskplan", default=None)

        if taskplan:
            # Task-by-task mode: execute each task sequentially
            return self._run_taskbased_implementation(spec, design, taskplan)
        else:
            # Legacy mode: implement everything at once
            return self._run_full_implementation(spec, design)

    def _run_full_implementation(self, spec: str, design: str) -> str:
        """Original implementation mode - implement everything in one pass."""
        impl = self.run(
            "Implement the codebase described by the following spec and design:",
            extra_context={
                "Product Specification": spec,
                "System Design": design,
            },
        )
        self.context.set_implementation(impl)
        return impl

    def _run_taskbased_implementation(self, spec: str, design: str, taskplan: str) -> str:
        """New task-based implementation - execute tasks one at a time."""
        import json
        import re

        # Strip markdown code fences if present
        taskplan_clean = taskplan
        if taskplan.startswith('```'):
            taskplan_clean = re.sub(r'^```(?:json)?\s*\n', '', taskplan)
            taskplan_clean = re.sub(r'\n```\s*$', '', taskplan_clean)

        try:
            plan = json.loads(taskplan_clean)
            tasks = plan.get("tasks", [])
        except json.JSONDecodeError as e:
            # Fall back to full implementation if taskplan is malformed
            logger.warning(f"[{self.name}] Taskplan JSON parsing failed: {e}. Falling back to full implementation.")
            return self._run_full_implementation(spec, design)

        # Sort tasks by priority
        tasks_sorted = sorted(tasks, key=lambda t: (t.get("priority", 999), t.get("id", "")))

        completed_tasks = []
        implementation_summary = []

        for task in tasks_sorted:
            task_id = task.get("id", "UNKNOWN")
            task_title = task.get("title", "Untitled")
            task_description = task.get("description", "")
            task_files = task.get("files", [])
            task_acceptance = task.get("acceptance_criteria", [])

            # Check dependencies
            deps = task.get("dependencies", [])
            if not all(dep in completed_tasks for dep in deps):
                implementation_summary.append(f"⏭️  SKIPPED {task_id}: {task_title} (dependencies not met)")
                continue

            # Execute this specific task
            result = self.run(
                f"Implement {task_id}: {task_title}",
                extra_context={
                    "Task ID": task_id,
                    "Task Description": task_description,
                    "Files to Create/Modify": "\n".join(task_files),
                    "Acceptance Criteria": "\n".join(f"- {ac}" for ac in task_acceptance),
                    "Product Specification (for reference)": spec[:2000],  # Truncated
                    "System Design (for reference)": design[:2000],  # Truncated
                },
            )

            completed_tasks.append(task_id)
            implementation_summary.append(f"✅ COMPLETED {task_id}: {task_title} ({len(task_files)} files)")

        # Store implementation summary
        impl_summary = "\n".join(implementation_summary)
        self.context.set_implementation(impl_summary)
        return impl_summary
