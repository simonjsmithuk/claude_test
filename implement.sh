#!/bin/bash
# implement.sh - Wrapper script for Jira Implementation Orchestrator
# Runs the coding → testing → review → merge pipeline

set -e  # Exit on error

# Colors for output
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m' # No Color

echo -e "${GREEN}======================================"
echo "Jira Implementation Orchestrator"
echo -e "======================================${NC}"
echo ""

# Parse arguments
TICKET_KEY=""
BASE_BRANCH="main"
INTERACTIVE=""
MAX_ITERATIONS=3
LOG_LEVEL="INFO"

while [[ $# -gt 0 ]]; do
    case $1 in
        --interactive|-i)
            INTERACTIVE="--interactive"
            shift
            ;;
        --base-branch|-b)
            BASE_BRANCH="$2"
            shift 2
            ;;
        --max-iterations)
            MAX_ITERATIONS="$2"
            shift 2
            ;;
        --log-level)
            LOG_LEVEL="$2"
            shift 2
            ;;
        --help|-h)
            echo "Usage: $0 TICKET-KEY [OPTIONS]"
            echo ""
            echo "Options:"
            echo "  -b, --base-branch BRANCH    Base branch to branch from (default: main)"
            echo "  -i, --interactive           Enable interactive prompts"
            echo "  --max-iterations N          Max retry iterations (default: 3)"
            echo "  --log-level LEVEL          Logging level: DEBUG|INFO|WARNING|ERROR (default: INFO)"
            echo ""
            echo "Examples:"
            echo "  $0 AIDAT-1"
            echo "  $0 AIDAT-1 --base-branch sprint10"
            echo "  $0 AIDAT-1 --interactive --max-iterations 5"
            exit 0
            ;;
        *)
            if [ -z "$TICKET_KEY" ]; then
                TICKET_KEY="$1"
            else
                echo -e "${RED}Error: Unknown argument '$1'${NC}"
                echo "Use --help for usage information"
                exit 1
            fi
            shift
            ;;
    esac
done

if [ -z "$TICKET_KEY" ]; then
    echo -e "${RED}Error: Missing required argument TICKET_KEY${NC}"
    echo "Usage: $0 TICKET-KEY [OPTIONS]"
    echo "Use --help for more information"
    exit 1
fi

echo "Ticket: $TICKET_KEY"
echo "Base Branch: $BASE_BRANCH"
echo ""

# Step 1: Check environment variables
echo -e "${YELLOW}Step 1: Checking environment variables...${NC}"

if [ -z "$JIRA_EMAIL" ]; then
    echo -e "${RED}✗ JIRA_EMAIL not set${NC}"
    echo "  Set it in ~/.bashrc: export JIRA_EMAIL=\"your.email@company.com\""
    exit 1
else
    echo -e "${GREEN}✓ JIRA_EMAIL set: $JIRA_EMAIL${NC}"
fi

if [ -z "$JIRA_API_TOKEN" ]; then
    echo -e "${RED}✗ JIRA_API_TOKEN not set${NC}"
    echo "  Generate token at: https://id.atlassian.com/manage-profile/security/api-tokens"
    echo "  Set it in ~/.bashrc: export JIRA_API_TOKEN=\"YOUR_TOKEN\""
    exit 1
else
    echo -e "${GREEN}✓ JIRA_API_TOKEN set (${#JIRA_API_TOKEN} characters)${NC}"
fi

if [ -z "$ANTHROPIC_API_KEY" ]; then
    echo -e "${RED}✗ ANTHROPIC_API_KEY not set${NC}"
    echo "  Get your API key from: https://console.anthropic.com/"
    echo "  Set it in ~/.bashrc: export ANTHROPIC_API_KEY=\"YOUR_KEY\""
    exit 1
else
    echo -e "${GREEN}✓ ANTHROPIC_API_KEY set (${#ANTHROPIC_API_KEY} characters)${NC}"
fi

echo ""

# Step 2: Activate virtual environment
echo -e "${YELLOW}Step 2: Activating virtual environment...${NC}"

if [ ! -d "venv" ]; then
    echo -e "${RED}✗ Virtual environment not found${NC}"
    echo "  Create it with: python3 -m venv venv"
    exit 1
fi

source venv/bin/activate
echo -e "${GREEN}✓ Virtual environment activated${NC}"
echo ""

# Step 3: Check dependencies
echo -e "${YELLOW}Step 3: Checking dependencies...${NC}"

for package in anthropic yaml requests; do
    if python3 -c "import $package" 2>/dev/null; then
        echo -e "${GREEN}✓ $package package installed${NC}"
    else
        echo -e "${RED}✗ $package package not found${NC}"
        echo "  Installing $package..."
        pip install $package
    fi
done

# Check gh CLI
if ! command -v gh &> /dev/null; then
    echo -e "${RED}✗ gh CLI not found${NC}"
    echo "  Install it from: https://cli.github.com/"
    echo "  Or on Ubuntu: sudo apt install gh"
    exit 1
else
    echo -e "${GREEN}✓ gh CLI installed${NC}"
fi

echo ""

# Step 4: Run Jira Implementation Orchestrator
echo -e "${YELLOW}Step 4: Running Jira Implementation Orchestrator...${NC}"
echo ""
echo "Command: python3 -m agents.jira_implementation_orchestrator $TICKET_KEY --base-branch $BASE_BRANCH --max-iterations $MAX_ITERATIONS --log-level $LOG_LEVEL $INTERACTIVE"
echo ""
echo -e "${GREEN}======================================${NC}"
echo ""

python3 -m agents.jira_implementation_orchestrator "$TICKET_KEY" \
    --base-branch "$BASE_BRANCH" \
    --max-iterations "$MAX_ITERATIONS" \
    --log-level "$LOG_LEVEL" \
    $INTERACTIVE
