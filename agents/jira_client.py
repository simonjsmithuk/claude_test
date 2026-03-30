"""Jira Client - Helper for reading Jira tickets.

This module provides a simple interface to read Jira tickets, either through:
1. MCP tools (when available in Claude Code environment)
2. Direct Atlassian REST API calls (when running standalone)
3. Interactive prompts (fallback)
"""
from __future__ import annotations

import logging
import os
from typing import Any, Optional

logger = logging.getLogger(__name__)


class JiraTicket:
    """Represents a Jira ticket with essential fields."""

    def __init__(
        self,
        key: str,
        summary: str,
        description: str,
        issue_type: str,
        status: str = "",
        assignee: str = "",
        raw_data: dict | None = None,
    ):
        self.key = key
        self.summary = summary
        self.description = description
        self.issue_type = issue_type
        self.status = status
        self.assignee = assignee
        self.raw_data = raw_data or {}

    def __repr__(self) -> str:
        return f"JiraTicket(key='{self.key}', type='{self.issue_type}', summary='{self.summary[:50]}...')"


class JiraClient:
    """Client for reading and updating Jira tickets."""

    def __init__(self, cloud_id: str, interactive: bool = False):
        """Initialize Jira client.

        Args:
            cloud_id: Jira cloud ID or site URL (e.g., "site.atlassian.net")
            interactive: Whether to fall back to interactive prompts
        """
        self.cloud_id = cloud_id
        self.interactive = interactive
        self._email = os.getenv("JIRA_EMAIL")
        self._api_token = os.getenv("JIRA_API_TOKEN")

    def get_ticket(self, ticket_key: str) -> JiraTicket:
        """Get ticket details from Jira.

        Tries multiple methods in order:
        1. MCP tools (if available)
        2. Direct API (if credentials available)
        3. Interactive prompts (if enabled)

        Args:
            ticket_key: Jira ticket key (e.g., "AIDAT-1")

        Returns:
            JiraTicket object with ticket details

        Raises:
            ValueError: If ticket cannot be read and not in interactive mode
        """
        logger.info("Fetching Jira ticket: %s", ticket_key)

        # Try MCP tools first (they handle auth automatically in Claude Code)
        try:
            return self._get_ticket_via_mcp(ticket_key)
        except Exception as e:
            logger.debug("MCP method failed: %s", e)

        # Try direct API if we have credentials
        try:
            return self._get_ticket_via_api(ticket_key)
        except Exception as e:
            logger.debug("Direct API method failed: %s", e)

        # Fall back to interactive prompts
        if self.interactive:
            logger.info("Falling back to interactive prompts for ticket details")
            return self._get_ticket_interactive(ticket_key)

        raise ValueError(
            f"Could not read Jira ticket {ticket_key}. "
            "MCP tools and API methods failed. Use --interactive to manually enter ticket details."
        )

    def _get_ticket_via_mcp(self, ticket_key: str) -> JiraTicket:
        """Get ticket via MCP tools (only works in Claude Code environment).

        This will fail when running standalone Python scripts, which is expected.
        """
        # MCP tools are only available when running inside Claude Code
        # When running as standalone Python, this will fail and we'll fall back
        raise NotImplementedError(
            "MCP tools not available in standalone Python environment. "
            "This orchestrator should be run through Claude Code for MCP integration, "
            "or use --interactive mode to enter ticket details manually."
        )

    def _get_ticket_via_api(self, ticket_key: str) -> JiraTicket:
        """Get ticket via direct Atlassian REST API.

        Requires JIRA_API_TOKEN and JIRA_EMAIL environment variables.
        """
        import requests
        from requests.auth import HTTPBasicAuth

        email = os.getenv("JIRA_EMAIL")
        api_token = os.getenv("JIRA_API_TOKEN")

        if not email or not api_token:
            raise ValueError(
                "JIRA_EMAIL and JIRA_API_TOKEN environment variables not set. "
                "Cannot use direct API method."
            )

        # Construct API URL
        base_url = f"https://{self.cloud_id}"
        if not base_url.endswith(".atlassian.net"):
            # Assume cloud_id is just the subdomain
            base_url = f"https://{self.cloud_id}.atlassian.net"

        url = f"{base_url}/rest/api/3/issue/{ticket_key}"

        logger.debug("Calling Jira API: %s", url)

        response = requests.get(
            url,
            auth=HTTPBasicAuth(email, api_token),
            headers={"Accept": "application/json"},
            timeout=30,
        )

        if response.status_code != 200:
            raise ValueError(
                f"Jira API returned {response.status_code}: {response.text}"
            )

        data = response.json()

        # Extract fields
        fields = data.get("fields", {})

        # Handle description - it might be in ADF format (dict) or plain text
        description_raw = fields.get("description", "")
        if isinstance(description_raw, dict):
            # ADF format - extract text from content nodes
            description = self._extract_text_from_adf(description_raw)
        else:
            description = description_raw or ""

        # Extract issue type safely
        issuetype = fields.get("issuetype") or {}
        issue_type_name = issuetype.get("name", "Task") if isinstance(issuetype, dict) else "Task"

        # Extract status safely
        status_obj = fields.get("status") or {}
        status_name = status_obj.get("name", "") if isinstance(status_obj, dict) else ""

        # Extract assignee safely
        assignee_obj = fields.get("assignee") or {}
        assignee_name = assignee_obj.get("displayName", "") if isinstance(assignee_obj, dict) else ""

        return JiraTicket(
            key=ticket_key,
            summary=fields.get("summary", ""),
            description=description,
            issue_type=issue_type_name,
            status=status_name,
            assignee=assignee_name,
            raw_data=data,
        )

    def _extract_text_from_adf(self, adf_content: dict) -> str:
        """Extract plain text from Atlassian Document Format (ADF).

        ADF is a structured JSON format used by Jira for rich text fields.
        This extracts the text content for use by the planning agents.
        """
        def extract_text(node):
            """Recursively extract text from ADF nodes."""
            if isinstance(node, str):
                return node

            if isinstance(node, dict):
                # If node has text, return it
                if "text" in node:
                    return node["text"]

                # If node has content, recurse into it
                if "content" in node:
                    return extract_text(node["content"])

                # Handle other node types
                return ""

            if isinstance(node, list):
                # Join text from all nodes with appropriate spacing
                texts = []
                for item in node:
                    text = extract_text(item)
                    if text:
                        texts.append(text)
                return "\n".join(texts)

            return ""

        return extract_text(adf_content)

    def _get_ticket_interactive(self, ticket_key: str) -> JiraTicket:
        """Get ticket details via interactive prompts."""
        print(f"\nCould not automatically fetch {ticket_key} from Jira.")
        print("Please enter the ticket details manually:\n")

        issue_type = input("Issue Type (Bug/Task/Story) [Bug]: ").strip() or "Bug"
        summary = input(f"Summary [{ticket_key}]: ").strip() or ticket_key

        print("Description (enter multiple lines, Ctrl+D or empty line to finish):")
        lines = []
        try:
            while True:
                line = input()
                if not line:
                    break
                lines.append(line)
        except EOFError:
            pass

        description = "\n".join(lines)

        return JiraTicket(
            key=ticket_key,
            summary=summary,
            description=description,
            issue_type=issue_type,
            status="",
            assignee="",
        )

    def add_comment(self, ticket_key: str, comment_text: str) -> bool:
        """Add a comment to a Jira ticket.

        Args:
            ticket_key: Jira ticket key (e.g., "AIDAT-1")
            comment_text: Plain text comment to add

        Returns:
            True if successful, False otherwise
        """
        import requests
        from requests.auth import HTTPBasicAuth

        email = os.getenv("JIRA_EMAIL")
        api_token = os.getenv("JIRA_API_TOKEN")

        if not email or not api_token:
            logger.warning("JIRA_EMAIL and JIRA_API_TOKEN not set, cannot add comment")
            return False

        # Construct API URL
        base_url = f"https://{self.cloud_id}"
        if not base_url.endswith(".atlassian.net"):
            base_url = f"https://{self.cloud_id}.atlassian.net"

        url = f"{base_url}/rest/api/3/issue/{ticket_key}/comment"

        # Format comment as ADF (Atlassian Document Format)
        adf_comment = {
            "body": {
                "type": "doc",
                "version": 1,
                "content": [
                    {
                        "type": "paragraph",
                        "content": [
                            {
                                "type": "text",
                                "text": comment_text
                            }
                        ]
                    }
                ]
            }
        }

        logger.debug("Adding comment to %s: %s", ticket_key, url)

        try:
            response = requests.post(
                url,
                auth=HTTPBasicAuth(email, api_token),
                headers={
                    "Accept": "application/json",
                    "Content-Type": "application/json"
                },
                json=adf_comment,
                timeout=30,
            )

            if response.status_code in (200, 201):
                logger.info("Successfully added comment to %s", ticket_key)
                return True
            else:
                logger.error("Failed to add comment: %s - %s", response.status_code, response.text)
                return False

        except Exception as e:
            logger.error("Error adding comment to %s: %s", ticket_key, e)
            return False

    def create_subtask(
        self,
        parent_key: str,
        summary: str,
        description: str,
        issue_type: str = "Sub-task"
    ) -> Optional[str]:
        """Create a subtask under a parent issue.

        Args:
            parent_key: Parent issue key (e.g., "AIDAT-1")
            summary: Summary for the subtask
            description: Description for the subtask
            issue_type: Issue type name (default: "Sub-task")

        Returns:
            Subtask key if successful, None otherwise
        """
        import requests
        from requests.auth import HTTPBasicAuth

        email = os.getenv("JIRA_EMAIL")
        api_token = os.getenv("JIRA_API_TOKEN")

        if not email or not api_token:
            logger.warning("JIRA_EMAIL and JIRA_API_TOKEN not set, cannot create subtask")
            return None

        # First, get the parent issue to extract project key
        parent_ticket = self.get_ticket(parent_key)
        project_key = parent_key.split("-")[0]  # e.g., "AIDAT-1" -> "AIDAT"

        # Construct API URL
        base_url = f"https://{self.cloud_id}"
        if not base_url.endswith(".atlassian.net"):
            base_url = f"https://{self.cloud_id}.atlassian.net"

        url = f"{base_url}/rest/api/3/issue"

        # Format description as ADF
        adf_description = {
            "type": "doc",
            "version": 1,
            "content": [
                {
                    "type": "paragraph",
                    "content": [
                        {
                            "type": "text",
                            "text": description
                        }
                    ]
                }
            ]
        }

        # Create subtask payload
        payload = {
            "fields": {
                "project": {"key": project_key},
                "summary": summary,
                "description": adf_description,
                "issuetype": {"name": issue_type},
                "parent": {"key": parent_key}
            }
        }

        logger.debug("Creating subtask for %s: %s", parent_key, summary)

        try:
            response = requests.post(
                url,
                auth=HTTPBasicAuth(email, api_token),
                headers={
                    "Accept": "application/json",
                    "Content-Type": "application/json"
                },
                json=payload,
                timeout=30,
            )

            if response.status_code in (200, 201):
                result = response.json()
                subtask_key = result.get("key")
                logger.info("Successfully created subtask %s under %s", subtask_key, parent_key)
                return subtask_key
            else:
                logger.error("Failed to create subtask: %s - %s", response.status_code, response.text)
                return None

        except Exception as e:
            logger.error("Error creating subtask for %s: %s", parent_key, e)
            return None

    def transition_issue(self, ticket_key: str, transition_name: str) -> bool:
        """Transition a ticket to a new status.

        Args:
            ticket_key: Jira ticket key (e.g., "AIDAT-1")
            transition_name: Name of the transition (e.g., "Open", "In Progress")

        Returns:
            True if successful, False otherwise
        """
        import requests
        from requests.auth import HTTPBasicAuth

        email = os.getenv("JIRA_EMAIL")
        api_token = os.getenv("JIRA_API_TOKEN")

        if not email or not api_token:
            logger.warning("JIRA_EMAIL and JIRA_API_TOKEN not set, cannot transition")
            return False

        # Construct API URL
        base_url = f"https://{self.cloud_id}"
        if not base_url.endswith(".atlassian.net"):
            base_url = f"https://{self.cloud_id}.atlassian.net"

        # First, get available transitions
        transitions_url = f"{base_url}/rest/api/3/issue/{ticket_key}/transitions"

        logger.debug("Getting available transitions for %s", ticket_key)

        try:
            response = requests.get(
                transitions_url,
                auth=HTTPBasicAuth(email, api_token),
                headers={"Accept": "application/json"},
                timeout=30,
            )

            if response.status_code != 200:
                logger.error("Failed to get transitions: %s - %s", response.status_code, response.text)
                return False

            transitions = response.json().get("transitions", [])

            # Find the transition ID by name
            transition_id = None
            for transition in transitions:
                if transition.get("name", "").lower() == transition_name.lower():
                    transition_id = transition.get("id")
                    break

            if not transition_id:
                logger.warning(
                    "Transition '%s' not found for %s. Available: %s",
                    transition_name,
                    ticket_key,
                    [t.get("name") for t in transitions]
                )
                return False

            # Perform the transition
            payload = {"transition": {"id": transition_id}}

            response = requests.post(
                transitions_url,
                auth=HTTPBasicAuth(email, api_token),
                headers={
                    "Accept": "application/json",
                    "Content-Type": "application/json"
                },
                json=payload,
                timeout=30,
            )

            if response.status_code == 204:
                logger.info("Successfully transitioned %s to '%s'", ticket_key, transition_name)
                return True
            else:
                logger.error("Failed to transition: %s - %s", response.status_code, response.text)
                return False

        except Exception as e:
            logger.error("Error transitioning %s: %s", ticket_key, e)
            return False
