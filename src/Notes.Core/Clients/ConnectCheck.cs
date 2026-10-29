using System;

namespace Notes.Core.Clients;

/// <summary>Что показать пользователю по кнопке «Подключить».</summary>
public enum ConnectVerdict
{
    /// <summary>Подключаемся.</summary>
    Ready,

    /// <summary>Выбранного клиента уже нет (закрыли игру).</summary>
    ProcessGone,

    /// <summary>Клиент запущен не из той папки, что указана «Игрой» сервера.</summary>
    OtherInstall,

    /// <summary>Правки не применены: подключиться можно только «всё равно».</summary>
    NotPatched,

    /// <summary>Правки не проверить (файл не распознан, не найден, не читается): тоже только «всё равно».</summary>
    UnknownBuild,
}

/// <summary>Вердикт и готовый текст подсказки (пусто у <see cref="ConnectVerdict.Ready"/>).</summary>
public sealed record ConnectDecision(ConnectVerdict Verdict, string Text);

/// <summary>
/// Проверка перед «Подключить»: жив ли клиент, тот ли это exe, стоят ли правки. Чистая функция — главный
/// предохранитель фичи, покрыта тестами; окну остаётся показать строку-подсказку и кнопку следующего шага.
/// </summary>
public static class ConnectCheck
{
    /// <param name="hasRecipe">У сервера есть рецепт правок; нет — проверять нечего, кроме живого процесса.</param>
    /// <param name="installPath">«Игра» сервера (exe, чьё состояние проверено), null — не задана.</param>
    /// <param name="clientPath">Путь exe запущенного клиента, null — не достать (права).</param>
    /// <param name="alive">Процесс выбранного клиента ещё запущен.</param>
    /// <param name="state">Состояние файла <paramref name="installPath"/> (или <paramref name="clientPath"/>, если «Игры» нет).</param>
    /// <param name="anyway">«Подключиться всё равно»: пропускает только проверку правок.</param>
    public static ConnectDecision Decide(bool hasRecipe, string? installPath, string? clientPath, bool alive,
        ClientPatchState state, bool anyway = false)
    {
        if (!alive)
            return new ConnectDecision(ConnectVerdict.ProcessGone, "Клиент закрыт — запустите игру и нажмите ⟳.");

        if (!hasRecipe)
            return Ready;

        if (installPath is not null && clientPath is not null
            && !string.Equals(installPath, clientPath, StringComparison.OrdinalIgnoreCase))
            return new ConnectDecision(ConnectVerdict.OtherInstall, $"Клиент запущен из другой папки: {clientPath}");

        if (anyway || state == ClientPatchState.Patched)
            return Ready;

        return state switch
        {
            ClientPatchState.Original => new ConnectDecision(ConnectVerdict.NotPatched,
                "Клиент без патча. Закройте игру и нажмите «Пропатчить»."),
            ClientPatchState.Unknown => new ConnectDecision(ConnectVerdict.UnknownBuild,
                "Версия клиента не распознана (обновление игры?) — патч не проверить."),
            ClientPatchState.Unreadable => new ConnectDecision(ConnectVerdict.UnknownBuild,
                "Файл клиента не читается (нет прав?) — патч не проверить."),
            ClientPatchState.Missing => new ConnectDecision(ConnectVerdict.UnknownBuild,
                "Файл клиента не найден — патч не проверить."),
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
        };
    }

    private static readonly ConnectDecision Ready = new(ConnectVerdict.Ready, "");
}
