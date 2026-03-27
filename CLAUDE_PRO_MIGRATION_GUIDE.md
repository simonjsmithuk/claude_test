# Claude Pro Migration Guide

**Migration from API Tokens → Claude Pro Account**

This guide explains how to use Claude Code subagents with your Claude Pro subscription instead of consuming API tokens.

---

## What Changed?

### Before (API Token Approach)
```python
# Used Anthropic API directly
client = anthropic.Anthropic(api_key="sk-ant-...")
response = client.messages.create(model="claude-sonnet-4-6", ...)
```
**Problem:** Consumes API tokens from your paid token budget

### After (Claude Pro Approach)
```markdown
<!-- .claude/agents/coder.md -->
# Coder Agent
You are a senior software engineer...
```
**Benefit:** Uses Claude Pro subscription (web interface limits, not token costs)

---

## Architecture Comparison

### Old: Python Orchestrator + API Tokens
```
┌─────────────┐
│  Your CLI   │
└──────┬──────┘
       │ API Key
       ▼
┌─────────────────┐
│ Python Agents   │
│ ───────────────│
│ • CoderAgent    │
│ • TestWriter    │
│ • Reviewer      │
└────────┬────────┘
         │ anthropic.Anthropic(api_key)
         ▼
┌──────────────────┐     ┌──────────────┐
│ Anthropic API    │────▶│ Token Budget │
└──────────────────┘     └──────────────┘
```

### New: Claude Code + Subagents + Claude Pro
```
┌──────────────────┐
│  Claude Code     │ (This session)
│  (VSCode)        │
└────────┬─────────┘
         │ Task tool
         ▼
┌──────────────────────────┐
│  Custom Subagents        │
│  (.claude/agents/*.md)   │
│  ─────────────────────── │
│  • coder.md              │
│  • test-writer.md        │
│  • reviewer.md           │
└────────┬─────────────────┘
         │ Uses Claude Pro
         ▼
┌──────────────────┐     ┌──────────────────┐
│  Claude API      │────▶│ Claude Pro Sub   │
└──────────────────┘     └──────────────────┘
```

---

## Files Created

### 1. Custom Subagent Definitions
**Location:** `.claude/agents/`

#### [coder.md](.claude/agents/coder.md)
- **Role:** Implements code from task specifications
- **Focus:** Spec compliance, Clean Architecture, production quality
- **Critical rule:** Match acceptance criteria EXACTLY

#### [test-writer.md](.claude/agents/test-writer.md)
- **Role:** Writes comprehensive tests for implemented code
- **Focus:** xUnit/.NET tests, Vitest/React tests, edge cases
- **Critical rule:** Test all acceptance criteria

#### [reviewer.md](.claude/agents/reviewer.md)
- **Role:** Reviews code for quality and spec compliance
- **Focus:** Spec compliance, compilation, security, architecture
- **Output:** APPROVED | APPROVED WITH SUGGESTIONS | CHANGES REQUIRED

### 2. Task Runner Script
**Location:** `run_task_with_subagents.py`

Semi-automated script that guides you through running a task via subagents.

**Usage:**
```bash
python3 run_task_with_subagents.py TASK-021
```

**Flow:**
1. Loads task from taskplan.json
2. Shows prompt for coder subagent → You invoke it manually
3. Shows prompt for test-writer subagent → You invoke it manually (if testable)
4. Shows prompt for reviewer subagent → You invoke it manually
5. Marks task as completed in taskplan.json

---

## How to Use Subagents

### Method 1: Via Task Tool (Recommended)

I (Claude Code) can invoke subagents directly using the `Task` tool:

```javascript
// You ask me:
"Implement TASK-021 using the coder subagent"

// I invoke:
Task({
  subagent_type: "coder",
  description: "Implement Auth Use Cases",
  prompt: "Implement TASK-021: Auth Use Cases\n\n[full task details]..."
})
```

### Method 2: Via run_task_with_subagents.py Script

Semi-automated approach:

```bash
# Step 1: Run script
python3 run_task_with_subagents.py TASK-021

# Step 2: Script prints prompt for coder
# Step 3: You copy prompt and ask me to invoke subagent
# Step 4: Repeat for test-writer and reviewer
# Step 5: Script updates task status
```

### Method 3: Direct Invocation

You can ask me directly:

```
"Use the coder subagent to implement TASK-021"
```

I'll invoke the subagent with the appropriate context.

---

## Workflow Examples

### Example 1: Implement a Single Task

**Old way (API tokens):**
```bash
python3 agents/task_level_orchestrator.py
# Burns through API tokens
```

**New way (Claude Pro):**
```
User: "Implement TASK-021 using subagents"

Claude Code:
1. Invokes coder subagent with task details
2. Coder completes, returns result
3. Invokes test-writer subagent
4. Test-writer completes
5. Invokes reviewer subagent
6. Reviewer approves or requests changes
7. Updates task status

Total cost: Uses Claude Pro subscription, no API tokens
```

### Example 2: Continue from Current Progress

You're at TASK-020 (completed). Next is TASK-021.

```
User: "Continue with TASK-021 using the coder subagent"

Claude Code:
1. Reads TASK-021 from taskplan.json
2. Loads spec and design from context store
3. Invokes coder subagent with full context
4. [Coder implements code]
5. Reports back to you
```

### Example 3: Fix a Failed Task

TASK-X failed review with "CHANGES REQUIRED".

