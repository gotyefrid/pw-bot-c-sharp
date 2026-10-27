using System;
using System.Security.Cryptography;
using System.Text;

namespace BotCH.Core.Clients;

/// <summary>
/// Имена именованных объектов Windows (мутексов) бота. Они видны всем процессам сеанса, поэтому в имени ни слова «бот», ни пути
/// установки — только отпечаток ключа: тот же ключ даёт то же имя (по нему окна находят друг друга), а прочитать по имени нечего.
/// </summary>
public static class MutexNames
{
    /// <summary>Имя для пары «назначение — ключ» (папка бота, PID клиента): <c>Local\</c> и 16 шестнадцатеричных знаков.</summary>
    public static string For(string purpose, string key)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(purpose + "|" + key));
        return @"Local\" + BitConverter.ToString(hash, 0, 8).Replace("-", "").ToLowerInvariant();
    }
}
