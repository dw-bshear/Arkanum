#!/usr/bin/env python3
"""Arkanum Linux GUI installer wrapper.

This Tkinter wizard collects install paths, SQL Server settings, and the listen URL,
then runs the packaged non-interactive Linux installer with those values. It is meant
for Ubuntu desktop-style administrator workstations. Headless servers can continue to
use installers/linux/install-arkanum.sh directly.
"""
from __future__ import annotations

import json
import os
import shlex
import subprocess
import sys
import tkinter as tk
from pathlib import Path
from tkinter import filedialog, messagebox, ttk

APP_TITLE = "Arkanum Linux Setup"
SCRIPT_DIR = Path(__file__).resolve().parent
PACKAGE_ROOT = SCRIPT_DIR.parent.parent
CLI_INSTALLER = SCRIPT_DIR / "install-arkanum.sh"
MANIFEST_PATH = PACKAGE_ROOT / "package-manifest.json"
PACKAGE_KIND = "auto"
if MANIFEST_PATH.exists():
    try:
        PACKAGE_KIND = json.loads(MANIFEST_PATH.read_text(encoding="utf-8")).get("packageKind", "auto") or "auto"
    except json.JSONDecodeError:
        PACKAGE_KIND = "auto"


def shell_quote(value: str) -> str:
    return shlex.quote(value)


def build_connection(values: dict[str, tk.StringVar], trust: tk.BooleanVar, encrypt: tk.BooleanVar) -> str:
    host = values["sql_host"].get().strip()
    port = values["sql_port"].get().strip() or "1433"
    database = values["sql_database"].get().strip()
    username = values["sql_user"].get().strip()
    password = values["sql_password"].get()
    if not all([host, database, username, password]):
        raise ValueError("SQL Server host, database, username, and password are required.")
    return (
        f"Server=tcp:{host},{port};"
        f"Database={database};"
        f"User Id={username};"
        f"Password={password};"
        f"Encrypt={'True' if encrypt.get() else 'False'};"
        f"TrustServerCertificate={'True' if trust.get() else 'False'};"
    )


