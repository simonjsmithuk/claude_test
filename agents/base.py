"""Base agent class for all SDLC agents."""
from __future__ import annotations

import json
import logging
from typing import Any

import anthropic

from context.store import ContextStore
from tools import ALL_TOOLS, handle_tool_call

logger = logging.getLogger(__name__)

DEFAULT_MODEL = "claude-sonnet-4-6"
MAX_TOOL_ITERATIONS = 10


class BaseAgent:
    """Base class for all SDLC agents.

    Subclasses must set:
      - name: str — human-readable agent name
      - system_prompt: str — the agent's persona and instructions
      - tools: list[dict] — subset of ALL_TOOLS this agent may use
    """

    name: str = "base"
    system_prompt: str = "You are a helpful AI assistant."
    tools: list[dict] = []

    def __init__(self, client: anthropic.Anthropic, context: ContextStore, model: str = DEFAULT_MODEL):
        self.client = client
        self.context = context
        self.model = model

    def run(self, task: str, extra_context: dict[str, Any] | None = None) -> str:
        """Run the agent on a task, executing tool calls until completion.

        Args:
            task: Natural language task description.
            extra_context: Additional key/value pairs injected into the user message.

        Returns:
            Final text response from the agent.
        """
        logger.info("[%s] Starting task: %s", self.name, task[:120])

        user_content = task
        if extra_context:
            ctx_block = "\n\n## Context\n" + "\n".join(f"**{k}**: {v}" for k, v in extra_context.items())
            user_content = task + ctx_block

        messages: list[dict] = [{"role": "user", "content": user_content}]

        for iteration in range(MAX_TOOL_ITERATIONS):
            response = self.client.messages.create(
                model=self.model,
                max_tokens=8096,
                system=self.system_prompt,
                tools=self.tools or [],
                messages=messages,
            )

            logger.debug("[%s] stop_reason=%s", self.name, response.stop_reason)

            # Collect text and tool-use blocks
            assistant_content = response.content
            messages.append({"role": "assistant", "content": assistant_content})

            if response.stop_reason == "end_turn":
                text = self._extract_text(assistant_content)
                logger.info("[%s] Completed after %d iteration(s).", self.name, iteration + 1)
                return text

            if response.stop_reason == "tool_use":
                tool_results = []
                for block in assistant_content:
                    if block.type == "tool_use":
                        logger.info("[%s] Calling tool: %s", self.name, block.name)
                        result = handle_tool_call(block.name, block.input)
                        tool_results.append({
                            "type": "tool_result",
                            "tool_use_id": block.id,
                            "content": result,
                        })
                messages.append({"role": "user", "content": tool_results})
            else:
                break

        return self._extract_text(messages[-1]["content"]) if messages else "No response."

    @staticmethod
    def _extract_text(content: list | str) -> str:
        if isinstance(content, str):
            return content
        parts = [block.text for block in content if hasattr(block, "text")]
        return "\n".join(parts)
