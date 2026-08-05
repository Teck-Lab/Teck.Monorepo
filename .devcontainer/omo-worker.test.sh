#!/usr/bin/env bash
set -euo pipefail

launcher="$(cd "$(dirname "$0")" && pwd)/omo-worker.sh"
fixture="$(mktemp -d)"
trap 'rm -rf "$fixture"' EXIT

git init "$fixture/worktree" >/dev/null
git -C "$fixture/worktree" config user.name 'Test Agent'
git -C "$fixture/worktree" config user.email 'agent@example.test'
printf 'fixture\n' > "$fixture/worktree/README.md"
git -C "$fixture/worktree" add README.md
git -C "$fixture/worktree" commit -m 'chore: initialize fixture' >/dev/null

planned="$(HOME="$fixture/home" "$launcher" --worktree "$fixture/worktree" --parent-issue 120 --issue 121 --slug 'Tax System' --mode planned --dry-run)"
bun -e 'const d=JSON.parse(await Bun.stdin.text()); if (d.session !== "teck-120-121-tax-system" || d.agent !== "prometheus" || d.harness !== "full" || d.existing !== false || !Number.isInteger(d.port)) process.exit(1)' <<<"$planned"

autonomous="$(HOME="$fixture/home" "$launcher" --worktree "$fixture/worktree" --parent-issue 120 --issue 122 --slug checkout --mode autonomous --dry-run)"
bun -e 'const d=JSON.parse(await Bun.stdin.text()); if (d.agent !== "hephaestus" || d.mode !== "autonomous") process.exit(1)' <<<"$autonomous"

slim="$(HOME="$fixture/home" "$launcher" --worktree "$fixture/worktree" --parent-issue 120 --issue 123 --slug invoice --harness slim --dry-run)"
bun -e 'const d=JSON.parse(await Bun.stdin.text()); if (d.agent !== "orchestrator" || d.harness !== "slim" || !d.configDir.endsWith("/profiles/slim")) process.exit(1)' <<<"$slim"

if HOME="$fixture/home" "$launcher" --worktree "$fixture/worktree" --parent-issue 120 --issue 124 --slug bad --mode unknown --dry-run >/dev/null 2>&1; then
  echo "launcher accepted an invalid mode" >&2
  exit 1
fi

echo "omo worker launcher tests passed"
