using System;
using System.Collections.Generic;
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
/// Сам поток в игре запускает <see cref="IRemoteRunner"/>.
/// </summary>
public sealed class GameCaller
{
    /// <summary>Номер приказа пету «атаковать» (как Alt+1).</summary>
    public const uint PetCommandAttack = 1;

    private readonly IMemory _memory;
    private readonly IRemoteRunner _runner;
    private readonly ProfileData _profile;
    private readonly HashSet<uint> _forbidden;
    private readonly Dictionary<string, FunctionLocation> _functions;

    public GameCaller(IMemory memory, IRemoteRunner runner, uint moduleBase, ProfileData profile, FunctionResolver? resolver = null)
    {
        _memory = memory;
        _runner = runner;
        _profile = profile;
        _forbidden = new HashSet<uint>(profile.ForbiddenFunctions.Values.Select(rva => moduleBase + rva));
        resolver ??= new FunctionResolver(memory, moduleBase);
        _functions = profile.Functions.ToDictionary(f => f.Key, f => resolver.Resolve(f.Key, f.Value));
    }

    /// <summary>Можно ли вызывать функцию (есть в профиле и найдена).</summary>
    public bool Can(string name) => _functions.TryGetValue(name, out var f) && f.IsUsable && !_forbidden.Contains(f.Address);

    public IReadOnlyCollection<FunctionLocation> Functions => _functions.Values;

    public CallResult SelectTarget(uint wid) => Call(GameFunctions.SelectTarget, 0, wid);

    public CallResult Unselect() => Call(GameFunctions.Unselect, 0);

    /// <summary>Обычная атака текущей цели, pvpMask 0.</summary>
    public CallResult NormalAttack() => Call(GameFunctions.NormalAttack, 0, 0);

    /// <summary>Подобрать пакетом: сервер поднимает только в радиусе ~10 м, персонаж не подходит.</summary>
    public CallResult Pickup(uint id, uint tid) => Call(GameFunctions.Pickup, 0, id, tid);

    /// <summary>Использовать 1 предмет из ячейки основной сумки (банка, корм пета).</summary>
    public CallResult UseItem(int slot, uint tid) => Call(GameFunctions.UseItem, 0, 0, (uint)slot, tid, 1);

    /// <summary>Призвать пета из клетки 1..10.</summary>
    public CallResult SummonPet(int cage)
        => cage is < 1 or > 10 ? CallResult.Refused($"клетка {cage} вне 1..10") : Call(GameFunctions.SummonPet, 0, (uint)(cage - 1));

    /// <summary>Скилл пакетом: цель targetWid, 0 — без цели (воскрешение пета).</summary>
    public CallResult CastSkill(int skillId, uint targetWid)
        => CallWithData(GameFunctions.CastSkill, BitConverter.GetBytes(targetWid),
            data => [(uint)skillId, 0, targetWid != 0 ? 1u : 0u, data]);

    /// <summary>Приказ пету атаковать цель (данные — 1 байт pvpMask = 0).</summary>
    public CallResult PetAttack(uint targetWid)
        => CallWithData(GameFunctions.PetCtrl, [0], data => [targetWid, PetCommandAttack, data, 1]);

    /// <summary>Скилл как нажатием кнопки: клиент сам подходит на дальность. target 0 — текущая цель.</summary>
    public CallResult ApplySkill(uint host, int skillId, uint targetWid = 0)
        => Call(GameFunctions.HostApplySkill, host, (uint)skillId, 0, targetWid, 0xFFFFFFFF);

    /// <summary>Подобрать как кликом мыши: клиент подводит персонажа и сам отправляет подбор. gather — собрать ресурс.</summary>
    public CallResult PickupObject(uint host, uint id, bool gather = false)
        => Call(GameFunctions.HostPickupObject, host, id, gather ? 1u : 0u);

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

        var point = new byte[12];
        Buffer.BlockCopy(BitConverter.GetBytes(x), 0, point, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(height), 0, point, 4, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(y), 0, point, 8, 4);
        return Result(_runner.Run(point, address => StubBuilder.MoveTo(workMan, addresses[0], addresses[1], addresses[2], address, 0)));
    }

    private CallResult Call(string name, uint thisPointer, params uint[] args)
        => Check(name) ?? Run(name, thisPointer, null, _ => args);

    private CallResult CallWithData(string name, byte[] data, Func<uint, uint[]> args)
        => Check(name) ?? Run(name, 0, data, args);

    private CallResult Run(string name, uint thisPointer, byte[]? data, Func<uint, uint[]> args)
    {
        var function = _functions[name];
        var convention = _profile.Functions[name].Convention;
        if (convention == CallingConvention.Thiscall && thisPointer == 0)
            return CallResult.Refused($"{name}: нет объекта для вызова");

        return Result(_runner.Run(data, address => StubBuilder.Call(function.Address, convention, thisPointer, args(address))));
    }

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
