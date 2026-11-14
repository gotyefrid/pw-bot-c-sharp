using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Notes.App.Mvvm;
using Notes.Core.Logging;
using Notes.Core.Profiles;
using Notes.Core.Resources;
using Notes.Core.Settings;
using Notes.Core.World;

namespace Notes.App.Panels;

/// <summary>
/// Окно «Связка скиллов»: строки по порядку — скилл, условия, «ждать», как часто. Бот смотрит строки сверху вниз и жмёт первую
/// подходящую. Связки — общие у всех персонажей и копий бота (<see cref="ComboStore"/>), как маршруты: у персонажа только
/// какая выбрана, а её строки панель кладёт в настройки (по ним бьёт бот). Строка правится в своём окне
/// (<see cref="ComboRowDraft"/>) — двойным щелчком или карандашом. Правка — сразу в общий файл; файл поменяла другая копия —
/// строки перечитываются. О правке говорит модели окна (та сохраняет и отдаёт боту).
/// </summary>
public sealed class ComboPanel : ObservableObject, IDisposable
{
    // Файл поменяли — перечитать не сразу: запись другой копии — это несколько событий подряд
    private static readonly TimeSpan ReloadDelay = TimeSpan.FromMilliseconds(300);

    private readonly Func<BotSettings> _settings;
    private readonly Func<WorldState?> _world;
    private readonly Func<ServerProfile> _profile;
    private readonly Func<int, ImageSource?> _icon;
    private readonly ILogger _log;
    private readonly Action _edited;
    private readonly ComboStore _store;
    private readonly FileSystemWatcher? _watcher;
    private readonly DispatcherTimer _reload = new() { Interval = ReloadDelay };
    private ComboLine? _selectedRow;
    private ComboRowDraft? _editing;
    private bool _syncing;

