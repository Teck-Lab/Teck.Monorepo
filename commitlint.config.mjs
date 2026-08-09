export default {
  extends: ["@commitlint/config-conventional"],
  ignores: [
    (commit) =>
      /^chore: Bump .+ from .+ to .+$/.test(commit.split(/\r?\n/, 1)[0]),
  ],
};
