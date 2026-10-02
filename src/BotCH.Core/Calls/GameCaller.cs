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
    }

    /// <summary>Можно ли вызывать функцию (есть в профиле и найдена).</summary>
    public bool Can(string name) => _functions.TryGetValue(name, out var f) && f.IsUsable && !_forbidden.Contains(f.Address);

    public IReadOnlyCollection<FunctionLocation> Functions => _functions.Values;

    /// <summary>Есть всё для «идти в точку»: менеджер работ и три его функции.</summary>
    public bool CanMoveTo => _profile.Host.WorkMan != 0
                             && Can(GameFunctions.WorkCreate) && Can(GameFunctions.WorkMoveSetDestination) && Can(GameFunctions.WorkStart);

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

    /// <summary>Скилл пакетом: цель targetWid, 0 — без цели (воскрешение пета).</summary>
    public CallResult CastSkill(int skillId, uint targetWid)
        => Call(GameFunctions.CastSkill, 0, BitConverter.GetBytes(targetWid), ["skill", "pvpMask", "count", DataArg],
            ("skill", (uint)skillId), ("pvpMask", 0), ("count", targetWid != 0 ? 1u : 0u));

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

    /// <summary>Идти в точку, как кликом по земле. Тип 0 — точка на земле (2 = направление, бежит бесконечно — запрещено).</summary>
    public CallResult MoveTo(uint host, float x, float height, float y)
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

        // StartWork: 1.3.6 — (1, work, 1, 0), у других клиентов — как в args профиля
        var startNames = _profile.Functions[GameFunctions.WorkStart].Args ?? ["1", WorkArg, "1", "0"];
        var startArgs = new uint?[startNames.Count];
        for (var i = 0; i < startNames.Count; i++)
        {
            if (startNames[i] == WorkArg)
                continue;
            if (!TryParseNumber(startNames[i], out var number))
                return CallResult.Refused($"{GameFunctions.WorkStart}: в профиле аргумент «{startNames[i]}», а бот его не даёт");
            startArgs[i] = number;
        }

        var point = new byte[12];
        Buffer.BlockCopy(BitConverter.GetBytes(x), 0, point, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(height), 0, point, 4, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(y), 0, point, 8, 4);
        return Result(_runner.Run(point, address => StubBuilder.MoveTo(workMan, addresses[0], addresses[1], addresses[2], address, 0, startArgs)));
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
