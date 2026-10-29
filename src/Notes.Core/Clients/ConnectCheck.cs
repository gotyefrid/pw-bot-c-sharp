using System;

namespace Notes.Core.Clients;

/// <summary>Что показать пользователю по кнопке «Подключить».</summary>
public enum ConnectVerdict
{
    /// <summary>Подключаемся.</summary>
    Ready,

    /// <summary>Выбранного клиента уже нет (закрыли игру).</summary>
    ProcessGone,

    /// <summary>Правки не применены: подключиться можно только «всё равно».</summary>
    NotPatched,

    /// <summary>Правки не проверить (файл не распознан, не найден, не читается): тоже только «всё равно».</summary>
    UnknownBuild,

    /// <summary>Сторож клиента (pwprotector) изменился: файл обновился, эту версию не разбирали.</summary>
    ProtectorChanged,

    /// <summary>Сторож не проверить (нет файла, не читается): что рядом с клиентом — неизвестно.</summary>
    ProtectorUnknown,
}

/// <summary>Вердикт и готовый текст подсказки (пусто у <see cref="ConnectVerdict.Ready"/>).</summary>
public sealed record ConnectDecision(ConnectVerdict Verdict, string Text);

/// <summary>
/// Проверка перед «Подключить»: жив ли клиент, стоят ли правки, тот ли сторож рядом с клиентом. Что клиент из exe
/// «Игры», проверять не нужно: другие в список не попадают (<see cref="ClientList.IsOfGame"/>). Чистая функция —
/// главный предохранитель фичи, покрыта тестами; окну остаётся показать строку-подсказку и кнопку следующего шага.
/// </summary>
public static class ConnectCheck
{
    /// <param name="hasRecipe">У сервера есть рецепт правок; нет — проверять нечего, кроме живого процесса.</param>
    /// <param name="alive">Процесс выбранного клиента ещё запущен.</param>
    /// <param name="state">Состояние файла «Игры» сервера.</param>
    /// <param name="anyway">«Подключиться всё равно»: пропускает проверку правок.</param>
    /// <param name="protector">Состояние сторожа (<see cref="ProtectorCheck"/>); null — проверять нечего (нет эталона).</param>
    public static ConnectDecision Decide(bool hasRecipe, bool alive, ClientPatchState state, bool anyway = false,
        ProtectorState? protector = null)
    {
        if (!alive)
            return new ConnectDecision(ConnectVerdict.ProcessGone, "Клиент закрыт — запустите игру и нажмите ⟳.");

        if (anyway)
            return Ready;

        if (hasRecipe && state != ClientPatchState.Patched)
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

        // Сторож: нет эталона в профиле или файл тот же — тишина. Сверяем последним: сначала «можно ли вообще
        // подключаться», иначе игрок увидит «сторож обновился» у клиента, который ещё и не пропатчен.
        return protector switch
        {
            null or ProtectorState.Known or ProtectorState.NotConfigured => Ready,
            ProtectorState.Changed => new ConnectDecision(ConnectVerdict.ProtectorChanged,
                "Защита клиента обновилась — эту версию бот не знает. Подключение может быть замечено."),
            ProtectorState.Missing => new ConnectDecision(ConnectVerdict.ProtectorUnknown,
                "Файл защиты клиента не найден — проверить нечего. Игра обновилась?"),
            ProtectorState.Unreadable => new ConnectDecision(ConnectVerdict.ProtectorUnknown,
                "Файл защиты клиента не читается (нет прав?) — проверить нечего."),
            _ => throw new ArgumentOutOfRangeException(nameof(protector), protector, null),
        };
    }

    private static readonly ConnectDecision Ready = new(ConnectVerdict.Ready, "");
}
