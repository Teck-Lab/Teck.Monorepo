# Paseo workspace ownership

- Use Paseo's native OMP adapter for workspace and agent lifecycle operations.
- Every Paseo agent must use the `omp` provider with an `omniroute/*` model. Never launch direct provider agents.
- Provider routing is a hard constraint. If an OMP/OmniRoute worker is unavailable, silent, slow, or fails, diagnose or retry that route; never substitute another Paseo provider.
- After starting a worker, do not steer, cancel, replace, or send follow-up prompts merely because its timeline is quiet or work is taking longer than expected. Wait for its completion notification; intervene only for an explicit permission request, concrete provider error, expired deadline, or new user instruction.
- Create a Paseo worktree before delegating file edits.
- Keep each editing task in one canonical workspace and supervise it through completion.
- Require focused validation and a signed conventional commit before accepting work.
