"""GitHub API tools available to agents (requires GITHUB_TOKEN env var)."""
from __future__ import annotations

import json
import os

import urllib.request
import urllib.error

GITHUB_TOOLS = [
    {
        "name": "github_create_pr",
        "description": "Create a GitHub pull request.",
        "input_schema": {
            "type": "object",
            "properties": {
                "repo": {"type": "string", "description": "owner/repo, e.g. 'simonjsmithuk/claude_test'."},
                "title": {"type": "string", "description": "PR title."},
                "body": {"type": "string", "description": "PR description (Markdown)."},
                "head": {"type": "string", "description": "Branch containing the changes."},
                "base": {"type": "string", "description": "Target branch, e.g. 'main'."},
            },
            "required": ["repo", "title", "body", "head", "base"],
        },
    },
    {
        "name": "github_create_issue",
        "description": "Create a GitHub issue.",
        "input_schema": {
            "type": "object",
            "properties": {
                "repo": {"type": "string", "description": "owner/repo."},
                "title": {"type": "string", "description": "Issue title."},
                "body": {"type": "string", "description": "Issue body (Markdown)."},
                "labels": {
                    "type": "array",
                    "items": {"type": "string"},
                    "description": "Labels to apply.",
                },
            },
            "required": ["repo", "title", "body"],
        },
    },
    {
        "name": "github_list_issues",
        "description": "List open issues in a GitHub repository.",
        "input_schema": {
            "type": "object",
            "properties": {
                "repo": {"type": "string", "description": "owner/repo."},
                "state": {"type": "string", "description": "'open', 'closed', or 'all'. Defaults to 'open'."},
            },
            "required": ["repo"],
        },
    },
]


def _github_request(method: str, path: str, data: dict | None = None) -> dict:
    token = os.environ.get("GITHUB_TOKEN")
    if not token:
        raise ValueError("GITHUB_TOKEN environment variable not set.")
    url = f"https://api.github.com{path}"
    headers = {
        "Authorization": f"Bearer {token}",
        "Accept": "application/vnd.github+json",
        "X-GitHub-Api-Version": "2022-11-28",
        "Content-Type": "application/json",
    }
    body = json.dumps(data).encode() if data else None
    req = urllib.request.Request(url, data=body, headers=headers, method=method)
    with urllib.request.urlopen(req) as resp:
        return json.loads(resp.read())


def handle_github_tool(name: str, input_: dict) -> str:
    try:
        if name == "github_create_pr":
            result = _github_request("POST", f"/repos/{input_['repo']}/pulls", {
                "title": input_["title"],
                "body": input_["body"],
                "head": input_["head"],
                "base": input_["base"],
            })
            return f"PR created: {result.get('html_url', result)}"

        if name == "github_create_issue":
            result = _github_request("POST", f"/repos/{input_['repo']}/issues", {
                "title": input_["title"],
                "body": input_["body"],
                "labels": input_.get("labels", []),
            })
            return f"Issue created: {result.get('html_url', result)}"

        if name == "github_list_issues":
            state = input_.get("state", "open")
            result = _github_request("GET", f"/repos/{input_['repo']}/issues?state={state}")
            if not result:
                return "No issues found."
            lines = [f"#{i['number']} [{i['state']}] {i['title']} — {i['html_url']}" for i in result]
            return "\n".join(lines)

    except urllib.error.HTTPError as exc:
        return f"GitHub API error {exc.code}: {exc.read().decode()}"
    except Exception as exc:
        return f"Tool error ({name}): {exc}"

    return f"Unknown GitHub tool: {name}"
