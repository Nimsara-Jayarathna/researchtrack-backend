# AuthService Mutation Runbook

From the repository root:

```bash
bash ./scripts/mutation-baseline.sh auth
```

The runner first executes the dedicated Auth mutation harness once. Only if it passes does Stryker run the six Auth feature subscopes.

Reports are written under:

```text
artifacts/mutation/auth/
  password-policy/
  authentication/
  password-reset/
  registration/
  user-account/
  user-directory/
```

For survivor analysis, open the HTML report in the affected subscope. Classify survivors as: meaningful test weakness, equivalent mutant, or justified non-business mutation. Do not exclude a meaningful mutant merely to raise the score.

After adding a justified test, run the normal Auth tests and the dedicated harness before rerunning Stryker.
