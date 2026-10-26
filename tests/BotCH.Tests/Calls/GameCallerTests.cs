using System;
using System.Collections.Generic;
using System.Linq;
using BotCH.Core.Calls;
using BotCH.Core.Memory;
using BotCH.Core.Profiles;
using BotCH.Core.World;
using Xunit;

namespace BotCH.Tests.Calls;

public class GameCallerTests
{
    private const uint ModuleBase = 0x400000;
    // Где лежит указатель на базу — в тестах любой адрес
    private const uint BasePointer = ModuleBase + 0x100;
    private const uint DataAddress = 0x7000_0100;
    private const uint Host = 0x1FA1F868;
    private const uint Session = 0x3300_0000;
    private static readonly ProfileData Profile = new ProfileCatalog().Load("pwclassic136").Data;

    private readonly MemoryImage _memory = new();
    private readonly FakeRunner _runner = new();

    private sealed class FakeRunner : IRemoteRunner
    {
        public List<(byte[]? Data, byte[] Stub)> Runs { get; } = [];
        public RemoteRunResult Result { get; set; } = new(RemoteRunStatus.Done);

        public RemoteRunResult Run(byte[]? data, Func<uint, byte[]> buildStub)
        {
            Runs.Add((data, buildStub(DataAddress)));
            return Result;
        }
    }

    public GameCallerTests()
    {
        // «Код клиента»: по адресу каждой функции — байты её сигнатуры (?? → 00)
        foreach (var function in Profile.Functions.Values)
            _memory.WriteBytes(ModuleBase + function.Rva, CodeFor(function.Signature!));
    }

    private static byte[] CodeFor(Signature signature)
        => signature.ToString().Split(' ').Select(b => b == "??" ? (byte)0 : Convert.ToByte(b, 16)).ToArray();

    private GameCaller Caller(ProfileData? profile = null) => new(_memory, _runner, ModuleBase, new GameRoots(_memory, BasePointer, profile ?? Profile), profile ?? Profile);

    private static uint Address(string name) => ModuleBase + Profile.Functions[name].Rva;

    // Персонаж в мире: цепочки профиля «база → game → персонаж» и «база → связь с сервером» ведут к ним
    private void PutHost(uint host = Host)
    {
        const uint basePtr = 0x1000_0000, game = 0x1001_0000;
        _memory.WriteUInt32(BasePointer, basePtr);
        _memory.WriteUInt32(basePtr + Profile.Base.Game, game);
        _memory.WriteUInt32(basePtr + Profile.Base.Session, Session);
        _memory.WriteUInt32(game + Profile.Host.Struct, host);
    }

    [Fact]
    public void SelectTargetRunsStubAtFunctionAddress()
    {
        PutHost();

        var result = Caller().SelectTarget(0x80104298);

        Assert.True(result.Ok, result.Details);
        var run = Assert.Single(_runner.Runs);
        Assert.Null(run.Data);
        Assert.Equal(StubBuilder.Call(Address(GameFunctions.SelectTarget), CallingConvention.Thiscall, Host, [0x80104298]), run.Stub);
    }

    [Fact]
    public void ChangedBytesBeforeCallCancelIt()
    {
        var caller = Caller();
        // Клиент обновился / в памяти чужой код — уже после запуска бота
        _memory.WriteBytes(Address(GameFunctions.SelectTarget), [0xCC, 0xCC, 0xCC, 0xCC]);

        var result = caller.SelectTarget(1);

        Assert.False(result.Ok);
        Assert.Contains("не те байты", result.Details);
        Assert.Empty(_runner.Runs);
    }

    [Fact]
    public void MissingFunctionIsRefused()
    {
        var result = Caller(new ProfileData()).SelectTarget(1);

        Assert.False(result.Ok);
        Assert.Contains("не найдена", result.Details);
    }