```
User: "Fix TASK-X issues and use the coder subagent to re-implement"

Claude Code:
1. Reads review feedback
2. Invokes coder subagent with:
   - Original task
   - Review feedback
   - Priority: Fix spec mismatches first
3. [Coder fixes issues]
4. Invokes reviewer again
5. If approved → mark completed
```

---

## Comparison: API vs Claude Pro

| Aspect | API Token Approach | Claude Pro Approach |
|--------|-------------------|---------------------|
| **Cost** | $3-15 per million tokens | Included in Claude Pro ($20/month) |
| **Limits** | Token budget | Usage limits (resets 5h) |
| **Speed** | Parallel execution possible | Sequential (one active agent) |
| **Context** | Limited to API call context | Full Claude Code context |
| **Tools** | Custom Python tool handlers | Claude Code native tools |
| **Setup** | API key + Python environment | Just Claude Code + .md files |
| **Best for** | Batch processing, automation | Interactive development |

---

## Migration Steps

### If You Want to Continue Current Session

**Option A: Finish with API tokens, switch for next session**
1. Let current pipeline finish (TASK-020 → TASK-031)
2. Next session, use Claude Pro approach
3. Benefit: Don't disrupt current progress

**Option B: Switch now**
1. Note current task (TASK-020 in progress)
2. Stop Python pipeline
3. Continue from TASK-021 using subagents
4. Benefit: Start saving API tokens immediately

### Recommended: Option A

Since you're at TASK-020 and making good progress:
- ✅ Let pipeline finish backend API POC (through TASK-031)
- ✅ This gives you a working backend to test
- ✅ Start using subagents for frontend tasks (TASK-032+)
- ✅ Frontend tasks are perfect for iterative, interactive development

---

## Subagent Configuration

### Customizing Subagents

Edit `.claude/agents/*.md` files to customize behavior:

```markdown
# coder.md

## Custom Instructions for .NET
- Always use async/await
- Use nullable reference types
- Follow C# naming conventions

## Custom Instructions for React
- Use TypeScript strict mode
- Prefer functional components
- Use hooks (useState, useEffect)
```

### Creating New Subagents

Add new `.md` files in `.claude/agents/`:

```markdown
# devops.md

You are a DevOps engineer specializing in Docker and CI/CD.

## Your Role
Create Docker configurations and deployment scripts.

## Tools Available
- Write (Dockerfile, docker-compose.yml)
- Bash (docker build, docker-compose up)
```

Then invoke with:
```
"Use the devops subagent to create Docker configuration"
```

---

## Token Usage Comparison

### Typical Backend API POC (TASK-001 through TASK-031)

**API Token Approach:**
- Input tokens: ~2M (specs, code context)
- Output tokens: ~500K (generated code)
- **Cost:** ~$30-50

**Claude Pro Approach:**
- Usage: 31 tasks × multiple review cycles
- **Cost:** $0 (included in Claude Pro)
- Limit: May hit usage limits, wait 5 hours, resume

---

## Advantages of Claude Pro Approach

### 1. Cost Savings
- No per-token charges
- Predictable monthly cost ($20)
- Good for experimentation

### 2. Better Integration
- Native Claude Code tools (Read, Write, Edit, Bash)
- Full workspace context
- Git integration

### 3. Iterative Development
- See changes in real-time
- Test as you go
- Fix issues immediately

### 4. Flexibility
- Pause anytime
- Switch tasks easily
- Experiment without cost concern

---

## Disadvantages & Limitations

### 1. Usage Limits
- Claude Pro has usage limits per 5-hour window
- May need to pause and wait
- Not suitable for 24/7 automation

### 2. Sequential Execution
- Can't run multiple agents in parallel
- Slower than API batch processing
- One task at a time

### 3. Manual Orchestration
- Requires human in the loop
- Can't fully automate
- Need to invoke each stage

### 4. Context Window
- Each subagent starts fresh
- Need to pass context explicitly
- Can't share state between agents

---

## Best Practices

### 1. Break Work into Sessions
- Morning: Implement 5-7 tasks
- Break when hitting usage limits
- Evening: Continue after reset

### 2. Test Early
- Run `dotnet build` after code stage
- Fix compilation errors before review
- Don't wait until end

### 3. Use Subagents Strategically
- **Coder** → For all implementation
- **Test-Writer** → For testable code only
- **Reviewer** → Final quality check

### 4. Save Context Frequently
- Commit after each successful task
- Document decisions
- Keep taskplan.json updated

---

## Troubleshooting

### "Usage limit reached"
**Solution:** Wait for 5-hour reset window, then continue

### "Subagent not found"
**Solution:** Ensure `.claude/agents/coder.md` exists

### "Context too long"
**Solution:** Truncate spec/design in prompts (keep to 5000 chars)

### "Agent keeps making same mistake"
**Solution:** Add explicit instruction in subagent .md file

---

## Next Steps

1. **Try it out:**
   ```
   "Implement TASK-021 using the coder subagent"
   ```

2. **Compare experience:**
   - API: Fire and forget, wait for results
   - Subagent: Interactive, see progress, fix immediately

3. **Choose your approach:**
   - Finish current session with API
   - Start fresh session with subagents

4. **Iterate:**
   - Adjust subagent prompts as needed
   - Add custom instructions
   - Create new subagents for specific tasks

---

## Summary

**Old Approach:** Python orchestrator + API tokens → Fast, automated, expensive

**New Approach:** Claude Code + Custom subagents + Claude Pro → Interactive, cost-effective, limited by usage caps

**Recommendation:** Use Claude Pro approach for interactive development, API approach for batch automation.

**For your current situation:** Finish backend POC with API (already 45% done), then switch to subagents for frontend development.
