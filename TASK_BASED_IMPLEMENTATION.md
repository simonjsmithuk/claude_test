# Task-Based Implementation Architecture

## Problem Statement

The original AI-SDLC framework had a critical flaw: the **CoderAgent** tried to implement an entire application in one pass, leading to:

- ❌ Hitting the 10-30 iteration limit before completing all files
- ❌ Generating only ~20% of required code (domain models only)
- ❌ No way to validate progress incrementally
- ❌ Difficult to resume or continue partial implementations

## Solution: Task Planning Stage

We've added a **NEW stage** between Design and Code that breaks implementation into manageable, testable tasks.

### New Pipeline Flow

```
User Request
     ↓
1. SPEC (PMAgent)           → Product Specification
     ↓
2. DESIGN (ArchitectAgent)  → System Design Document
     ↓
3. TASKPLAN (TaskPlannerAgent) ← NEW!  → JSON Task Breakdown
     ↓
4. CODE (CoderAgent)        → Implements task-by-task
     ↓
5. TESTS (TestWriterAgent)  → Test Suite
     ↓
6. REVIEW (CodeReviewerAgent) → Code Review
     ↓
7. DEVOPS (DevOpsAgent)     → CI/CD, Docker
     ↓
8. RELEASE (ReleaseAgent)   → Changelog, Release Notes
```

## How Task Planning Works

### 1. TaskPlannerAgent Analyzes the Design

**Input:**
- Product Specification Document
- System Design Document

**Output:**
A JSON task breakdown like this:

```json
{
  "project_name": "DataViewer",
  "total_tasks": 25,
  "total_estimated_iterations": 180,
  "phases": ["core", "infrastructure", "api", "frontend", "config"],
  "tasks": [
    {
      "id": "TASK-001",
      "title": "Create solution and project structure",
      "description": "Generate .sln file and three .csproj files",
      "priority": 1,
      "phase": "core",
      "dependencies": [],
      "files": [
        "src/DataViewer.sln",
        "src/DataViewer.Core/DataViewer.Core.csproj",
        "src/DataViewer.Infrastructure/DataViewer.Infrastructure.csproj",
        "src/DataViewer.API/DataViewer.API.csproj"
      ],
      "acceptance_criteria": [
        "Solution file references all three projects",
        "Projects target .NET 8.0",
        "Project references are correct"
      ],
      "estimated_iterations": 2,
      "testable": false
    },
    {
      "id": "TASK-002",
      "title": "Implement Core Domain Models",
      "description": "Create all entity classes",
      "priority": 1,
      "phase": "core",
      "dependencies": ["TASK-001"],
      "files": [
        "src/DataViewer.Core/Domain/User.cs",
        "src/DataViewer.Core/Domain/S3CredentialProfile.cs",
        "src/DataViewer.Core/Domain/AuditLog.cs"
      ],
      "acceptance_criteria": [
        "All entity classes follow C# naming conventions",
        "Private fields prefixed with underscore",
        "Navigation properties defined"
      ],
      "estimated_iterations": 5,
      "testable": false
    },
    {
      "id": "TASK-003",
      "title": "Create ApplicationDbContext",
      "description": "Implement EF Core DbContext with all DbSets",
      "priority": 2,
      "phase": "infrastructure",
      "dependencies": ["TASK-002"],
      "files": [
        "src/DataViewer.Infrastructure/Data/ApplicationDbContext.cs"
      ],
      "acceptance_criteria": [
        "DbContext has DbSet for each domain model",
        "OnModelCreating configures relationships",
        "Supports MySQL and PostgreSQL via configuration"
      ],
      "estimated_iterations": 3,
      "testable": true
    }
    // ... 22 more tasks
  ]
}
```

### 2. CoderAgent Executes Tasks Sequentially

**Modified Behavior:**

Instead of implementing everything at once, CoderAgent now:

1. **Reads the taskplan** from context
2. **Sorts tasks by priority** (1, 2, 3...)
3. **For each task:**
   - ✅ Check dependencies are met
   - ✅ Execute just that task (focused context)
   - ✅ Mark task complete
   - ✅ Move to next task
4. **Returns summary** of what was completed

**Key Benefits:**

