# Implementation Pipeline - Automated Coding to Merge

**Date**: 2026-03-30
**Status**: ✅ Ready to use

## Overview

The **Jira Implementation Orchestrator** automates the complete coding-to-merge workflow:

```
Jira Ticket (Estimated)
  ↓
1. Transition to "In Progress"
  ↓
2. Create Git branch: <base>_<ticket>_<description>
  ↓
3. Run Coder Agent (implements code)
  ↓
4. Run Test Writer Agent (creates tests)
  ↓
5. Commit & push to origin
  ↓
6. Create GitHub PR
  ↓
7. Run Code Reviewer Agent
  ↓
8a. ✅ Review APPROVED → Merge PR → Move ticket to "Included In Build"
8b. ❌ Review FAILED → Add comments to PR → Retry (max 3 iterations)
```

## Quick Start

### Prerequisites

1. **Planning phase completed** - Run `./plan.sh AIDAT-1` first to create spec, design, and taskplan
2. **Ticket status** - Ticket should be in "Estimated" or "Open" status
3. **Environment variables** - Set in `~/.bashrc`:
   ```bash
   export JIRA_EMAIL="your.email@company.com"
   export JIRA_API_TOKEN="ATATT3xFfGF0..."
   export ANTHROPIC_API_KEY="sk-ant-api03-..."
   ```
4. **GitHub CLI** - Install `gh` CLI and authenticate:
   ```bash
   gh auth login
   ```

### Run Implementation Pipeline

```bash
# Simple: Use main as base branch
./implement.sh AIDAT-1

# Advanced: Specify base branch (e.g., for sprint branches)
./implement.sh AIDAT-1 --base-branch sprint10

# With custom settings
./implement.sh AIDAT-1 \
  --base-branch sprint10 \
  --max-iterations 5 \
  --log-level DEBUG \
  --interactive
```

## Complete Workflow Example

### Step 1: Planning (Already Done)

You've already run:
```bash
./plan.sh AIDAT-1
```

**Result:**
- ✅ Spec added to Jira
- ✅ Design added to Jira
- ✅ Ticket transitioned to "Estimated"

### Step 2: Implementation (New!)

```bash
./implement.sh AIDAT-1 --base-branch main
```

**What Happens:**

#### Iteration 1

**1. Jira Transition**
```
Status: Estimated → In Progress
```

**2. Git Branch Created**
```bash
git checkout main
git pull origin main
git checkout -b main_aidat-1_logout_button_does

# Branch naming: <base>_<ticket>_<first-3-words-of-summary>
```

**3. Coder Agent Runs**
- Reads spec and design from context store (`.sdlc_runs/jira_aidat-1_*/`)
- Implements the 18 tasks from the taskplan
- Creates/modifies files according to design

**4. Test Writer Agent Runs**
- Reads the implemented code
- Generates comprehensive unit and integration tests
- Follows testing best practices

**5. Commit & Push**
```bash
git add .
git commit -m "AIDAT-1: Logout button does not work (iteration 1)"
git push -u origin main_aidat-1_logout_button_does
```

**6. Create GitHub PR**
```
Title: AIDAT-1: Logout button does not work
Body:
  ## Jira Ticket
  AIDAT-1

  ## Summary
  Logout button does not work

  ## Implementation
  This PR implements the changes specified in the Jira ticket.

  Iteration: 1/3

  🤖 Generated with AI-SDLC Pipeline
```

**7. Code Reviewer Agent Runs**
- Reviews all changes in the PR
- Checks code quality, test coverage, adherence to design
- Returns APPROVED or CHANGES_REQUESTED

**8a. If APPROVED:**
```bash
gh pr merge <PR#> --squash --delete-branch
```
- PR merged to main
- Feature branch deleted
- Jira ticket → "Included In Build"
- **DONE! ✅**

**8b. If CHANGES_REQUESTED:**
- Review comments added to PR
- Moves to **Iteration 2**
- Coder agent addresses feedback
- Process repeats (max 3 iterations)

