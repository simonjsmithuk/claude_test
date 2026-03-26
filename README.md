# AI-SDLC: Multi-Agent Software Development Lifecycle

A multi-agent framework powered by [Claude](https://anthropic.com) that automates the full software development lifecycle — from requirements to release.

## Architecture

```
User Request
     │
     ▼
┌─────────────────┐
│   Orchestrator  │  ← routes tasks, manages context store
└────────┬────────┘
         │
    ┌────┴─────────────────────────────────────┐
    │                                          │
    ▼                                          ▼
┌──────────┐  spec   ┌───────────┐  design  ┌──────────┐
│ PM Agent ├────────►│ Architect ├─────────►│  Coder   │
└──────────┘         └───────────┘          └────┬─────┘
                                                  │ implementation
                                    ┌─────────────┼──────────────┐
                                    ▼             ▼              ▼
                              ┌──────────┐ ┌──────────┐ ┌────────────┐
                              │   Test   │ │  Code    │ │   DevOps   │
                              │  Writer  │ │ Reviewer │ │   Agent    │
                              └────┬─────┘ └────┬─────┘ └─────┬──────┘
                                   │             │             │
                                   └─────────────┼─────────────┘
                                                 ▼
                                         ┌──────────────┐
                                         │ Release Agent│
                                         └──────────────┘
```

## Agents

| Agent | Role | Key Output |
|-------|------|------------|
| **PM Agent** | Requirements analysis | Product Specification Document |
| **Architect Agent** | System design | SDD + Architecture Decision Records |
| **Coder Agent** | Implementation | Source code files |
| **Test Writer Agent** | QA | Unit + integration + e2e tests |
| **Code Reviewer Agent** | Review | Code Review Report (Approve/Request Changes) |
| **DevOps Agent** | Infrastructure | Dockerfile, CI/CD pipeline, IaC |
| **Release Agent** | Release management | CHANGELOG, release notes, version bump |
| **Orchestrator** | Pipeline coordination | Routes tasks, manages shared context |

## Quick Start

```bash
# 1. Clone and install
git clone git@github.com:simonjsmithuk/claude_test.git
cd claude_test
pip install -e ".[dev]"

# 2. Configure
cp .env.example .env
# Edit .env and add your ANTHROPIC_API_KEY

# 3. Run
python main.py "Build a REST API for a task manager"
```

## Usage

```
usage: main.py [-h] [--stages STAGES] [--model MODEL] [--run-id RUN_ID]
               [--output OUTPUT] [--verbose] [--list-stages]
               [request]

positional arguments:
  request              Feature request or project description

options:
  --stages STAGES      Comma-separated stages (default: all)
  --model MODEL        Claude model ID (default: claude-sonnet-4-6)
  --run-id RUN_ID      Custom run ID for context store
  --output OUTPUT      Write results as JSON to this file
  --verbose, -v        Enable debug logging
  --list-stages        Print available stages and exit
```

### Run a subset of stages

```bash
# Only spec and design
python main.py --stages spec,design "Build a CLI weather app"

# Skip devops and release
python main.py --stages spec,design,code,tests,review "Add OAuth2 to existing API"
```

### GitHub Actions

Trigger via workflow dispatch:
1. Go to **Actions → AI-SDLC Pipeline → Run workflow**
2. Enter your feature request
3. Results are uploaded as workflow artifacts and (if triggered by issue) posted as a comment

Or label a GitHub issue with `sdlc-run` to trigger automatically.

## Configuration

Edit `config/agents.yml` to:
- Enable/disable stages
- Override the model per agent (e.g. use Opus for complex reasoning, Haiku for cheap stages)
- Adjust token budgets

## Environment Variables

| Variable | Required | Description |
|----------|----------|-------------|
| `ANTHROPIC_API_KEY` | Yes | Your Anthropic API key |
| `GITHUB_TOKEN` | No | GitHub PAT for PR/issue tools |

## Project Structure

```
.
├── agents/
│   ├── base.py              # BaseAgent with tool-use loop
│   ├── orchestrator.py      # Pipeline orchestration
│   ├── pm_agent.py          # Requirements → Spec
│   ├── architect_agent.py   # Spec → Design
│   ├── coder_agent.py       # Design → Code
│   ├── test_writer_agent.py # Code → Tests
│   ├── code_reviewer_agent.py # Review report
│   ├── devops_agent.py      # CI/CD + IaC
│   └── release_agent.py     # Changelog + release notes
├── tools/
│   ├── file_tools.py        # Read/write/search files
│   ├── git_tools.py         # Git operations
│   └── github_tools.py      # GitHub API (PRs, issues)
├── context/
│   └── store.py             # File-backed inter-agent context store
├── config/
│   └── agents.yml           # Stage and model configuration
├── .github/workflows/
│   └── sdlc.yml             # GitHub Actions pipeline
├── main.py                  # CLI entrypoint
└── pyproject.toml
```

## Extending

To add a new agent:
1. Create `agents/my_agent.py` subclassing `BaseAgent`
2. Set `name`, `system_prompt`, and `tools`
3. Add a `run_<stage>()` method that reads/writes to `self.context`
4. Register it in `OrchestratorAgent.run()` and `config/agents.yml`
