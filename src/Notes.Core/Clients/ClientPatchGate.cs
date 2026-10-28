using System;

namespace Notes.Core.Clients;

/// <summary>Подключение к клиенту без правок — на усмотрение пользователя (хранится в settings.json).</summary>
public enum ClientPatchPolicy
{
    /// <summary>Спросить: «подключиться на свой страх и риск?» (по умолчанию).</summary>
    Ask,

    /// <summary>Не подключаться, пока правки не применены.</summary>
    Require,

    /// <summary>Правки не проверять (пользователь сам знает, что делает).</summary>
    Ignore,
}

/// <summary>
/// Что решить по подключению. <see cref="Problem"/> — суть (для лога), <see cref="Text"/> — готовый текст
/// вопроса или отказа (Problem + что делать); у «пустить» оба пустые.
/// </summary>
public sealed record ClientPatchDecision(ClientPatchVerdict Verdict, string Problem, string Text);

public enum ClientPatchVerdict
{
    /// <summary>Подключаемся без вопросов.</summary>
    Allow,

    /// <summary>Спросить пользователя; Text — полный текст вопроса.</summary>
    Ask,

    /// <summary>Не подключаться; Text — полный текст отказа.</summary>
    Refuse,
}

/// <summary>
/// Ворота подключения к клиенту: политика × состояние файла → пустить / спросить / отказать. Чистая функция —
/// главный предохранитель фичи, покрыт тестами; окну остаётся показать вопрос или отказ.
/// </summary>
public static class ClientPatchGate
{
    public static ClientPatchDecision Decide(ClientPatchPolicy policy, ClientPatchState state)
    {
        if (policy == ClientPatchPolicy.Ignore || state == ClientPatchState.Patched)
            return new ClientPatchDecision(ClientPatchVerdict.Allow, "", "");

        var problem = state switch
        {
            ClientPatchState.Original => "Файл клиента исходный — правки не применены.",
            ClientPatchState.Unknown => "Файл клиента не похож на известный билд (обновление игры?) — правки не проверить.",
            ClientPatchState.Unreadable => "Файл клиента не читается (нет прав?) — правки не проверить.",
            ClientPatchState.Missing => "Файл клиента не найден — правки не проверить.",
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
        };

        return policy == ClientPatchPolicy.Require
            ? new ClientPatchDecision(ClientPatchVerdict.Refuse, problem,
                problem + "\n\nПодключение запрещено настройкой «Без патча: не подключаться». Закройте игру и нажмите «Пропатчить».")
            : new ClientPatchDecision(ClientPatchVerdict.Ask, problem,
                problem + "\n\nПодключиться на свой страх и риск?");
    }
}
