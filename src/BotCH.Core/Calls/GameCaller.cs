using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BotCH.Core.Actions;
using BotCH.Core.Memory;
using BotCH.Core.Profiles;
using BotCH.Core.World;

namespace BotCH.Core.Calls;

public sealed record CallResult(bool Ok, string Details)
{
    public static readonly CallResult Done = new(true, "");

    public static CallResult Refused(string reason) => new(false, reason);
}

/// <summary>
/// Действия бота прямыми вызовами функций клиента по профилю сервера (работают и при неактивном окне игры).
/// Правила безопасности — здесь, в одном месте:
/// <list type="bullet">
/// <item>функция должна быть найдена (<see cref="FunctionResolver"/>: Found/Relocated);</item>
/// <item>перед КАЖДЫМ вызовом её первые байты сверяются с сигнатурой ещё раз;</item>
/// <item>адреса из «forbidden» профиля (выход из игры, отпустить пета) не вызываются никогда.</item>
/// </list>
/// Функции профиля разбираются один раз, при создании: откуда this, как собрать каждый аргумент и регистр. Ошибка профиля
/// («аргумент, которого бот не даёт») видна сразу (<see cref="Problems"/>, в лог при «Старт»), а не посреди боя.
/// Сам вызов в игре выполняет <see cref="IRemoteRunner"/>.
/// </summary>
public sealed class GameCaller : IGameActions
{
    /// <summary>Номер приказа пету «атаковать» (как Alt+1).</summary>
    public const uint PetCommandAttack = 1;

    /// <summary>Адрес данных, записанных рядом с заглушкой.</summary>
    private const string DataArg = "data";

    /// <summary>Аргумент StartWork «созданная работа» — подставляет заглушка.</summary>
    private const string WorkArg = "work";

    /// <summary>Аргумент SetDestination «тип точки» (по прямой / автопуть / полёт), см. <see cref="MoveTypes"/>.</summary>
    private const string TypeArg = "type";

    /// <summary>
    /// Что бот даёт каждой функции: порядок аргументов по умолчанию (как у PW Classic 1.3.6 — если в профиле нет args).
    /// Имена — значения, которые бот даёт этой функции (в профиле их можно переставить, убрать или заменить числом), числа — как есть.
    /// </summary>
    private static readonly Dictionary<string, string[]> Defaults = new()
    {
        [GameFunctions.SelectTarget] = ["wid"],
        [GameFunctions.Unselect] = [],
        [GameFunctions.NormalAttack] = ["pvpMask"],
        [GameFunctions.Pickup] = ["id", "tid"],
        [GameFunctions.UseItem] = ["where", "slot", "tid", "count"],
        [GameFunctions.SummonPet] = ["index"],
        [GameFunctions.RecallPet] = [],
        [GameFunctions.CastSkill] = ["skill", "pvpMask", "count", DataArg],
        [GameFunctions.CancelAction] = [],
        [GameFunctions.PetCtrl] = ["target", "command", DataArg, "size"],
        [GameFunctions.HostApplySkill] = ["skill", "force", "target", "pvp"],
        [GameFunctions.HostPickupObject] = ["id", "gather"],
        [GameFunctions.HostFly] = ["force"],
        // 1.3.6: SetDestination(type, &point), StartWork(1, work, 1, 0)
        [GameFunctions.WorkMoveSetDestination] = [TypeArg, DataArg],
        [GameFunctions.WorkStart] = ["1", WorkArg, "1", "0"],
    };

    /// <summary>Методы, у которых this подставляет сам бот (менеджер работ, созданная работа), а не профиль.</summary>
    private static readonly HashSet<string> OwnThis = [GameFunctions.WorkCreate, GameFunctions.WorkMoveSetDestination, GameFunctions.WorkStart];

    /// <summary>Откуда взять аргумент: имя значения бота (в том числе адрес данных и работа) или число (<see cref="Name"/> = null).</summary>
    private readonly record struct Arg(string? Name, uint Number);

    /// <summary>Функция, разобранная при создании: аргументы и регистры по порядку; <see cref="Problem"/> — почему вызывать нельзя.</summary>
    private sealed record Prepared(GameFunction Definition, IReadOnlyList<Arg> Args, Arg? Ecx, Arg? Edx, string? Problem);

