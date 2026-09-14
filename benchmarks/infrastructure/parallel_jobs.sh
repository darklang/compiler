#!/bin/bash
# Run benchmark phase workers with a bounded number of concurrent processes.

PARALLEL_JOB_PIDS=()

parallel_reap_finished_job() {
    local i
    for i in "${!PARALLEL_JOB_PIDS[@]}"; do
        local pid="${PARALLEL_JOB_PIDS[$i]}"
        local state
        state=$(ps -p "$pid" -o stat= 2>/dev/null | tr -d '[:space:]')
        if [ -z "$state" ] || [[ "$state" == Z* ]]; then
            wait "$pid" || true
            unset 'PARALLEL_JOB_PIDS[$i]'
            PARALLEL_JOB_PIDS=("${PARALLEL_JOB_PIDS[@]}")
            return 0
        fi
    done
    return 1
}

parallel_wait_for_available_slot() {
    local job_count="$1"
    while [ "${#PARALLEL_JOB_PIDS[@]}" -ge "$job_count" ]; do
        if ! parallel_reap_finished_job; then
            sleep 0.1
        fi
    done
}

parallel_wait_for_all_jobs() {
    local pid
    for pid in "${PARALLEL_JOB_PIDS[@]}"; do
        wait "$pid" || true
    done
    PARALLEL_JOB_PIDS=()
}

run_parallel_jobs() {
    local job_count="$1"
    local worker="$2"
    shift 2

    if [ "$job_count" -le 1 ]; then
        local item
        for item in "$@"; do
            "$worker" "$item"
        done
        return 0
    fi

    PARALLEL_JOB_PIDS=()
    local item
    for item in "$@"; do
        parallel_wait_for_available_slot "$job_count"
        "$worker" "$item" &
        PARALLEL_JOB_PIDS+=("$!")
    done
    parallel_wait_for_all_jobs
}
