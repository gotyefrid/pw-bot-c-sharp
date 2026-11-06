using System;
using System.Security.Cryptography;
using System.Text;

namespace Notes.Core.Clients;

/// <summary>
/// Пароль персонажа на диске — зашифрованным средствами Windows (DPAPI, учётная запись пользователя): расшифровать его можно
/// только под той же учётной записью Windows на том же компьютере. Скопированный куда-то файл паролей не раскроет.
/// </summary>
public static class PasswordVault
{
    private const string Prefix = "dpapi:";

    // Своя «соль»: чужая программа того же пользователя не расшифрует строку, просто вызвав DPAPI без неё
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Notes.Characters.v1");

    public static string Protect(string password)
    {
        if (password.Length == 0)
            return "";

        var data = ProtectedData.Protect(Encoding.UTF8.GetBytes(password), Entropy, DataProtectionScope.CurrentUser);
        return Prefix + Convert.ToBase64String(data);
    }

    /// <summary>Пароль; null — пароля нет или его не расшифровать (другая учётная запись Windows, другой компьютер, порча).</summary>
    public static string? Unprotect(string stored)
    {
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal))
            return null;

        try
        {
            var data = ProtectedData.Unprotect(Convert.FromBase64String(stored.Substring(Prefix.Length)), Entropy,
                DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(data);
        }
        catch (Exception e) when (e is CryptographicException or FormatException)
        {
            return null;
        }
    }
}
