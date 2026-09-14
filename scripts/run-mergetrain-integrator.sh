#!/usr/bin/env bash
# run-mergetrain-integrator.sh - Land auto-approved trains and ask Codex to repair conflicts.

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
interval_seconds=15
attempt_dir="/tmp/dark-compiler-mergetrain-codex-attempts"
run_once=false

usage() {
  echo "Usage: $0 [--repo PATH] [--interval SECONDS] [--attempt-dir PATH] [--once]"
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --repo)
      repo_root="$2"
      shift 2
      ;;
    --interval)
      interval_seconds="$2"
      shift 2
      ;;
    --attempt-dir)
      attempt_dir="$2"
      shift 2
      ;;
    --once)
      run_once=true
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      usage >&2
      echo "Unknown argument: $1" >&2
      exit 2
      ;;
  esac
done

if [[ ! "$interval_seconds" =~ ^[1-9][0-9]*$ ]]; then
  echo "--interval must be a positive integer" >&2
  exit 2
fi

for required_command in codex git mergetrain python3; do
  if ! command -v "$required_command" >/dev/null 2>&1; then
    echo "Required command not found: $required_command" >&2
    exit 1
  fi
done

repo_root="$(cd "$repo_root" && pwd -P)"
mkdir -p "$attempt_dir"
attempt_dir="$(cd "$attempt_dir" && pwd -P)"

json_value() {
  local path="$1"
  python3 -c '
import json
import sys

value = json.load(sys.stdin)
for component in sys.argv[1].split("."):
    value = value.get(component) if isinstance(value, dict) else None
if value is None:
    print("")
elif isinstance(value, bool):
    print("true" if value else "false")
else:
    print(value)
' "$path"
}

repair_job() {
  local snapshot="$1"
  local job_id details category reason worktree branch old_head attempt_marker output_file
  local current_branch new_head dirty

  job_id="$(json_value next_action.target_job_id <<<"$snapshot")"
  if [[ -z "$job_id" ]]; then
    echo "Mergetrain requested conflict repair without a target job" >&2
    exit 1
  fi

  details="$(mergetrain --repo "$repo_root" inspect "$job_id" --json)"
  category="$(json_value outcome.failure_category <<<"$details")"
  reason="$(json_value outcome.message <<<"$details")"
  case "$category" in
    merge_conflict|semantic_conflict)
      ;;
    push_rejected)
      if [[ "$reason" != *non-fast-forward* ]]; then
        echo "Job #$job_id has a non-recoverable push rejection: $reason" >&2
        exit 1
      fi
      ;;
    *)
      echo "Job #$job_id needs operator attention ($category); refusing an automatic repair" >&2
      exit 1
      ;;
  esac

  worktree="$(json_value job.worktree_path <<<"$details")"
  branch="$(json_value job.branch <<<"$details")"
  old_head="$(json_value job.head_sha <<<"$details")"
  if [[ -z "$worktree" || -z "$branch" || -z "$old_head" || ! -d "$worktree" ]]; then
    echo "Job #$job_id does not identify a usable owning worktree" >&2
    exit 1
  fi

  attempt_marker="$attempt_dir/$job_id-$old_head.attempted"
  output_file="$attempt_dir/$job_id-$old_head.last-message.txt"
  if [[ -e "$attempt_marker" ]]; then
    echo "Codex already attempted job #$job_id at $old_head; operator review required" >&2
    exit 1
  fi
  touch "$attempt_marker"

  if ! printf '%s\n' "$details" |
    codex exec \
      -C "$worktree" \
      --sandbox workspace-write \
      --approve-for-me \
      --ephemeral \
      --output-last-message "$output_file" \
      "Repair mergetrain job #$job_id on branch $branch after a $category failure.

Read and follow AGENTS.md and the repository documentation. The mergetrain
inspection JSON is provided on stdin. Fetch the configured integration ref,
rebase this task branch onto it, understand both sides of any conflict, and
resolve it without discarding either change. Work only in this job's owning
worktree. Run all relevant verification and commit the repair.

For this recovery run, do not invoke ./land. Do not push, deploy, enqueue,
retry, reconcile, cancel, dismiss, or modify mergetrain queue state; the
integrator owns the retry. If a confident repair is not possible, leave the
branch unchanged and explain the blocker."; then
    echo "Codex failed while repairing job #$job_id; see $output_file" >&2
    exit 1
  fi

  current_branch="$(git -C "$worktree" branch --show-current)"
  new_head="$(git -C "$worktree" rev-parse HEAD)"
  dirty="$(git -C "$worktree" status --porcelain)"
  if [[ "$current_branch" != "$branch" || "$new_head" == "$old_head" || -n "$dirty" ]]; then
    echo "Codex did not leave job #$job_id on a clean, newly committed $branch" >&2
    echo "Review its result in $output_file" >&2
    exit 1
  fi

  mergetrain --repo "$repo_root" retry "$job_id" --json
}

while true; do
  # The native one-shot daemon owns queue locking, validation, and deployment.
  mergetrain --repo "$repo_root" daemon --once

  snapshot="$(mergetrain --repo "$repo_root" status --json)"
  contract_version="$(json_value contract_version <<<"$snapshot")"
  next_action="$(json_value next_action.code <<<"$snapshot")"

  if [[ "$contract_version" != "4" ]]; then
    echo "Unsupported mergetrain contract version: $contract_version" >&2
    exit 1
  fi

  case "$next_action" in
    fix_blocked_job)
      repair_job "$snapshot"
      ;;
    enqueue_clean_branch|gc_available|run_daemon_when_approved)
      ;;
    wait_for_runner)
      echo "Another mergetrain runner owns the queue; waiting"
      ;;
    *)
      echo "Mergetrain requires operator action: $next_action" >&2
      echo "$snapshot" >&2
      exit 1
      ;;
  esac

  if [[ "$run_once" == true ]]; then
    exit 0
  fi
  sleep "$interval_seconds"
done
