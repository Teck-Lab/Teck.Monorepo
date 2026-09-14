# Paseo workspace ownership

- Use Paseo's native OMP adapter for workspace and agent lifecycle operations.
- Every Paseo agent must use the `omp` provider with an `omniroute/*` model. Never launch direct provider agents.
- Create a Paseo worktree before delegating file edits.
- Keep each editing task in one canonical workspace and supervise it through completion.
- Require focused validation and a signed conventional commit before accepting work.
