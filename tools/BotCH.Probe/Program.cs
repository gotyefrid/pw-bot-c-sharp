using System;
using System.Diagnostics;
using System.Linq;
using BotCH.Core.Memory;

namespace BotCH.Probe;

internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        if (args.Length == 0)
        {
            PrintUsage();
            return 0;
        }

        try
        {
            switch (args[0])
            {
                case "attach":
                    return Attach(args.Skip(1).ToArray());
            }
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"❌ {e.Message}");
            return 2;
        }

        Console.Error.WriteLine($"Неизвестная команда: {args[0]}");
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("BotCH.Probe <команда> [PID]");
        Console.WriteLine("  attach   подключиться к клиенту (только чтение) и показать базовые адреса и ник");
    }

    // TODO(часть 1, шаг 3): смещения взять из профиля сервера
    private const uint BaseAddrOffset = 0x5B3EEC, GameOffset = 0x1C, PersOffset = 0x20, PersNameOffset = 0x608;

    private static int Attach(string[] args)
    {
        using var game = OpenClient(args);
        Console.WriteLine($"Процесс: {game.ProcessName} PID {game.Pid}, модуль 0x{game.MainModuleBase:X8} ({game.MainModuleSize / 1024} КБ)");

        var gameAddress = game.ReadPointerChain(game.MainModuleBase + BaseAddrOffset, GameOffset);
        Console.WriteLine($"game      = 0x{gameAddress:X8}");

        var pers = game.ReadUInt32(gameAddress + PersOffset);
        Console.WriteLine($"персонаж  = 0x{pers:X8}, vtable 0x{game.ReadUInt32(pers):X8} (ожидается 0x008D74C8 — CECHostPlayer)");

        var namePointer = game.ReadUInt32(pers + PersNameOffset);
        Console.WriteLine($"ник       = {game.ReadUnicodeString(namePointer)}");
        return 0;
    }

    private static GameProcess OpenClient(string[] args)
    {
        var pid = args.Length > 0
            ? int.Parse(args[0])
            : Process.GetProcessesByName("elementclient").FirstOrDefault()?.Id
              ?? throw new InvalidOperationException("Клиент elementclient не найден");

        return GameProcess.Open(pid);
    }
}
