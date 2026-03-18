using System.IO;
using DiscordVoiceTranslator.UI;
using Newtonsoft.Json;

namespace DiscordVoiceTranslator;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        string root = GetProjectRoot();
        string settingsPath = Path.Combine(root, "config", "settings.json");
        string mainPyPath = Path.Combine(root, "ai_engine", "main.py");

        var settings = LoadSettings(settingsPath);

        Application.Run(new MainForm(settings, settingsPath, mainPyPath));
    }

    private static string GetProjectRoot()
    {
        // Traverse up from exe location to find the project root (contains 'config' dir)
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "config")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return AppContext.BaseDirectory;
    }

    private static AppSettings LoadSettings(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to load settings from {path}:\n{ex.Message}\nUsing defaults.",
                "Warning",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        return new AppSettings();
    }
}
