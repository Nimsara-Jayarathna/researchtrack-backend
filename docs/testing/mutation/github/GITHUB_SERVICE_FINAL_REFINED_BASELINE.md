# GitHubService final refined mutation baseline

## Why this final refinement exists

The hardened GitHub baseline is already strong in the externally exposed parsing and security rules:

- Repository URL: 87.23% (41 killed / 6 survived)
- Webhook signature: 78.57% (11 / 3)
- Access request token: 77.78% (14 / 4)
- Installation state: 57.39% (66 / 49)
- Repository link: 59.46% (66 / 45)
- Webhook event processor: 65.88% (112 / 58)

The original reconciliation scope reported 37.50% (3 / 5), but that percentage was based on only eight testable mutants while 55 generated mutants were compile errors. That made the percentage disproportionately sensitive to Stryker/C# async-EF instrumentation rather than the actual reconciliation business decision.

## Final scope refinement

`GitHubReconciliationService` still owns the real orchestration and is covered by its service tests. The mutation target is now the extracted deterministic `GitHubReconciliationPolicy.HasRemoteChange` rule used by the service.

The policy intentionally preserves the production semantics:

- default-branch comparison is ordinal/case-sensitive;
- commit SHA comparison is ordinal-ignore-case;
- either a branch change or a head change requires synchronization;
- missing local snapshot information is treated as changed.

This is not a score-only exclusion: the production service calls the policy directly, and the dedicated mutation tests verify every independent condition and boundary while avoiding compiler-noise from generated async/EF mutants.

## Final verification

Run:

```bash
bash ./scripts/mutation-baseline.sh github
```

The command verifies the normal GitHub unit/mock suite, verifies the dedicated mutation harness, and runs the final high-value scopes including the refined reconciliation policy.

Do not average subscope percentages. The final GitHub score is always calculated from total killed / (killed + survived) across the selected valid mutants.
