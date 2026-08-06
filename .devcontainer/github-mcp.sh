#!/usr/bin/env bash
set -euo pipefail

secret_dir="${TECK_GITHUB_SECRET_DIR:-/run/secrets/teck-github}"
app_env="$secret_dir/github-app.env"
app_key="$secret_dir/github-app.pem"

[ -r "$app_env" ] || {
  echo "GitHub MCP is not configured: missing $app_env" >&2
  echo "See .devcontainer/github-app/README.md." >&2
  exit 1
}
[ -r "$app_key" ] || {
  echo "GitHub MCP is not configured: missing $app_key" >&2
  echo "Download a private key from the GitHub App settings; do not commit it." >&2
  exit 1
}

set -a
# Local-only file, mounted read-only. It contains simple KEY=value assignments.
# shellcheck disable=SC1090
. "$app_env"
set +a

: "${GITHUB_APP_ID:?GITHUB_APP_ID is missing from github-app.env}"
: "${GITHUB_APP_INSTALLATION_ID:?GITHUB_APP_INSTALLATION_ID is missing from github-app.env}"
export GITHUB_APP_PRIVATE_KEY_PATH="$app_key"

# Deliberately excludes create_branch, create_or_update_file, push_files,
# delete_file, update_pull_request_branch, pull_request_review_write,
# merge_pull_request, and actions_run_trigger. Local Git owns all commits and
# branch integration; the human owns approval and merge.
export GITHUB_TOOLS="${GITHUB_TOOLS:-get_repository_tree,get_file_contents,get_commit,list_branches,list_commits,issue_read,issue_write,sub_issue_write,add_issue_comment,search_issues,list_issues,create_pull_request,update_pull_request,pull_request_read,list_pull_requests,search_pull_requests,actions_get,actions_list,get_job_logs}"

exec /usr/local/bin/github-mcp-server stdio
