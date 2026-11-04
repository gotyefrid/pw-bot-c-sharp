using System;
using System.IO;
using System.Linq;

namespace Notes.Patcher;

/// <summary>
/// Отдельный патчер «быстрый полёт»: кладётся рядом с client.exe (или перетащить client.exe на него).
/// Ничего не запускает и не читает память — только правит файл на диске и делает бэкап .orig.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        var client = FindClient(args, out var problem);
        if (client is null)
        {
            Console.WriteLine(problem);
            Pause();
            return 2;
        }

        var mode = args.FirstOrDefault(a => a is "--apply" or "--revert" or "--status");
        if (mode is not null)
        {
            if (mode == "--status")
            {
                ShowState(client);
                return 0;
            }

            return Run(client, apply: mode == "--apply");
        }

        while (true)
        {
            Console.WriteLine();
            Console.WriteLine("Патчер быстрого полёта Perfect World");
            Console.WriteLine($"Клиент: {client}");
            ShowState(client);
            Console.WriteLine();
            Console.WriteLine("1 — пропатчить (полёт сразу на максимальной скорости)");
            Console.WriteLine("2 — откатить");
            Console.WriteLine("0 — выход");
            Console.Write("Выбор: ");
            var choice = Console.ReadLine()?.Trim();
            if (choice is null or "" or "0")
                break;
            if (choice is "1" or "2")
            {
                Run(client, apply: choice == "1");
                Pause();
            }
            else
            {
                Console.WriteLine("Нет такого пункта.");
            }
        }

        return 0;
    }

    private static int Run(string client, bool apply)
    {
        try
        {
            if (apply)
                FlightPatch.Apply(client, Console.WriteLine);
            else
                FlightPatch.Revert(client, Console.WriteLine);
            return 0;
        }
        catch (Exception e)
        {
            Console.WriteLine("Ошибка: " + e.Message);
            return 1;
        }
    }

    private static void ShowState(string client)
    {
        var state = FlightPatch.Detect(client, out var details);
        Console.WriteLine("Состояние: " + state switch
        {
            FlightPatch.State.Original => "оригинал (патча нет)",
            FlightPatch.State.Patched => "пропатчен (быстрый полёт)",
            FlightPatch.State.Unknown => "не узнаю этот клиент — возможно, обновился",
            FlightPatch.State.NotClient => "это не client.exe",
            FlightPatch.State.Unreadable => "файл не читается",
            _ => "неизвестно",
        });
        Console.WriteLine("  " + details);
    }

    private static string? FindClient(string[] args, out string problem)
    {
        problem = "";
        foreach (var arg in args.Where(a => !a.StartsWith("--")))
        {
            if (File.Exists(arg))
                return Path.GetFullPath(arg);
            if (Directory.Exists(arg))
            {
                var inside = Path.Combine(arg, "client.exe");
                if (File.Exists(inside))
                    return Path.GetFullPath(inside);
            }
        }

        var near = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "client.exe");
        if (File.Exists(near))
            return Path.GetFullPath(near);
        var here = Path.Combine(Directory.GetCurrentDirectory(), "client.exe");
        if (File.Exists(here))
            return Path.GetFullPath(here);

        problem = "Не нашёл client.exe. Положи патчер рядом с client.exe, перетащи client.exe на патчер "
                  + "или укажи путь: Notes.Patcher.exe --status \"C:\\...\\element\\client.exe\"";
        return null;
    }

    private static void Pause()
    {
        if (Console.IsInputRedirected)
            return;
        Console.WriteLine();
        Console.Write("Нажми Enter...");
        Console.ReadLine();
    }
}
