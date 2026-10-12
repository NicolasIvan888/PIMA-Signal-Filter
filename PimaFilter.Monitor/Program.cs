namespace PimaFilter.Monitor;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var configArgument = args.FirstOrDefault(argument => argument.StartsWith("--config=", StringComparison.OrdinalIgnoreCase));
        var configPath = configArgument is null
            ? @"C:\Program Files\PimaFilter\appsettings.json"
            : configArgument["--config=".Length..].Trim('"');
        try
        {
            ObjectCodeMapper.LoadConfiguration(configPath);
        }
        catch (Exception exception)
        {
            MessageBox.Show($"Cannot load route configuration: {exception.Message}", "SurGard Signal Filter", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var dataArgument = args.FirstOrDefault(argument =>
            argument.StartsWith("--data=", StringComparison.OrdinalIgnoreCase));
        var dataDirectory = dataArgument is null
            ? @"C:\ProgramData\PimaFilter"
            : dataArgument["--data=".Length..].Trim('"');
        var snapshotArgument = args.FirstOrDefault(argument =>
            argument.StartsWith("--snapshot=", StringComparison.OrdinalIgnoreCase));
        if (snapshotArgument is not null)
        {
            var snapshotPath = Path.GetFullPath(snapshotArgument["--snapshot=".Length..].Trim('"'));
            using var form = new MainForm(dataDirectory)
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-32000, -32000),
                ShowInTaskbar = false
            };
            if (args.Any(argument => argument.Equals("--blacklist", StringComparison.OrdinalIgnoreCase)))
            {
                form.ShowBlacklistTab();
            }
            form.Show();
            var refresh = form.RefreshNowAsync();
            while (!refresh.IsCompleted)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
            refresh.GetAwaiter().GetResult();
            Application.DoEvents();
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);
            bitmap.Save(snapshotPath, System.Drawing.Imaging.ImageFormat.Png);
            form.Close();
            Application.ExitThread();
            Environment.Exit(0);
            return;
        }

        Application.Run(new MainForm(dataDirectory));
    }
}
