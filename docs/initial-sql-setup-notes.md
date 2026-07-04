# Arkanum initial SQL Server setup notes

This is a planning note for the installer/setup workflow. The admin Settings page can now store SQL Server connection values, but those values do not by themselves move a running Arkanum instance to a different database. The running database provider is selected at application startup from `ConnectionStrings:DefaultConnection`.

Open questions for the installation workflow:

1. Whether the installer creates the SQL Server database or connects to an existing database.
2. Which credential is used for one-time setup/migration work. This should be separate from the runtime web-application credential.
3. Which least-privilege SQL login Arkanum uses at runtime after setup.
4. Where the final active connection string is stored on Windows Server 2022/IIS, such as environment variables, appsettings production transform, or a protected configuration provider.
5. Whether setup should run schema creation/migration automatically or require an explicit administrator confirmation.
6. How setup reports success before restart: database exists, schema applied, runtime login verified, and application restarted against SQL Server.

Recommended direction:

- Use an elevated setup credential only during installation to create the database/schema and runtime SQL login.
- Store only the runtime SQL connection for normal application use.
- Restart Arkanum after SQL settings are applied so Entity Framework uses the selected provider from startup.
- Keep the admin Settings page as the control surface for connection values and status, but treat initial database creation as a dedicated setup/install step rather than a normal in-app save action.