    private readonly IMemory _memory;
    private readonly IRemoteRunner _runner;
    private readonly GameRoots _roots;
    private readonly ProfileData _profile;
    private readonly HashSet<uint> _forbidden;
    private readonly Dictionary<string, FunctionLocation> _functions;
    private readonly Dictionary<string, Prepared> _prepared;

    public GameCaller(IMemory memory, IRemoteRunner runner, uint moduleBase, ProfileData profile, FunctionResolver? resolver = null)
    {
        _memory = memory;
        _runner = runner;
        _roots = new GameRoots(memory, moduleBase, profile);
        _profile = profile;
        _forbidden = new HashSet<uint>(profile.ForbiddenFunctions.Values.Select(rva => moduleBase + rva));
        resolver ??= new FunctionResolver(memory, moduleBase);
        _functions = profile.Functions.ToDictionary(f => f.Key, f => resolver.Resolve(f.Key, f.Value));
        _prepared = profile.Functions.ToDictionary(f => f.Key, f => Prepare(f.Key, f.Value));
        Capabilities = Capabilities.From(profile, WhyNot);
    }

    public string Mode => "вызовы";

    /// <summary>Что можно делать в этом клиенте — по тому, что реально найдено (а не только заявлено в профиле).</summary>
    public Capabilities Capabilities { get; }

    /// <summary>Можно ли вызывать функцию (есть в профиле, найдена, профиль описывает её без ошибок).</summary>
    public bool Can(string name) => WhyNot(name) is null;

    /// <summary>Функции профиля, которые вызывать нельзя, и почему — для лога при «Старт».</summary>
    public IReadOnlyList<(string Name, string Why)> Problems
        => _profile.Functions.Keys.Select(name => (Name: name, Why: WhyNot(name))).Where(p => p.Why is not null).Select(p => (p.Name, p.Why!)).ToList();

    // Почему функцию нельзя вызывать; null — можно
    private string? WhyNot(string name)
        => !_functions.TryGetValue(name, out var f) ? $"в профиле нет функции {name}"
            : !f.IsUsable ? $"{name}: {f.Details}"
            : _forbidden.Contains(f.Address) ? $"{name} — запрещённый адрес"
            : _prepared[name].Problem is { } problem ? $"{name}: {problem}"
            : null;

    // ── Действия ─────────────────────────────────────────────────────────────

    public CallResult SelectTarget(uint wid) => Call(GameFunctions.SelectTarget, null, ("wid", wid));

    public CallResult Unselect() => Call(GameFunctions.Unselect, null);

    /// <summary>Обычная атака текущей цели, pvpMask 0.</summary>
    public CallResult NormalAttack() => Call(GameFunctions.NormalAttack, null, ("pvpMask", 0));

    /// <summary>Подобрать пакетом: сервер поднимает только в радиусе ~10 м, персонаж не подходит.</summary>
    public CallResult Pickup(GroundItem item) => Call(GameFunctions.Pickup, null, ("id", item.Id), ("tid", item.Tid));

    /// <summary>Использовать 1 предмет из ячейки основной сумки (банка, корм пета).</summary>
    public CallResult UseItem(InventoryItem item)
        => Call(GameFunctions.UseItem, null, ("where", 0), ("slot", (uint)item.Slot), ("tid", item.Tid), ("count", 1));

    /// <summary>Призвать пета из клетки 1..N (N — число клеток в профиле).</summary>
    public CallResult SummonPet(int cage)
    {
        var cages = _profile.PetManager.CageCount;
        return cage < 1 || cage > cages
            ? CallResult.Refused($"клетка {cage} вне 1..{cages}")
            : Call(GameFunctions.SummonPet, null, ("index", (uint)(cage - 1)));
    }

    /// <summary>Отозвать призванного пета в клетку (c2s 0x65; не «отпустить» 0x66 — тот в запрещённых).</summary>
    public CallResult RecallPet() => Call(GameFunctions.RecallPet, null);

    /// <summary>Скилл пакетом: цель targetWid, 0 — без цели (воскрешение пета).</summary>
    public CallResult CastSkill(int skillId, uint targetWid)
        => Call(GameFunctions.CastSkill, BitConverter.GetBytes(targetWid), ("skill", (uint)skillId), ("pvpMask", 0), ("count", targetWid != 0 ? 1u : 0u));

    /// <summary>Отменить текущее действие персонажа (каст, копание) — как Esc.</summary>
    public CallResult CancelAction() => Call(GameFunctions.CancelAction, null);

