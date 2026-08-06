import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import {
  currentLifecycle,
  labelsToRemove,
  lifecycleNames,
  planIssueEvent,
} from "./agent-lifecycle.mjs";

const config = JSON.parse(
  await readFile(new URL("../agent-lifecycle.json", import.meta.url), "utf8"),
);
const event = (action, labels, label = null) => ({
  action,
  issue: { number: 42, labels: labels.map((name) => ({ name })) },
  label: label ? { name: label } : undefined,
});

test("defines five unique lifecycle labels", () => {
  assert.equal(lifecycleNames(config).size, 5);
});

test("allows a ready issue to be claimed", () => {
  assert.deepEqual(
    planIssueEvent(event("labeled", ["agent:ready", "agent:claimed"], "agent:claimed"), config),
    {
      operation: "transition",
      target: "agent:claimed",
    },
  );
});

test("rejects claiming an unmanaged issue", () => {
  assert.deepEqual(planIssueEvent(event("labeled", ["agent:claimed"], "agent:claimed"), config), {
    operation: "reject",
    target: "agent:claimed",
    reason: "unmanaged cannot transition to agent:claimed",
  });
});

test("allows needs-input work to resume with the same orchestrator", () => {
  assert.deepEqual(
    planIssueEvent(
      event("labeled", ["agent:needs-input", "agent:claimed"], "agent:claimed"),
      config,
    ),
    {
      operation: "transition",
      target: "agent:claimed",
    },
  );
});

test("closing managed work completes it", () => {
  assert.deepEqual(planIssueEvent(event("closed", ["agent:in-review"]), config), {
    operation: "transition",
    target: "agent:completed",
  });
});

test("closing an unmanaged issue does nothing", () => {
  assert.deepEqual(planIssueEvent(event("closed", ["security"]), config), { operation: "noop" });
});

test("reopening completed work requests human input", () => {
  assert.deepEqual(planIssueEvent(event("reopened", ["agent:completed"]), config), {
    operation: "transition",
    target: "agent:needs-input",
  });
});

test("non-lifecycle labels do not affect state", () => {
  assert.deepEqual(
    planIssueEvent(event("labeled", ["security", "agent:ready"], "security"), config),
    {
      operation: "noop",
    },
  );
});

test("a transition removes every other lifecycle label", () => {
  const labels = ["security", "agent:ready", "agent:needs-input", "severity:high"];
  assert.deepEqual(labelsToRemove(labels, "agent:claimed", config), [
    "agent:needs-input",
    "agent:ready",
  ]);
  assert.deepEqual(currentLifecycle(labels, config), ["agent:needs-input", "agent:ready"]);
});
