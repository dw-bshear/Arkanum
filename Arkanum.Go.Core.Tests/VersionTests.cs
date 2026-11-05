using System.Reflection;
using Arkanum.Go.Core;

namespace Arkanum.Go.Core.Tests;

public class VersionTests
{
    [Fact]
    public void Both_isolated_assemblies_have_beta_versions_not_inherited_web_versions()
    {
        foreach (var assembly in new[] { typeof(AdSid).Assembly, typeof(VersionTests).Assembly })
        {
            Assert.Equal(new Version(1, 0, 0, 0), assembly.GetName().Version);
            Assert.Equal("1.0.0.0", assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()!.Version);
            Assert.Equal("1.0.0-beta.1", assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion);
        }
    }
}