class Wizard(tk.Tk):
    def __init__(self) -> None:
        super().__init__()
        self.title(APP_TITLE)
        self.geometry("760x640")
        self.resizable(True, True)
        self.vars = {
            "install_action": tk.StringVar(value="auto"),
            "database_setup_mode": tk.StringVar(value="use-existing-database"),
            "restore_source": tk.StringVar(value="dba-restored-database"),
            "install_dir": tk.StringVar(value="/opt/arkanum"),
            "config_dir": tk.StringVar(value="/etc/arkanum"),
            "data_dir": tk.StringVar(value="/var/lib/arkanum"),
            "log_dir": tk.StringVar(value="/var/log/arkanum"),
            "service_user": tk.StringVar(value="arkanum"),
            "app_urls": tk.StringVar(value="https://127.0.0.1:5068"),
            "https_certificate_path": tk.StringVar(value=""),
            "https_certificate_password": tk.StringVar(value=""),
            "sql_host": tk.StringVar(value=""),
            "sql_port": tk.StringVar(value="1433"),
            "sql_database": tk.StringVar(value="Arkanum"),
            "sql_user": tk.StringVar(value="arkanum_app"),
            "sql_password": tk.StringVar(value=""),
            "setup_sql_user": tk.StringVar(value=""),
            "setup_sql_password": tk.StringVar(value=""),
            "runtime_sql_user": tk.StringVar(value="arkanum_app"),
            "runtime_sql_password": tk.StringVar(value=""),
            "sql_backup_file": tk.StringVar(value=""),
            "restore_key_file": tk.StringVar(value=""),
            "restore_key": tk.StringVar(value=""),
        }
        self.encrypt = tk.BooleanVar(value=True)
        self.trust = tk.BooleanVar(value=False)
        self.generate_runtime_password = tk.BooleanVar(value=False)
        self.ack = tk.BooleanVar(value=False)
        self.command_text = tk.StringVar(value="")
        self.status = tk.StringVar(value="Complete the pages, then generate or run the installer command.")
        self._build()

    def _build(self) -> None:
        root = ttk.Frame(self, padding=16)
        root.pack(fill="both", expand=True)
        ttk.Label(root, text="Arkanum Setup", font=("TkDefaultFont", 18, "bold")).pack(anchor="w")
        ttk.Label(root, text=f"Guided Linux installer for the packaged Arkanum {PACKAGE_KIND} release.").pack(anchor="w", pady=(0, 12))
        notebook = ttk.Notebook(root)
        notebook.pack(fill="both", expand=True)
        notebook.add(self._paths_page(notebook), text="Install paths")
        notebook.add(self._sql_page(notebook), text="SQL Server")
        notebook.add(self._recovery_page(notebook), text="Recovery Key")
        notebook.add(self._run_page(notebook), text="Run")
        ttk.Label(root, textvariable=self.status).pack(anchor="w", pady=(8, 0))

    def _entry(self, frame: ttk.Frame, label: str, key: str, show: str | None = None) -> None:
        ttk.Label(frame, text=label).pack(anchor="w", pady=(8, 0))
        ttk.Entry(frame, textvariable=self.vars[key], show=show).pack(fill="x")

    def _paths_page(self, parent: ttk.Notebook) -> ttk.Frame:
        frame = ttk.Frame(parent, padding=12)
        ttk.Label(frame, text="Install action").pack(anchor="w", pady=(8, 0))
        action = ttk.Combobox(
            frame,
            textvariable=self.vars["install_action"],
            values=[
                "auto",
                "install",
                "upgrade existing installation",
                "Restore from database backup",
                "repair current installation",
                "install side-by-side",
            ],
            state="readonly",
        )
        action.pack(fill="x")
        ttk.Label(frame, text="Auto detects an existing install-state.json and selects upgrade. Use side-by-side for isolated test installs.", wraplength=680).pack(anchor="w", pady=(4, 8))
        ttk.Label(frame, text="Restore database source").pack(anchor="w", pady=(8, 0))
        restore_source = ttk.Combobox(
            frame,
            textvariable=self.vars["restore_source"],
            values=[
                "dba-restored-database",
                "Use database already restored by DBA",
                "Restore from SQL backup file",
                "Attach existing database files / DBA handoff",
            ],
            state="readonly",
        )
        restore_source.pack(fill="x")
        ttk.Label(frame, text="This release supports the DBA-restored database path. SQL backup and attach paths are shown for workflow planning but are not automated yet.", wraplength=680).pack(anchor="w", pady=(4, 8))
        for label, key in [
            ("Install directory", "install_dir"),
            ("Configuration directory", "config_dir"),
            ("Data directory", "data_dir"),
            ("Log directory", "log_dir"),
            ("Service user", "service_user"),
            ("Listen URL", "app_urls"),
            ("HTTPS certificate PFX path", "https_certificate_path"),
        ]:
            self._entry(frame, label, key)
        self._entry(frame, "HTTPS certificate password", "https_certificate_password", show="*")
        ttk.Label(frame, text="Use HTTPS with a trusted Kestrel PFX certificate, or terminate TLS before Arkanum and bind the service to an internal http://127.0.0.1 URL only.", wraplength=680).pack(anchor="w", pady=10)
        ttk.Label(frame, text="SQL backup file").pack(anchor="w", pady=(8, 0))
        ttk.Entry(frame, textvariable=self.vars["sql_backup_file"]).pack(fill="x")
        ttk.Label(frame, text="Recovery Key file").pack(anchor="w", pady=(8, 0))
        ttk.Entry(frame, textvariable=self.vars["restore_key_file"]).pack(fill="x")
        ttk.Label(frame, text="Paste Recovery Key").pack(anchor="w", pady=(8, 0))
        ttk.Entry(frame, textvariable=self.vars["restore_key"], show="*").pack(fill="x")
        ttk.Label(frame, text="Restore from database backup requires either a Recovery Key file or pasted Recovery Key.", wraplength=680).pack(anchor="w", pady=10)
        return frame

    def _sql_page(self, parent: ttk.Notebook) -> ttk.Frame:
        frame = ttk.Frame(parent, padding=12)
        ttk.Label(frame, text="Database setup mode").pack(anchor="w", pady=(8, 0))
        ttk.Combobox(frame, textvariable=self.vars["database_setup_mode"], values=["Use existing database", "Create new database", "use-existing-database", "create-new-database"], state="readonly").pack(fill="x")
        for label, key in [
            ("SQL Server host", "sql_host"),
            ("SQL Server port", "sql_port"),
            ("Database name", "sql_database"),
            ("SQL username", "sql_user"),
        ]:
            self._entry(frame, label, key)
        self._entry(frame, "SQL password", "sql_password", show="*")
        self._entry(frame, "Setup/admin SQL username", "setup_sql_user")
        self._entry(frame, "Setup/admin SQL password", "setup_sql_password", show="*")
        self._entry(frame, "Runtime app login", "runtime_sql_user")
        self._entry(frame, "Runtime app password", "runtime_sql_password", show="*")
        ttk.Checkbutton(frame, text="Generate runtime password", variable=self.generate_runtime_password).pack(anchor="w", pady=(10, 0))
        ttk.Checkbutton(frame, text="Encrypt SQL connection", variable=self.encrypt).pack(anchor="w", pady=(10, 0))
        ttk.Checkbutton(frame, text="Trust SQL Server certificate (development/testing only)", variable=self.trust).pack(anchor="w")
        ttk.Button(frame, text="Build connection string", command=self.generate_command).pack(anchor="w", pady=12)
        return frame

    def _recovery_page(self, parent: ttk.Notebook) -> ttk.Frame:
        frame = ttk.Frame(parent, padding=12)
        ttk.Label(frame, text="Recovery Key", font=("TkDefaultFont", 14, "bold")).pack(anchor="w")
        ttk.Label(
            frame,
            text=(
                "The installer creates /etc/arkanum/master-key if one does not already exist. "
                "The WebUI first-run setup will display the Arkanum Recovery Key again and require "
                "an acknowledgment before the first administrator can be created."
            ),
            wraplength=680,
        ).pack(anchor="w", pady=10)
        ttk.Label(frame, text="Recoverable = SQL database backup + matching Arkanum Recovery Key", wraplength=680).pack(anchor="w")
        ttk.Label(frame, text="Not recoverable = SQL database backup alone", wraplength=680).pack(anchor="w")
        ttk.Checkbutton(
            frame,
            text="I understand the recovery key must be saved before operational use.",
            variable=self.ack,
        ).pack(anchor="w", pady=16)
        return frame

    def _run_page(self, parent: ttk.Notebook) -> ttk.Frame:
        frame = ttk.Frame(parent, padding=12)
        ttk.Button(frame, text="Generate install command", command=self.generate_command).pack(anchor="w")
        ttk.Button(frame, text="Run installer", command=self.run_installer).pack(anchor="w", pady=6)
        ttk.Label(frame, text="Command preview").pack(anchor="w", pady=(12, 0))
        text = tk.Text(frame, height=13, wrap="word")
        text.pack(fill="both", expand=True)
        self.command_text.trace_add("write", lambda *_: (text.delete("1.0", "end"), text.insert("1.0", self.command_text.get())))
        ttk.Label(frame, text="Run installer uses pkexec when available, otherwise sudo. You may be prompted for administrator credentials.", wraplength=680).pack(anchor="w", pady=8)
        return frame

    def generate_command(self) -> str:
        action_map = {
            "auto": "auto",
            "install": "install",
            "upgrade existing installation": "upgrade",
            "Restore from database backup": "restore",
            "repair current installation": "repair",
            "install side-by-side": "side-by-side",
        }
        selected_action = action_map.get(self.vars["install_action"].get(), "auto")
        is_state_preserving_update = PACKAGE_KIND == "update" and selected_action in {"auto", "upgrade", "repair"}
        conn = ""
        if not is_state_preserving_update or self.vars["sql_host"].get().strip() or self.vars["sql_password"].get():
            try:
                conn = build_connection(self.vars, self.trust, self.encrypt)
            except ValueError as exc:
                messagebox.showerror(APP_TITLE, str(exc))
                raise
        database_setup_map = {"Use existing database": "use-existing-database", "Create new database": "create-new-database"}
        selected_database_setup = database_setup_map.get(self.vars["database_setup_mode"].get(), self.vars["database_setup_mode"].get() or "use-existing-database")
        restore_source_map = {
            "Use database already restored by DBA": "dba-restored-database",
            "Restore from SQL backup file": "sql-backup-file",
            "Attach existing database files / DBA handoff": "attach-database-files",
        }
        selected_restore_source = restore_source_map.get(self.vars["restore_source"].get(), self.vars["restore_source"].get() or "dba-restored-database")
        env = {
            "ACTION": selected_action,
            "DATABASE_SETUP_MODE": selected_database_setup,
            "SQL_SERVER_HOST": self.vars["sql_host"].get().strip(),
            "SQL_SERVER_PORT": self.vars["sql_port"].get().strip(),
            "DATABASE_NAME": self.vars["sql_database"].get().strip(),
            "SETUP_SQL_USERNAME": self.vars["setup_sql_user"].get().strip(),
            "SETUP_SQL_PASSWORD": self.vars["setup_sql_password"].get(),
            "RUNTIME_SQL_LOGIN": self.vars["runtime_sql_user"].get().strip(),
            "RUNTIME_SQL_PASSWORD": self.vars["runtime_sql_password"].get(),
            "GENERATE_RUNTIME_PASSWORD": "true" if self.generate_runtime_password.get() else "false",
            "RESTORE_SOURCE": selected_restore_source,
            "SQL_BACKUP_FILE": self.vars["sql_backup_file"].get().strip(),
            "ARKANUM_PACKAGE_VERSION": "1.1.0",
            "PACKAGE_KIND": PACKAGE_KIND,
            "INSTALL_DIR": self.vars["install_dir"].get().strip(),
            "CONFIG_DIR": self.vars["config_dir"].get().strip(),
            "DATA_DIR": self.vars["data_dir"].get().strip(),
            "LOG_DIR": self.vars["log_dir"].get().strip(),
            "SERVICE_USER": self.vars["service_user"].get().strip(),
            "APP_URLS": self.vars["app_urls"].get().strip(),
            "ARKANUM_HTTPS_CERTIFICATE_PATH": self.vars["https_certificate_path"].get().strip(),
            "ARKANUM_HTTPS_CERTIFICATE_PASSWORD": self.vars["https_certificate_password"].get(),
            "ARKANUM_SQL_CONNECTION": conn,
            "ARKANUM_RESTORE_KEY_FILE": self.vars["restore_key_file"].get().strip(),
            "ARKANUM_RESTORE_KEY": self.vars["restore_key"].get().strip(),
        }
        assignments = " ".join(f"{k}={shell_quote(v)}" for k, v in env.items())
        cmd = f"env {assignments} {shell_quote(str(CLI_INSTALLER))}"
        self.command_text.set(cmd)
        self.status.set("Install command generated. Review it or run installer.")
        return cmd

    def run_installer(self) -> None:
        if not self.ack.get():
            messagebox.showerror(APP_TITLE, "Acknowledge the Recovery Key requirement before running setup.")
            return
        cmd = self.generate_command()
        escalator = "pkexec" if shutil_which("pkexec") else "sudo"
        full = [escalator, "/bin/bash", "-lc", cmd]
        if not messagebox.askyesno(APP_TITLE, "Run the Arkanum installer now? You may be prompted for administrator credentials."):
            return
        result = subprocess.run(full, text=True, capture_output=True)
        if result.returncode == 0:
            messagebox.showinfo(APP_TITLE, "Arkanum installer completed. Open the WebUI first-run setup to save the Recovery Key and create the first administrator.")
        else:
            messagebox.showerror(APP_TITLE, f"Installer failed.\n\nSTDOUT:\n{result.stdout[-2000:]}\n\nSTDERR:\n{result.stderr[-2000:]}")


def shutil_which(name: str) -> str | None:
    for folder in os.environ.get("PATH", "").split(os.pathsep):
        candidate = Path(folder) / name
        if candidate.exists() and os.access(candidate, os.X_OK):
            return str(candidate)
    return None


def main() -> int:
    if not CLI_INSTALLER.exists():
        print(f"Missing CLI installer: {CLI_INSTALLER}", file=sys.stderr)
        return 2
    try:
        Wizard().mainloop()
        return 0
    except tk.TclError as exc:
        print("Unable to start the GUI installer. Install python3-tk and run from a graphical desktop session.", file=sys.stderr)
        print(exc, file=sys.stderr)
        return 3


if __name__ == "__main__":
    raise SystemExit(main())
