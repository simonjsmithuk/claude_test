# Environment Setup for Jira Planning Orchestrator

## Quick Setup

Add these environment variables to your `~/.bashrc` file:

```bash
# Open your .bashrc
nano ~/.bashrc

# Add these lines at the end:

# Jira API Credentials
export JIRA_EMAIL="your.email@company.com"
export JIRA_API_TOKEN="ATATT3xFfGF0..."  # Get from https://gdslink.atlassian.net

# Anthropic API Key (if not already set)
export ANTHROPIC_API_KEY="sk-ant-api03-..."

# Save and reload
source ~/.bashrc
```

## Getting Your Jira API Token

1. Go to https://gdslink.atlassian.net
2. Click your profile icon → **Account Settings**
3. Click **Security** in the left sidebar
4. Scroll to **API tokens** section
5. Click **Create API token**
6. Give it a name: `AI-SDLC Pipeline`
7. Click **Create**
8. **Copy the token** (you won't be able to see it again!)
9. Paste it into your `.bashrc` as `JIRA_API_TOKEN`

## Verification

After adding to `.bashrc`, reload and verify:

```bash
# Reload bashrc
source ~/.bashrc

# Verify JIRA_EMAIL
echo $JIRA_EMAIL
# Should output: your.email@company.com

# Verify JIRA_API_TOKEN is set (without showing the value)
[ -n "$JIRA_API_TOKEN" ] && echo "JIRA_API_TOKEN is set" || echo "JIRA_API_TOKEN is NOT set"
# Should output: JIRA_API_TOKEN is set

# Verify ANTHROPIC_API_KEY is set
[ -n "$ANTHROPIC_API_KEY" ] && echo "ANTHROPIC_API_KEY is set" || echo "ANTHROPIC_API_KEY is NOT set"
# Should output: ANTHROPIC_API_KEY is set
```

## Using the plan.sh Script

Once environment variables are set up, you can use the convenient `plan.sh` script:

```bash
# Basic usage
./plan.sh AIDAT-1

# With interactive mode (if you want to confirm each step)
./plan.sh AIDAT-1 --interactive

# With debug logging
./plan.sh AIDAT-1 --log-level DEBUG

# Interactive + debug
./plan.sh AIDAT-1 --interactive --log-level DEBUG
```

## What plan.sh Does

The script automatically:

1. ✅ **Checks environment variables** - Verifies JIRA_EMAIL, JIRA_API_TOKEN, ANTHROPIC_API_KEY are set
2. ✅ **Activates virtual environment** - Sources `venv/bin/activate`
3. ✅ **Installs dependencies** - Ensures anthropic, pyyaml, requests are installed
4. ✅ **Runs the orchestrator** - Executes the planning pipeline for your ticket

## Example Output

```bash
$ ./plan.sh AIDAT-1

======================================
Jira Planning Orchestrator
======================================

Ticket: AIDAT-1

Step 1: Checking environment variables...
✓ JIRA_EMAIL set: your.email@company.com
✓ JIRA_API_TOKEN set (24 characters)
✓ ANTHROPIC_API_KEY set (108 characters)

Step 2: Activating virtual environment...
✓ Virtual environment activated

Step 3: Checking dependencies...
✓ anthropic package installed
✓ yaml package installed
✓ requests package installed

Step 4: Running Jira Planning Orchestrator...

Command: python3 -m agents.jira_planning_orchestrator AIDAT-1

======================================

=== Reading Jira Ticket: AIDAT-1 ===
Type: Bug
Summary: Fix login password hash corruption
Description: Password hash is corrupted during password reset...

[INFO] Running Architect agent...
[INFO] Architect agent complete (3847 chars)

[INFO] Running Task Planner agent...
[INFO] Task Planner agent complete (2156 chars)

======================================
✓ Planning complete!
======================================

Artifacts saved to: .sdlc_runs/jira_aidat-1_*/

Next steps:
  1. Review the generated spec, design, and taskplan
  2. Implement tasks using the coder agent
  3. Run tests and deploy
```

## Troubleshooting

### "JIRA_EMAIL not set"

**Solution:**
```bash
# Add to ~/.bashrc
export JIRA_EMAIL="your.email@company.com"

# Reload
source ~/.bashrc
```

### "JIRA_API_TOKEN not set"

**Solution:**
```bash
# Generate token at https://gdslink.atlassian.net
# Then add to ~/.bashrc
export JIRA_API_TOKEN="ATATT3xFfGF0..."

# Reload
source ~/.bashrc
```

### "ANTHROPIC_API_KEY not set"

**Solution:**
```bash
# Add to ~/.bashrc
export ANTHROPIC_API_KEY="sk-ant-api03-..."

# Reload
source ~/.bashrc
```

### "Virtual environment not found"

**Solution:**
```bash
# Create virtual environment
python3 -m venv venv

# Activate it
source venv/bin/activate

# Install dependencies
pip install anthropic pyyaml requests
```

### "Jira API returned 401"

**Problem:** Your API token is invalid or expired.

**Solution:**
1. Generate a new API token at https://gdslink.atlassian.net
2. Update JIRA_API_TOKEN in ~/.bashrc
3. Reload: `source ~/.bashrc`
4. Try again: `./plan.sh AIDAT-1`

### "Jira API returned 404"

**Problem:** The ticket doesn't exist or you don't have permission to view it.

**Solution:**
1. Check the ticket key is correct (case-sensitive!)
2. Verify you have access to the ticket in Jira
3. Check you're using the right Jira instance (gdslink.atlassian.net)

## Alternative: One-Time Setup

If you don't want to add to `.bashrc`, you can set variables for a single session:

```bash
# Set for this session only
export JIRA_EMAIL="your.email@company.com"
export JIRA_API_TOKEN="ATATT3xFfGF0..."
export ANTHROPIC_API_KEY="sk-ant-api03-..."

# Then run
./plan.sh AIDAT-1
```

**Note:** These will be lost when you close the terminal!

## Security Notes

- ✅ **DO** add API tokens to ~/.bashrc (it's in your home directory, private to you)
- ✅ **DO** use file permissions to protect ~/.bashrc (`chmod 600 ~/.bashrc`)
- ❌ **DON'T** commit API tokens to git
- ❌ **DON'T** share your API tokens
- ❌ **DON'T** paste tokens in public channels

## Summary

**One-time setup:**
1. Generate Jira API token at https://gdslink.atlassian.net
2. Add JIRA_EMAIL and JIRA_API_TOKEN to ~/.bashrc
3. Reload: `source ~/.bashrc`

**Then forever after:**
```bash
./plan.sh AIDAT-1  # That's it!
```

The script handles everything else automatically! 🚀
