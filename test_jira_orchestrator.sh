#!/bin/bash
# Test script for Jira Planning Orchestrator
# This simulates running the orchestrator in interactive mode

set -e

echo "=============================================="
echo "Jira Planning Orchestrator - Demo Test"
echo "=============================================="
echo ""

# Activate virtual environment
source venv/bin/activate

# Show help
echo "1. Showing help..."
python3 -m agents.jira_planning_orchestrator --help
echo ""

# Explain what would happen
echo "=============================================="
echo "2. What happens when you run:"
echo "   python3 -m agents.jira_planning_orchestrator AIDAT-1 --interactive"
echo "=============================================="
echo ""
echo "The orchestrator would:"
echo "  1. Prompt for Jira cloud ID (if not in config)"
echo "  2. Read AIDAT-1 ticket details (currently simulated via prompts)"
echo "  3. Ask if you want to run PM agent (skip for bugs)"
echo "  4. Run Architect Agent to design the fix"
echo "  5. Show design preview and ask to update Jira"
echo "  6. Run Task Planner Agent to break down into tasks"
echo "  7. Show task plan and ask to create subtasks"
echo "  8. Ask to transition ticket from 'Awaiting Estimate' to 'Open'"
echo ""

echo "=============================================="
echo "3. Configuration"
echo "=============================================="
echo ""
echo "Jira settings in config/agents.yml:"
grep -A 10 "^jira:" config/agents.yml || echo "Not configured yet"
echo ""

echo "=============================================="
echo "4. Directory Structure"
echo "=============================================="
echo ""
echo "Agent files:"
ls -lh agents/*_agent.py | awk '{print "  " $9 " (" $5 ")"}'
echo ""
echo "Orchestrators:"
ls -lh agents/*orchestrator*.py | awk '{print "  " $9 " (" $5 ")"}'
echo ""

echo "=============================================="
echo "5. Next Steps"
echo "=============================================="
echo ""
echo "To actually run the orchestrator with AIDAT-1:"
echo ""
echo "  # Interactive mode (recommended for first use)"
echo "  python3 -m agents.jira_planning_orchestrator AIDAT-1 --interactive"
echo ""
echo "  # Or with full Jira MCP integration (when available):"
echo "  python3 -m agents.jira_planning_orchestrator AIDAT-1 \\"
echo "    --cloud-id yoursite.atlassian.net"
echo ""
echo "  # Debug mode to see detailed logs:"
echo "  python3 -m agents.jira_planning_orchestrator AIDAT-1 \\"
echo "    --interactive --log-level DEBUG"
echo ""
echo "=============================================="
echo "Test Complete!"
echo "=============================================="
