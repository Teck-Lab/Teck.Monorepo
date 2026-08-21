#!/usr/bin/env bash
set -euo pipefail

guard=tools/orca-coordinator-hook
session="guard-test-$$"
transcript="$(mktemp)"
trap 'rm -f "$transcript" "/tmp/teck-orca-stop-guard/$session.json"' EXIT

post_payload() {
  jq -cn --arg session "$session" --arg transcript "$transcript" --arg command "$1" --arg response "$2" \
    '{session_id:$session,transcript_path:$transcript,hook_event_name:"PostToolUse",tool_input:{command:$command},tool_response:{output:$response}}' | "$guard"
}

stop_payload() {
  jq -cn --arg session "$session" --arg transcript "$transcript" --arg message "$1" \
    '{session_id:$session,transcript_path:$transcript,hook_event_name:"Stop",last_assistant_message:$message}' | "$guard"
}

post_payload 'orca orchestration check --json' 'type worker_done: independent review rejected with one actionable defect' >/dev/null
test "$(stop_payload 'Processed the review delivery.' | jq -r .decision)" = block

post_payload 'orca orchestration worker-start --task task_repair --json' '{"id":"dispatch_repair"}' >/dev/null
test "$(stop_payload 'Repair Dispatch is active; continuing the foreground wait.' | jq -r '.decision // "allow"')" = allow

test "$(stop_payload 'The repair must be dispatched next.' | jq -r .decision)" = block

printf '%s\n' 'You are working inside Orca, a multi-agent IDE. You are a dispatched worker.' >"$transcript"
test "$(stop_payload 'The repair must be dispatched next.' | jq -r '.decision // "allow"')" = allow

echo "orca coordinator hook tests passed"