## Configuration

Edit [config/agents.yml](config/agents.yml#L66-L79):

```yaml
jira:
  cloud_id: "gdslink.atlassian.net"

  transitions:
    # Planning phase
    awaiting_estimate_to_open: "Estimated"

    # Implementation phase
    open_to_in_progress: "In Progress"
    in_progress_to_done: "Included In Build"
```

**To find your workflow's transition names:**
1. Go to Jira → Project Settings → Workflows
2. View your workflow diagram
3. Note the transition names between statuses
4. Update the config with exact names (case-sensitive)

## Branch Naming Convention

Format: `<base_branch>_<ticket_key>_<short_description>`

**Examples:**
```
main_aidat-1_logout_button_does
sprint10_aidat-2_fix_token_revocation
feature-auth_aidat-3_implement_jwt_middleware
```

**Rules:**
- Ticket key is lowercase: `AIDAT-1` → `aidat-1`
- Description uses first 3 words of summary
- Spaces replaced with underscores
- Special characters removed

## Retry Logic

The pipeline automatically retries on review failure:

**Iteration 1:**
- Code + Tests → Review
- If FAILED → Add comments, retry

**Iteration 2:**
- Re-run Coder Agent (with review feedback in context)
- Re-run Test Writer Agent
- Push changes to same PR
- Re-run Code Reviewer
- If FAILED → Add comments, retry

**Iteration 3:**
- Final attempt
- If still FAILED → Pipeline stops, PR remains open

**Manual intervention required after 3 failures:**
- Review PR comments
- Manually fix issues
- Request human review
- Merge manually

## Command Line Options

```bash
./implement.sh TICKET-KEY [OPTIONS]

Options:
  -b, --base-branch BRANCH    Base branch to branch from (default: main)
  -i, --interactive           Enable interactive prompts
  --max-iterations N          Max retry iterations (default: 3)
  --log-level LEVEL          Logging level: DEBUG|INFO|WARNING|ERROR (default: INFO)
  -h, --help                 Show help message

Examples:
  ./implement.sh AIDAT-1
  ./implement.sh AIDAT-1 --base-branch sprint10
  ./implement.sh AIDAT-1 --interactive --max-iterations 5
  ./implement.sh AIDAT-1 --log-level DEBUG
```

## Directory Structure

```
.sdlc_runs/
  jira_aidat-1_1774903946/        # Planning run
    spec.json                      # Product spec
    design.json                    # System design
    taskplan.json                  # 18 implementation tasks

  jira_aidat-1_1774920000/        # Implementation run
    code_iteration_1.log           # Coder agent output
    tests_iteration_1.log          # Test writer output
    review_iteration_1.txt         # Review comments
    code_iteration_2.log           # Retry outputs (if needed)
    ...
```

## Troubleshooting

### "gh: command not found"

**Problem:** GitHub CLI not installed.

**Solution:**
```bash
# Ubuntu/Debian
sudo apt install gh

# macOS
brew install gh

# Authenticate
gh auth login
```

### "Failed to transition ticket to 'In Progress'"

**Problem:** Your Jira workflow doesn't have a transition named "In Progress".

**Solution:**
1. Check logs for available transitions
2. Update [config/agents.yml](config/agents.yml#L78) with correct name
3. Re-run

### "PR creation failed: head branch already exists"

**Problem:** A branch with the same name already exists.

**Solution:**
```bash
# Delete the old branch
git branch -D main_aidat-1_logout_button_does
git push origin --delete main_aidat-1_logout_button_does

# Re-run orchestrator
./implement.sh AIDAT-1
```

### "Coder Agent failed: No design found in context"

**Problem:** Planning phase hasn't been run yet.

**Solution:**
```bash
# Run planning first
./plan.sh AIDAT-1

# Then run implementation
./implement.sh AIDAT-1
```

### "Review agent failed 3 times"

**Problem:** Code quality issues preventing approval.

**Solution:**
1. Check PR for review comments
2. Review `.sdlc_runs/jira_aidat-1_*/review_iteration_*.txt`
3. Manually fix critical issues
4. Push to the PR branch
5. Request human review
6. Merge manually when ready

## Integration with Existing Workflows

### Sprint Branches

If your team uses sprint branches:

```bash
# Create sprint branch (once per sprint)
git checkout -b sprint10
git push -u origin sprint10

# Run planning
./plan.sh AIDAT-1

# Run implementation with sprint branch as base
./implement.sh AIDAT-1 --base-branch sprint10

# Result: PR will merge into sprint10, not main
```

### Feature Branches

For long-running features:

```bash
# Create feature branch
git checkout -b feature-auth
git push -u origin feature-auth

# Implement tickets on this feature
./implement.sh AIDAT-1 --base-branch feature-auth
./implement.sh AIDAT-2 --base-branch feature-auth

# All PRs merge to feature-auth
# When feature is complete, merge feature-auth → main
```

## Agent Customization

Edit [config/agents.yml](config/agents.yml#L8-L17) to adjust token budgets:

```yaml
agent_max_tokens:
  coder_agent: 8096          # Increase if tasks are complex
  test_writer_agent: 8096    # Increase for comprehensive tests
  code_reviewer_agent: 8096  # Increase for detailed reviews
```

**Guidelines:**
- Small tasks (1-2 files): 8096 tokens (default)
- Medium tasks (3-5 files): 16384 tokens
- Large tasks (6+ files): 20480 tokens

## What's Next?

### For This Ticket (AIDAT-1)

**Current Status:**
- ✅ Planning complete (spec + design in Jira)
- ✅ Ticket in "Estimated" status
- ⏳ **Ready for implementation!**

**Next Step:**
```bash
./implement.sh AIDAT-1 --base-branch main
```

This will:
1. Move ticket to "In Progress"
2. Create branch `main_aidat-1_logout_button_does`
3. Implement all 18 tasks
4. Create tests
5. Create PR
6. Auto-review
7. Auto-merge if approved
8. Move ticket to "Included In Build"

**Estimated Time:** 10-20 minutes (depending on complexity and review iterations)

### Future Enhancements

Planned features:
- [ ] Parallel task execution (multiple agents working simultaneously)
- [ ] Incremental PR updates (push after each task instead of all at once)
- [ ] Custom review criteria (security, performance, accessibility)
- [ ] Integration with CI/CD (wait for tests to pass before merging)
- [ ] Slack/email notifications on completion
- [ ] Automatic PR template generation
- [ ] Support for draft PRs (human review before merge)

## Files Created

### New Files
- [agents/jira_implementation_orchestrator.py](agents/jira_implementation_orchestrator.py) - Main orchestrator (600+ lines)
- [implement.sh](implement.sh) - Wrapper script with validation
- [IMPLEMENTATION_PIPELINE.md](IMPLEMENTATION_PIPELINE.md) - This documentation

### Modified Files
- [config/agents.yml](config/agents.yml#L72-L79) - Added implementation transitions

## Summary

You now have **two automated pipelines**:

**1. Planning Pipeline** (`./plan.sh TICKET`)
- Reads Jira ticket
- Generates spec, design, taskplan
- Adds to Jira as comments
- Transitions to "Estimated"

**2. Implementation Pipeline** (`./implement.sh TICKET`)
- Transitions to "In Progress"
- Creates Git branch
- Implements code + tests
- Creates GitHub PR
- Auto-reviews
- Auto-merges on approval
- Transitions to "Included In Build"

**Full Workflow:**
```bash
# 1. Planning
./plan.sh AIDAT-1
# Result: Ticket has spec + design, status = "Estimated"

# 2. Implementation
./implement.sh AIDAT-1
# Result: Code merged to main, ticket = "Included In Build"
```

**Total automation:** From Jira ticket → Merged code in ~15 minutes! 🚀

---

**Ready to go?** Run `./implement.sh AIDAT-1` to see it in action!
