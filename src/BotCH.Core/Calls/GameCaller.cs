using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BotCH.Core.Memory;
using BotCH.Core.Profiles;

namespace BotCH.Core.Calls;

public sealed record CallResult(bool Ok, string Details)
{
    public static readonly CallResult Done = new(true, "");

    public static CallResult Refused(string reason) => new(false, reason);
}

/// <summary>
/// Вызовы функций клиента по профилю сервера. Правила безопасности — здесь, в одном месте:
/// <list type="bullet">
/// <item>функция должна быть найдена (<see cref="FunctionResolver"/>: Found/Relocated);</item>
/// <item>перед КАЖДЫМ вызовом её первые байты сверяются с сигнатурой ещё раз;</item>
/// <item>адреса из «forbidden» профиля (выход из игры, отпустить пета) не вызываются никогда.</item>
/// </list>
/// Каждый метод отдаёт значения по именам; какие из них и в каком порядке уйдут в функцию, решает профиль (args),
/// иначе — порядок по умолчанию (PW Classic 1.3.6). Имя «data» — адрес данных, записанных рядом с заглушкой.
/// Сам поток в игре запускает <see cref="IRemoteRunner"/>.
/// </summary>
public sealed class GameCaller
{
    /// <summary>Номер приказа пету «атаковать» (как Alt+1).</summary>
    public const uint PetCommandAttack = 1;

    private const string DataArg = "data";

    /// <summary>Аргумент StartWork «созданная работа».</summary>
    private const string WorkArg = "work";

    /// <summary>Аргумент SetDestination «тип точки» (по прямой / автопуть), см. <see cref="MoveTypes"/>.</summary>
    private const string TypeArg = "type";

    private readonly IMemory _memory;
    private readonly IRemoteRunner _runner;
    private readonly uint _moduleBase;
    private readonly ProfileData _profile;
    private readonly HashSet<uint> _forbidden;
    private readonly Dictionary<string, FunctionLocation> _functions;

    public GameCaller(IMemory memory, IRemoteRunner runner, uint moduleBase, ProfileData profile, FunctionResolver? resolver = null)
    {
        _memory = memory;
        _runner = runner;
        _moduleBase = moduleBase;
        _profile = profile;
        _forbidden = new HashSet<uint>(profile.ForbiddenFunctions.Values.Select(rva => moduleBase + rva));
        resolver ??= new FunctionResolver(memory, moduleBase);
        _functions = profile.Functions.ToDictionary(f => f.Key, f => resolver.Resolve(f.Key, f.Value));
        Capabilities = Capabilities.From(profile, WhyNot);
    }

    /// <summary>Что можно делать в этом клиенте — по тому, что реально найдено (а не только заявлено в профиле).</summary>
    public Capabilities Capabilities { get; }

    /// <summary>Можно ли вызывать функцию (есть в профиле и найдена).</summary>
    public bool Can(string name) => WhyNot(name) is null;

    // Почему функцию нельзя вызывать; null — можно
    private string? WhyNot(string name)
        => !_functions.TryGetValue(name, out var f) ? $"в профиле нет функции {name}"
            : !f.IsUsable ? $"{name}: {f.Details}"
            : _forbidden.Contains(f.Address) ? $"{name} — запрещённый адрес"
            : null;

    public IReadOnlyCollection<FunctionLocation> Functions => _functions.Values;

    public CallResult SelectTarget(uint wid) => Call(GameFunctions.SelectTarget, 0, null, ["wid"], ("wid", wid));

    public CallResult Unselect() => Call(GameFunctions.Unselect, 0, null, []);

    /// <summary>Обычная атака текущей цели, pvpMask 0.</summary>
    public CallResult NormalAttack() => Call(GameFunctions.NormalAttack, 0, null, ["pvpMask"], ("pvpMask", 0));

    /// <summary>Подобрать пакетом: сервер поднимает только в радиусе ~10 м, персонаж не подходит.</summary>
    public CallResult Pickup(uint id, uint tid) => Call(GameFunctions.Pickup, 0, null, ["id", "tid"], ("id", id), ("tid", tid));

    /// <summary>Использовать 1 предмет из ячейки основной сумки (банка, корм пета).</summary>
    public CallResult UseItem(int slot, uint tid)
        => Call(GameFunctions.UseItem, 0, null, ["where", "slot", "tid", "count"], ("where", 0), ("slot", (uint)slot), ("tid", tid), ("count", 1));

    /// <summary>Призвать пета из клетки 1..N (N — число клеток в профиле).</summary>
    public CallResult SummonPet(int cage)
    {
        var cages = _profile.PetManager.CageCount;
        return cage < 1 || cage > cages
            ? CallResult.Refused($"клетка {cage} вне 1..{cages}")
            : Call(GameFunctions.SummonPet, 0, null, ["index"], ("index", (uint)(cage - 1)));
    }