    [Fact]
    public void ApplySkillIsThiscallOnHost()
    {
        // Персонажа вызов находит сам, свежим (раньше 1.3.6 брал его из снимка)
        PutHost(0x1FA1F868);

        Caller().ApplySkill(299, 0);

        Assert.Equal(StubBuilder.Call(Address(GameFunctions.HostApplySkill), CallingConvention.Thiscall, 0x1FA1F868, [299, 0, 0, 0xFFFFFFFF]),
            _runner.Runs.Single().Stub);
    }

    [Fact]
    public void MacroIsStartedLikePanelButton()
    {
        // Как кнопка макроса на панели: у персонажа, номер с 0, повтор не пропускать, «атаковать силой» — как в настройках (-1)
        var comeback = new ProfileCatalog().Load("comeback136").Data;
        foreach (var function in comeback.Functions.Values)
            _memory.WriteBytes(ModuleBase + function.Rva, CodeFor(function.Signature!));
        const uint basePtr = 0x1000_0000, game = 0x1001_0000, host = 0x1FA1F868;
        _memory.WriteUInt32(BasePointer, basePtr);
        _memory.WriteUInt32(basePtr + comeback.Base.Game, game);
        _memory.WriteUInt32(game + comeback.Host.Struct, host);
        var caller = Caller(comeback);

        var result = caller.ApplyMacro(1);

        Assert.True(result.Ok, result.Details);
        var address = ModuleBase + comeback.Functions[GameFunctions.HostApplyComboSkill].Rva;
        Assert.Equal(StubBuilder.Call(address, CallingConvention.Thiscall, host, [0, 0, 0xFFFFFFFF]), _runner.Runs.Single().Stub);
        Assert.Contains("вне 1..8", caller.ApplyMacro(9).Details);
        Assert.Single(_runner.Runs);
    }

    [Fact]
    public void ThiscallWithoutHostIsRefused()
    {
        var result = Caller().ApplySkill(299, 0);

        Assert.False(result.Ok);
        Assert.Contains("не в мире", result.Details);
        Assert.Empty(_runner.Runs);
    }

    [Fact]
    public void ThiscallWithoutThisInProfileIsRefused()
    {
        var apply = Profile.Functions[GameFunctions.HostApplySkill];
        var profile = new ProfileData
        {
            Functions = new() { [GameFunctions.HostApplySkill] = new GameFunction { Rva = apply.Rva, Signature = apply.Signature, Convention = CallingConvention.Thiscall } },
        };

        var result = Caller(profile).ApplySkill(299, 0);

        Assert.False(result.Ok);
        Assert.Contains("this", result.Details);
        Assert.Empty(_runner.Runs);
    }

    [Fact]
    public void CastSkillPassesTargetsThroughData()
    {
        PutHost();

        Caller().CastSkill(330, 0x80116200);

        var run = _runner.Runs.Single();
        Assert.Equal(BitConverter.GetBytes(0x80116200u), run.Data);
        Assert.Equal(StubBuilder.Call(Address(GameFunctions.CastSkill), CallingConvention.Thiscall, Session, [330, 0, 1, DataAddress]), run.Stub);
    }

    [Fact]
    public void CastSkillWithoutTargetHasZeroCount()
    {
        PutHost();

        Caller().CastSkill(329, 0);

        Assert.Equal(StubBuilder.Call(Address(GameFunctions.CastSkill), CallingConvention.Thiscall, Session, [329, 0, 0, DataAddress]),
            _runner.Runs.Single().Stub);
    }

    [Fact]
    public void PetAttackSendsCommandOneWithPvpByte()
    {
        PutHost();

        Caller().PetAttack(0x8010429C);

        var run = _runner.Runs.Single();
        Assert.Equal([0], run.Data);
        Assert.Equal(StubBuilder.Call(Address(GameFunctions.PetCtrl), CallingConvention.Thiscall, Session, [0x8010429C, 1, DataAddress, 1]), run.Stub);
    }

