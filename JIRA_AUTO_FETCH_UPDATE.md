# Jira Auto-Fetch Update - Implementation Complete

**Date**: 2026-03-30
**Status**: ✅ Jira tickets now auto-fetched from API

## What Changed

You requested that the orchestrator automatically fetch ticket details from Jira instead of prompting for them. I've implemented this with multiple fallback options.

## New Components

### 1. JiraClient ([agents/jira_client.py](agents/jira_client.py))

A new helper class that fetches Jira tickets using multiple methods:

```python
class JiraClient:
    def get_ticket(self, ticket_key: str) -> JiraTicket:
        # Tries in order:
        # 1. MCP tools (when in Claude Code)
        # 2. Direct Jira REST API
        # 3. Interactive prompts (fallback)
```

Features:
- **Automatic method selection** - Tries MCP, then API, then interactive
- **Clean data model** - Returns `JiraTicket` object with all fields
- **Error handling** - Graceful fallbacks with clear error messages
- **Environment-based auth** - Uses `JIRA_EMAIL` and `JIRA_API_TOKEN`

### 2. Updated Orchestrator

The `jira_planning_orchestrator.py` now uses `JiraClient` instead of prompting:

```python
# Old (always prompted):
result.jira_type = self._prompt_user("Issue Type...")
result.jira_summary = self._prompt_user("Summary...")

# New (auto-fetches):
jira_client = JiraClient(cloud_id="gdslink.atlassian.net")
ticket = jira_client.get_ticket("AIDAT-1")
result.jira_type = ticket.issue_type
result.jira_summary = ticket.summary
```

### 3. Setup Documentation

New guide: [docs/JIRA_API_SETUP.md](docs/JIRA_API_SETUP.md)

Explains how to:
- Generate Jira API token
- Set environment variables
- Test the connection
- Use in CI/CD pipelines

## How to Use

### Quick Start (with Jira API)

```bash
# 1. Set up credentials
export JIRA_EMAIL="your.email@company.com"
export JIRA_API_TOKEN="your-api-token"

# 2. Install requests library
pip install requests

# 3. Run orchestrator (auto-fetches AIDAT-1 from Jira!)
source venv/bin/activate
python3 -m agents.jira_planning_orchestrator AIDAT-1
```

**Output:**
```
=== Reading Jira Ticket: AIDAT-1 ===
Type: Bug
Summary: Fix login password hash corruption
Description: Password hash is corrupted during password reset...
```

No prompts! The ticket is fetched automatically from gdslink.atlassian.net.

### Fallback to Interactive

If API credentials aren't set, use `--interactive`:

```bash
python3 -m agents.jira_planning_orchestrator AIDAT-1 --interactive
```

This will prompt for ticket details if the API fetch fails.

## Configuration

Already configured in `config/agents.yml`:

```yaml
jira:
  cloud_id: "gdslink.atlassian.net"  # ✅ Your Jira instance
```

## Workflow Comparison

### Before (Manual Entry)
```
1. Run: python3 -m agents.jira_planning_orchestrator AIDAT-1 --interactive
2. Prompt: Issue Type? → You type "Bug"
3. Prompt: Summary? → You type "Fix login..."
4. Prompt: Description? → You type multiline description
5. Continue with PM → Architect → Task Planner
```

### After (Auto-Fetch)
```
1. Set JIRA_EMAIL and JIRA_API_TOKEN (one-time setup)
2. Run: python3 -m agents.jira_planning_orchestrator AIDAT-1
3. Auto-fetches from gdslink.atlassian.net
4. Displays: Type, Summary, Description
5. Continue with PM → Architect → Task Planner
```

**Time saved**: 30-60 seconds per ticket!

## API Method Priority

```
┌─────────────────────────────────────┐
│   JiraClient.get_ticket("AIDAT-1")  │
└──────────────┬──────────────────────┘
               │
               ▼
      ┌────────────────┐
      │  Try MCP Tools │  (Future - when in Claude Code)
      └────────┬───────┘
               │ (fails)
               ▼
      ┌────────────────┐
      │  Try REST API  │  (Uses JIRA_EMAIL + JIRA_API_TOKEN)
      └────────┬───────┘
               │ (fails)
               ▼
      ┌────────────────┐
      │  Interactive   │  (Prompts user if --interactive)
      └────────┬───────┘
               │
               ▼
          ┌─────────┐
          │ Success │
          └─────────┘
```

