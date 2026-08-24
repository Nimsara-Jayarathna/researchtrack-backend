# US-103 Create Research Project — Full Creation Flow

Implemented behavior:

- Supervisor searches registered students through AuthService: `GET /api/v1/users/students?query=...`.
- Supervisor creates a project through `POST /api/v1/projects` with project basics, selected student ids, optional leader, and initial milestones.
- ProjectService resolves/validates the selected students through AuthService before persistence.
- The create operation persists the Project, Supervisor membership, Student memberships, and initial milestones in one Project database transaction.
- `GET /api/v1/projects` is role-aware: Supervisors receive owned projects; Students receive projects where they have STUDENT membership.
- `GET /api/v1/projects/{id}` is resource-aware: owner Supervisor or assigned Student only; unrelated callers receive `404`.
- ProjectService never queries AuthService's database directly and has no cross-service database foreign key. User profile snapshots are stored on project memberships so project reads do not require AuthService.
- Project creation starts in `PLANNING` with 0% progress; the project's milestone date is the earliest initial milestone.
- Student membership management after creation remains a later management operation, but initial membership is part of project creation.
