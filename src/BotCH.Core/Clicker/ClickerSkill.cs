namespace BotCH.Core.Clicker;

/// <summary>Скилл, выбранный в шаге кликера (действие «Использовать скилл», условие «Скилл на перезарядке»): как его называть.</summary>
public static class ClickerSkill
{
    /// <summary>«Огненный шар» — по названию, запомненному при выборе; названия нет — «скилл 123».</summary>
    public static string Title(int id, string name) => name.Length > 0 ? $"«{name}»" : $"скилл {id}";
}
