#!/bin/bash
# plan.sh - Run Jira Planning Orchestrator for PM → Architect → Task Planner
#
# Usage: ./plan.sh TICKET_KEY [OPTIONS]
# Example: ./plan.sh AIDAT-1
#          ./plan.sh AIDAT-1 --interactive
#          ./plan.sh AIDAT-1 --log-level DEBUG

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Check if ticket key is provided
if [ -z "$1" ]; then
    echo -e "${RED}Error: Ticket key required${NC}"
    echo ""
    echo "Usage: ./plan.sh TICKET_KEY [OPTIONS]"
    echo ""
    echo "Examples:"
    echo "  ./plan.sh AIDAT-1"
    echo "  ./plan.sh AIDAT-1 --interactive"
    echo "  ./plan.sh AIDAT-1 --log-level DEBUG"
    echo ""
    exit 1
fi

TICKET_KEY="$1"
shift  # Remove ticket key from arguments, rest are options

echo -e "${GREEN}======================================${NC}"
echo -e "${GREEN}Jira Planning Orchestrator${NC}"
echo -e "${GREEN}======================================${NC}"
echo ""
echo "Ticket: $TICKET_KEY"
echo ""

# Step 1: Check environment variables
echo -e "${YELLOW}Step 1: Checking environment variables...${NC}"
if [ -z "$JIRA_EMAIL" ]; then
    echo -e "${RED}✗ JIRA_EMAIL not set${NC}"
    echo ""
    echo "Please add to your ~/.bashrc:"
    echo '  export JIRA_EMAIL="your.email@company.com"'
    echo ""
    echo "Then reload: source ~/.bashrc"
    echo ""
    echo "Or use --interactive mode: ./plan.sh $TICKET_KEY --interactive"
    exit 1
else
    echo -e "${GREEN}✓ JIRA_EMAIL set: $JIRA_EMAIL${NC}"
fi

if [ -z "$JIRA_API_TOKEN" ]; then
    echo -e "${YELLOW}⚠ JIRA_API_TOKEN not set (will fall back to interactive mode if needed)${NC}"
else
    echo -e "${GREEN}✓ JIRA_API_TOKEN set (${#JIRA_API_TOKEN} characters)${NC}"
fi

if [ -z "$ANTHROPIC_API_KEY" ]; then
    echo -e "${RED}✗ ANTHROPIC_API_KEY not set${NC}"
    echo ""
    echo "Please add to your ~/.bashrc:"
    echo '  export ANTHROPIC_API_KEY="sk-ant-api03-..."'
    echo ""
    echo "Then reload: source ~/.bashrc"
    exit 1
else
    echo -e "${GREEN}✓ ANTHROPIC_API_KEY set (${#ANTHROPIC_API_KEY} characters)${NC}"
fi
echo ""

# Step 2: Activate virtual environment
echo -e "${YELLOW}Step 2: Activating virtual environment...${NC}"
if [ ! -d "venv" ]; then
    echo -e "${RED}✗ Virtual environment not found${NC}"
    echo ""
    echo "Please create it first:"
    echo "  python3 -m venv venv"
    echo "  source venv/bin/activate"
    echo "  pip install anthropic pyyaml requests"
    exit 1
fi

source venv/bin/activate
echo -e "${GREEN}✓ Virtual environment activated${NC}"
echo ""

# Step 3: Check dependencies
echo -e "${YELLOW}Step 3: Checking dependencies...${NC}"
python3 -c "import anthropic" 2>/dev/null || {
    echo -e "${RED}✗ anthropic package not installed${NC}"
    echo "Installing..."
    pip install anthropic
}
echo -e "${GREEN}✓ anthropic package installed${NC}"

python3 -c "import yaml" 2>/dev/null || {
    echo -e "${RED}✗ yaml package not installed${NC}"
    echo "Installing..."
    pip install pyyaml
}
echo -e "${GREEN}✓ yaml package installed${NC}"

python3 -c "import requests" 2>/dev/null || {
    echo -e "${YELLOW}⚠ requests package not installed (needed for Jira API)${NC}"
    echo "Installing..."
    pip install requests
}
echo -e "${GREEN}✓ requests package installed${NC}"
echo ""

# Step 4: Run the orchestrator
echo -e "${YELLOW}Step 4: Running Jira Planning Orchestrator...${NC}"
echo ""
echo "Command: python3 -m agents.jira_planning_orchestrator $TICKET_KEY $@"
echo ""
echo -e "${GREEN}======================================${NC}"
echo ""

# Run the orchestrator with all remaining arguments
python3 -m agents.jira_planning_orchestrator "$TICKET_KEY" "$@"

# Check exit code
EXIT_CODE=$?
echo ""
if [ $EXIT_CODE -eq 0 ]; then
    echo -e "${GREEN}======================================${NC}"
    echo -e "${GREEN}✓ Planning complete!${NC}"
    echo -e "${GREEN}======================================${NC}"
    echo ""
    echo "Artifacts saved to: .sdlc_runs/jira_${TICKET_KEY,,}_*/"
    echo ""
    echo "Next steps:"
    echo "  1. Review the generated spec, design, and taskplan"
    echo "  2. Implement tasks using the coder agent"
    echo "  3. Run tests and deploy"
else
    echo -e "${RED}======================================${NC}"
    echo -e "${RED}✗ Planning failed with exit code $EXIT_CODE${NC}"
    echo -e "${RED}======================================${NC}"
    echo ""
    echo "Check the logs above for error details."
    echo ""
    echo "Common issues:"
    echo "  - Jira credentials incorrect (check JIRA_EMAIL and JIRA_API_TOKEN)"
    echo "  - Ticket not found (check ticket key is correct)"
    echo "  - Network issues (check connectivity to gdslink.atlassian.net)"
    echo ""
    echo "Try running with --log-level DEBUG for more details:"
    echo "  ./plan.sh $TICKET_KEY --log-level DEBUG"
fi

exit $EXIT_CODE
