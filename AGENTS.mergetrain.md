# mergetrain agent contract

Purpose: Serialize committed local task branches through one merge/test/push/verify runner.

## Existing queues and explanations

- This repository uses status → `./land` → wait even for one branch. Queue counts alone do not establish health, runner ownership, or recovery needs; read `health`, `state`, and `next_action` together.
- For explanation-only requests, read the skill documentation when permitted and explain the procedure without Git or product commands. Distinguish hypothetical steps from observed state.

## Current command reference

- The v3 core commands are `init`, `status`, `enqueue`, `validate`, `deploy`, and `inspect`.
- Start with `mergetrain status --json`. Use `mergetrain status --diagnose --json` only for configuration, Git, runtime, or lock detail, and `mergetrain inspect JOB_ID --json` for job evidence. `doctor` is removed, not an alias for `status --diagnose`.
- Confirm uncertain syntax with the installed `mergetrain --version` and command-specific `--help` only when command execution is permitted. Otherwise use this reference and identify missing details; do not invent commands or copy older syntax from unversioned web results. Inspection and `next_action` do not authorize recovery or deployment.

## Rules

1. Work on a task-specific branch and worktree.
2. Commit a clean HEAD before handing work off.
3. Read mergetrain status --json and follow its next action before changing queue state.
4. Land every named finished branch in the requested order with `./land --task "TASK"`; the script resolves the branch and worktree, enqueues the exact commit with bounded unattended approval, and waits for deployment. Do not continue until it prints `landed` unless it exits with a reported failure.
5. Never push configured integration refs directly. One authorized runner owns validation and deployment; recovery and destructive actions require their stated approval.

## Safety boundary

- A task agent runs `./land` and waits for its exact job. The script's internal `--auto` enqueue authorizes only the configured runner's bounded unattended validation and deployment; it does not authorize the task agent to run either operation.
- Only a separately authorized runner uses `deploy` or a daemon.
- Deployment requires either confirmation of the human-readable exact plan or prior bounded unattended approval. Agents never select train IDs or supply plan hashes; structured evidence may include identifiers for inspection.
- Unattended approval is bound to the exact destination and execution policy. Any change blocks before push.
- Recovery and destructive cleanup require their stated approval. Follow `status.next_action`; never rewrite permanent deploy audit refs.

## Stable machine contract

- Every JSON payload carries `contract_version`; ignore unknown keys and fail closed on unknown safety actions.
- `deploy` means the atomic Git ref update plus configured verification. A downstream provider release is separate.