    /// <param name="settings">Настройки текущего персонажа (меняются при смене персонажа).</param>
    /// <param name="world">Последний снимок; null — мир не читается (скиллы — только из строк, без иконок).</param>
    /// <param name="profile">Профиль сервера: какие скиллы не атакующие.</param>
    /// <param name="icon">Картинка иконки скилла; null — нет.</param>
    /// <param name="edited">Правка настроек из этого окна.</param>
    /// <param name="store">Общий файл связок.</param>
    public ComboPanel(Func<BotSettings> settings, Func<WorldState?> world, Func<ServerProfile> profile, Func<int, ImageSource?> icon,
        ILogger log, Action edited, ComboStore store, IDialogs dialogs)
    {
        _settings = settings;
        _world = world;
        _profile = profile;
        _icon = icon;
        _log = log;
        _edited = edited;
        _store = store;
        Dialogs = dialogs;
        AddComboCommand = new RelayCommand(AddCombo);
        CopyComboCommand = new RelayCommand(CopyCombo, () => SelectedCombo is not null);
        RenameComboCommand = new RelayCommand(RenameCombo, () => SelectedCombo is not null);
        RemoveComboCommand = new RelayCommand(RemoveCombo, () => SelectedCombo is not null);
        AddRowCommand = new RelayCommand(AddRow, () => SkillChoices.Count > 0);
        EditRowCommand = new RelayCommand(EditSelected, () => SelectedRow is not null);
        RemoveRowCommand = new RelayCommand(RemoveRow, () => SelectedRow is not null);
        RowUpCommand = new RelayCommand(() => MoveRow(-1), () => SelectedRow is { } r && Rows.IndexOf(r) > 0);
        RowDownCommand = new RelayCommand(() => MoveRow(1), () => SelectedRow is { } r && Rows.IndexOf(r) is var i && i >= 0 && i < Rows.Count - 1);
        _reload.Tick += (_, _) =>
        {
            _reload.Stop();
            Reload();
        };
        if (Path.GetDirectoryName(store.Path) is { Length: > 0 } folder && Directory.Exists(folder))
        {
            _watcher = new FileSystemWatcher(folder, Path.GetFileName(store.Path))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            };
            FileSystemEventHandler changed = (_, _) => UiThread.Run(() =>
            {
                _reload.Stop();
                _reload.Start();
            });
            _watcher.Changed += changed;
            _watcher.Created += changed;
            _watcher.Renamed += (o, e) => changed(o, e);
            _watcher.EnableRaisingEvents = true;
        }
    }

    private CombatSettings Combat => _settings().Combat;

    /// <summary>Вопросы игроку. Пока открыто окно связок — его диалоги (поверх него), потом — снова окна бота.</summary>
    public IDialogs Dialogs { get; set; }

    /// <summary>Название связки: окну спросить у игрока (заголовок, текущее название); null — передумал.</summary>
    public Func<string, string, string?>? AskName { get; set; }

    /// <summary>Окно строки: показать черновик поверх окна связок (пока оно открыто); null — окна связок нет.</summary>
    public Action<ComboRowDraft>? EditRequested { get; set; }

    // ── Связки ───────────────────────────────────────────────────────────────

    /// <summary>Сохранённые связки (общие у всех персонажей) по названию.</summary>
    public ObservableCollection<ComboOption> Combos { get; } = new();

    public ICommand AddComboCommand { get; }
    public ICommand CopyComboCommand { get; }
    public ICommand RenameComboCommand { get; }
    public ICommand RemoveComboCommand { get; }

    // Пока список пересобирается — null, как у маршрутов: иначе окно потеряет выбор
    public ComboOption? SelectedCombo
    {
        get => _syncing ? null : Combos.FirstOrDefault(r => r.Id == Combat.ComboId);
        set
        {
            if (_syncing || value is null || value.Id == Combat.ComboId)
                return;
            Choose(value.Id);
        }
    }

    // ── Строки ───────────────────────────────────────────────────────────────

    /// <summary>Строки выбранной связки по порядку.</summary>
    public ObservableCollection<ComboLine> Rows { get; } = new();

    public ICommand AddRowCommand { get; }
    public ICommand EditRowCommand { get; }
    public ICommand RemoveRowCommand { get; }
    public ICommand RowUpCommand { get; }
    public ICommand RowDownCommand { get; }

    public ComboLine? SelectedRow { get => _selectedRow; set => SetProperty(ref _selectedRow, value); }

    /// <summary>Скиллы для строки: атакующие изученные (как в «Атаке», без макросов), а скиллы строк, которых нет, — с пометкой.</summary>
    public ObservableCollection<ComboSkillChoice> SkillChoices { get; } = new();

    /// <summary>Скиллы для проверок «скилл готов» и др.: все изученные.</summary>
    public ObservableCollection<ComboSkillChoice> ConditionSkills { get; } = new();

    // ── Загрузка и сверка с файлом ───────────────────────────────────────────

    /// <summary>
    /// Связка в окне ← настройки персонажа и общий файл связок (при запуске и смене персонажа). Выбранную поменяли или удалили
    /// в другой копии — берём, как в файле.
    /// </summary>
    public void Load()
    {
        SyncWithStore();
        LoadCombos();
        LoadRows();
    }

    /// <summary>Новый снимок: изученные скиллы и иконки (пересобираются, только если что-то поменялось).</summary>
    public void Observe() => LoadSkills();

    // Файл связок поменялся (мы сами или другая копия): перерисовать, только если что-то другое
    private void Reload()
    {
        var sync = SyncWithStore();
        var combos = _store.All();
        var same = combos.Select(r => (r.Id, r.Name, r.Rows.Count)).SequenceEqual(Combos.Select(r => (r.Id, r.Name, r.Count)));
        if (sync == ComboSync.Unchanged && same)
            return;
        if (!same)
            LoadCombos(combos);
        if (sync != ComboSync.Unchanged)
            LoadRows();
    }

    // Строки персонажа ← файл; поменялись — сохранить персонажа и отдать боту
    private ComboSync SyncWithStore()
    {
        var combat = Combat;
        var sync = _store.Sync(combat);
        if (_store.Problem is { } problem)
            _log.Warning(problem);
        switch (sync)
        {
            case ComboSync.Loaded:
                _log.Info($"Связка скиллов «{_store.Find(combat.ComboId)?.Name}» изменена в другой копии бота — строки обновлены");
                break;
            case ComboSync.Restored:
                _log.Info($"Связки скиллов «{_store.Find(combat.ComboId)?.Name}» не было в файле связок — вернул её из строк персонажа");
                break;
            case ComboSync.Deleted:
                _log.Info("Выбранная связка скиллов удалена — связка не выбрана");
                break;
        }
        if (sync != ComboSync.Unchanged)
            _edited();
        return sync;
    }

    private void LoadCombos(IReadOnlyList<SavedCombo>? combos = null)
    {
        _syncing = true;
        try
        {
            Combos.Clear();
            foreach (var combo in combos ?? _store.All())
                Combos.Add(new ComboOption(combo.Id, combo.Name, combo.Rows.Count));
        }
        finally
        {
            _syncing = false;
        }
        OnPropertyChanged(nameof(SelectedCombo));
    }

    // Строки в окне ← строки персонажа; выбор остаётся на том же месте
    private void LoadRows(int? select = null)
    {
        var at = select ?? (SelectedRow is { } selected ? Rows.IndexOf(selected) : -1);
        Rows.Clear();
        var rows = Combat.Combo;
        for (var i = 0; i < rows.Count; i++)
            Rows.Add(new ComboLine(i + 1, rows[i], () => RowsEdited()));
        LoadSkills(force: true);
        SelectedRow = at >= 0 && at < Rows.Count ? Rows[at] : null;
    }

    // Списки скиллов ← снимок и строки; пересобраны — перечитать выбор в окне
    private void LoadSkills(bool force = false)
    {
        var world = _world();
        var rows = Combat.Combo;
        var notAttack = _profile().Data.Skills.NotAttack;
        var attack = Choices(ComboEditing.Skills(world?.Skills, rows, id => !notAttack.Contains(id)), world is not null);
        var all = Choices(ComboEditing.Skills(world?.Skills, rows), world is not null);
        var changed = Replace(SkillChoices, attack) | Replace(ConditionSkills, all);
        if (!changed && !force)
            return;

        foreach (var line in Rows)
            line.Show(SkillChoices.FirstOrDefault(s => s.Id == line.Row.SkillId));
        _editing?.Reselect();
    }

    private List<ComboSkillChoice> Choices(IEnumerable<ComboSkill> skills, bool inGame)
        => skills.Select(s => new ComboSkillChoice(s.Id, s.Name, inGame ? _icon(s.Id) : null, s.Missing)).ToList();

    private static bool Replace(ObservableCollection<ComboSkillChoice> target, List<ComboSkillChoice> items)
    {
        if (items.SequenceEqual(target))
            return false;
        target.Clear();
        foreach (var item in items)
            target.Add(item);
        return true;
    }

    public void Dispose()
    {
        _reload.Stop();
        _watcher?.Dispose();
    }

    // ── Правка ───────────────────────────────────────────────────────────────

    // Выбрали связку: её строки — персонажу и боту
    private void Choose(string id)
    {
        var combat = Combat;
        var saved = _store.Find(id);
        combat.ComboId = saved?.Id ?? "";
        combat.Combo = saved is null ? [] : ComboStore.Copy(saved.Rows);
        _log.Info(saved is null ? "Связка скиллов не выбрана" : $"Связка скиллов «{saved.Name}»: строк {saved.Rows.Count}");
        LoadCombos();
        LoadRows(select: -1);
        _edited();
    }

    private void AddCombo()
    {
        var combo = _store.Create();
        _log.Info($"Новая связка скиллов «{combo.Name}»");
        Choose(combo.Id);
    }

    private void CopyCombo()
    {
        if (SelectedCombo is not { } source)
            return;
        var combo = _store.Create(Combat.Combo);
        _log.Info($"Связка скиллов «{source.Name}» скопирована в «{combo.Name}»");
        Choose(combo.Id);
    }

    private void RenameCombo()
    {
        if (SelectedCombo is not { } combo || AskName?.Invoke("Название связки", combo.Name) is not { } name)
            return;
        if (name.Trim() == combo.Name)
            return;
        if (!_store.Rename(combo.Id, name))
        {
            Dialogs.Tell(name.Trim().Length == 0 ? "Название не может быть пустым" : $"Связка «{name.Trim()}» уже есть — выберите другое название");
            return;
        }
        _log.Info($"Связка скиллов «{combo.Name}» переименована в «{name.Trim()}»");
        LoadCombos();
    }

    private void RemoveCombo()
    {
        if (SelectedCombo is not { } combo
            || !Dialogs.AskYesNo($"Удалить связку «{combo.Name}» (строк: {combo.Count})?\n\nОна общая — пропадёт у всех персонажей."))
            return;
        _store.Remove(combo.Id);
        _log.Info($"Связка скиллов «{combo.Name}» удалена");
        Choose("");
    }

    // Строки выбранной связки поменяли в окне — в общий файл (связка не выбрана или её удалили в другой копии — новая)
    private void RowsEdited()
    {
        var combat = Combat;
        if (combat.ComboId.Length == 0 || !_store.SaveRows(combat.ComboId, combat.Combo))
        {
            var created = _store.Create(combat.Combo);
            combat.ComboId = created.Id;
            _log.Info($"Строки связки записаны в новую связку «{created.Name}»");
        }
        if (!_store.All().Select(r => (r.Id, r.Name, r.Rows.Count)).SequenceEqual(Combos.Select(r => (r.Id, r.Name, r.Count))))
            LoadCombos();
        _edited();
    }

    /// <summary>
    /// Править выбранную строку в её окне: скилл, условия, «ждать», как часто (двойной щелчок по строке или карандаш).
    /// </summary>
    public void EditSelected()
    {
        if (SelectedRow is not { } line || EditRequested is not { } edit)
            return;

        var row = line.Row;
        var draft = new ComboRowDraft(Rows.IndexOf(line) + 1, row, SkillChoices, ConditionSkills, edited =>
        {
            // Пока окно строки было открыто, связку перечитали из файла (правка в другой копии) — строки уже другие
            if (!Combat.Combo.Contains(row))
                return "Связку только что изменили в другой копии бота — закройте окно и откройте строку заново";
            ComboEditing.Apply(row, edited);
            _log.Info($"Строка {Combat.Combo.IndexOf(row) + 1} связки изменена: {row.Title}, когда {ComboEditing.ConditionsText(row)}, "
                      + $"{ComboEditing.RepeatText(row)}{(row.WaitReady ? ", ждать перезарядку" : "")}");
            // Строки могли пересобраться (файл перечитан), а строка та же: правим её строку в таблице
            RowEdited(Rows.FirstOrDefault(l => l.Row == row) ?? line);
            return null;
        });
        _editing = draft;
        try
        {
            edit(draft);
        }
        finally
        {
            _editing = null;
        }
    }

    // Поменяли поле строки: таблица, списки скиллов (скилл мог стать «не изучен») и файл
    private void RowEdited(ComboLine line)
    {
        line.Refresh();
        RowsEdited();
        LoadSkills();
        line.Show(SkillChoices.FirstOrDefault(s => s.Id == line.Row.SkillId));
    }

    // Новая строка в конце: изученный скилл, которого ещё нет в связке (все есть — первый)
    private void AddRow()
    {
        var used = new HashSet<int>(Combat.Combo.Select(r => r.SkillId));
        var skill = SkillChoices.FirstOrDefault(s => !s.Missing && !used.Contains(s.Id)) ?? SkillChoices.FirstOrDefault();
        if (skill is null)
            return;

        Combat.Combo.Add(new ComboRow { SkillId = skill.Id, SkillName = skill.Name });
        RowsEdited();
        LoadRows(select: Combat.Combo.Count - 1);
    }

    private void RemoveRow()
    {
        if (SelectedRow is not { } line)
            return;
        var at = Rows.IndexOf(line);
        Combat.Combo.Remove(line.Row);
        RowsEdited();
        LoadRows(select: Math.Min(at, Combat.Combo.Count - 1));
    }

    private void MoveRow(int step)
    {
        if (SelectedRow is not { } line)
            return;
        var to = ComboEditing.Move(Combat.Combo, Combat.Combo.IndexOf(line.Row), step);
        if (to < 0)
            return;
        RowsEdited();
        LoadRows(select: to);
    }
}

/// <summary>Связка в выпадающем списке: «Название (N строк)».</summary>
public sealed record ComboOption(string Id, string Name, int Count)
{
    public override string ToString() => $"{Name} ({Count})";
}