    /// <summary>Приказ пету атаковать цель (данные — 1 байт pvpMask = 0).</summary>
    public CallResult PetAttack(uint targetWid)
        => Call(GameFunctions.PetCtrl, [0], ("target", targetWid), ("command", PetCommandAttack), ("size", 1));

    /// <summary>Скилл как нажатием кнопки: клиент сам подходит на дальность. target 0 — текущая цель.</summary>
    public CallResult ApplySkill(int skillId, uint targetWid)
        => Call(GameFunctions.HostApplySkill, null, ("skill", (uint)skillId), ("force", 0), ("target", targetWid), ("pvp", 0xFFFFFFFF));

    /// <summary>Подобрать как кликом мыши: клиент подводит персонажа и сам отправляет подбор.</summary>
    public CallResult PickupObject(GroundItem item) => PickupObject(item.Id, gather: false);

    /// <summary>Собрать ресурс (трава, руда) как кликом мыши: PickupObject с gather — клиент подходит и копает сам.</summary>
    public CallResult Gather(GroundItem resource) => PickupObject(resource.Id, gather: true);

    private CallResult PickupObject(uint id, bool gather)
        => Call(GameFunctions.HostPickupObject, null, ("id", id), ("gather", gather ? 1u : 0u));

    /// <summary>
    /// Кнопка «Полёт» (CECHostPlayer::CmdFly, this = перс): на земле — взлететь, в воздухе — сесть. Клиент сам проверяет,
    /// что полётник надет и сейчас можно; сидит — сначала встаёт (и тогда не взлетает).
    /// </summary>
    public CallResult ToggleFly() => Call(GameFunctions.HostFly, null, ("force", 0));

    /// <summary>Лететь в точку вместе с её высотой (тип точки <see cref="MoveTypes.Fly"/>). Только в воздухе.</summary>
    public CallResult FlyTo(Position point)
        => Capabilities.WhyNot(Capability.FlyTo) is { } why ? CallResult.Refused(why) : MoveTo(point, _profile.MoveTypes.Fly);

    /// <summary>
    /// Идти в точку: по прямой, как кликом по земле, или <paramref name="smart"/> — с автопутём, как кликом по карте (если он есть
    /// у сервера, иначе по прямой). Тип точки — из <see cref="MoveTypes"/> (2 — направление, бежит бесконечно, не использовать).
    /// </summary>
    public CallResult MoveTo(Position point, bool smart)
        => MoveTo(point, smart && Capabilities.Has(Capability.SmartMove) ? _profile.MoveTypes.Smart : _profile.MoveTypes.Direct);

    // Три вызова одной заглушкой: work = WorkMan->CreateWork(1); work->SetDestination(…); WorkMan->StartWork(…)
    private CallResult MoveTo(Position point, uint type)
    {
        var names = new[] { GameFunctions.WorkCreate, GameFunctions.WorkMoveSetDestination, GameFunctions.WorkStart };
        foreach (var name in names)
        {
            if (Check(name) is { } refused)
                return refused;
        }

        if (!_roots.TryHost(out var host))
            return CallResult.Refused("персонаж не в мире");
        if (_profile.Host.WorkMan == 0 || !_memory.TryReadUInt32(host + _profile.Host.WorkMan, out var workMan) || workMan == 0)
            return CallResult.Refused("нет менеджера работ персонажа");

        var data = new byte[12];
        Buffer.BlockCopy(BitConverter.GetBytes(point.X), 0, data, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(point.Height), 0, data, 4, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(point.Y), 0, data, 8, 4);
        var destination = _prepared[GameFunctions.WorkMoveSetDestination];
        var start = _prepared[GameFunctions.WorkStart];
        var values = new Dictionary<string, uint> { [TypeArg] = type };
        return Result(_runner.Run(data, address =>
        {
            // Созданную работу в StartWork подставляет сама заглушка — на её месте null
            uint? Slot(Arg arg) => arg.Name == WorkArg ? null : Value(arg, values, address);
            return StubBuilder.MoveTo(workMan, _functions[names[0]].Address, _functions[names[1]].Address,
                destination.Args.Select(a => Value(a, values, address)).ToArray(), _functions[names[2]].Address, start.Args.Select(Slot).ToArray());
        }));
    }

    // ── Разбор профиля и вызов ───────────────────────────────────────────────