- ✅ Each task stays under iteration limits (typically 2-10 iterations)
- ✅ Progress is incremental and trackable
- ✅ Failed tasks don't block the entire pipeline
- ✅ Easy to resume: just run incomplete tasks

### 3. Progress Tracking

The CoderAgent now outputs a completion summary:

```
✅ COMPLETED TASK-001: Create solution and project structure (4 files)
✅ COMPLETED TASK-002: Implement Core Domain Models (6 files)
✅ COMPLETED TASK-003: Create ApplicationDbContext (1 file)
✅ COMPLETED TASK-004: Implement Repositories (5 files)
⏭️  SKIPPED TASK-005: Create Controllers (dependencies not met)
✅ COMPLETED TASK-006: Implement S3Service (1 file)
...
```

## Task Sizing Guidelines

### Small Tasks (Recommended)
- **Files:** 1-3
- **Iterations:** 2-5
- **Example:** "Create User domain model"

### Medium Tasks
- **Files:** 4-8
- **Iterations:** 5-10
- **Example:** "Implement all repositories"

### Large Tasks (Avoid)
- **Files:** 9+
- **Iterations:** 10+
- **Problem:** Will hit iteration limit and fail

**Rule of Thumb:** Keep tasks under 10 iterations (enforced by `max_tool_iterations` config)

## Task Dependency Management

Tasks can depend on other tasks:

```json
{
  "id": "TASK-010",
  "title": "Create AuthController",
  "dependencies": ["TASK-002", "TASK-007", "TASK-008"],
  "description": "Requires User model (TASK-002), TokenService (TASK-007), and UserRepository (TASK-008)"
}
```

**CoderAgent** automatically skips tasks if dependencies aren't complete.

## Task Phases

Tasks are grouped into logical phases:

| Phase | Description | Example Tasks |
|-------|-------------|---------------|
| **core** | Foundation: domain models, interfaces, DTOs | Domain entities, IRepository interfaces |
| **infrastructure** | Data access, external services | DbContext, S3Service, EncryptionService |
| **api** | Controllers, middleware, startup | AuthController, Program.cs |
| **frontend** | React components, Redux | LoginPage.tsx, redux/authSlice.ts |
| **config** | Configuration files | appsettings.json, docker-compose.yml |
| **tests** | Test suites | xUnit tests, Jest tests |

Phases execute in priority order (Priority 1 tasks before Priority 2, etc.)

## Configuration

Enable/disable task planning in `config/agents.yml`:

```yaml
stages:
  spec:
    enabled: true
  design:
    enabled: true
  taskplan:          # NEW STAGE
    enabled: true    # Set to false to use legacy mode
  code:
    enabled: true
  # ... other stages
```

## Usage Examples

### Run Full Pipeline with Task Planning (Default)

```bash
python3 main.py "Build a REST API for task management"
```

This will:
1. Generate spec
2. Generate design
3. **Generate task breakdown** (new!)
4. Execute tasks one-by-one
5. Run tests, review, devops, release

### Run Only Task Planning

```bash
python3 main.py --stages spec,design,taskplan "Build a REST API"
```

This generates the task breakdown without implementing. Useful for:
- Reviewing the plan before coding
- Estimating effort (total_estimated_iterations)
- Creating work items in your issue tracker

### Skip Task Planning (Legacy Mode)

```bash
python3 main.py --stages spec,design,code "Build a small utility"
```

Omitting `taskplan` stage makes CoderAgent use the old "implement everything at once" mode. Only recommended for very small projects.

### Continue Incomplete Implementation

If a previous run didn't complete all tasks:

```bash
python3 main.py --stages code --run-id run_abc123 "Continue implementation"
```

The CoderAgent will:
- Load the existing taskplan
- Skip already-completed tasks
- Resume from where it left off

## Comparison: Old vs New

### Old Architecture (Before Task Planning)

```
DESIGN → CODE (tries to write 100+ files) → Fails at 10 iterations
```

**Problems:**
- Agent receives massive context (20k+ chars)
- Tries to plan and execute everything simultaneously
- Hits iteration limit early
- No way to track progress
- Cannot resume partial work

### New Architecture (With Task Planning)

```
DESIGN → TASKPLAN (generates 25 tasks) → CODE (executes 25 tasks, 2-5 iterations each)
```

