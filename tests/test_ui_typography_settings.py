"""Offline UI regression contracts (also runnable without the .NET SDK)."""
from pathlib import Path
import re
import struct
import unittest

ROOT = Path(__file__).resolve().parents[1]
WEB = ROOT / "Arkanum.Web"


class UiTypographySettingsTests(unittest.TestCase):
    def test_font_uses_bundled_variable_faces_without_network_or_system_lookup(self):
        css = (WEB / "wwwroot/css/site.css").read_text()
        faces = re.findall(r"@font-face\s*\{([^}]+)\}", css)
        for style, filename in (("normal", "SourceSans3VF-Upright.ttf"), ("italic", "SourceSans3VF-Italic.ttf")):
            with self.subTest(style=style):
                self.assertTrue(any(all(value in face for value in (
                    'font-family: "Source Sans 3";',
                    f"url('../fonts/source-sans-3/{filename}') format('truetype')",
                    f"font-style: {style};", "font-weight: 200 900;", "font-display: swap;",
                )) for face in faces), f"Missing local variable {style} face")
        body_match = re.search(r"(?m)^body\s*\{([^}]+)\}", css)
        assert body_match is not None
        body = body_match[1]
        self.assertIn('font-family: "Source Sans 3", sans-serif;', body)
        for external in ("@import", "local(", "https://", "http://"):
            self.assertNotIn(external, css)

    def test_bundled_fonts_are_real_variable_fonts_supporting_existing_weights(self):
        for filename in ("SourceSans3VF-Upright.ttf", "SourceSans3VF-Italic.ttf"):
            with self.subTest(filename=filename):
                font_path = WEB / "wwwroot/fonts/source-sans-3" / filename
                self.assertTrue(font_path.is_file(), f"Missing bundled font: {filename}")
                data = font_path.read_bytes()
                self.assertEqual(data[:4], b"\x00\x01\x00\x00")
                table_count = struct.unpack_from(">H", data, 4)[0]
                tables = {}
                for index in range(table_count):
                    tag, _, offset, length = struct.unpack_from(">4sIII", data, 12 + index * 16)
                    tables[tag] = data[offset:offset + length]
                fvar = tables[b"fvar"]
                axes_offset, _, axis_count, axis_size = struct.unpack_from(">HHHH", fvar, 4)
                axes = {}
                for index in range(axis_count):
                    tag, minimum, default, maximum = struct.unpack_from(">4siii", fvar, axes_offset + index * axis_size)
                    axes[tag] = (minimum / 65536, default / 65536, maximum / 65536)
                self.assertEqual(axes[b"wght"], (200, 200, 900))

    def test_font_license_is_distributed(self):
        license_path = WEB / "wwwroot/fonts/source-sans-3/LICENSE.md"
        self.assertTrue(license_path.is_file(), "Missing bundled font redistribution license")
        license_text = license_path.read_text()
        for required in ("Adobe", "SIL OPEN FONT LICENSE Version 1.1", "Permission is hereby granted"):
            self.assertIn(required, license_text)

    def test_existing_typography_formatting_is_preserved(self):
        css = (WEB / "wwwroot/css/site.css").read_text()
        for rule in (
            "h1 { font-size: clamp(2rem, 5vw, 4.2rem); line-height: .95; font-weight: 850; letter-spacing: -.06em; margin-bottom: 1rem; }",
            "h2 { font-weight: 800; font-size: 1.15rem; }",
            "letter-spacing: .12em; font-weight: 850; font-size: .78rem;",
            ".brand { display: flex; align-items: center; gap: .6rem; font-weight: 800; letter-spacing: -.02em; }",
        ):
            self.assertIn(rule, css)

    def test_settings_removes_only_layout_explainer_and_keeps_functional_controls(self):
        markup = (WEB / "Pages/Settings/Index.cshtml").read_text()
        self.assertTrue("settings layout" not in markup.lower(), "Settings layout explainer must be removed")
        self.assertNotIn("Tabs separate dashboard copy", markup)
        self.assertIn("Your permission view", markup)
        self.assertIn("@foreach (var role in Model.CurrentRoles)", markup)
        self.assertIn("data-preserve-settings-tab data-preserve-scroll", markup)
        for tab in ("dashboard", "directory", "database", "federation", "notifications", "users", "logs"):
            self.assertIn(f'data-settings-tab="settings-{tab}"', markup)
            self.assertIn(f'id="settings-{tab}"', markup)
        for handler in ("DashboardText", "Ldap", "RunDirectorySync", "LdapSyncSchedule", "SqlServer", "Federation", "Mail", "NotificationTemplates", "SendAllUsersNotification", "ToggleUserAccess", "UpdateUserRoles", "Syslog"):
            self.assertIn(f'asp-page-handler="{handler}"', markup)


if __name__ == "__main__":
    unittest.main()
