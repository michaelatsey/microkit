#!/usr/bin/env bash
# Blocks `gh pr merge ... --delete-branch` when the PR head branch is `dev`.
# Fails closed: if the head branch cannot be determined, the command is blocked.
set -uo pipefail

input=$(cat)
cmd=$(printf '%s' "$input" | jq -r '.tool_input.command // empty')

case "$cmd" in
  *"gh pr merge"*"--delete-branch"*)
    pr=$(printf '%s' "$cmd" | grep -oE 'gh pr merge[[:space:]]+[0-9]+' | grep -oE '[0-9]+$')
    head=""
    [ -n "$pr" ] && head=$(gh pr view "$pr" --json headRefName -q .headRefName 2>/dev/null)

    if [ "$head" = "dev" ] || [ -z "$head" ]; then
      echo "BLOCKED: --delete-branch on a PR whose head branch is '${head:-unknown}'." >&2
      echo "Verify first:  gh pr view ${pr:-<n>} --json headRefName" >&2
      exit 2
    fi
    ;;
esac

exit 0
