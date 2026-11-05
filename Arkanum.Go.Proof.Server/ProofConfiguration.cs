using Arkanum.Web.Domain;
namespace Arkanum.Go.Proof.Server;
public sealed class ProofConfiguration
{
    public const int MaximumLeaseSeconds = 172800;
    public int LeaseSeconds {get;set;} = MaximumLeaseSeconds;
    internal void ValidateLease()
    {
        if (LeaseSeconds is < 1 or > MaximumLeaseSeconds)
            throw new InvalidDataException("LeaseSeconds must be between 1 and 172800 seconds (48 hours).");
    }
    public int Port {get;set;}=7443;
    public string ListenAddress {get;set;}="127.0.0.1";
    public string StateDirectory {get;set;}="";
    public string EnrollmentDirectory {get;set;}="";
    internal void ValidateEnrollmentDirectory()
    {
        if (string.IsNullOrWhiteSpace(StateDirectory) || !Path.IsPathFullyQualified(StateDirectory) ||
            string.IsNullOrWhiteSpace(EnrollmentDirectory) || !Path.IsPathFullyQualified(EnrollmentDirectory))
            throw new InvalidDataException("Absolute state and independently protected enrollment directories are required.");
        var state = Path.TrimEndingDirectorySeparator(Path.GetFullPath(StateDirectory)) + Path.DirectorySeparatorChar;
        var enrollment = Path.TrimEndingDirectorySeparator(Path.GetFullPath(EnrollmentDirectory)) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (state.StartsWith(enrollment, comparison) || enrollment.StartsWith(state, comparison))
            throw new InvalidDataException("EnrollmentDirectory must be separate from, and not nested with, StateDirectory.");
    }
    public ProofUser[] Users {get;set;}=[];
    public ProofDevice[] Devices {get;set;}=[];
    public ProofVault[] Vaults {get;set;}=[];
}
public sealed class ProofUser
{
    public string Id {get;set;}="";
    public string Sid {get;set;}="";
    public string DomainSid {get;set;}="";
    public Guid ObjectGuid {get;set;}
    public string Domain {get;set;}="";
    public string VerifiedBy {get;set;}="";
    public DateTimeOffset VerifiedAt {get;set;}
    public bool Enabled {get;set;}
    public ArkanumRole[] Roles {get;set;}=[];
}
public sealed class ProofDevice
{
    public string Id {get;set;}="";
    public bool Approved {get;set;}
    public string CertificateSha256 {get;set;}="";
    public string UnwrapSpki {get;set;}="";
    public DateTimeOffset IssuedAt {get;set;}
    // Legacy JSON compatibility only. Never an authorization source or restriction.
    public string[]? UserIds {get;set;}=[];
    public string[] VaultIds {get;set;}=[];
}
public sealed class ProofVault
{
    public string Id {get;set;}="";
    public bool OfflineEnabled {get;set;}
    public string[] MemberUserIds {get;set;}=[];
}
