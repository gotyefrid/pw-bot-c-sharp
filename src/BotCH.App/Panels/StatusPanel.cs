using System;
using System.Linq;
using BotCH.App.Mvvm;
using BotCH.Core.World;

namespace BotCH.App.Panels;

/// <summary>Состояние на вкладке «Бот»: персонаж (HP, MP), цель, пет и коротко о снимке.</summary>
public sealed class StatusPanel : ObservableObject
{
    private string _hostName = "—";
    public string HostName { get => _hostName; private set => SetProperty(ref _hostName, value); }

    private string _hostDetails = "";
    public string HostDetails { get => _hostDetails; private set => SetProperty(ref _hostDetails, value); }

    private double _hpPercent;
    public double HpPercent { get => _hpPercent; private set => SetProperty(ref _hpPercent, value); }

    private string _hpText = "—";
    public string HpText { get => _hpText; private set => SetProperty(ref _hpText, value); }

    private double _mpPercent;
    public double MpPercent { get => _mpPercent; private set => SetProperty(ref _mpPercent, value); }

    private string _mpText = "—";
    public string MpText { get => _mpText; private set => SetProperty(ref _mpText, value); }

    private bool _hasTarget;
    public bool HasTarget { get => _hasTarget; private set => SetProperty(ref _hasTarget, value); }

    private string _targetName = "Нет цели";
    public string TargetName { get => _targetName; private set => SetProperty(ref _targetName, value); }

    private string _targetDetails = "";
    public string TargetDetails { get => _targetDetails; private set => SetProperty(ref _targetDetails, value); }

    private bool _hasPet;
    public bool HasPet { get => _hasPet; private set => SetProperty(ref _hasPet, value); }

    private string _petTitle = "Пета нет";
    public string PetTitle { get => _petTitle; private set => SetProperty(ref _petTitle, value); }

    private double _petHpPercent;
    public double PetHpPercent { get => _petHpPercent; private set => SetProperty(ref _petHpPercent, value); }

    private string _petHpText = "";
    public string PetHpText { get => _petHpText; private set => SetProperty(ref _petHpText, value); }

    private string _petDetails = "";
    public string PetDetails { get => _petDetails; private set => SetProperty(ref _petDetails, value); }

    private string _snapshotInfo = "";

    /// <summary>Коротко о снимке («мобов рядом …») или почему мир не читается.</summary>
    public string SnapshotInfo { get => _snapshotInfo; set => SetProperty(ref _snapshotInfo, value); }

    public void Show(WorldState w)
    {
        var h = w.Host;
        HostName = h.Name;
        HostDetails = $"ур. {h.Level}" + (h.IsCasting ? " · кастует" : "") + (h.IsDead ? " · мёртв" : "");
        HpPercent = Percent(h.Hp, h.MaxHp);
        HpText = $"{h.Hp} / {h.MaxHp}";
        MpPercent = h.MaxMp is int maxMp ? Percent(h.Mp, maxMp) : 100;
        MpText = h.MaxMp is null ? $"{h.Mp}" : $"{h.Mp} / {h.MaxMp}";

        var target = w.Target;
        HasTarget = target is not null;
        TargetName = target?.Name ?? (h.TargetWid == 0 ? "Нет цели" : "Цель вне списка мобов");
        TargetDetails = target is null ? "" : $"HP {target.Hp} · {target.Offset} · {StateText(target, w)}";

        var pet = w.Pet;
        var active = pet?.ActiveCage is int cage ? pet.InCage(cage) : null;
        HasPet = active is not null;
        PetTitle = pet is null ? "Пета нет" : active is null ? "Пет не призван" : $"Пет · клетка {active.Cage}";
        PetHpPercent = active?.HpPercent ?? 0;
        PetHpText = active is null ? "" : $"{active.HpPercent} %";
        PetDetails = active is null ? "" : active.IsHungry ? "голоден" : "сыт";

        SnapshotInfo = $"мобов рядом {w.Mobs.Count(m => !m.IsDead)} · лута {w.GroundItems.Count}";
    }

    // Мир не читается (другой сервер, загрузка, отключились) — старые HP/MP/пет не показываем, как будто они верные
    public void Clear()
    {
        HostName = "—";
        HostDetails = "";
        HpPercent = 0;
        HpText = "—";
        MpPercent = 0;
        MpText = "—";
        HasTarget = false;
        TargetName = "Нет цели";
        TargetDetails = "";
        HasPet = false;
        PetTitle = "Пета нет";
        PetHpPercent = 0;
        PetHpText = "";
        PetDetails = "";
        SnapshotInfo = "";
    }

    private static string StateText(NpcInfo n, WorldState w)
    {
        var state = n.State.Text() ?? "";
        if (n.TargetWid != 0 && n.TargetWid == w.Host.Wid)
            state += ", бьёт вас";
        else if (n.TargetWid != 0 && n.TargetWid == w.Pet?.ActiveWid)
            state += ", бьёт пета";
        return state;
    }

    private static double Percent(int value, int max) => max <= 0 ? 0 : Math.Max(0, Math.Min(100, value * 100.0 / max));
}
