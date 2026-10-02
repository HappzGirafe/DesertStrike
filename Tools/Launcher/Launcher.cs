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
using System.Threading;
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
            if (!File.Exists(game) || !File.Exists(stamp) || File.ReadAllText(stamp) != version)
                Unpack(folder, stamp, version);

            var start = new ProcessStartInfo(game) { WorkingDirectory = folder };
            if (args.Length > 0) start.Arguments = "\"" + string.Join("\" \"", args) + "\"";
            Process.Start(start);
        }
        catch (Exception e)
        {
            MessageBox.Show("Desert Strike could not start:\n\n" + e.Message +
                            "\n\nIf the game is already running, close it and try again.",
                            "Desert Strike", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // Unpacks on a background thread while a small window says what is happening.
    static void Unpack(string folder, string stamp, string version)
    {
        var form = new Form
        {
            Text = "Desert Strike",
            Width = 440,
            Height = 130,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen,
            ControlBox = false,
        };
        form.Controls.Add(new Label
        {
            Text = "Unpacking Desert Strike... (only the first time)",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 11f),
        });

        Exception error = null;
        form.Shown += delegate
        {
            var worker = new Thread(delegate ()
            {
                try
                {
                    if (Directory.Exists(folder)) Directory.Delete(folder, true);
                    Directory.CreateDirectory(folder);
                    using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("game.zip"))
                    using (var zip = new ZipArchive(stream))
                        zip.ExtractToDirectory(folder);
                    File.WriteAllText(stamp, version);
                }
                catch (Exception e)
                {
                    error = e;
                }
                form.BeginInvoke(new Action(form.Close));
            });
            worker.IsBackground = true;
            worker.Start();
        };
        Application.Run(form);
        if (error != null) throw error;
    }
}
