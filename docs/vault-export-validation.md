# Vault export validation

Vault CSV export packages use `AES-256-GCM/PBKDF2-SHA256` with a per-export salt, a 12-byte nonce, and a 16-byte authentication tag. The manifest records the export algorithm, encryption version, encrypted entry name, and integrity metadata. Manifest fields that define the export context are used as AES-GCM associated data so changes to the protected metadata or ciphertext cause validation failure.

## Validation path

Use `VaultExportService.ValidateAndDecryptExportCsvZip(exportBytes, passphrase)` from maintenance/import tooling to verify and decrypt an export package. The method:

1. Reads `manifest.json`.
2. Confirms the algorithm/version is supported.
3. Rebuilds the authenticated metadata context.
4. Decrypts `credentials.csv.aesgcm` with AES-GCM.
5. Raises an integrity validation error if the manifest, authentication tag, ciphertext, or passphrase has been tampered with or is incorrect.

Successful validation returns the decrypted CSV text for an approved import/recovery workflow. Failed validation must be treated as a rejected export package; do not partially import or display payload contents after a tamper failure.
