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
  local script_path="$fixture/tools/security-scan.sh"
  mkdir -p "$fixture/tools"
  cp "$REAL_SCRIPT" "$script_path"
  PATH="$fixture/bin:$PATH" bash "$script_path" ${mode:+$mode} > "$fixture/run.log" 2>&1 || true
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

# --------------------------------------------------------------------- run ----
test_changed_mode_excludes_symlinks
test_staged_linked_worktree_mounts

echo "ALL PASS"