    /// <summary>Отозвать призванного пета в клетку (c2s 0x65; не «отпустить» 0x66 — тот в запрещённых).</summary>
    public CallResult RecallPet() => Call(GameFunctions.RecallPet, 0, null, []);

    /// <summary>Скилл пакетом: цель targetWid, 0 — без цели (воскрешение пета).</summary>
    public CallResult CastSkill(int skillId, uint targetWid)
        => Call(GameFunctions.CastSkill, 0, BitConverter.GetBytes(targetWid), ["skill", "pvpMask", "count", DataArg],
            ("skill", (uint)skillId), ("pvpMask", 0), ("count", targetWid != 0 ? 1u : 0u));

    /// <summary>Отменить текущее действие персонажа (каст, копание) — как Esc.</summary>
    public CallResult CancelAction() => Call(GameFunctions.CancelAction, 0, null, []);

    /// <summary>Приказ пету атаковать цель (данные — 1 байт pvpMask = 0).</summary>
    public CallResult PetAttack(uint targetWid)
        => Call(GameFunctions.PetCtrl, 0, [0], ["target", "command", DataArg, "size"],
            ("target", targetWid), ("command", PetCommandAttack), ("size", 1));

    /// <summary>Скилл как нажатием кнопки: клиент сам подходит на дальность. target 0 — текущая цель.</summary>
    public CallResult ApplySkill(uint host, int skillId, uint targetWid = 0)
        => Call(GameFunctions.HostApplySkill, host, null, ["skill", "force", "target", "pvp"],
            ("skill", (uint)skillId), ("force", 0), ("target", targetWid), ("pvp", 0xFFFFFFFF));

    /// <summary>Подобрать как кликом мыши: клиент подводит персонажа и сам отправляет подбор. gather — собрать ресурс.</summary>
    public CallResult PickupObject(uint host, uint id, bool gather = false)
        => Call(GameFunctions.HostPickupObject, host, null, ["id", "gather"], ("id", id), ("gather", gather ? 1u : 0u));

    /// <summary>
    /// Кнопка «Полёт» (CECHostPlayer::CmdFly, this = перс): на земле — взлететь, в воздухе — сесть. Клиент сам проверяет,
    /// что полётник надет и сейчас можно; сидит — сначала встаёт (и тогда не взлетает).
    /// </summary>
    public CallResult ToggleFly(uint host) => Call(GameFunctions.HostFly, host, null, ["force"], ("force", 0));

    /// <summary>Лететь в точку вместе с её высотой (тип точки <see cref="MoveTypes.Fly"/>). Только в воздухе.</summary>
    public CallResult FlyTo(uint host, float x, float height, float y)
        => Capabilities.WhyNot(Capability.FlyTo) is { } why ? CallResult.Refused(why) : MoveTo(host, x, height, y, _profile.MoveTypes.Fly);

    /// <summary>
    /// Идти в точку: по прямой, как кликом по земле, или <paramref name="smart"/> — с автопутём, как кликом по карте (если он есть
    /// у сервера, иначе по прямой). Аргументы SetDestination и StartWork — из профиля, по умолчанию как в 1.3.6; тип точки — из
    /// <see cref="MoveTypes"/> (2 — направление, бежит бесконечно, не использовать).
    /// </summary>
    public CallResult MoveTo(uint host, float x, float height, float y, bool smart = false)
        => MoveTo(host, x, height, y, smart && Capabilities.Has(Capability.SmartMove) ? _profile.MoveTypes.Smart : _profile.MoveTypes.Direct);

    private CallResult MoveTo(uint host, float x, float height, float y, uint type)
    {
        var names = new[] { GameFunctions.WorkCreate, GameFunctions.WorkMoveSetDestination, GameFunctions.WorkStart };
        var addresses = new uint[names.Length];
        for (var i = 0; i < names.Length; i++)
        {
            if (Check(names[i]) is { } refused)
                return refused;
            addresses[i] = _functions[names[i]].Address;
        }

        if (_profile.Host.WorkMan == 0 || !_memory.TryReadUInt32(host + _profile.Host.WorkMan, out var workMan) || workMan == 0)
            return CallResult.Refused("нет менеджера работ персонажа");

        // 1.3.6: SetDestination(type, &point), StartWork(1, work, 1, 0); у других клиентов — как в args профиля
        if (MoveArgs(GameFunctions.WorkMoveSetDestination, [TypeArg, DataArg], DataArg, out var destinationArgs, (TypeArg, type)) is { } badDestination)
            return badDestination;
        if (MoveArgs(GameFunctions.WorkStart, ["1", WorkArg, "1", "0"], WorkArg, out var startArgs) is { } badStart)
            return badStart;

        var point = new byte[12];
        Buffer.BlockCopy(BitConverter.GetBytes(x), 0, point, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(height), 0, point, 4, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(y), 0, point, 8, 4);
        return Result(_runner.Run(point, address => StubBuilder.MoveTo(workMan, addresses[0], addresses[1],
            destinationArgs.Select(a => a ?? address).ToArray(), addresses[2], startArgs)));
    }

