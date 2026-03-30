# Jira Planning Orchestrator

## Overview

The Jira Planning Orchestrator automates the first three phases of the AI-SDLC pipeline:
1. **PM Agent** - Creates Product Specification Document
2. **Architect Agent** - Designs system architecture and ADRs
3. **Task Planner Agent** - Breaks down work into implementable tasks

It integrates directly with Jira to:
- Read ticket details (Bug, Task, or User Story)
- Update tickets with specifications and designs
- Create subtasks from the task breakdown
- Transition tickets through workflow states

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                Jira Planning Orchestrator                    │
│                                                               │
│  1. Read Jira Ticket (AIDAT-1)                              │
│          │                                                    │
│          ▼                                                    │
│  2. PM Agent (if Task/Story, skip for Bugs)                 │
│          │                                                    │
│          ▼                                                    │
│  3. Update Jira with Specification                          │
│          │                                                    │
│          ▼                                                    │
│  4. Architect Agent                                          │
│          │                                                    │
│          ▼                                                    │
│  5. Update Jira with Design                                 │
│          │                                                    │
│          ▼                                                    │
│  6. Task Planner Agent                                       │
│          │                                                    │
│          ▼                                                    │
│  7. Create Jira Subtasks                                    │
│          │                                                    │
│          ▼                                                    │
│  8. Transition: "Awaiting Estimate" → "Open"                │
└─────────────────────────────────────────────────────────────┘
```

## Prerequisites

1. **Anthropic API Key**: Set `ANTHROPIC_API_KEY` environment variable
2. **Jira Access**: Authenticate with Atlassian MCP (Claude Code handles this)
3. **Configuration**: Configure `config/agents.yml` (optional)

## Installation

The orchestrator is part of the AI-SDLC agents package. No additional installation needed.

```bash
# Ensure you're in the project root
cd /path/to/claude_test

# Verify agents exist
ls agents/jira_planning_orchestrator.py
```

## Configuration

Edit `config/agents.yml` to configure Jira settings:

```yaml
# Jira Integration Configuration
jira:
  # Cloud ID or site URL (e.g., "yoursite.atlassian.net")
  cloud_id: "yoursite.atlassian.net"

  # Workflow transitions
  transitions:
    awaiting_estimate_to_open: "Open"
    open_to_in_progress: "In Progress"
    in_progress_to_done: "Done"

  # Custom field mappings (optional)
  custom_fields:
    story_points: "customfield_10016"
    epic_link: "customfield_10014"
```

**Optional**: Leave `cloud_id` empty and use `--interactive` mode to be prompted.

## Usage

### Interactive Mode (Recommended for First Use)

```bash
python3 -m agents.jira_planning_orchestrator AIDAT-1 --interactive
```

This will:
1. Prompt for Jira cloud ID (if not configured)
2. Prompt for ticket details (in development, will use Jira API when integrated)
3. Show previews of each agent's output
4. Ask for confirmation before updating Jira
5. Create subtasks with user confirmation

### Non-Interactive Mode

```bash
# With cloud ID in config
python3 -m agents.jira_planning_orchestrator AIDAT-1

# Or specify cloud ID on command line
python3 -m agents.jira_planning_orchestrator AIDAT-1 --cloud-id yoursite.atlassian.net
```

### Custom Model

```bash
# Use Claude Opus for better quality (costs more)
python3 -m agents.jira_planning_orchestrator AIDAT-1 --model claude-opus-4-6 --interactive
```

### Debug Mode

```bash
python3 -m agents.jira_planning_orchestrator AIDAT-1 --interactive --log-level DEBUG
```

## Workflow Example

Let's say you have a Jira ticket `AIDAT-1` with status "Awaiting Estimate":

```
AIDAT-1: Fix login password hash corruption
Type: Bug
Status: Awaiting Estimate
Description: Password hash is corrupted during reset due to shell expansion...
```

### Step 1: Run the Orchestrator

```bash
$ python3 -m agents.jira_planning_orchestrator AIDAT-1 --interactive

