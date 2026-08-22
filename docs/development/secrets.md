# Secrets and runtime configuration

ResearchTrack uses service-owned environment configuration.

Each component has a committed contract at `config/env/<service>/.env.example`. Real local values belong in the corresponding `.env.local`, which is ignored by Git.

Secrets and configurable business policy are both external to source code. Examples include database passwords, JWT signing keys, GitHub/Jira OAuth credentials, storage credentials, institutional email domains, identifier rules, password policies, and synchronization intervals.

For test/production, inject the same configuration keys from the CI/CD or hosting platform secret/environment system. Do not copy developer `.env.local` files into deployment artifacts.

If any real secret is committed or posted to an uncontrolled system, rotate it immediately.
