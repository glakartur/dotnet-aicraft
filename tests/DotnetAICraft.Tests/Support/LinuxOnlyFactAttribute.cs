using Xunit;

namespace DotnetAICraft.Tests.Support;

/// <summary>A fact that is reported as skipped (not passed) outside Linux.</summary>
internal sealed class LinuxOnlyFactAttribute : FactAttribute
{
    public LinuxOnlyFactAttribute()
    {
        if (!OperatingSystem.IsLinux())
            Skip = "Linux only: needs a case-sensitive file system.";
    }
}
