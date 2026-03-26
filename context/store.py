"""Shared context store — passes artifacts between agents in a pipeline run."""
from __future__ import annotations

import json
import time
from pathlib import Path
from typing import Any


class ContextStore:
    """Simple file-backed key/value store for inter-agent artifacts.

    Each pipeline run gets its own directory under ``base_dir``.  Artifacts
    are stored as JSON files so they survive process restarts and can be
    inspected manually.
    """

    def __init__(self, run_id: str, base_dir: str = ".sdlc_runs"):
        self.run_id = run_id
        self.run_dir = Path(base_dir) / run_id
        self.run_dir.mkdir(parents=True, exist_ok=True)
        self._cache: dict[str, Any] = {}

    # ------------------------------------------------------------------
    # Core read / write
    # ------------------------------------------------------------------

    def set(self, key: str, value: Any) -> None:
        """Persist a value under *key*."""
        self._cache[key] = value
        path = self.run_dir / f"{key}.json"
        path.write_text(json.dumps({"key": key, "value": value, "ts": time.time()}, indent=2))

    def get(self, key: str, default: Any = None) -> Any:
        """Return the value for *key*, or *default* if not found."""
        if key in self._cache:
            return self._cache[key]
        path = self.run_dir / f"{key}.json"
        if path.exists():
            data = json.loads(path.read_text())
            self._cache[key] = data["value"]
            return self._cache[key]
        return default

    def all_keys(self) -> list[str]:
        return [p.stem for p in self.run_dir.glob("*.json")]

    def summary(self) -> dict[str, Any]:
        """Return all stored artifacts as a dict (for display / logging)."""
        return {k: self.get(k) for k in self.all_keys()}

    # ------------------------------------------------------------------
    # Convenience helpers used by agents
    # ------------------------------------------------------------------

    def set_spec(self, spec: str) -> None:
        self.set("spec", spec)

    def get_spec(self) -> str:
        return self.get("spec", "")

    def set_design(self, design: str) -> None:
        self.set("design", design)

    def get_design(self) -> str:
        return self.get("design", "")

    def set_implementation(self, impl: str) -> None:
        self.set("implementation", impl)

    def get_implementation(self) -> str:
        return self.get("implementation", "")

    def set_tests(self, tests: str) -> None:
        self.set("tests", tests)

    def get_tests(self) -> str:
        return self.get("tests", "")

    def set_review(self, review: str) -> None:
        self.set("review", review)

    def get_review(self) -> str:
        return self.get("review", "")

    def set_pipeline(self, pipeline: str) -> None:
        self.set("pipeline", pipeline)

    def get_pipeline(self) -> str:
        return self.get("pipeline", "")

    def set_changelog(self, changelog: str) -> None:
        self.set("changelog", changelog)

    def get_changelog(self) -> str:
        return self.get("changelog", "")
