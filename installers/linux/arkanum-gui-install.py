#!/usr/bin/env python3
"""Arkanum Linux GUI installer wrapper.

This Tkinter wizard collects install paths, SQL Server settings, and the listen URL,
then runs the packaged non-interactive Linux installer with those values. It is meant
for Ubuntu desktop-style administrator workstations. Headless servers can continue to
use installers/linux/install-arkanum.sh directly.
"""
from __future__ import annotations

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
            "install_dir": tk.StringVar(value="/opt/arkanum"),
            "config_dir": tk.StringVar(value="/etc/arkanum"),
            "data_dir": tk.StringVar(value="/var/lib/arkanum"),
            "log_dir": tk.StringVar(value="/var/log/arkanum"),
            "service_user": tk.StringVar(value="arkanum"),
            "app_urls": tk.StringVar(value="http://127.0.0.1:5068"),
            "sql_host": tk.StringVar(value=""),
            "sql_port": tk.StringVar(value="1433"),
            "sql_database": tk.StringVar(value="Arkanum"),
            "sql_user": tk.StringVar(value="arkanum_app"),
            "sql_password": tk.StringVar(value=""),
        }
        self.encrypt = tk.BooleanVar(value=True)
        self.trust = tk.BooleanVar(value=True)
        self.ack = tk.BooleanVar(value=False)
        self.command_text = tk.StringVar(value="")
        self.status = tk.StringVar(value="Complete the pages, then generate or run the installer command.")
        self._build()

    def _build(self) -> None:
        root = ttk.Frame(self, padding=16)
        root.pack(fill="both", expand=True)
        ttk.Label(root, text="Arkanum Setup", font=("TkDefaultFont", 18, "bold")).pack(anchor="w")
        ttk.Label(root, text="Guided Linux installer for the packaged Arkanum release.").pack(anchor="w", pady=(0, 12))
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
                "repair current installation",
                "install side-by-side",
            ],
            state="readonly",
        )
        action.pack(fill="x")
        ttk.Label(frame, text="Auto detects an existing install-state.json and selects upgrade. Use side-by-side for isolated test installs.", wraplength=680).pack(anchor="w", pady=(4, 8))
        for label, key in [
            ("Install directory", "install_dir"),
            ("Configuration directory", "config_dir"),
            ("Data directory", "data_dir"),
            ("Log directory", "log_dir"),
            ("Service user", "service_user"),
            ("Listen URL", "app_urls"),
        ]:
            self._entry(frame, label, key)
        ttk.Label(frame, text="Use http://0.0.0.0:5068 if the site should be reachable from other LAN clients.", wraplength=680).pack(anchor="w", pady=10)
        return frame

    def _sql_page(self, parent: ttk.Notebook) -> ttk.Frame:
        frame = ttk.Frame(parent, padding=12)
        for label, key in [
            ("SQL Server host", "sql_host"),
            ("SQL Server port", "sql_port"),
            ("Database name", "sql_database"),
            ("SQL username", "sql_user"),
        ]:
            self._entry(frame, label, key)
        self._entry(frame, "SQL password", "sql_password", show="*")
        ttk.Checkbutton(frame, text="Encrypt SQL connection", variable=self.encrypt).pack(anchor="w", pady=(10, 0))
        ttk.Checkbutton(frame, text="Trust SQL Server certificate", variable=self.trust).pack(anchor="w")
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
        try:
            conn = build_connection(self.vars, self.trust, self.encrypt)
        except ValueError as exc:
            messagebox.showerror(APP_TITLE, str(exc))
            raise
        action_map = {
            "auto": "auto",
            "install": "install",
            "upgrade existing installation": "upgrade",
            "repair current installation": "repair",
            "install side-by-side": "side-by-side",
        }
        selected_action = action_map.get(self.vars["install_action"].get(), "auto")
        env = {
            "ACTION": selected_action,
            "ARKANUM_PACKAGE_VERSION": "2026.0.3",
            "INSTALL_DIR": self.vars["install_dir"].get().strip(),
            "CONFIG_DIR": self.vars["config_dir"].get().strip(),
            "DATA_DIR": self.vars["data_dir"].get().strip(),
            "LOG_DIR": self.vars["log_dir"].get().strip(),
            "SERVICE_USER": self.vars["service_user"].get().strip(),
            "APP_URLS": self.vars["app_urls"].get().strip(),
            "ARKANUM_SQL_CONNECTION": conn,
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
