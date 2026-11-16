using System.Runtime.CompilerServices;

namespace Clamshield.Tests;

/// <summary>Points the app's data folder and home at a throwaway folder BEFORE any Clamshield code runs, so tests never touch real data.</summary>
internal static class TestEnvironment
{
    public static string Root { get; } = Path.Combine(Path.GetTempPath(), "clamshield-tests-" + Guid.NewGuid().ToString("N"));

    [ModuleInitializer]
    internal static void Init()
    {
        Directory.CreateDirectory(Root);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", Path.Combine(Root, "config"));
        Environment.SetEnvironmentVariable("HOME", Path.Combine(Root, "home"));
        Directory.CreateDirectory(Path.Combine(Root, "home"));
    }

    public static string NewDir(string name)
    {
        var d = Path.Combine(Root, name + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(d);
        return d;
    }

    /// <summary>Finds a file in the repository by walking up from the test binaries.</summary>
    public static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var p = Path.Combine(dir.FullName, relative);
            if (File.Exists(p)) return p;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(relative);
    }
}
