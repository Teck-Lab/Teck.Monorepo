# Oh My OpenAgent — All-GPT Team Mode Migration

**Date:** 2026-07-24  
**Status:** Approved for planning  
**Supersedes:** The OMO portion of `2026-07-20-oh-my-opencode-slim-migration-design.md`

## Summary

Replace `oh-my-opencode-slim` 2.2.8 with normal Oh My OpenAgent (the current
upstream name for Oh My OpenCode) across the active user setup and the committed
dev-container template. Configure every OMO agent and delegation category with
an `openai/*` GPT model, enable Team Mode, and turn on tmux visualization from
the first run.

This migration changes agent orchestration only. It does not change application
code, the LiteLLM gateway, MCP definitions, `opencode-mem`, `cc-safety-net`,
`opencode-snip`, or Superpowers.

## Goals

- Use normal OMO's 11-agent runtime and Team Mode instead of slim's seven-agent
  runtime and background-subagent emulation.
- Restrict model resolution and fallbacks to native `openai/*` GPT models.
- Start Team Mode with live tmux visualization enabled.
- Keep the dev-container template and the active user config aligned.
- Retain a reversible backup of the active slim setup.

## Non-goals

- Do not alter source code under `src/` or test configuration.
- Do not remove the LiteLLM gateway; it remains available to unrelated tools but
  is not used by OMO agent routing.
- Do not add new MCP servers or alter existing MCP permissions.
- Do not run slim and normal OMO together.

## Architecture

### Plugin and configuration ownership

| Location | Responsibility |
|---|---|
| `~/.config/opencode/opencode.json` | Active user plugin registration; remove slim and add normal OMO. |
| `~/.config/opencode/oh-my-openagent.jsonc` | Active Team Mode and all-GPT agent routing. |
| `.devcontainer/opencode/opencode.json` | Version-controlled plugin registration for rebuilt containers. |
| `.devcontainer/opencode/oh-my-openagent.jsonc` | Version-controlled configuration template copied into the user's config directory. |
| `.devcontainer/postCreate.sh` | Seed the template and replace the `omos` launcher/install path with normal OMO's tmux-compatible startup path. |

Before changing the active configuration, copy `opencode.json` and
`oh-my-opencode-slim.jsonc` to timestamped backups. The committed slim template
is removed only after the normal template has passed its smoke tests.

### Model routing

All values use the `openai/` provider prefix. The exact model IDs are a
preflight gate: use only IDs returned by `opencode models --refresh` for the
authenticated OpenAI account.

| Workload | Target GPT tier | Agent/category assignments |
|---|---|---|
| Heavy coding, orchestration, architecture, review | `gpt-5.6-sol` | `sisyphus`, `hephaestus`, `oracle`, `atlas`, `momus`, `unspecified-high`, `ultrabrain`, `visual-engineering` |
| Deep autonomous work | `gpt-5.6-terra` | `deep` |
| Fast navigation, research, routine delegation | `gpt-5.6-luna` | `librarian`, `explore`, `sisyphus-junior`, `unspecified-low` |
| Fast plans and trivial tasks | `gpt-5-nano` | `prometheus`, `metis`, `quick` |

`multimodal-looker` uses the strongest OpenAI GPT model with confirmed image
input support; if `gpt-5.6-sol` cannot accept images through OpenCode, it uses
the strongest image-capable `openai/*` model returned by the preflight instead.

Every configured fallback remains within `openai/*`. No Claude, Gemini, Kimi,
Grok, GitHub Copilot, LiteLLM, or OpenCode-hosted provider is permitted in the
agent or category maps. The compatibility `no-sisyphus-gpt` hook remains
enabled.

### Team Mode and tmux

Team Mode is explicitly enabled with tmux visualization from the first run:

```jsonc
"team_mode": {
  "enabled": true,
  "max_parallel_members": 4,
  "max_members": 8,
  "tmux_visualization": true
}
```

Normal OMO must start inside an existing tmux session. The setup verifies that
`tmux` is installed and that the dev-container launcher gives a clear failure
message when no tmux session is active. Team Mode's four-member parallel limit
is deliberately lower than the global OpenAI task limit to leave capacity for
the orchestrator and background work.

```jsonc
"background_task": {
  "defaultConcurrency": 5,
  "providerConcurrency": {
    "openai": 5
  }
}
```

### Target configuration shape

The generated `oh-my-openagent.jsonc` contains:

1. The current upstream schema URL.
2. Explicit `agents` entries for all 11 normal-OMO agents:
   `sisyphus`, `hephaestus`, `prometheus`, `atlas`, `oracle`, `librarian`,
   `explore`, `multimodal-looker`, `metis`, `momus`, and `sisyphus-junior`.
3. Explicit `categories` entries for `quick`, `unspecified-low`,
   `unspecified-high`, `visual-engineering`, `deep`, and `ultrabrain`.
4. The Team Mode and background-task blocks above.
5. Existing useful prompt intent translated to the matching normal-OMO agents,
   without preserving slim-specific agent names, presets, or hooks.

Configuration syntax, accepted model variants, and model IDs are validated
against the installed OMO/OpenCode versions before the config becomes active.

## Migration sequence

1. Check `opencode --version`; upgrade to OpenCode `>=1.4.0` if required.
2. Back up active OpenCode and slim configuration files.
3. Authenticate native OpenAI, refresh available models, and select verified
   GPT IDs for the routing table.
4. Remove the slim plugin registration from the active and committed configs.
5. Install normal OMO with `bunx oh-my-openagent install`, then retain only the
   resulting normal-OMO plugin registration.
6. Add the validated all-GPT `oh-my-openagent.jsonc` active config and committed
   dev-container template.
7. Update `postCreate.sh`, documentation, and launch instructions to use normal
   OMO in an active tmux session.
8. Restart OpenCode, because it only loads config at process startup.
9. Run normal delegation and a bounded four-member Team Mode smoke test.
10. Remove the obsolete slim template/config only after the tests pass.

## Verification

1. `opencode models --refresh` lists every model ID written to the config.
2. `bunx oh-my-openagent doctor --verbose` reports a valid plugin, schema,
   model resolution, and enabled Team Mode.
3. `opencode` starts inside tmux and exposes normal OMO agents without any slim
   agent or hook collision.
4. A regular implementation delegation resolves to an `openai/*` model.
5. A Team Mode task starts four visible tmux member panes and completes without
   non-OpenAI model fallback.
6. Existing Context7, GitHub, SearXNG, Crawl4AI, memory, safety-net, snip, and
   Superpowers integrations still load.
7. A container rebuild seeds the same normal-OMO configuration and launcher.

## Rollback

Stop OpenCode, restore the timestamped `opencode.json` and slim configuration,
remove the normal-OMO plugin/configuration, then restart OpenCode inside tmux.
This is safe because the migration does not modify application code or the
LiteLLM gateway.

## Risks and mitigations

| Risk | Mitigation |
|---|---|
| Requested GPT model is unavailable to native OpenAI auth | Select only refreshed IDs; stop before activating the config. |
| OpenAI rate limits under parallel work | Start with five global and four Team Mode concurrent tasks; tune only after smoke tests. |
| tmux visualization cannot attach | Require an existing tmux session and test the launcher before delegation. |
| Normal and slim OMO are both registered | Treat this as a blocking preflight failure; do not restart until only one is registered. |
| GPT-only mapping regresses visual/image work | Validate `multimodal-looker` image capability before routing visual tasks. |
