using System.Diagnostics;

namespace Clamshield.Services;

/// <summary>Native desktop notifications on Linux, macOS and Windows.</summary>
public static class NotificationService
{
    public static void Show(string title, string message)
    {
        Task.Run(() =>
        {
            try
            {
                ProcessStartInfo psi;
                if (OperatingSystem.IsLinux())
                {
                    psi = new ProcessStartInfo("notify-send");
                    psi.ArgumentList.Add("-a"); psi.ArgumentList.Add("Clamshield");
                    psi.ArgumentList.Add(title); psi.ArgumentList.Add(message);
                }
                else if (OperatingSystem.IsMacOS())
                {
                    psi = new ProcessStartInfo("osascript");
                    psi.ArgumentList.Add("-e");
                    psi.ArgumentList.Add($"display notification \"{Esc(message)}\" with title \"{Esc(title)}\"");
                }
                else
                {
                    psi = new ProcessStartInfo("powershell");
                    psi.ArgumentList.Add("-NoProfile"); psi.ArgumentList.Add("-Command");
                    var t = title.Replace("'", "''"); var m = message.Replace("'", "''");
                    psi.ArgumentList.Add("[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null;" +
                        "$x=[Windows.UI.Notifications.ToastNotificationManager]::GetTemplateContent(1);" +
                        $"$n=$x.GetElementsByTagName('text');$n[0].AppendChild($x.CreateTextNode('{t}'))|Out-Null;$n[1].AppendChild($x.CreateTextNode('{m}'))|Out-Null;" +
                        "[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('Clamshield').Show([Windows.UI.Notifications.ToastNotification]::new($x))");
                    psi.CreateNoWindow = true;
                }
                psi.UseShellExecute = false;
                using var p = Process.Start(psi);
            }
            catch { /* notifications are best-effort */ }
        });
    }

    static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
