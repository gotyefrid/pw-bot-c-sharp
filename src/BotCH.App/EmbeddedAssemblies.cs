using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace BotCH.App;

/// <summary>
/// Бот — один exe: BotCH.Core.dll и Newtonsoft.Json.dll вшиты в него ресурсами (см. csproj).
/// Когда .NET не находит такую dll рядом с exe, она загружается из ресурса.
/// </summary>
internal static class EmbeddedAssemblies
{
    private static readonly Dictionary<string, Assembly> Loaded = new(StringComparer.OrdinalIgnoreCase);

    public static void Register() => AppDomain.CurrentDomain.AssemblyResolve += Resolve;

    private static Assembly? Resolve(object sender, ResolveEventArgs args)
    {
        var name = new AssemblyName(args.Name).Name;
        lock (Loaded)
        {
            if (Loaded.TryGetValue(name, out var cached))
                return cached;

            using var stream = typeof(EmbeddedAssemblies).Assembly.GetManifestResourceStream(name + ".dll");
            if (stream is null)
                return null;

            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var assembly = Assembly.Load(buffer.ToArray());
            Loaded[name] = assembly;
            return assembly;
        }
    }
}