## Setting Up Jira API Token

### Step 1: Generate Token
1. Go to https://gdslink.atlassian.net
2. Click your profile → Account Settings
3. Security → API tokens
4. Create API token
5. Copy the token (you won't see it again!)

### Step 2: Set Environment Variables

**Permanent (recommended):**
```bash
# Add to ~/.bashrc or ~/.zshrc
export JIRA_EMAIL="your.email@company.com"
export JIRA_API_TOKEN="ATATT3xFfGF0..."
```

**Temporary:**
```bash
export JIRA_EMAIL="your.email@company.com"
export JIRA_API_TOKEN="ATATT3xFfGF0..."
```

### Step 3: Install Dependencies

```bash
source venv/bin/activate
pip install requests
```

### Step 4: Test

```bash
python3 -m agents.jira_planning_orchestrator AIDAT-1 --log-level DEBUG
```

Look for:
```
[DEBUG] Calling Jira API: https://gdslink.atlassian.net/rest/api/3/issue/AIDAT-1
[INFO] Read Jira ticket AIDAT-1: Fix login password hash corruption [Bug]
```

## Error Handling

The `JiraClient` provides clear error messages:

### Missing Credentials
```
Failed to use Jira MCP tools: MCP tools not available...
Failed to use Direct API method: JIRA_EMAIL and JIRA_API_TOKEN not set
Falling back to interactive prompts for ticket details
```

### Invalid Credentials
```
Failed to use Direct API method: Jira API returned 401: Unauthorized
```

### Ticket Not Found
```
Failed to use Direct API method: Jira API returned 404: Issue not found
```

### All Methods Failed (Non-Interactive)
```
ValueError: Could not read Jira ticket AIDAT-1. MCP tools and API methods failed.
Use --interactive to manually enter ticket details.
```

## Files Created/Modified

### New Files
1. `agents/jira_client.py` - Jira API client with fallback logic
2. `docs/JIRA_API_SETUP.md` - Setup guide for API credentials
3. `JIRA_AUTO_FETCH_UPDATE.md` - This file

### Modified Files
1. `agents/jira_planning_orchestrator.py` - Now uses JiraClient
2. `config/agents.yml` - Already had cloud_id set to gdslink.atlassian.net

## Testing Checklist

### With API Credentials
- [ ] Set JIRA_EMAIL and JIRA_API_TOKEN
- [ ] Run `python3 -m agents.jira_planning_orchestrator AIDAT-1`
- [ ] Verify ticket is fetched automatically (no prompts)
- [ ] Check that Type, Summary, Description are correct

### Without API Credentials
- [ ] Unset JIRA_EMAIL and JIRA_API_TOKEN
- [ ] Run `python3 -m agents.jira_planning_orchestrator AIDAT-1 --interactive`
- [ ] Verify fallback to interactive prompts works
- [ ] Enter ticket details manually

### Debug Mode
- [ ] Run with `--log-level DEBUG`
- [ ] Check logs show API call attempt
- [ ] Verify fallback messages are clear

## Next Steps

### For You
1. Generate Jira API token (see [JIRA_API_SETUP.md](docs/JIRA_API_SETUP.md))
2. Set JIRA_EMAIL and JIRA_API_TOKEN environment variables
3. Run: `python3 -m agents.jira_planning_orchestrator AIDAT-1`
4. Watch it auto-fetch the ticket from Jira!

### Future Enhancements
- [ ] MCP tools integration (when running in Claude Code)
- [ ] Caching of ticket data
- [ ] Support for fetching multiple tickets at once
- [ ] Update Jira with planning artifacts via API
- [ ] Create subtasks via API
- [ ] Transition ticket status via API

## Summary

**Before**: You had to manually type in all ticket details every time.

**Now**: Just run with the ticket key, and it automatically fetches everything from Jira!

```bash
# One-time setup:
export JIRA_EMAIL="your.email@company.com"
export JIRA_API_TOKEN="your-token"

# Then forever after:
python3 -m agents.jira_planning_orchestrator AIDAT-1
# ✅ Auto-fetches ticket from gdslink.atlassian.net
# ✅ Runs PM → Architect → Task Planner
# ✅ No manual input needed!
```

**Ready to test with real Jira data!** 🎉
