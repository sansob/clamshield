using System.Reflection;

namespace Clamshield.Services;

/// <summary>Works out how to relaunch this app (native apphost, or "dotnet app.dll" under dotnet run).</summary>
public static class AppLauncher
{
    public static (string File, string[] LeadingArgs) Command()
    {
        var exe = Environment.ProcessPath ?? "clamshield";
        var name = Path.GetFileNameWithoutExtension(exe);
        if (name.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            string? dll = null;
            try
            {
                var a0 = Environment.GetCommandLineArgs().FirstOrDefault();
                if (!string.IsNullOrEmpty(a0) && a0.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) dll = Path.GetFullPath(a0);
#pragma warning disable IL3000   // only reached when started through "dotnet Clamshield.dll", never in a single-file or AOT build
                dll ??= Assembly.GetEntryAssembly()?.Location;
#pragma warning restore IL3000
            }
            catch { }
            if (!string.IsNullOrEmpty(dll)) return (exe, new[] { dll });
        }
        return (exe, Array.Empty<string>());
    }

    /// <summary>Double-quote a string for shell / .desktop Exec use, escaping \ " $ `.</summary>
    public static string Quote(string s)
    {
        var sb = new System.Text.StringBuilder("\"");
        foreach (var c in s)
        {
            if (c is '\\' or '"' or '$' or '`') sb.Append('\\');
            sb.Append(c);
        }
        return sb.Append('"').ToString();
    }
}
