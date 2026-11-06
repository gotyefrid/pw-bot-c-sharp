using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Notes.Core.Settings;

namespace Notes.Core.Clients;

/// <summary>
/// Персонаж в «Персонажах»: чем и как его запускать. Класс и уровень — какими бот видел его последний раз.
/// </summary>
public sealed class CharacterAccount
{
    /// <summary>Постоянный номер записи: по нему правка и удаление, даже если ник поменяли.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Сервер — id профиля.</summary>
    public string Server { get; set; } = "";

    public string Nick { get; set; } = "";

    public string Login { get; set; } = "";

    /// <summary>Пароль, зашифрованный <see cref="PasswordVault"/>; пусто — не задан.</summary>
    public string Password { get; set; } = "";

    /// <summary>Exe клиента; пусто — «Игра» сервера.</summary>
    public string ExePath { get; set; } = "";

    /// <summary>Класс (номер как в игре, <see cref="World.PlayerClasses"/>); null — ещё не видели.</summary>
    public int? Class { get; set; }

    /// <summary>Уровень; 0 — ещё не видели.</summary>
    public int Level { get; set; }

    /// <summary>Когда бот последний раз видел персонажа в мире.</summary>
    public DateTime? SeenAt { get; set; }

    public bool Is(string server, string nick) => Server == server && string.Equals(Nick, nick, StringComparison.Ordinal);

    public CharacterAccount Clone() => (CharacterAccount)MemberwiseClone();

    /// <summary>
    /// Команда запуска клиента с автовходом. <paramref name="gamePath"/> — «Игра» сервера, если у персонажа свой exe не задан.
    /// Не из чего собрать — <see cref="InvalidOperationException"/> с текстом для игрока.
    /// </summary>
    public ClientLaunch Launch(IReadOnlyList<string> launchArguments, string? gamePath)
    {
        var exe = ExePath.Length > 0 ? ExePath : gamePath;
        if (string.IsNullOrWhiteSpace(exe))
            throw new InvalidOperationException("Не выбран клиент: укажите его у персонажа или «Игру» сервера.");
        if (!File.Exists(exe))
            throw new InvalidOperationException($"Нет файла клиента: {exe}");
        if (launchArguments.Count == 0)
            throw new InvalidOperationException("Запуск клиента для этого сервера ещё не настроен.");
        if (Login.Trim().Length == 0)
            throw new InvalidOperationException("У персонажа не задан логин.");
        var password = PasswordVault.Unprotect(Password)
                       ?? throw new InvalidOperationException("Пароль не расшифровать (другая учётная запись Windows?) — введите его заново.");

        return ClientLaunch.WithCredentials(exe!, System.IO.Path.GetDirectoryName(exe)!, launchArguments, Login.Trim(), password, Nick.Trim());
    }
}

/// <summary>
/// Список персонажей: <c>characters\accounts.json</c> в папке бота (не в репозитории). Файл общий у всех копий бота из одной
/// папки, поэтому каждая правка — «прочитать с диска, поменять, записать» под общим замком: правки другой копии не теряются.
/// </summary>
public sealed class CharacterBook(string path)
{
    public const string FileName = "accounts.json";

    private static readonly TimeSpan LockWait = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerSettings Json = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.Indented,
    };

    public string Path { get; } = path;

    /// <summary>Все персонажи по порядку: сервер, ник. Файл испорчен — пустой список, копия <c>.bad</c>, причина в <paramref name="problem"/>.</summary>
    public List<CharacterAccount> Load(out string? problem)
    {
        var list = JsonFile.Load(Path, Parse, () => new List<CharacterAccount>(), out var details);
        problem = details is null ? null : $"Список персонажей испорчен. {details}";
        return Sorted(list);
    }

    /// <summary>Добавить или заменить (по <see cref="CharacterAccount.Id"/>).</summary>
    public List<CharacterAccount> Save(CharacterAccount account) => Change(list =>
    {
        var index = list.FindIndex(a => a.Id == account.Id);
        if (index >= 0)
            list[index] = account.Clone();
        else
            list.Add(account.Clone());
        return true;
    });

    public List<CharacterAccount> Remove(string id) => Change(list => list.RemoveAll(a => a.Id == id) > 0);

    /// <summary>
    /// Бот видел персонажа в мире: запомнить класс и уровень. Персонажа нет в списке или всё как было — файл не трогаем.
    /// true — запись поменялась.
    /// </summary>
    public bool Seen(string server, string nick, int? @class, int level, DateTime now)
    {
        var changed = false;
        Change(list =>
        {
            foreach (var account in list.Where(a => a.Is(server, nick)))
            {
                if (account.Level == level && (@class is null || account.Class == @class))
                    continue;

                account.Level = level;
                account.Class = @class ?? account.Class;
                account.SeenAt = now;
                changed = true;
            }

            return changed;
        });
        return changed;
    }

    /// <summary>Правка под замком. <paramref name="change"/> вернул false — ничего не поменялось, файл не пишется.</summary>
    private List<CharacterAccount> Change(Func<List<CharacterAccount>, bool> change)
    {
        using var mutex = new Mutex(initiallyOwned: false, MutexNames.For("characters", System.IO.Path.GetFullPath(Path).ToLowerInvariant()));
        var owned = false;
        try
        {
            try
            {
                owned = mutex.WaitOne(LockWait);
            }
            catch (AbandonedMutexException)
            {
                // Другая копия упала посреди записи: файл всё равно целый (запись атомарная)
                owned = true;
            }

            // Испорченный файл: Load уже сохранил его копию .bad, дальше пишем новый список
            var list = Load(out _);
            if (change(list))
                JsonFile.Save(Path, JsonConvert.SerializeObject(new BookFile { Characters = list }, Json));
            return Sorted(list);
        }
        finally
        {
            if (owned)
                mutex.ReleaseMutex();
        }
    }

    private static List<CharacterAccount> Parse(string text)
        => JsonConvert.DeserializeObject<BookFile>(text, Json)?.Characters ?? new List<CharacterAccount>();

    private static List<CharacterAccount> Sorted(IEnumerable<CharacterAccount> list)
        => list.OrderBy(a => a.Server, StringComparer.OrdinalIgnoreCase).ThenBy(a => a.Nick, StringComparer.CurrentCultureIgnoreCase).ToList();

    private sealed class BookFile
    {
        public List<CharacterAccount> Characters { get; set; } = new();
    }
}
