using System;

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

        Console.Error.WriteLine($"Неизвестная команда: {args[0]}");
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("BotCH.Probe <команда>");
        Console.WriteLine("Команды появятся по мере готовности частей (attach, sigcheck, snapshot, ...).");
    }
}
