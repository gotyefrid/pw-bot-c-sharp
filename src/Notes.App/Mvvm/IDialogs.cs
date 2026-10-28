namespace Notes.App.Mvvm;

/// <summary>
/// Вопросы и сообщения пользователю — окно показывает MessageBox и окна «Открыть»/«Сохранить как». Модели получают
/// их в конструкторе: без диалогов модель не создать, и вопрос не уйдёт в молчаливую заглушку.
/// </summary>
public interface IDialogs
{
    /// <summary>Вопрос «да/нет».</summary>
    bool AskYesNo(string text);

    /// <summary>Сообщить об ошибке.</summary>
    void Tell(string text);

    /// <summary>Окно «Открыть»; null — передумали.</summary>
    /// <param name="filter">Как у окон Windows: «Описание (*.ext)|*.ext|…».</param>
    string? AskOpenPath(string title, string filter);

    /// <summary>Окно «Сохранить как» с предложенным именем файла; null — передумали.</summary>
    string? AskSavePath(string title, string filter, string fileName);
}
