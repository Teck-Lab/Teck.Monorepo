# Test-driven development contract

Apply this contract to every execution member. The delivery architect chooses
one development mode before dispatch; an executor cannot weaken it.

## Mode selection

Use `tdd` when the member changes observable product behavior, fixes a defect,
changes domain logic, or changes an API or security contract. Define the
smallest behavioral boundary that can fail for the intended reason.

Use `required-validation-only` only when a meaningful pre-change behavioral
failure cannot be produced, such as documentation, generated artifacts,
metadata, mechanical wiring, or configuration whose supported validator is the
behavioral oracle. The manifest must name the validation boundary and explain
why a red test is not meaningful. Convenience, time pressure, or an already
written implementation are not valid exceptions.

## TDD execution

For `tdd`, preserve evidence of this sequence:

1. **Red:** add or change the smallest focused test, run it against the
   pre-fix behavior, and record the expected failure. A test that unexpectedly
   passes is not red; investigate whether the behavior already exists, the test
   is ineffective, or the manifest is stale before editing production code.
2. **Green:** implement the smallest complete change that makes the focused
   test pass and record the successful command.
3. **Refactor:** improve the implementation or explicitly record that no
   refactor was needed, then rerun the focused test and required validation.

Never manufacture a red phase by breaking working code, weakening assertions,
or reporting an unobserved failure. Existing implementation recovered from a
partial attempt does not permit invented history: preserve verifiable evidence,
or escalate so the coordinator can revise the development mode or recovery
contract before continuing.

For `required-validation-only`, run the manifest's named validation before and
after the change when that comparison is meaningful, then run all required
post-change gates. Record the exception reason and exact results.

## Evidence and review

The implementation result repeats the approved mode and boundary. TDD results
include distinct `red`, `green`, and `refactor` evidence. Validation-only
results include `exception-reason` and `validation-only-evidence` instead.

Code review verifies that evidence is reproducible and matches the committed
diff. Missing, contradictory, fabricated, or unjustified evidence is a bounded
omission. QA consumes the evidence but remains an independent integrated
acceptance gate; QA is never a substitute for member-level TDD.
