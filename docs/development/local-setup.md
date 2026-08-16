# Local development setup

## Requirements

- Git
- .NET 10 SDK compatible with root `global.json` (team baseline 10.0.300)
- MySQL 8+ server and CLI
- curl
- Bash (Linux/macOS/WSL for provided scripts)

## Setup

```bash
./scripts/setup.sh
```

Start the local MySQL service using your OS-specific method, then:

```bash
./scripts/db-init.sh
./scripts/db-status.sh
./scripts/migrate.sh all
./scripts/dev.sh core
./scripts/health.sh core
```

Use `./scripts/stop.sh core` when done.

The repository deliberately does not manage MySQL through Docker at this stage.
