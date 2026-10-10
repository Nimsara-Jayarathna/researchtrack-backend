# ProjectService survivor hardening

## Baseline that triggered this pass

The project-wide mutation baseline measured ProjectService at **57.89% weighted** (275 killed / 200 survived):

- deterministic rules: 75.00% (147 / 49)
- ProjectService orchestration: 45.15% (121 / 147)
- SupervisorDashboardService: 63.64% (7 / 4)

The weak point was therefore orchestration assertions, not basic rule execution.

## Hardening strategy

This pass keeps production behavior unchanged and strengthens tests around independently observable business decisions. It deliberately avoids exclusions or production refactors whose only purpose would be to increase the score.

The same mutation-oriented behavior suite exists in both the official xUnit v3 ProjectService test project and the dedicated xUnit mutation harness. The suite covers:

- independent supervisor identity and role authorization during project creation;
- complete create-project aggregate persistence, normalization, student ordering, leader selection, milestone sequencing, and timestamps;
- supervisor and student access predicates, including cross-project and wrong-role memberships;
- accessible-project filtering, ordering, member counts, supervisor projection, and unknown roles;
- accessible-project detail authorization, supervisor/leader mapping, and milestone ordering;
- project update normalization, all supported lifecycle states, null-status preservation, authorization, timestamps, and no-write failure paths;
- leader assignment/clearing, same-project student membership, wrong-role rejection, and timestamps;
- member-add orchestration, unresolved students, duplicate membership conflicts, all-or-nothing behavior, directory interaction boundaries, mapped fields, and activity timestamps;
- member removal, cross-project/wrong-role protection, leader clearing only when appropriate, and no-write failure paths;
- milestone creation authorization, chronology, description normalization, sequencing, project aggregates, and timestamps;
- milestone update status/due-date decisions, neighbour chronology boundaries, aggregate recalculation only when required, and no-write failure paths;
- dashboard supervisor isolation, status aggregates, case-insensitive status matching, 14-day inclusive milestone window, completed-project exclusion, ordering/tie-breakers, recent-project limit, member counts, and neutral Jira projection fields.

## Verification

Run the complete ProjectService pass once:

```bash
bash ./scripts/mutation-baseline.sh project
```

The runner first verifies the normal ProjectService unit/mock suite and then the dedicated mutation harness before executing the three Project subscopes. The resulting service score must be taken from generated killed/survived counts rather than by averaging the three percentages.

Do not set or raise the Stryker `break` threshold until the hardened result has been measured and reviewed. Remaining survivors should be classified as business-significant, observable but low-risk, or equivalent/non-business-value before any further test work.
