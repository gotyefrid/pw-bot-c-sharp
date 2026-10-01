using System;
using System.IO;
using System.Linq;
using System.Windows;
using BotCH.Core.Profiles;
using BotCH.Core.Settings;

namespace BotCH.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        CoreInfo.Text = DescribeCore();
    }

    // Временно, до части 3: показывает, что вшитые BotCH.Core и Newtonsoft.Json загрузились
    private static string DescribeCore()
    {
        var catalog = ProfileCatalog.Default();
        var servers = string.Join(", ", catalog.Ids.Select(id => catalog.Load(id).Name));
        var settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
        var settings = new SettingsStore(settingsPath).Load(out _);
        return $"Серверы: {servers}\nПет: {(settings.Pet.Enabled ? $"клетка {settings.Pet.Cage}" : "выключен")}, банка HP < {settings.Potions.HpPercent} %";
    }
}
