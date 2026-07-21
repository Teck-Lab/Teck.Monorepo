#!/usr/bin/env bash
# Regression tests for tools/security-scan.sh.
#
# 1. changed-mode SAST target selection drops tracked git symlinks (mode 120000)
#    whose targets may resolve outside the Docker /src mount.
# 2. --staged mode mounts a linked git worktree and its common git directory at
#    their original absolute paths so Gitleaks protect --staged can read the
#    staged index.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REAL_SCRIPT="$SCRIPT_DIR/security-scan.sh"
export FIXTURE="$(mktemp -d)"
# shellcheck disable=SC2064
trap "rm -rf '$FIXTURE'" EXIT

fail() { echo "FAIL: $*" >&2; exit 1; }

# Install a fake docker binary at $1 that records invocations under $2.
install_fake_docker() {
  local bin_dir="$1"
  local log_dir="$2"
  export TEST_LOG_DIR="$log_dir"
  mkdir -p "$bin_dir"
  cat > "$bin_dir/docker" <<'DOCKER'
#!/usr/bin/env bash
set -e
# Record every invocation for diagnostics.
echo "$*" >> "$TEST_LOG_DIR/docker_calls.log"

# Capture Semgrep target paths.
if [[ "$*" == *semgrep/semgrep:* ]]; then
  for arg in "$@"; do
    if [[ "$arg" == /src/* ]]; then
      printf '%s\n' "$arg" >> "$TEST_LOG_DIR/semgrep_targets.txt"
    fi
  done
fi

# Capture Gitleaks full command lines.
if [[ "$*" == *gitleaks/gitleaks:* ]]; then
  echo "$*" >> "$TEST_LOG_DIR/gitleaks_calls.log"
fi

echo "PASS: stubbed docker"
exit 0
DOCKER
  chmod +x "$bin_dir/docker"
}

# Copy the production script into $1/tools and run it with mode $2.
run_scanner() {
  local fixture="$1"
  local mode="${2:-}"
  local updates="${3:-}"
  local script_path="$fixture/tools/security-scan.sh"
  local status
  mkdir -p "$fixture/tools"
  cp "$REAL_SCRIPT" "$script_path"

  set +e
  if [ -n "$updates" ]; then
    printf '%s\n' "$updates" | PATH="$fixture/bin:$PATH" bash "$script_path" ${mode:+$mode} > "$fixture/run.log" 2>&1
  else
    PATH="$fixture/bin:$PATH" bash "$script_path" ${mode:+$mode} > "$fixture/run.log" 2>&1
  fi
  status=$?
  set -e
  return "$status"
}

# ------------------------------------------------------------------ test 1 ----
# Changed mode must exclude tracked symlinks from Semgrep targets.
test_changed_mode_excludes_symlinks() {
  local test_dir="$FIXTURE/symlink"
  mkdir -p "$test_dir"
  cd "$test_dir"

  git init --quiet
  git config user.email "test@example.com"
  git config user.name "Test"

  mkdir -p src
  printf 'base content\n' > src/base.cs
  git add src/base.cs
  git commit --quiet -m "base"

  printf 'Console.WriteLine("hello");\n' > src/Program.cs
  mkdir -p .claude/skills
  ln -s /tmp/escaped-outside-mount .claude/skills/review-skill
  git add src/Program.cs .claude/skills/review-skill
  git commit --quiet -m "change"

  install_fake_docker "$test_dir/bin" "$test_dir"
  SECURITY_SCAN_BASE=HEAD~1 run_scanner "$test_dir" ""

  [ -f "$test_dir/semgrep_targets.txt" ] || fail "Semgrep was never invoked"
  grep -qxF '/src/src/Program.cs' "$test_dir/semgrep_targets.txt" \
    || fail "regular file /src/src/Program.cs missing from Semgrep targets"
  grep -qxF '/src/.claude/skills/review-skill' "$test_dir/semgrep_targets.txt" \
    && fail "symlink /src/.claude/skills/review-skill was sent to Semgrep"

  echo "PASS: changed mode excludes tracked symlinks"
}

# ------------------------------------------------------------------ test 2 ----
# --staged mode must mount a linked worktree and its common git directory at
# their original absolute paths, with source/report under the worktree.
test_staged_linked_worktree_mounts() {
  local test_dir="$FIXTURE/worktree"
  mkdir -p "$test_dir"
  cd "$test_dir"

  # Create a bare repo and a normal clone so we can push a base commit.
  git init --bare repo.git --quiet
  git -C repo.git symbolic-ref HEAD refs/heads/main
  git clone repo.git checkout --quiet
  cd checkout
  git config user.email "test@example.com"
  git config user.name "Test"
  printf 'base\n' > base.txt
  git add base.txt
  git commit --quiet -m "base"
  git push --quiet origin main

  # Add a linked worktree; its .git file will point to repo.git/worktrees/wt.
  cd "$test_dir"
  git -C repo.git worktree add "$test_dir/wt" main --quiet

  cd "$test_dir/wt"
  git config user.email "test@example.com"
  git config user.name "Test"
  printf 'staged value\n' > staged.txt
  git add staged.txt

  install_fake_docker "$test_dir/wt/bin" "$test_dir"
  run_scanner "$test_dir/wt" "--staged"

  [ -f "$test_dir/gitleaks_calls.log" ] || fail "Gitleaks was not invoked in --staged mode"

  grep -qF -- "-v $test_dir/wt:$test_dir/wt" "$test_dir/gitleaks_calls.log" \
    || fail "linked worktree not mounted at original absolute path"
  grep -qF -- "-v $test_dir/repo.git/worktrees/wt:$test_dir/repo.git/worktrees/wt:ro" "$test_dir/gitleaks_calls.log" \
    || fail "worktree-specific git directory not mounted read-only at original absolute path"
  grep -qF -- "-v $test_dir/repo.git:$test_dir/repo.git:ro" "$test_dir/gitleaks_calls.log" \
    || fail "common git directory not mounted read-only at original absolute path"
  grep -qF -- "--source=$test_dir/wt" "$test_dir/gitleaks_calls.log" \
    || fail "--source not set to original worktree path"
  grep -qF -- "--report-path=$test_dir/wt/.security/gitleaks.json" "$test_dir/gitleaks_calls.log" \
    || fail "report path not under original worktree path"

  echo "PASS: --staged mounts linked worktree and common git directory"
}

create_remote_checkout() {
  local test_dir="$1"
  mkdir -p "$test_dir"
  git init --bare "$test_dir/repo.git" --quiet
  git -C "$test_dir/repo.git" symbolic-ref HEAD refs/heads/main
  git clone "$test_dir/repo.git" "$test_dir/checkout" --quiet
  git -C "$test_dir/checkout" config user.email "test@example.com"
  git -C "$test_dir/checkout" config user.name "Test"
  printf 'base\n' > "$test_dir/checkout/base.txt"
  git -C "$test_dir/checkout" add base.txt
  git -C "$test_dir/checkout" commit --quiet -m "base"
  git -C "$test_dir/checkout" push --quiet origin main
}

test_pre_push_scopes_single_ref() {
  local test_dir="$FIXTURE/pre-push-single"
  local zero_sha="0000000000000000000000000000000000000000"
  create_remote_checkout "$test_dir"
  cd "$test_dir/checkout"
  git switch --quiet -c feature
  printf 'feature\n' > feature.txt
  git add feature.txt
  git commit --quiet -m "feature"
  local feature_sha
  feature_sha="$(git rev-parse HEAD)"

  install_fake_docker "$test_dir/checkout/bin" "$test_dir"
  run_scanner "$test_dir/checkout" "--pre-push" \
    "refs/heads/feature $feature_sha refs/heads/feature $zero_sha"

  [ -f "$test_dir/gitleaks_calls.log" ] || fail "Gitleaks was not invoked for a pushed ref"
  grep -qF -- "--log-opts=origin/main..$feature_sha" "$test_dir/gitleaks_calls.log" \
    || fail "pre-push Gitleaks range missing"
  grep -qF -- "--all" "$test_dir/gitleaks_calls.log" \
    && fail "pre-push Gitleaks scan must not use --all"
  echo "PASS: pre-push scopes a single ref"
}

test_pre_push_scopes_multiple_refs() {
  local test_dir="$FIXTURE/pre-push-multiple"
  local zero_sha="0000000000000000000000000000000000000000"
  create_remote_checkout "$test_dir"
  cd "$test_dir/checkout"
  git switch --quiet -c feature-one
  printf 'one\n' > one.txt
  git add one.txt
  git commit --quiet -m "one"
  local one_sha
  one_sha="$(git rev-parse HEAD)"
  git switch --quiet -c feature-two origin/main
  printf 'two\n' > two.txt
  git add two.txt
  git commit --quiet -m "two"
  local two_sha
  two_sha="$(git rev-parse HEAD)"

  install_fake_docker "$test_dir/checkout/bin" "$test_dir"
  run_scanner "$test_dir/checkout" "--pre-push" "$(printf '%s\n%s' \
    "refs/heads/feature-one $one_sha refs/heads/feature-one $zero_sha" \
    "refs/heads/feature-two $two_sha refs/heads/feature-two $zero_sha")"

  [ "$(wc -l < "$test_dir/gitleaks_calls.log")" -eq 1 ] \
    || fail "multiple refs must use one Gitleaks invocation"
  grep -qF -- "--log-opts=origin/main..$one_sha origin/main..$two_sha" \
    "$test_dir/gitleaks_calls.log" || fail "both pre-push ranges missing"
  echo "PASS: pre-push scopes multiple refs"
}

test_pre_push_skips_deletion_only_update() {
  local test_dir="$FIXTURE/pre-push-deletion"
  local zero_sha="0000000000000000000000000000000000000000"
  create_remote_checkout "$test_dir"
  cd "$test_dir/checkout"
  local main_sha
  main_sha="$(git rev-parse HEAD)"

  install_fake_docker "$test_dir/checkout/bin" "$test_dir"
  run_scanner "$test_dir/checkout" "--pre-push" \
    "refs/heads/obsolete $zero_sha refs/heads/obsolete $main_sha"

  [ ! -f "$test_dir/gitleaks_calls.log" ] \
    || fail "deletion-only push must not scan Git history"
  echo "PASS: pre-push skips deletion-only updates"
}

test_pre_push_requires_origin_main() {
  local test_dir="$FIXTURE/pre-push-no-base"
  local zero_sha="0000000000000000000000000000000000000000"
  mkdir -p "$test_dir"
  cd "$test_dir"
  git init --quiet
  git config user.email "test@example.com"
  git config user.name "Test"
  printf 'orphan\n' > orphan.txt
  git add orphan.txt
  git commit --quiet -m "orphan"
  local orphan_sha
  orphan_sha="$(git rev-parse HEAD)"

  install_fake_docker "$test_dir/bin" "$test_dir"
  if run_scanner "$test_dir" "--pre-push" \
    "refs/heads/feature $orphan_sha refs/heads/feature $zero_sha"; then
    fail "pre-push accepted a repository without origin/main"
  fi
  grep -qF -- "cannot resolve origin/main; run 'git fetch origin main'" "$test_dir/run.log" \
    || fail "missing-base guidance not reported"
  [ ! -f "$test_dir/gitleaks_calls.log" ] \
    || fail "Gitleaks must not run without origin/main"
  echo "PASS: pre-push requires origin/main"
}

# --------------------------------------------------------------------- run ----
test_changed_mode_excludes_symlinks
test_staged_linked_worktree_mounts
test_pre_push_scopes_single_ref
test_pre_push_scopes_multiple_refs
test_pre_push_skips_deletion_only_update
test_pre_push_requires_origin_main

echo "ALL PASS"
