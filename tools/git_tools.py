"""Git tools available to agents."""
from __future__ import annotations

import subprocess

GIT_TOOLS = [
    {
        "name": "git_status",
        "description": "Show the working tree status.",
        "input_schema": {"type": "object", "properties": {}, "required": []},
    },
    {
        "name": "git_diff",
        "description": "Show changes between commits, commit and working tree, etc.",
        "input_schema": {
            "type": "object",
            "properties": {
                "args": {"type": "string", "description": "Extra git diff arguments, e.g. 'HEAD~1 HEAD'."},
            },
            "required": [],
        },
    },
    {
        "name": "git_log",
        "description": "Show the commit log.",
        "input_schema": {
            "type": "object",
            "properties": {
                "n": {"type": "integer", "description": "Number of commits to show. Defaults to 10."},
            },
            "required": [],
        },
    },
    {
        "name": "git_create_branch",
        "description": "Create and checkout a new git branch.",
        "input_schema": {
            "type": "object",
            "properties": {
                "branch": {"type": "string", "description": "Name of the new branch."},
            },
            "required": ["branch"],
        },
    },
    {
        "name": "git_add_commit",
        "description": "Stage all changes and create a commit.",
        "input_schema": {
            "type": "object",
            "properties": {
                "message": {"type": "string", "description": "Commit message."},
            },
            "required": ["message"],
        },
    },
]


def _run(cmd: list[str]) -> str:
    result = subprocess.run(cmd, capture_output=True, text=True)
    output = result.stdout + result.stderr
    return output.strip() or "(no output)"


def handle_git_tool(name: str, input_: dict) -> str:
    try:
        if name == "git_status":
            return _run(["git", "status"])

        if name == "git_diff":
            args = input_.get("args", "").split() if input_.get("args") else []
            return _run(["git", "diff"] + args)

        if name == "git_log":
            n = input_.get("n", 10)
            return _run(["git", "log", f"-{n}", "--oneline"])

        if name == "git_create_branch":
            return _run(["git", "checkout", "-b", input_["branch"]])

        if name == "git_add_commit":
            _run(["git", "add", "-A"])
            return _run(["git", "commit", "-m", input_["message"]])

    except Exception as exc:
        return f"Tool error ({name}): {exc}"

    return f"Unknown git tool: {name}"