**Benefits:**
- ✅ Agent receives focused context per task
- ✅ Each task completes within iteration limits
- ✅ Clear progress tracking (20/25 tasks complete)
- ✅ Easy to resume (run incomplete tasks)
- ✅ Better error isolation (1 task fails ≠ entire pipeline fails)

## Implementation Details

### Files Modified

1. **agents/taskplanner_agent.py** (NEW)
   - Analyzes spec + design
   - Generates JSON task breakdown
   - Estimates iterations per task

2. **agents/coder_agent.py** (MODIFIED)
   - Added `_run_taskbased_implementation()` method
   - Reads taskplan from context
   - Executes tasks sequentially
   - Checks dependencies
   - Falls back to legacy mode if no taskplan

3. **agents/orchestrator.py** (MODIFIED)
   - Added `_run_taskplan()` stage handler
   - Updated default stages list
   - Stores taskplan in ContextStore

4. **config/agents.yml** (MODIFIED)
   - Added `taskplan` stage configuration

### Backward Compatibility

✅ **Fully backward compatible**

- If `taskplan` stage is disabled or skipped, CoderAgent uses legacy mode
- Existing runs without taskplan continue to work
- Old pipelines (without taskplan) still execute normally

## Real-World Example: DataViewer

### What Happened Before Task Planning

Running DataViewer:
```bash
python3 main.py "Build DataViewer app"
```

**Result:**
- SPEC ✅ Complete (19,672 chars)
- DESIGN ✅ Complete
- CODE ❌ **Only 20% complete** (7/100+ files)
- Hit iteration limit after generating domain models only

### What Happens With Task Planning

Running DataViewer:
```bash
python3 main.py "Build DataViewer app"
```

**Result (Expected):**
- SPEC ✅ Complete
- DESIGN ✅ Complete
- TASKPLAN ✅ Complete (25 tasks identified)
- CODE ✅ Task 1/25 complete (4 files)
- CODE ✅ Task 2/25 complete (6 files)
- CODE ✅ Task 3/25 complete (1 file)
- ... continues until all 25 tasks complete

## Testing the New Architecture

### Test with a Simple Project

```bash
python3 main.py "Build a simple CLI calculator that adds two numbers"
```

**Expected output:**
- Taskplan with ~3-5 tasks
- All tasks complete within limits
- 100% implementation

### Test with DataViewer

```bash
python3 main.py --stages spec,design,taskplan,code --run-id dataviewer_v2 "Build DataViewer..."
```

**Expected output:**
- Taskplan with ~25-30 tasks
- Progressive completion (can monitor .sdlc_runs/dataviewer_v2/)
- Much higher completion rate (aim for 80-100%)

## Future Enhancements

### Possible Improvements:

1. **Parallel Task Execution**
   - Tasks without dependencies could run in parallel
   - Would significantly speed up implementation

2. **Task Retry Logic**
   - If a task fails, retry with adjusted context
   - Helps recover from transient errors

3. **User Approval Checkpoints**
   - Pause after critical tasks for human review
   - "Review domain models before continuing? (y/n)"

4. **Task Estimation Accuracy**
   - Track actual vs. estimated iterations
   - Improve estimation over time with ML

5. **Task Templates**
   - Pre-defined task templates for common patterns
   - "CRUD API" → generates standard task breakdown

## Conclusion

The **Task Planning stage** transforms the AI-SDLC framework from a "generate everything at once (and fail)" approach to a **systematic, incremental, trackable implementation process**.

**Key Takeaway:** Large projects are now feasible by breaking them into small, manageable tasks that fit within agent iteration limits.

---

## Quick Reference

| Command | Purpose |
|---------|---------|
| `python3 main.py "description"` | Full pipeline with task planning |
| `python3 main.py --stages spec,design,taskplan "desc"` | Generate task breakdown only |
| `python3 main.py --stages code --run-id <id> "continue"` | Resume incomplete tasks |
| `python3 main.py --stages spec,design,code "desc"` | Skip task planning (legacy mode) |

| Config Setting | Value | Effect |
|----------------|-------|--------|
| `stages.taskplan.enabled` | `true` | Use task-based implementation |
| `stages.taskplan.enabled` | `false` | Use legacy "all at once" mode |
| `max_tool_iterations` | `30` | Max iterations per task (recommended: 20-30) |
