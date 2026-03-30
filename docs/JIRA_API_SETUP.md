# Jira API Setup for Planning Orchestrator

The Jira Planning Orchestrator can read tickets from Jira automatically using either:

1. **Jira REST API** (recommended for standalone/CI use)
2. **Interactive prompts** (fallback when API is not available)

## Option 1: Jira REST API (Recommended)

### Step 1: Generate Jira API Token

1. Log in to https://gdslink.atlassian.net
2. Go to Account Settings → Security → API tokens
3. Click "Create API token"
4. Give it a name like "AI-SDLC Pipeline"
5. Copy the generated token (you won't be able to see it again!)

### Step 2: Set Environment Variables

```bash
# Add to your ~/.bashrc or ~/.zshrc
export JIRA_EMAIL="your.email@company.com"
export JIRA_API_TOKEN="your-api-token-here"
```

Or set them temporarily for a session:

```bash
export JIRA_EMAIL="your.email@company.com"
export JIRA_API_TOKEN="ATATT3xFfGF0..."
```

### Step 3: Install Python Requests Library

```bash
source venv/bin/activate
pip install requests
```

### Step 4: Test the Connection

```bash
# This will automatically fetch AIDAT-1 from Jira
python3 -m agents.jira_planning_orchestrator AIDAT-1 --log-level DEBUG
```

You should see:

```
=== Reading Jira Ticket: AIDAT-1 ===
Type: Bug
Summary: <actual summary from Jira>
Description: <actual description from Jira>
```

## Option 2: Interactive Mode (Fallback)

If you don't have API credentials set up, use interactive mode:

```bash
python3 -m agents.jira_planning_orchestrator AIDAT-1 --interactive
```

The orchestrator will prompt you to enter ticket details manually.

## How It Works

The `JiraClient` tries three methods in order:

1. **MCP Tools** (when running in Claude Code) - Not yet implemented
2. **Direct API** (when `JIRA_EMAIL` and `JIRA_API_TOKEN` are set)
3. **Interactive** (when `--interactive` flag is used)

## Configuration

The Jira cloud ID is already configured in `config/agents.yml`:

```yaml
jira:
  cloud_id: "gdslink.atlassian.net"
```

## Troubleshooting

### "JIRA_EMAIL and JIRA_API_TOKEN environment variables not set"

Solution: Set the environment variables as shown in Step 2 above.

### "Jira API returned 401"

Solution: Your API token may be invalid or expired. Generate a new one.

### "Jira API returned 404"

Solution: The ticket key may be wrong, or you don't have permission to view it.

### "No module named 'requests'"

Solution: Install the requests library:
```bash
pip install requests
```

## Security Notes

- **Never commit API tokens to git!**
- Use environment variables or a secrets manager
- API tokens have the same permissions as your user account
- Rotate tokens periodically
- Use least-privilege accounts for automation

## Example: Complete Workflow with API

```bash
# 1. Set up environment
export JIRA_EMAIL="your.email@company.com"
export JIRA_API_TOKEN="ATATT3xFfGF0..."

# 2. Activate venv
source venv/bin/activate

# 3. Run orchestrator (will auto-fetch AIDAT-1)
python3 -m agents.jira_planning_orchestrator AIDAT-1

# The orchestrator will:
# - Fetch ticket details from Jira automatically
# - Run PM → Architect → Task Planner
# - Generate spec, design, and task breakdown
# - Save to .sdlc_runs/jira_aidat-1_*/
```

## Example: Interactive Mode (No API)

```bash
# 1. Activate venv
source venv/bin/activate

# 2. Run with interactive flag
python3 -m agents.jira_planning_orchestrator AIDAT-1 --interactive

# You'll be prompted for:
# - Issue Type: Bug
# - Summary: Fix login password hash corruption
# - Description: (multiline input)
```

## Advanced: Using with CI/CD

Store credentials in your CI/CD secrets and run:

```yaml
# Example GitHub Actions
- name: Run Jira Planning
  env:
    JIRA_EMAIL: ${{ secrets.JIRA_EMAIL }}
    JIRA_API_TOKEN: ${{ secrets.JIRA_API_TOKEN }}
  run: |
    source venv/bin/activate
    python3 -m agents.jira_planning_orchestrator ${{ github.event.issue.key }}
```

## Next Steps

Once the ticket is read automatically:
- PM → Architect → Task Planner pipeline runs
- Spec, design, and taskplan are generated
- Ready to implement with coder agents!
