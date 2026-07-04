# Arkanum Phase 3 role separation pass

This Phase 3 slice makes Arkanum's roles explicit in the UI and starts enforcing least-privilege behavior in vault workflows.

## Role descriptions

| Role | Intended use |
| --- | --- |
| Administrator | Full system administration: settings, users, roles, directory, health/evidence review, and vault oversight. |
| Vault Manager | Operational vault stewardship: create and manage vaults, folders, access, and entries without access to system-wide settings. |
| Auditor | Read-only oversight: review vault inventory and audit history without revealing or changing protected secret values. |
| User | Day-to-day vault use: open assigned vaults, add entries to accessible vaults, and reveal/copy secrets when authorized by vault membership. |

The same short descriptions are shown in Settings > User Management so administrators do not need to infer what each role means.

## Multi-role feasibility

Multi-role assignment is feasible with the current design and is already implemented at the data/UI level:

- Users store `RolesCsv`, a comma-separated role set.
- Create-user and update-user forms post checkbox lists.
- Login emits one role claim for each assigned role.
- Dashboard/visibility code already evaluates a role set instead of a single role.

The Phase 3 pass keeps that model and documents effective access as additive, with one important guardrail: an account whose only elevated purpose is Auditor remains read-only for vault contents. If an administrator intentionally assigns Auditor plus Vault Manager or Administrator, the higher operational role supplies the extra access.

A future production schema can normalize roles into `Users`, `Roles`, and `UserRoles`, but that is not required for the current preview to support multiple role assignments.

## Enforced role boundaries in this slice

- Administrator: manage system settings/users and can oversee/manage any vault.
- Vault Manager: can review/manage all vaults, create folders, invite users, and add entries without receiving system-settings access.
- Auditor: can list and open vaults for inventory review, but reveal/copy, invite, edit, new folder, and add-entry/add-note controls are hidden and server-side blocked for auditor-only accounts.
- User: can use assigned vaults and reveal/copy secrets when vault membership permits.

Server-side handlers now check explicit role/capability helpers instead of relying only on whether a user can open the vault page.
