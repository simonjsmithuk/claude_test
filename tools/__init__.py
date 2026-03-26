from .file_tools import FILE_TOOLS, handle_file_tool
from .git_tools import GIT_TOOLS, handle_git_tool
from .github_tools import GITHUB_TOOLS, handle_github_tool

ALL_TOOLS = FILE_TOOLS + GIT_TOOLS + GITHUB_TOOLS


def handle_tool_call(tool_name: str, tool_input: dict) -> str:
    if tool_name in {t["name"] for t in FILE_TOOLS}:
        return handle_file_tool(tool_name, tool_input)
    if tool_name in {t["name"] for t in GIT_TOOLS}:
        return handle_git_tool(tool_name, tool_input)
    if tool_name in {t["name"] for t in GITHUB_TOOLS}:
        return handle_github_tool(tool_name, tool_input)
    return f"Unknown tool: {tool_name}"
