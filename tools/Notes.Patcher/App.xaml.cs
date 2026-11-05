using System.IO;
using System.Linq;
using System.Windows;

namespace Notes.Patcher;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var window = new MainWindow();

        // Путь можно передать аргументом (перетащить client.exe на exe патчера).
        var path = e.Args.FirstOrDefault(File.Exists);
        if (path is null)
        {
            var directory = e.Args.FirstOrDefault(Directory.Exists);
            if (directory is not null)
            {
                var inside = Path.Combine(directory, "client.exe");
                if (File.Exists(inside))
                    path = inside;
            }
        }

        if (path is not null)
            window.SetClientPath(Path.GetFullPath(path));

        window.Show();
    }
}
