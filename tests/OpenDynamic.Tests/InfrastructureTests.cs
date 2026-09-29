using System.Reflection;
using OpenDynamic.Core;
using Xunit;

namespace OpenDynamic.Tests;

public class InfrastructureTests
{
    [Fact]
    public void Infrastructure_ShouldRunUnitTestsSuccessfully()
    {
        Assert.Equal("openDynamic.Core", CoreInfo.ProjectName);
    }

    [Fact]
    public void CoreAssembly_ShouldNotReferenceWindowsOrWpfAssemblies()
    {
        var coreAssembly = typeof(CoreInfo).Assembly;
        var referencedAssemblies = coreAssembly.GetReferencedAssemblies();

        var forbiddenNames = new[]
        {
            "PresentationFramework",
            "PresentationCore",
            "WindowsBase",
            "System.Windows.Forms",
            "Windows.Foundation",
            "Windows.Media"
        };

        foreach (var assemblyName in referencedAssemblies)
        {
            foreach (var forbidden in forbiddenNames)
            {
                Assert.False(
                    assemblyName.Name?.StartsWith(forbidden, StringComparison.OrdinalIgnoreCase) == true,
                    $"OpenDynamic.Core must not reference Windows/WPF assembly: {assemblyName.Name}");
            }
        }
    }
}