=== Jira Planning Pipeline starting for AIDAT-1 ===

=== Reading Jira Ticket: AIDAT-1 ===
Issue Type (Bug/Task/Story) [Bug]:
Summary [Example issue]: Fix login password hash corruption
Description (multiline, end with empty line):
Password hash is corrupted during password reset due to shell expansion bug.
The exclamation mark in 'Admin123!' is being interpreted by bash.
<enter>

[INFO] Read Jira ticket AIDAT-1: Fix login password hash corruption [Bug]
```

### Step 2: PM Agent (Skipped for Bugs)

```
This is a bug. Skip PM agent and use bug description as spec? (y/n) [y]: y
[INFO] Skipped PM agent for bug, using description as spec
```

### Step 3: Architect Agent

```
[INFO] Running Architect agent...
[INFO] Architect agent complete (3847 chars)

=== Design Generated (3847 chars) ===
# System Design: Password Hash Fix

## Architecture Overview
This is a bug fix requiring changes to the password reset script...

[Preview truncated]

Update Jira ticket with design? (y/n) [y]: y
[INFO] Design would be added to Jira ticket
```

### Step 4: Task Planner Agent

```
[INFO] Running Task Planner agent...
[INFO] Task Planner agent complete (2156 chars)

=== Task Plan Generated: 3 tasks ===
1. [TASK-001] Fix shell expansion in reset_admin_password.sh (Priority 1)
2. [TASK-002] Test password hash generation (Priority 2)
3. [TASK-003] Update documentation (Priority 3)

Create 3 Jira subtasks? (y/n) [y]: y
[INFO] Would create subtask: AIDAT-SUBTASK-TASK-001 - Fix shell expansion...
[INFO] Would create subtask: AIDAT-SUBTASK-TASK-002 - Test password hash...
[INFO] Would create subtask: AIDAT-SUBTASK-TASK-003 - Update documentation
```

### Step 5: Transition Ticket

```
Transition AIDAT-1 to 'Open' status? (y/n) [y]: y
[INFO] Ticket would be transitioned to Open

================================================================================
# Jira Planning Run: AIDAT-1
**Type**: Bug
**Summary**: Fix login password hash corruption

- ✓ **spec** (0.5s)
- ✓ **design** (12.3s)
- ✓ **taskplan** (8.7s)

## Subtasks Created (3)
- AIDAT-SUBTASK-TASK-001
- AIDAT-SUBTASK-TASK-002
- AIDAT-SUBTASK-TASK-003
================================================================================
```

## Agent Behavior by Ticket Type

### Bug
- **PM Agent**: Skipped (uses bug description as spec)
- **Architect Agent**: Analyzes impact, proposes fix approach
- **Task Planner**: Breaks fix into implementation steps

### Task
- **PM Agent**: Expands task into full specification with requirements
- **Architect Agent**: Designs technical approach
- **Task Planner**: Creates detailed implementation tasks

### User Story
- **PM Agent**: Converts story into PSD with acceptance criteria
- **Architect Agent**: Designs feature architecture
- **Task Planner**: Breaks feature into vertical slices

## Integration with Full SDLC Pipeline

After planning is complete, you can continue with implementation:

```bash
# Option 1: Run full pipeline from existing context
python3 -m agents.orchestrator --run-id jira_aidat-1_1234567890 --stages code,tests,review

