#!/usr/bin/env bash
set -euo pipefail

export GITHUB_PERSONAL_ACCESS_TOKEN="${GITHUB_PERSONAL_ACCESS_TOKEN:-$(gh auth token)}"

# Deliberately excludes create_branch, create_or_update_file, push_files,
# delete_file, update_pull_request_branch, pull_request_review_write,
# merge_pull_request, and actions_run_trigger. Local Git owns all commits and
# branch integration; the human owns approval and merge.
export GITHUB_TOOLS="${GITHUB_TOOLS:-get_repository_tree,get_file_contents,get_commit,list_branches,list_commits,issue_read,issue_write,sub_issue_write,add_issue_comment,search_issues,list_issues,create_pull_request,update_pull_request,pull_request_read,list_pull_requests,search_pull_requests,actions_get,actions_list,get_job_logs}"

exec /usr/local/bin/github-mcp-server stdio
