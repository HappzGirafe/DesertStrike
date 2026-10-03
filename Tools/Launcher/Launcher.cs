// One-file Windows launcher for Desert Strike (built by Tools/build_launcher.ps1).
//
// The game is a folder of files, so this exe carries the whole folder inside it (the "game.zip" resource).
// The first time it runs, and after an update, it unpacks the game to %LOCALAPPDATA%\DesertStrike;
// then it starts the game from there. Written for the C# 5 compiler that ships with Windows.
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyTitle("Desert Strike")]
[assembly: AssemblyProduct("Desert Strike")]

static class Launcher
{
    [STAThread]
    static void Main(string[] args)
    {
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesertStrike");
        string game = Path.Combine(folder, "DesertStrike.exe");
        string stamp = Path.Combine(folder, "launcher-version.txt");
        // Changes with every build of this launcher, so an updated launcher unpacks its newer game.
        string version = Assembly.GetExecutingAssembly().ManifestModule.ModuleVersionId.ToString();

        try
        {
            string unpacked = File.Exists(stamp) ? File.ReadAllText(stamp) : "none";
            Log("Launcher " + version + ", unpacked game: " + unpacked);
            if (!File.Exists(game) || unpacked != version)
                Unpack(folder, stamp, version);

            var start = new ProcessStartInfo(game) { WorkingDirectory = folder };
            if (args.Length > 0) start.Arguments = "\"" + string.Join("\" \"", args) + "\"";
            Process.Start(start);
            Log("Started the game");
        }
        catch (Exception e)
        {
            Log("Failed: " + e);
            MessageBox.Show("Desert Strike could not start:\n\n" + e.Message +
                            "\n\nIf the game is already running, close it and try again.",
                            "Desert Strike", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // Unpacks file by file while a small window says what is happening. Everything runs on this thread, so the
    // launcher only goes on to start the game once the whole game is unpacked.
    static void Unpack(string folder, string stamp, string version)
    {
        using (var form = new Form
        {
            Text = "Desert Strike",
            Width = 440,
            Height = 130,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen,
            ControlBox = false,
        })
        {
            form.Controls.Add(new Label
            {
                Text = "Unpacking Desert Strike... (only the first time)",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 11f),
            });
            form.Show();
            Application.DoEvents();

            Log("Unpacking to " + folder);
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            Directory.CreateDirectory(folder);
            string root = Path.GetFullPath(folder) + Path.DirectorySeparatorChar;
            int files = 0;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("game.zip"))
            using (var zip = new ZipArchive(stream))
            {
                foreach (var entry in zip.Entries)
                {
                    string target = Path.GetFullPath(Path.Combine(folder, entry.FullName));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;   // stays inside the folder
                    if (entry.Name.Length == 0)
                    {
                        Directory.CreateDirectory(target);
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    entry.ExtractToFile(target, true);
                    files++;
                    Application.DoEvents();
                }
            }
            File.WriteAllText(stamp, version);
            Log("Unpacked " + files + " files");
        }
    }

    // %LOCALAPPDATA%\DesertStrike-launcher.log: what the launcher did the last few times, for finding problems.
    static void Log(string line)
    {
        try
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesertStrike-launcher.log");
            if (File.Exists(path) && new FileInfo(path).Length > 64 * 1024) File.Delete(path);
            File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + line + Environment.NewLine);
        }
        catch (Exception)
        {
            // Logging must never stop the game from starting.
        }
    }
}