    /// <summary>
    /// Числа из args профиля (или значения по именам из <paramref name="values"/>); на месте <paramref name="slot"/> — null
    /// (подставится при сборке заглушки).
    /// </summary>
    private CallResult? MoveArgs(string function, string[] defaultArgs, string slot, out uint?[] args, params (string Name, uint Value)[] values)
    {
        var names = _profile.Functions[function].Args ?? (IReadOnlyList<string>)defaultArgs;
        args = new uint?[names.Count];
        for (var i = 0; i < names.Count; i++)
        {
            if (names[i] == slot)
                continue;
            var known = values.Where(v => v.Name == names[i]).ToArray();
            if (known.Length > 0)
            {
                args[i] = known[0].Value;
                continue;
            }

            if (!TryParseNumber(names[i], out var number))
                return CallResult.Refused($"{function}: в профиле аргумент «{names[i]}», а бот его не даёт");
            args[i] = number;
        }

        return null;
    }

    /// <param name="defaultArgs">Порядок аргументов, если в профиле нет args.</param>
    /// <param name="values">Значения, которые функция может получить, по именам.</param>
    private CallResult Call(string name, uint thisPointer, byte[]? data, string[] defaultArgs, params (string Name, uint Value)[] values)
        => Check(name) ?? Run(name, thisPointer, data, defaultArgs, values);

    private CallResult Run(string name, uint thisPointer, byte[]? data, string[] defaultArgs, (string Name, uint Value)[] values)
    {
        var function = _functions[name];
        var definition = _profile.Functions[name];
        if (definition.This == FunctionThis.Host && !TryReadHost(out thisPointer))
            return CallResult.Refused($"{name}: персонаж не в мире");
        if (definition.This == FunctionThis.Session && !TryReadSession(out thisPointer))
            return CallResult.Refused($"{name}: нет связи с сервером");
        if (definition.Convention == CallingConvention.Thiscall && thisPointer == 0)
            return CallResult.Refused($"{name}: нет объекта для вызова");

        var names = definition.Args ?? (IReadOnlyList<string>)defaultArgs;
        var registers = definition.Registers ?? new Dictionary<string, string>();
        var known = values.ToDictionary(v => v.Name, v => v.Value);
        foreach (var arg in names.Concat(registers.Values))
        {
            if (arg == DataArg ? data is null : !known.ContainsKey(arg) && !TryParseNumber(arg, out _))
                return CallResult.Refused($"{name}: в профиле аргумент «{arg}», а бот его не даёт");
        }

        var unknownRegister = registers.Keys.FirstOrDefault(r => r is not ("ecx" or "edx"));
        if (unknownRegister is not null)
            return CallResult.Refused($"{name}: в профиле регистр «{unknownRegister}», умеем только ecx и edx");
        if (definition.Convention == CallingConvention.Thiscall && registers.ContainsKey("ecx"))
            return CallResult.Refused($"{name}: у thiscall в ecx уже лежит объект");

        return Result(_runner.Run(data, address =>
        {
            uint Value(string arg) => arg == DataArg ? address : known.TryGetValue(arg, out var value) ? value : ParseNumber(arg);
            uint? Register(string register) => registers.TryGetValue(register, out var arg) ? Value(arg) : null;
            return StubBuilder.Call(function.Address, definition.Convention, thisPointer, names.Select(Value).ToArray(),
                Register("ecx"), Register("edx"));
        }));
    }

    private bool TryReadHost(out uint host)
    {
        host = 0;
        return _memory.TryReadUInt32(_moduleBase + _profile.Base.BasePointer, out var basePointer)
               && _memory.TryReadUInt32(basePointer + _profile.Base.Game, out var game)
               && _memory.TryReadUInt32(game + _profile.Host.Struct, out host)
               && host != 0;
    }

    private bool TryReadSession(out uint session)
    {
        session = 0;
        return _profile.Base.Session != 0
               && _memory.TryReadUInt32(_moduleBase + _profile.Base.BasePointer, out var basePointer)
               && _memory.TryReadUInt32(basePointer + _profile.Base.Session, out session)
               && session != 0;
    }

    private static uint ParseNumber(string text) => TryParseNumber(text, out var value) ? value : 0;

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

        // Сигнатура — перед каждым вызовом: клиент мог обновиться или в памяти оказался чужой код
        var signature = _profile.Functions[name].Signature!;
        var actual = new byte[signature.Length];
        if (!_memory.TryRead(function.Address, actual, actual.Length) || !signature.Matches(actual))
            return CallResult.Refused($"{name}: по адресу 0x{function.Address:X8} не те байты — вызов отменён");

        return null;
    }

    private static CallResult Result(RemoteRunResult run) => run.IsDone ? CallResult.Done : CallResult.Refused(run.Details);
}
