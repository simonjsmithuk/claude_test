"""File system tools available to agents."""
from __future__ import annotations

import os
from pathlib import Path

FILE_TOOLS = [
    {
        "name": "read_file",
        "description": "Read the contents of a file at the given path.",
        "input_schema": {
            "type": "object",
            "properties": {
                "path": {"type": "string", "description": "Relative or absolute file path to read."}
            },
            "required": ["path"],
        },
    },
    {
        "name": "write_file",
        "description": "Write content to a file, creating parent directories as needed.",
        "input_schema": {
            "type": "object",
            "properties": {
                "path": {"type": "string", "description": "Relative or absolute file path to write."},
                "content": {"type": "string", "description": "Content to write to the file."},
            },
            "required": ["path", "content"],
        },
    },
    {
        "name": "list_directory",
        "description": "List files and directories at the given path.",
        "input_schema": {
            "type": "object",
            "properties": {
                "path": {"type": "string", "description": "Directory path to list. Defaults to '.'."},
            },
            "required": [],
        },
    },
    {
        "name": "search_files",
        "description": "Search for a pattern in files under a directory.",
        "input_schema": {
            "type": "object",
            "properties": {
                "pattern": {"type": "string", "description": "Text or regex pattern to search for."},
                "directory": {"type": "string", "description": "Directory to search in. Defaults to '.'."},
                "file_glob": {"type": "string", "description": "Glob pattern to filter files, e.g. '*.py'."},
            },
            "required": ["pattern"],
        },
    },
]


def handle_file_tool(name: str, input_: dict) -> str:
    try:
        if name == "read_file":
            path = Path(input_["path"])
            if not path.exists():
                return f"Error: file not found: {path}"
            return path.read_text(encoding="utf-8")

        if name == "write_file":
            path = Path(input_["path"])
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(input_["content"], encoding="utf-8")
            return f"Written {len(input_['content'])} bytes to {path}"

        if name == "list_directory":
            directory = Path(input_.get("path", "."))
            if not directory.exists():
                return f"Error: directory not found: {directory}"
            entries = sorted(directory.iterdir(), key=lambda p: (p.is_file(), p.name))
            lines = [f"{'DIR ' if e.is_dir() else 'FILE'} {e.name}" for e in entries]
            return "\n".join(lines) or "(empty)"

        if name == "search_files":
            import re
            import fnmatch

            pattern = input_["pattern"]
            directory = Path(input_.get("directory", "."))
            file_glob = input_.get("file_glob", "*")
            results = []
            for root, _, files in os.walk(directory):
                for fname in files:
                    if fnmatch.fnmatch(fname, file_glob):
                        fpath = Path(root) / fname
                        try:
                            text = fpath.read_text(encoding="utf-8", errors="ignore")
                            for i, line in enumerate(text.splitlines(), 1):
                                if re.search(pattern, line):
                                    results.append(f"{fpath}:{i}: {line.strip()}")
                        except Exception:
                            pass
            return "\n".join(results[:200]) or "No matches found."

    except Exception as exc:
        return f"Tool error ({name}): {exc}"

    return f"Unknown file tool: {name}"
