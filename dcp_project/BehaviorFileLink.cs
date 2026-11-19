using System.Diagnostics;
using System.IO;
using System.Windows;
using Dsp.Chatbot;

namespace dcp_project;

internal static class BehaviorFileLink
{
    internal static bool IsAllowed(string? url) => string.Equals(url, UserInstructions.LinkUrl, StringComparison.Ordinal);

    internal static void Open()
    {
        try
        {
            UserInstructions.EnsureExists();
            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "notepad.exe"))
            {
                UseShellExecute = false
            };
            start.ArgumentList.Add(UserInstructions.FilePath);
            Process.Start(start);
        }
        catch (Exception)
        {
            MessageBox.Show("Не удалось открыть инструкции поведения. Файл: " + UserInstructions.FilePath, "Чат DSP");
        }
    }
}
