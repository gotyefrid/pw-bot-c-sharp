using System;
using System.Collections.Generic;
using BotCH.Core.Actions;
using BotCH.Core.Brain;
using BotCH.Core.Settings;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BotCH.Core.Clicker;

/// <summary>
/// Действие кликера: его настройки (какую клавишу, сколько ждать) и как его выполнить (<see cref="Start"/>). Поля меняются
/// через <see cref="SettingsPart.Set{T}"/> — окно привязано к ним напрямую и узнаёт о правке.
/// <para>
/// Новое действие: класс-наследник (поля, <see cref="Describe"/>, <see cref="Start"/>, при нужде <see cref="Problem"/> и
/// <see cref="Normalize"/>), строка в <see cref="ClickerKinds.Actions"/>, в окне — шаблон его полей. Остальное не трогается.
/// </para>
/// </summary>
public abstract class ClickerAction : SettingsPart
{
    /// <summary>Вид в файле настроек («PressKey») — по нему файл читается обратно (<see cref="ClickerJson"/>).</summary>
    [JsonProperty(Order = -2)]
    public string Kind => KindId();

    protected virtual string KindId() => ClickerKinds.Actions.IdOf(GetType());

    /// <summary>Для списка в окне и лога: «клавиша F1», «пауза 1 с».</summary>
    public abstract string Describe();

    /// <summary>Почему выполнять нечего (клавиша не выбрана); null — можно. Такой шаг пропускается.</summary>
    [JsonIgnore]
    public virtual string? Problem => null;

    /// <summary>Начать выполнение — новое на каждый запуск шага, со своим состоянием (<see cref="ClickerRuns"/> — готовые).</summary>
    public abstract IClickerRun Start(BrainContext c);

    /// <summary>Значения — в допустимые пределы (после ручной правки файла или старой версии).</summary>
    public virtual void Normalize()
    {
    }
}

/// <summary>Нажать клавишу в окне игры (<see cref="KeyAction"/>): ушло нажатие — сделано, следующий шаг — в тот же ход.</summary>
public sealed class PressKeyAction : ClickerAction
{
    private string _key = "";

    /// <summary>Клавиша, как на клавиатуре: «F1», «Ctrl+2» (<see cref="KeyStroke"/>). Пусто — не выбрана.</summary>
    public string Key { get => _key; set => Set(ref _key, value ?? ""); }

    /// <summary>Клавиша; null — не выбрана, не разобрать или её нельзя жать (<see cref="KeyStroke.Forbidden"/>).</summary>
    [JsonIgnore]
    public KeyStroke? Stroke => KeyStroke.TryParse(Key, out var stroke) && stroke.Forbidden is null ? stroke : null;

    public override string? Problem => Stroke is null ? "клавиша не выбрана" : null;

    public override string Describe() => Stroke is { } stroke ? $"клавиша {stroke}" : "клавиша не выбрана";

    public override IClickerRun Start(BrainContext c)
        => Stroke is { } stroke ? ClickerRuns.Send(new KeyAction(stroke), doneWhenSent: true) : ClickerRuns.Failed(Problem!);

    public override void Normalize() => Key = Stroke?.ToString() ?? "";
}

/// <summary>
/// Пауза: следующий шаг — через столько миллисекунд. Время кликер сверяет по снимкам (раз в 250 мс): пауза короче
/// снимка всё равно ждёт следующего.
/// </summary>
public sealed class PauseAction : ClickerAction
{
    public const int MaxMilliseconds = 24 * 3600 * 1000;

    private int _milliseconds = 1000;

    public int Milliseconds { get => _milliseconds; set => Set(ref _milliseconds, value); }

    public override string Describe() => $"пауза {Milliseconds} мс";

    public override IClickerRun Start(BrainContext c) => ClickerRuns.Wait(TimeSpan.FromMilliseconds(Milliseconds));

    public override void Normalize() => Milliseconds = Math.Min(MaxMilliseconds, Math.Max(0, Milliseconds));
}

/// <summary>
/// Действие вида, которого эта версия бота не знает (файл от более новой копии): не выполняется, но и не теряется — в файл
/// пишется как было (<see cref="Fields"/>).
/// </summary>
public sealed class UnknownAction(string kind) : ClickerAction
{
    /// <summary>Поля из файла, кроме вида, — как были.</summary>
    [JsonExtensionData]
    public IDictionary<string, JToken> Fields { get; set; } = new Dictionary<string, JToken>();

    protected override string KindId() => kind;

    public override string? Problem => $"действие «{kind}» из другой версии бота";

    public override string Describe() => $"«{kind}» — неизвестно этой версии бота";

    public override IClickerRun Start(BrainContext c) => ClickerRuns.Failed(Problem!);
}