# Option 2: Use task-level orchestrator for specific tasks
python3 -m agents.task_level_orchestrator jira_aidat-1_1234567890 TASK-001
```

## Output Artifacts

All artifacts are stored in `.sdlc_runs/jira_{ticket}_{timestamp}/`:

```
.sdlc_runs/jira_aidat-1_1711234567/
├── spec.json           # Product Specification
├── design.json         # System Design Document
└── taskplan.json       # Task Breakdown (JSON)
```

## Jira Updates (TODO - Requires MCP Integration)

The orchestrator will update Jira as follows:

1. **Specification**: Added as comment with "## Product Specification" header
2. **Design**: Added as comment with "## System Design" header
3. **Subtasks**: Created as child issues with:
   - Summary from task title
   - Description from task description
   - Labels: `ai-generated`, `phase:{phase}`, `priority:{priority}`
   - Estimated story points (if configured)
4. **Transition**: Status changed from "Awaiting Estimate" → "Open"

## Troubleshooting

### "Jira cloud ID not configured"
- Add `cloud_id` to `config/agents.yml` under `jira:` section
- Or use `--interactive` mode to be prompted
- Or pass `--cloud-id yoursite.atlassian.net` on command line

### "Cannot read Jira ticket in non-interactive mode"
- The Jira MCP integration is not yet fully implemented
- Use `--interactive` mode to manually enter ticket details
- Or wait for MCP integration to be completed

### "PM agent failed"
- Check `--log-level DEBUG` for detailed error
- Verify `ANTHROPIC_API_KEY` is set correctly
- Check token limits in `config/agents.yml` (pm_agent should have 16384)

### "Task Planner returned invalid JSON"
- This can happen with very large projects
- Increase `agent_max_tokens.taskplanner_agent` in config
- Or break down the ticket into smaller scopes

## Command Line Options

```
python3 -m agents.jira_planning_orchestrator [OPTIONS] TICKET_KEY

Arguments:
  TICKET_KEY             Jira ticket key (e.g., AIDAT-1)

Options:
  -i, --interactive      Enable interactive mode (prompts for missing info)
  --cloud-id ID          Jira cloud ID or site URL
  --model MODEL          Claude model to use (default: claude-sonnet-4-6)
  --log-level LEVEL      Logging level: DEBUG, INFO, WARNING, ERROR
  -h, --help             Show help message
```

## Examples

### Bug Fix Planning
```bash
python3 -m agents.jira_planning_orchestrator PROJ-123 --interactive
```

### Feature Development Planning
```bash
python3 -m agents.jira_planning_orchestrator PROJ-456 \
  --interactive \
  --model claude-opus-4-6 \
  --cloud-id mycompany.atlassian.net
```

### Quick Planning (Non-Interactive)
```bash
# Assumes cloud_id configured in agents.yml
python3 -m agents.jira_planning_orchestrator PROJ-789
```

## Next Steps

After planning is complete:

1. **Review the Generated Plan**: Check `.sdlc_runs/jira_*/` for spec, design, and taskplan
2. **Refine if Needed**: Edit the JSON files manually if adjustments are needed
3. **Implement Tasks**: Use the task-level orchestrator or coder agent:
   ```bash
   python3 -m agents.task_level_orchestrator {run_id} TASK-001
   ```
4. **Update Jira**: Manually update subtask status as work progresses
5. **Review and Deploy**: Use code reviewer and devops agents when implementation is complete

## Future Enhancements

- [ ] Full Jira MCP integration (read/write tickets)
- [ ] Automatic subtask creation via Jira API
- [ ] Workflow state detection and auto-transition
- [ ] Story point estimation from task complexity
- [ ] Link to epic/parent automatically
- [ ] Attach spec/design as Jira attachments
- [ ] Support for custom fields and Jira extensions
- [ ] Bi-directional sync (update agents from Jira comments)
- [ ] Slack/Teams notifications on completion

## See Also

- [Main Orchestrator](../agents/orchestrator.py) - Full SDLC pipeline
- [PM Agent](../agents/pm_agent.py) - Product specification
- [Architect Agent](../agents/architect_agent.py) - System design
- [Task Planner Agent](../agents/taskplanner_agent.py) - Task breakdown
- [Agent Configuration](../config/agents.yml) - Configuration file
