# Auto-Fix Loop Improvements

## Current Implementation Status

The auto-fix loop has been implemented with the following features:
- ✅ Automatic retry on review failure (up to 3 attempts)
- ✅ CoderAgent receives review feedback and attempts fixes
- ✅ Each fix triggers a new review
- ✅ Pipeline continues on failure (doesn't stop)

## Issue Discovered: TASK-002

### What Happened
TASK-002 failed all 3 review attempts despite the code being high quality.

**Root Cause:** The CoderAgent used `DateTimeOffset` instead of `DateTime` for timestamp fields.

**Why This Happened:**
- `DateTimeOffset` is the **correct modern best practice**
- Better for timezone handling, distributed systems, API compatibility
- The CoderAgent chose quality over strict spec compliance
- The ReviewAgent correctly flagged the spec mismatch

### Fix Attempts Analysis

**Attempt 1:** Initial implementation with DateTimeOffset
- Review: ❌ Failed (type mismatch with acceptance criteria)

**Attempt 2:** Fix applied
- Likely kept DateTimeOffset as it's technically superior
- Review: ❌ Failed (still doesn't match spec)

**Attempt 3:** Second fix applied
- Still couldn't resolve the fundamental conflict
- Review: ❌ Failed (3rd strike)

**Manual Fix:** Changed DateTimeOffset → DateTime
- Matches spec exactly
- Would pass review ✅

## The Fundamental Problem

The auto-fix loop faces a dilemma when:
1. The **spec says X** (DateTime)
2. **Best practice is Y** (DateTimeOffset)
3. The agent knows Y is better
4. But the review checks against X

**Current Behavior:** The CoderAgent tries to fix issues while maintaining code quality, creating a loop where it can't satisfy both constraints.

## Proposed Improvements

### 1. Smart Spec vs Best-Practice Detection

Add logic to detect when an implementation is technically superior but doesn't match spec:

```python
def _analyze_review_feedback(self, feedback: str, task: dict) -> dict:
    """Analyze review feedback to detect spec vs best-practice conflicts.

    Returns:
        {
            'type': 'bug' | 'spec_mismatch' | 'best_practice_upgrade',
            'severity': 'critical' | 'major' | 'minor',
            'can_auto_fix': bool,
            'recommendation': str
        }
    """
    # Use LLM to classify the review feedback
    # Detect patterns like:
    # - "should use X but uses Y" (spec mismatch)
    # - "missing required field" (bug)
    # - "type mismatch" (could be either)
```

### 2. Two-Path Fix Strategy

When a spec vs best-practice conflict is detected:

```python
def _run_fix_stage_with_options(self, task, spec, design, review_feedback):
    """Run fix stage with multiple strategy options."""

    analysis = self._analyze_review_feedback(review_feedback, task)

    if analysis['type'] == 'best_practice_upgrade':
        # Offer two paths
        logger.info(f"Detected best-practice upgrade in {task['id']}")
        logger.info("Path A: Match spec exactly (auto-fix)")
        logger.info("Path B: Keep better implementation (requires approval)")

        # For now, default to Path A (match spec)
        # Future: Could prompt user or use config flag
        return self._fix_to_match_spec(task, review_feedback)
    else:
        # Standard fix for bugs and actual issues
        return self._standard_fix(task, review_feedback)
```

### 3. Enhanced Fix Prompt

Update the fix prompt to explicitly handle this scenario:

```python
fix_prompt = f"""Fix the issues identified in code review for {task_id}.

REVIEW FEEDBACK:
{review_feedback}

IMPORTANT INSTRUCTION:
If the review identified a type or implementation difference where:
- Your implementation uses a BETTER/MORE MODERN approach (e.g., DateTimeOffset vs DateTime)
- But the acceptance criteria explicitly requires something else
- Then MATCH THE ACCEPTANCE CRITERIA EXACTLY, even if it's technically inferior

Your implementation will be improved in a later refactoring task. For now:
1. First priority: Match the acceptance criteria EXACTLY
2. Second priority: Fix any actual bugs
3. Third priority: Code quality and best practices

If the acceptance criteria says "DateTime", use DateTime (not DateTimeOffset).
If it says "string", use string (not a custom type).
If it says "int", use int (not long).

The files to fix are:
{chr(10).join('- ' + f for f in task.get('files', []))}
"""
```

### 4. Flexible Acceptance Criteria

Allow acceptance criteria to specify flexibility:

```json
{
  "acceptance_criteria": [
    {
      "field": "CreatedAt",
      "type": "DateTime",
      "flexibility": "strict",  // Must be exactly DateTime
      "note": "Legacy requirement"
    },
    {
      "field": "UserId",
      "type": "Guid",
      "flexibility": "compatible",  // Guid, string, or any compatible type
      "note": "Can be improved"
    }
  ]
}
```

### 5. Review Stage Intelligence

Make the review stage smarter about "better than spec" scenarios:

```python
def _parse_review_result(self, result: str, task: dict) -> dict:
    """Parse review and detect if failure is due to upgrade vs actual issue."""

    result_lower = result.lower()

    # Check for upgrade keywords
    upgrade_keywords = [
        'technically superior',
        'better practice',
        'more modern',
        'recommended over',
        'however uses',
        'although this is better'
    ]

    has_upgrade = any(kw in result_lower for kw in upgrade_keywords)
    has_critical = 'critical' in result_lower or 'failed' in result_lower

    if has_upgrade and not has_critical:
        # Implementation is better but doesn't match spec
        # Could pass with warning instead of failing
        return {
            'success': True,
            'message': result,
            'note': 'Passed with best-practice upgrade'
        }

    # Standard pass/fail logic
    # ...
```

### 6. Configuration Flags

Add configuration to control auto-fix behavior:

```python
class TaskLevelOrchestrator:
    def __init__(self, ..., fix_strategy='strict'):
        """
        fix_strategy options:
        - 'strict': Always match spec exactly (current behavior)
        - 'pragmatic': Allow best-practice upgrades
        - 'ask': Prompt user for spec conflicts
        """
        self.fix_strategy = fix_strategy
```

## Implementation Priority

### High Priority (Implement Soon)
1. **Enhanced Fix Prompt** - Quick win, just update the prompt string
2. **Match-spec-exactly instruction** - Tells agent to prioritize spec compliance

### Medium Priority
3. **Review feedback analysis** - Classify types of issues
4. **Two-path fix strategy** - Handle upgrades differently than bugs

### Low Priority (Future Enhancement)
5. **Flexible acceptance criteria** - Requires taskplan schema changes
6. **Configuration flags** - Adds complexity, needed for team environments

## Testing Strategy

### Test Case 1: Spec Mismatch (DateTimeOffset vs DateTime)
- Expected: Fix should change to DateTime to match spec
- Result: Should pass review on 2nd attempt

### Test Case 2: Actual Bug (Missing Required Field)
- Expected: Fix should add the missing field
- Result: Should pass review on 2nd attempt

### Test Case 3: Naming Inconsistency
- Expected: Fix should align naming
- Result: Should pass review on 2nd attempt

### Test Case 4: Multiple Issues
- Expected: Fix all issues in one pass
- Result: Should pass review on 2nd or 3rd attempt

## Metrics to Track

1. **Fix Success Rate**: % of tasks that pass after fix attempts
2. **Attempts to Success**: Average number of fix attempts before passing
3. **Fix Failure Reasons**: Categorize why fixes fail
4. **Best-Practice vs Spec Conflicts**: How often this occurs

## Next Steps

1. ✅ Document current findings (this document)
2. ⏭️ Update fix prompt with "match spec exactly" instruction
3. ⏭️ Test with remaining pipeline tasks
4. ⏭️ Implement review feedback analysis
5. ⏭️ Add two-path fix strategy

## Related Files

- `agents/task_level_orchestrator.py` - Contains auto-fix loop implementation
- `agents/coder_agent.py` - Generates code and applies fixes
- `agents/code_reviewer_agent.py` - Reviews code against acceptance criteria
- `TASK-002-REVIEW.md` - Analysis of TASK-002 failure
