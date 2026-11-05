"""Linux build/contract gates; these do NOT claim Windows native acceptance."""
from pathlib import Path
import os
import subprocess

ROOT = Path(__file__).resolve().parents[1]
APP = ROOT / 'Arkanum.Go.Proof.Windows'
DOTNET = os.environ.get('DOTNET', str(Path.home() / '.dotnet10/dotnet'))


def test_deployable_broker_exists():
    assert (APP / 'Arkanum.Go.Proof.Windows.csproj').exists(), 'Missing deployable Windows broker project'


def test_native_security_boundaries_are_explicit():
    src = (APP / 'Native.cs').read_text()
    for required in ['StartServiceCtrlDispatcherW', 'RegisterServiceCtrlHandlerExW',
                     'QueryServiceStatusEx', 'GetNamedPipeServerProcessId', 'OpenProcessToken',
                     'PIPE_REJECT_REMOTE_CLIENTS', 'FILE_FLAG_FIRST_PIPE_INSTANCE',
                     '0x0012019B', 'RunAsClient', 'WTSQuerySessionInformationW',
                     'QueryInterruptTime', 'NtQuerySystemInformation', 'TokenSessionId',
                     'ExposeVerifierHandles', 'SetKernelObjectSecurity']:
        assert required in src, required
    assert 'GW;;;BU' not in src


def test_service_crypto_storage_and_authorization_contracts():
    src = (APP / 'Broker.cs').read_text()
    for required in ['CngExportPolicies.None', 'MachineKey', 'Security Descr', 'ArkanumGoSyncProof-auth',
                     'ArkanumGoSyncProof-unwrap', 'CreateSigningRequestPem', 'CopyWithPrivateKey',
                     'RSASignaturePadding.Pss', 'RSAEncryptionPadding.OaepSHA256',
                     'ProofWire.EncodeUnsigned', 'SnapshotProofReader', 'LeaseClock.Advance',
                     'Flush(true)', 'MoveFileExW', 'Denied', 'CryptographicOperations.ZeroMemory', 'storageFault']:
        assert required in src, required
    assert 'ServerCertificateCustomValidationCallback' not in src
    assert 'DangerousAcceptAnyServerCertificateValidator' not in src


def test_package_and_enrollment_handoff():
    install = (APP / 'install.ps1').read_text()
    assert 'NT SERVICE\\ArkanumGoSyncProof' in install
    assert 'sidtype' in install and 'unrestricted' in install
    assert 'schtasks' not in install.lower()
    doc = (ROOT / 'docs/arkanum-go/proof-windows.md').read_text()
    for text in ['not proven', '--read', '--status', 'client.cer', 'signer.spki', 'sticky', 'uninstall.ps1']:
        assert text in doc


def test_real_portable_policy_and_crypto_harness():
    result = subprocess.run([DOTNET, 'run', '--project', str(APP), '-c', 'Release', '--', '--self-test'],
                            cwd=ROOT, capture_output=True, text=True, timeout=180)
    assert result.returncode == 0, result.stdout + result.stderr
    assert 'PASS portable-proof-windows' in result.stdout
    assert 'plaintext' not in result.stdout.lower()


def test_real_server_json_transport_contract():
    result = subprocess.run([DOTNET, 'run', '--project', str(APP / 'ContractTests'), '-c', 'Release'],
                            cwd=ROOT, capture_output=True, text=True, timeout=180)
    assert result.returncode == 0, result.stdout + result.stderr
    assert 'PASS server-results-json-shared-record-signature' in result.stdout
    # These markers come from executed real-stream/seam tests, not source matching.
    for case in ['distinguishes-empty-and-truncated-frames',
                 'retries-only-busy-with-cancellation-budget',
                 'recovers-only-known-peer-closure',
                 'accept-and-cleanup-faults-fail-closed',
                 'reuses-instance-after-close-partial-and-cancellation']:
        assert f'PASS transport-{case}' in result.stdout, result.stdout
    if os.name == 'nt':
        assert 'PASS transport-native-busy-retry' in result.stdout
    else:
        assert 'SKIP transport-native-busy-retry (Windows required)' in result.stdout


def test_status_commit_and_native_identity_boundaries():
    broker = (APP / 'Broker.cs').read_text()
    native = (APP / 'Native.cs').read_text()
    program = (APP / 'Program.cs').read_text()
    for marker in ['service-status.json', 'LastSuccessfulRefreshUtc', 'OriginalGrantExpiresUtc', 'StateSha256', 'CommitSnapshot']:
        assert marker in broker, marker
    for marker in ['SessionUserMatches', 'WTSEnumerateSessionsW', 'RequireAdministrator', 'ReadDiagnostics']:
        assert marker in native, marker
    assert '--diagnostics' in program
    assert 'sidOverride' not in program


def test_reviewed_release_key_and_establishment_guards():
    broker = (APP / 'Broker.cs').read_text()
    program = (APP / 'Program.cs').read_text()
    native = (APP / 'Native.cs').read_text()
    install = (APP / 'install.ps1').read_text()
    for marker in ['Dispatch', 'WithGate', 'CanRelease', 'initialized.marker', 'ValidateKeySecurity', 'ValidateDistinctKeys']:
        assert marker in broker, marker
    assert 'broker.Dispatch' in program
    assert 'broker.Open' not in program
    assert 'Assert-ProofTrustedTree' in install
    assert install.index('Assert-ProofTrustedTree $Data') < install.index('New-Item -ItemType Directory')
    assert 'ValidateTrustedDescriptor' in native


