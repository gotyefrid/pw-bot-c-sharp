using System;
using System.Collections.Generic;
using System.Linq;
using BotCH.Core.Calls;
using BotCH.Core.Memory;
using BotCH.Core.Profiles;
using Xunit;

namespace BotCH.Tests.Calls;

public class GameCallerTests
{
    private const uint ModuleBase = 0x400000;
    private const uint DataAddress = 0x7000_0100;
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

    private GameCaller Caller(ProfileData? profile = null) => new(_memory, _runner, ModuleBase, profile ?? Profile);

    private static uint Address(string name) => ModuleBase + Profile.Functions[name].Rva;

    [Fact]
    public void SelectTargetRunsCdeclStubAtFunctionAddress()
    {
        var result = Caller().SelectTarget(0x80104298);

        Assert.True(result.Ok, result.Details);
        var run = Assert.Single(_runner.Runs);
        Assert.Null(run.Data);
        Assert.Equal(StubBuilder.Call(Address(GameFunctions.SelectTarget), CallingConvention.Cdecl, 0, [0x80104298]), run.Stub);
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
    public void ForbiddenAddressIsNeverCalled()
    {
        // Ошибка в профиле: «выбор цели» указывает на адрес выхода из игры
        var logout = Profile.ForbiddenFunctions["logout"];
        var select = Profile.Functions[GameFunctions.SelectTarget];
        _memory.WriteBytes(ModuleBase + logout, CodeFor(select.Signature!));
        var broken = new ProfileData
        {
            Functions = new() { [GameFunctions.SelectTarget] = new GameFunction { Rva = logout, Signature = select.Signature, Convention = select.Convention } },
            ForbiddenFunctions = Profile.ForbiddenFunctions,
        };

        var caller = Caller(broken);
        var result = caller.SelectTarget(1);

        Assert.False(caller.Can(GameFunctions.SelectTarget));
        Assert.False(result.Ok);
        Assert.Contains("запрещённых", result.Details);
        Assert.Empty(_runner.Runs);
    }

    [Fact]
    public void ProfileForbidsLogoutAndReleasePet()
    {
        Assert.Equal(0x1F0A10u, Profile.ForbiddenFunctions["logout"]);
        Assert.Equal(0x1F1FC0u, Profile.ForbiddenFunctions["releasePet"]);
        Assert.DoesNotContain(Profile.Functions.Values, f => Profile.ForbiddenFunctions.ContainsValue(f.Rva));
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
        Caller().ApplySkill(0x1FA1F868, 299);

        Assert.Equal(StubBuilder.Call(Address(GameFunctions.HostApplySkill), CallingConvention.Thiscall, 0x1FA1F868, [299, 0, 0, 0xFFFFFFFF]),
            _runner.Runs.Single().Stub);
    }

    [Fact]
    public void ThiscallWithoutHostIsRefused()
    {
        Assert.False(Caller().ApplySkill(0, 299).Ok);
        Assert.Empty(_runner.Runs);
    }

    [Fact]
    public void CastSkillPassesTargetsThroughData()
    {
        Caller().CastSkill(330, 0x80116200);

        var run = _runner.Runs.Single();
        Assert.Equal(BitConverter.GetBytes(0x80116200u), run.Data);
        Assert.Equal(StubBuilder.Call(Address(GameFunctions.CastSkill), CallingConvention.Cdecl, 0, [330, 0, 1, DataAddress]), run.Stub);
    }

    [Fact]
    public void CastSkillWithoutTargetHasZeroCount()
    {
        Caller().CastSkill(329, 0);

        Assert.Equal(StubBuilder.Call(Address(GameFunctions.CastSkill), CallingConvention.Cdecl, 0, [329, 0, 0, DataAddress]), _runner.Runs.Single().Stub);
    }

    [Fact]
    public void PetAttackSendsCommandOneWithPvpByte()
    {
        Caller().PetAttack(0x8010429C);

        var run = _runner.Runs.Single();
        Assert.Equal([0], run.Data);
        Assert.Equal(StubBuilder.Call(Address(GameFunctions.PetCtrl), CallingConvention.Cdecl, 0, [0x8010429C, 1, DataAddress, 1]), run.Stub);
    }

    [Theory]
    [InlineData(1, 0u)]
    [InlineData(10, 9u)]
    public void SummonPetUsesZeroBasedCage(int cage, uint index)
    {
        Caller().SummonPet(cage);

        Assert.Equal(StubBuilder.Call(Address(GameFunctions.SummonPet), CallingConvention.Cdecl, 0, [index]), _runner.Runs.Single().Stub);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void SummonPetOutsideCagesIsRefused(int cage)
    {
        Assert.False(Caller().SummonPet(cage).Ok);
        Assert.Empty(_runner.Runs);
    }

    [Fact]
    public void UseItemFromBag()
    {
        Caller().UseItem(2, 8618);

        Assert.Equal(StubBuilder.Call(Address(GameFunctions.UseItem), CallingConvention.Cdecl, 0, [0, 2, 8618, 1]), _runner.Runs.Single().Stub);
    }

    [Fact]
    public void MoveToBuildsThreeCallsWithPoint()
    {
        const uint host = 0x1FA1F868, workMan = 0x2222_0000;
        _memory.WriteUInt32(host + Profile.Host.WorkMan, workMan);

        var result = Caller().MoveTo(host, -1800f, 220f, -110.5f);

        Assert.True(result.Ok, result.Details);
        var run = _runner.Runs.Single();
        Assert.Equal(BitConverter.GetBytes(-1800f).Concat(BitConverter.GetBytes(220f)).Concat(BitConverter.GetBytes(-110.5f)), run.Data!);
        Assert.Equal(StubBuilder.MoveTo(workMan, Address(GameFunctions.WorkCreate), Address(GameFunctions.WorkMoveSetDestination),
            Address(GameFunctions.WorkStart), DataAddress, 0), run.Stub);
    }

    // Comeback 1.4.6: «выбрать цель» — метод персонажа (this = перс, stdcall-очистка как у thiscall), «снять цель» — он же с 0
    private ProfileData HostMethodProfile(uint host)
    {
        const uint basePtr = 0x1000_0000, game = 0x1001_0000;
        _memory.WriteUInt32(ModuleBase + 0x100, basePtr);
        _memory.WriteUInt32(basePtr + 0x1C, game);
        _memory.WriteUInt32(game + 0x28, host);
        var select = Profile.Functions[GameFunctions.SelectTarget];
        GameFunction Method(params string[] args) => new() { Rva = select.Rva, Signature = select.Signature, Convention = CallingConvention.Thiscall,
            This = FunctionThis.Host, Args = [.. args] };
        return new ProfileData
        {
            Base = new BaseOffsets { BasePointer = 0x100, Game = 0x1C },
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

        Caller(profile).UseItem(1, 8647);

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

        var result = Caller(profile).UseItem(1, 8647);

        Assert.False(result.Ok);
        Assert.Contains("esi", result.Details);
        Assert.Empty(_runner.Runs);
    }

    [Fact]
    public void RunnerTimeoutIsReported()
    {
        _runner.Result = new RemoteRunResult(RemoteRunStatus.Timeout, "поток в игре не закончился за 5 с");

        var result = Caller().Unselect();

        Assert.False(result.Ok);
        Assert.Contains("5 с", result.Details);
    }
}
