# Coverage Gap Register

This register prevents coverage work from turning into random test-count inflation.

| Priority | Area | Baseline observation | Phase 4 action |
|---|---|---|---|
| P0 | ProjectService | ~20% line, very low branch | Expanded access/update/leader/member tests |
| P0 | SubmissionRequirementService | 0% | Added lifecycle/validation/persistence tests |
| P0 | SupervisorDashboardService | 0% | Added aggregation/window/recent-project tests |
| P1 | Auth UserDirectoryService | 0% | Added search/resolve/get tests |
| P1 | GitHubEvidenceQueryService | 0% | Added validation/auth/repository/page tests |
| P1 | ResearchSubmissionService | low | Existing mocked upload-session tests retained; complete/review are next mutation-driven targets |
| P1 | JiraSyncService | 0% | Keep as a mutation/mock follow-up because synchronization has a large persistence/external API surface |
| P1 | GitHubRepositorySynchronizationService | 0% | Keep as mutation/mock follow-up; do not fake coverage through controller tests |
| P2 | Controllers / DTOs | often 0% | Intentionally outside unit quality gate |
| P2 | HTTP/S3 adapters | often low | Covered selectively; integration/contract testing is a better fit |

The register should be revisited after each merged coverage and mutation report. Surviving mutants and high-CRAP business methods take precedence over increasing the raw repository-wide percentage.