def test_sanitized_startup_failure_is_observable():
    native = (APP / 'Native.cs').read_text()
    broker = (APP / 'Broker.cs').read_text()
    assert 'WriteStartupFailure' in native
    assert 'startup-failure.json' in native
    assert 'ExceptionType' in native and 'HResult' in native and 'TopMethod' in native
    assert 'startupStage = "expose-verifier-handles"' in native
    assert 'StartupStage = "open-auth-key"' in broker
    assert 'StartupStage = "open-unwrap-key"' in broker


def test_admin_provision_is_separate_from_service_key_open():
    broker = (APP / 'Broker.cs').read_text()
    program = (APP / 'Program.cs').read_text()
    assert '--provision' in program and 'Provisioning.Run(Broker.Root)' in program
    assert 'CngKey.Create' not in broker, 'Virtual service must never create machine keys'
    provision = (APP / 'Provisioning.cs').read_text()
    assert provision.index('Native.RequireAdministrator();') < provision.index('CngKey.Exists(')
    assert provision.index('Native.RequireTrustedTree(directory') < provision.index('CngKey.Exists(')
    for value in ['CngKeyCreationOptions.MachineKey', 'CngExportPolicies.None', '3072',
                  'CngKeyUsages.Signing', 'CngKeyUsages.Decryption', 'O:BAD:P', 'BootstrapReceipt']:
        assert value in provision
    assert 'OverwriteExistingKey' not in provision


def test_receipt_is_consumed_before_first_state_commit():
    broker = (APP / 'Broker.cs').read_text()
    assert 'BootstrapReceipt.Validate' in broker
    assert broker.index('File.Delete(receiptPath)') < broker.index('StartupStage = "commit-initial-state"')
    assert 'Native.RequireTrustedTree(root' in broker
    assert 'BootstrapAllowed' in broker


def test_installer_provisions_after_configuration_before_service_start():
    install = (APP / 'install.ps1').read_text()
    assert install.index("'config.json'),$json") < install.index("@('--provision')") < install.index('Start-Service')
    wrapper = install[install.index('function Invoke-Native'):install.index('if ([string]')]
    assert "$ErrorActionPreference='Continue'" in wrapper
    assert 'finally' in wrapper and '$global:LASTEXITCODE' in wrapper
    assert '2>&1' in wrapper


def test_installer_retains_existing_configuration_bytes():
    install = (APP / 'install.ps1').read_text()
    assert '$existing.DeviceId -cne $DeviceId' in install
    assert '$existing.Endpoint -cne $Endpoint.AbsoluteUri' in install
    assert 'if (!(Test-Path -LiteralPath $Config))' in install


def test_native_wrapper_handles_stderr_and_fresh_exit():
    import shutil
    import pytest
    powershell = shutil.which('pwsh')
    if not powershell:
        pytest.skip('PowerShell unavailable; Windows 5.1 is a separate acceptance gate')
    install = (APP / 'install.ps1').read_text()
    wrapper = install[install.index('function Invoke-Native'):install.index('if ([string]')]
    # No installation code is executed. These children are constant harmless fixtures.
    if os.name == 'nt':
        command, ok, bad = 'cmd.exe', ['/c', 'echo fixture 1>&2 & exit /b 0'], ['/c', 'exit /b 9']
    else:
        command, ok, bad = '/bin/sh', ['-c', 'printf fixture >&2; exit 0'], ['-c', 'exit 9']
    import json
    script = wrapper + '''
$ErrorActionPreference='Stop'
$spec=$env:ARKANUM_NATIVE_SPEC | ConvertFrom-Json
Invoke-Native $spec.command $spec.ok | Out-Null
if($ErrorActionPreference -cne 'Stop') {throw 'EAP not restored'}
$rejected=$false
try { Invoke-Native $spec.command $spec.bad } catch { $rejected=$true }
if(!$rejected) {throw 'Nonzero accepted'}
$rejected=$false
try { Invoke-Native 'arkanum-nonexistent-synthetic-command' @() } catch { $rejected=$true }
if(!$rejected) {throw 'Missing executable accepted stale exit'}
if($ErrorActionPreference -cne 'Stop') {throw 'EAP not restored after failure'}
# Model Windows PowerShell stderr ErrorRecord stream with a real harmless child.
$ErrorActionPreference='Stop'
function Synthetic-Stderr { Write-Error 'synthetic-stderr'; $script:LASTEXITCODE=0 }
Invoke-Native 'Synthetic-Stderr' @() | Out-Null
Write-Output 'PASS native-wrapper'
'''
    result = subprocess.run([powershell, '-NoProfile', '-NonInteractive', '-Command', script],
                            env={**os.environ, 'ARKANUM_NATIVE_SPEC': json.dumps(dict(command=command, ok=ok, bad=bad))},
                            capture_output=True, text=True, timeout=30)
    assert result.returncode == 0, result.stdout + result.stderr
    assert 'PASS native-wrapper' in result.stdout


def test_response_delivery_is_acknowledged_before_disconnect():
    program = (APP / 'Program.cs').read_text()
    assert '(data, dispatchToken) => SendReply(pipe, data, dispatchToken)' in program
    assert 'internal static async Task SendReply' in program
    client = program[program.index('static async Task<int> Client'):program.index('static async Task Send(')]
    assert client.index('data = await Receive') < client.index('Native.ValidateServer(pipe);', client.index('data = await Receive')) < client.index('await Acknowledge(')
    assert 'WaitForPipeDrain' not in program, 'Unbounded synchronous drain permits a local denial of service'