    [Theory]
    [InlineData(1, 0u)]
    [InlineData(10, 9u)]
    public void SummonPetUsesZeroBasedCage(int cage, uint index)
    {
        PutHost();

        Caller().SummonPet(cage);

        Assert.Equal(StubBuilder.Call(Address(GameFunctions.SummonPet), CallingConvention.Thiscall, Host, [index]), _runner.Runs.Single().Stub);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public void SummonPetOutsideCagesIsRefused(int cage)
    {
        Assert.False(Caller().SummonPet(cage).Ok);
        Assert.Empty(_runner.Runs);
    }

    [Fact]
    public void UseItemFromBag()
    {
        Caller().UseItem(new InventoryItem(2, 8618, 0, 1, null, null));

        // Где (0 — сумка) и ячейка — в регистрах, как у клиента
        Assert.Equal(StubBuilder.Call(Address(GameFunctions.UseItem), CallingConvention.Cdecl, 0, [8618, 1], ecx: 0, edx: 2), _runner.Runs.Single().Stub);
    }

    [Fact]
    public void MoveToBuildsThreeCallsWithPoint()
    {
        const uint host = 0x1FA1F868, workMan = 0x2222_0000;
        PutHost(host);
        _memory.WriteUInt32(host + Profile.Host.WorkMan, workMan);

        var result = Caller().MoveTo(new Position(-1800f, 220f, -110.5f), smart: false);

        Assert.True(result.Ok, result.Details);
        var run = _runner.Runs.Single();
        Assert.Equal(BitConverter.GetBytes(-1800f).Concat(BitConverter.GetBytes(220f)).Concat(BitConverter.GetBytes(-110.5f)), run.Data!);
        Assert.Equal(StubBuilder.MoveTo(workMan, Address(GameFunctions.WorkCreate), Address(GameFunctions.WorkMoveSetDestination),
            [0, DataAddress], Address(GameFunctions.WorkStart), [1, null, 0]), run.Stub);
    }

    [Fact]
    public void MoveToTakesStartArgsFromProfile()
    {
        // Comeback 1.4.6: SetDestination(5, &point) — автопуть, StartWork(1, work, 0) — на аргумент меньше, чем в 1.3.6
        const uint host = 0x1FA1F868, workMan = 0x2222_0000;
        PutHost(host);
        _memory.WriteUInt32(host + Profile.Host.WorkMan, workMan);
        var start = Profile.Functions[GameFunctions.WorkStart];
        var destination = Profile.Functions[GameFunctions.WorkMoveSetDestination];
        var profile = new ProfileData
        {
            Base = Profile.Base,
            Host = Profile.Host,
            Functions = new(Profile.Functions)
            {
                [GameFunctions.WorkStart] = new GameFunction { Rva = start.Rva, Signature = start.Signature, Convention = start.Convention,
                    Args = ["1", "work", "0"] },
                [GameFunctions.WorkMoveSetDestination] = new GameFunction { Rva = destination.Rva, Signature = destination.Signature,
                    Convention = destination.Convention, Args = ["type", "data"] },
            },
            MoveTypes = new MoveTypes { Direct = 0, Smart = 5 },
        };

        var result = Caller(profile).MoveTo(new Position(1f, 2f, 3f), smart: true);

        Assert.True(result.Ok, result.Details);
        Assert.Equal(StubBuilder.MoveTo(workMan, Address(GameFunctions.WorkCreate), Address(GameFunctions.WorkMoveSetDestination),
            [5, DataAddress], Address(GameFunctions.WorkStart), [1, null, 0]), _runner.Runs.Single().Stub);
    }

    [Fact]
    public void MoveToWithoutSmartPathGoesDirect()
    {
        // Автопуть у сервера не найден — просьба «умно» бежит по прямой (тип 0)
        const uint host = 0x1FA1F868, workMan = 0x2222_0000;
        PutHost(host);
        _memory.WriteUInt32(host + Profile.Host.WorkMan, workMan);
        var profile = new ProfileData { Base = Profile.Base, Host = Profile.Host, Functions = Profile.Functions, MoveTypes = new MoveTypes() };

        Assert.Equal("у сервера нет автопути", Caller(profile).Capabilities.WhyNot(Capability.SmartMove));
        Caller(profile).MoveTo(new Position(1f, 2f, 3f), smart: true);

        Assert.Equal(StubBuilder.MoveTo(workMan, Address(GameFunctions.WorkCreate), Address(GameFunctions.WorkMoveSetDestination),
            [0, DataAddress], Address(GameFunctions.WorkStart), [1, null, 0]), _runner.Runs.Single().Stub);
    }

    // Comeback 1.4.6: «выбрать цель» — метод персонажа (this = перс, stdcall-очистка как у thiscall), «снять цель» — он же с 0
    private ProfileData HostMethodProfile(uint host)
    {
        const uint basePtr = 0x1000_0000, game = 0x1001_0000;
        _memory.WriteUInt32(BasePointer, basePtr);
        _memory.WriteUInt32(basePtr + 0x1C, game);
        _memory.WriteUInt32(game + 0x28, host);
        var select = Profile.Functions[GameFunctions.SelectTarget];
        GameFunction Method(params string[] args) => new() { Rva = select.Rva, Signature = select.Signature, Convention = CallingConvention.Thiscall,
            This = FunctionThis.Host, Args = [.. args] };
        return new ProfileData
        {
            Base = new BaseOffsets { Game = 0x1C },
            Host = new HostOffsets { Struct = 0x28 },
            Functions = new() { [GameFunctions.SelectTarget] = Method("wid"), [GameFunctions.Unselect] = Method("0") },
        };
    }

    [Fact]
    public void HostMethodGetsHostFromMemory()
    {
        const uint host = 0x2F37_2008;

        var result = Caller(HostMethodProfile(host)).SelectTarget(0x801040C0);

        Assert.True(result.Ok, result.Details);
        Assert.Equal(StubBuilder.Call(Address(GameFunctions.SelectTarget), CallingConvention.Thiscall, host, [0x801040C0]), _runner.Runs.Single().Stub);
    }

    [Fact]
    public void ArgsFromProfileCanBeNumbers()
    {
        const uint host = 0x2F37_2008;

        Caller(HostMethodProfile(host)).Unselect();

        Assert.Equal(StubBuilder.Call(Address(GameFunctions.SelectTarget), CallingConvention.Thiscall, host, [0]), _runner.Runs.Single().Stub);
    }

    [Fact]
    public void HostMethodWithoutHostIsRefused()
    {
        var result = Caller(HostMethodProfile(0)).SelectTarget(1);

        Assert.False(result.Ok);
        Assert.Contains("не в мире", result.Details);
        Assert.Empty(_runner.Runs);
    }

    [Theory]
    [InlineData("pwclassic136")]
    [InlineData("comeback136")]
    [InlineData("comeback146")]
    public void ShippedProfilesDescribeEveryFunctionCorrectly(string id)
    {
        // Функции на своих местах — значит, всё, что осталось в Problems, — ошибка описания в профиле (её видно при «Старт»)
        var profile = new ProfileCatalog().Load(id).Data;
        var memory = new MemoryImage();
        foreach (var function in profile.Functions.Values)
            memory.WriteBytes(ModuleBase + function.Rva, CodeFor(function.Signature!));

        Assert.Empty(new GameCaller(memory, _runner, ModuleBase, new GameRoots(memory, BasePointer, profile), profile).Problems);
    }

    [Fact]
    public void ProfileMistakeIsKnownBeforeAnyCall()
    {
        var select = Profile.Functions[GameFunctions.SelectTarget];
        var profile = new ProfileData
        {
            Functions = new() { [GameFunctions.SelectTarget] = new GameFunction { Rva = select.Rva, Signature = select.Signature, Args = ["wid", "опечатка"] } },
        };

        var caller = Caller(profile);

        Assert.False(caller.Can(GameFunctions.SelectTarget));
        var problem = Assert.Single(caller.Problems);
        Assert.Equal(GameFunctions.SelectTarget, problem.Name);
        Assert.Contains("опечатка", problem.Why);
    }

    [Fact]
    public void UnknownArgInProfileIsRefused()
    {
        var select = Profile.Functions[GameFunctions.SelectTarget];
        var profile = new ProfileData
        {
            Functions = new() { [GameFunctions.SelectTarget] = new GameFunction { Rva = select.Rva, Signature = select.Signature, Args = ["wid", "опечатка"] } },
        };

        var result = Caller(profile).SelectTarget(1);

        Assert.False(result.Ok);
        Assert.Contains("опечатка", result.Details);
        Assert.Empty(_runner.Runs);
    }

    [Fact]
    public void RegistersFromProfile()
    {
        var useItem = Profile.Functions[GameFunctions.UseItem];
        var profile = new ProfileData
        {
            Functions = new()
            {
                [GameFunctions.UseItem] = new GameFunction
                {
                    Rva = useItem.Rva, Signature = useItem.Signature, Convention = CallingConvention.Cdecl,
                    Args = ["tid", "count"], Registers = new() { ["ecx"] = "where", ["edx"] = "slot" },
                },
            },
        };

        Caller(profile).UseItem(new InventoryItem(1, 8647, 0, 1, null, null));

        Assert.Equal(StubBuilder.Call(Address(GameFunctions.UseItem), CallingConvention.Cdecl, 0, [8647, 1], ecx: 0, edx: 1),
            _runner.Runs.Single().Stub);
    }

    [Fact]
    public void UnknownRegisterIsRefused()
    {
        var useItem = Profile.Functions[GameFunctions.UseItem];
        var profile = new ProfileData
        {
            Functions = new()
            {
                [GameFunctions.UseItem] = new GameFunction { Rva = useItem.Rva, Signature = useItem.Signature, Registers = new() { ["esi"] = "slot" } },
            },
        };

        var result = Caller(profile).UseItem(new InventoryItem(1, 8647, 0, 1, null, null));

        Assert.False(result.Ok);
        Assert.Contains("esi", result.Details);
        Assert.Empty(_runner.Runs);
    }

    [Fact]
    public void SessionMethodGetsSessionFromMemory()
    {
        const uint basePtr = 0x1000_0000, session = 0x3300_0000;
        _memory.WriteUInt32(BasePointer, basePtr);
        _memory.WriteUInt32(basePtr + 0x20, session);
        var cast = Profile.Functions[GameFunctions.CastSkill];
        var profile = new ProfileData
        {
            Base = new BaseOffsets { Session = 0x20 },
            Functions = new()
            {
                [GameFunctions.CastSkill] = new GameFunction
                {
                    Rva = cast.Rva, Signature = cast.Signature, Convention = CallingConvention.Thiscall, This = FunctionThis.Session,
                },
            },
        };

        Caller(profile).CastSkill(330, 0x80116200);

        Assert.Equal(StubBuilder.Call(Address(GameFunctions.CastSkill), CallingConvention.Thiscall, session, [330, 0, 1, DataAddress]),
            _runner.Runs.Single().Stub);
    }

    [Fact]
    public void SessionMethodWithoutSessionOffsetIsRefused()
    {
        var cast = Profile.Functions[GameFunctions.CastSkill];
        var profile = new ProfileData
        {
            Functions = new() { [GameFunctions.CastSkill] = new GameFunction { Rva = cast.Rva, Signature = cast.Signature, This = FunctionThis.Session } },
        };

        Assert.False(Caller(profile).CastSkill(330, 1).Ok);
        Assert.Empty(_runner.Runs);
    }

    [Fact]
    public void RunnerTimeoutIsReported()
    {
        _runner.Result = new RemoteRunResult(RemoteRunStatus.Timeout, "поток в игре не закончился за 5 с");
        PutHost();

        var result = Caller().Unselect();

        Assert.False(result.Ok);
        Assert.Contains("5 с", result.Details);
    }
}