    // Один раз: что бот даёт функции и совпадает ли это с тем, что просит профиль
    private static Prepared Prepare(string name, GameFunction definition)
    {
        // Вызывается не сама по себе (WorkCreate — внутри заглушки «идти»): разбирать нечего
        if (!Defaults.TryGetValue(name, out var defaults))
            return new Prepared(definition, [], null, null, null);

        var given = new HashSet<string>(defaults.Where(a => !TryParseNumber(a, out _)));
        string? problem = null;
        Arg Parse(string text)
        {
            if (given.Contains(text))
                return new Arg(text, 0);
            if (TryParseNumber(text, out var number))
                return new Arg(null, number);
            problem ??= $"в профиле аргумент «{text}», а бот его не даёт";
            return default;
        }

        var args = (definition.Args ?? (IReadOnlyList<string>)defaults).Select(Parse).ToList();
        var registers = definition.Registers ?? new Dictionary<string, string>();
        Arg? ecx = registers.TryGetValue("ecx", out var e) ? Parse(e) : null;
        Arg? edx = registers.TryGetValue("edx", out var d) ? Parse(d) : null;
        if (registers.Keys.FirstOrDefault(r => r is not ("ecx" or "edx")) is { } unknown)
            problem ??= $"в профиле регистр «{unknown}», умеем только ecx и edx";
        if (definition.Convention == CallingConvention.Thiscall && ecx is not null)
            problem ??= "у thiscall в ecx уже лежит объект";
        if (definition.Convention == CallingConvention.Thiscall && definition.This == FunctionThis.None && !OwnThis.Contains(name))
            problem ??= "thiscall, а в профиле не сказано, чей это метод (this)";
        return new Prepared(definition, args, ecx, edx, problem);
    }

    private static uint Value(Arg arg, IReadOnlyDictionary<string, uint> values, uint data)
        => arg.Name is null ? arg.Number : arg.Name == DataArg ? data : values[arg.Name];

    /// <param name="values">Значения, которые бот даёт этой функции, по именам (все имена из <see cref="Defaults"/>).</param>
    private CallResult Call(string name, byte[]? data, params (string Name, uint Value)[] values)
    {
        if (Check(name) is { } refused)
            return refused;

        var prepared = _prepared[name];
        // this — свежий на каждом вызове (после перезахода персонаж уже другой объект)
        var thisPointer = 0u;
        if (prepared.Definition.This == FunctionThis.Host && !_roots.TryHost(out thisPointer))
            return CallResult.Refused($"{name}: персонаж не в мире");
        if (prepared.Definition.This == FunctionThis.Session && !_roots.TrySession(out thisPointer))
            return CallResult.Refused($"{name}: нет связи с сервером");

        var known = values.ToDictionary(v => v.Name, v => v.Value);
        var address = _functions[name].Address;
        return Result(_runner.Run(data, dataAddress => StubBuilder.Call(address, prepared.Definition.Convention, thisPointer,
            prepared.Args.Select(a => Value(a, known, dataAddress)).ToArray(),
            prepared.Ecx is { } ecx ? Value(ecx, known, dataAddress) : null,
            prepared.Edx is { } edx ? Value(edx, known, dataAddress) : null)));
    }

    private static bool TryParseNumber(string text, out uint value)
        => text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? uint.TryParse(text.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value)
            : uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    /// <summary>null — можно вызывать; иначе причина отказа.</summary>
    private CallResult? Check(string name)
    {
        if (!_functions.TryGetValue(name, out var function) || !function.IsUsable)
            return CallResult.Refused($"{name}: функция не найдена в клиенте ({function?.Details ?? "нет в профиле"})");
        if (_forbidden.Contains(function.Address))
            return CallResult.Refused($"{name}: адрес 0x{function.Address:X8} в списке запрещённых");
        if (_prepared[name].Problem is { } problem)
            return CallResult.Refused($"{name}: {problem}");

        // Сигнатура — перед каждым вызовом: клиент мог обновиться или в памяти оказался чужой код
        var signature = _profile.Functions[name].Signature!;
        var actual = new byte[signature.Length];
        if (!_memory.TryRead(function.Address, actual, actual.Length) || !signature.Matches(actual))
            return CallResult.Refused($"{name}: по адресу 0x{function.Address:X8} не те байты — вызов отменён");

        return null;
    }

    private static CallResult Result(RemoteRunResult run) => run.IsDone ? CallResult.Done : CallResult.Refused(run.Details);
}
