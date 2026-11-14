using System.Windows;
using Notes.App.Panels;

namespace Notes.App;

/// <summary>Окно правки строки связки скиллов: скилл, условия, «ждать», как часто. «Отмена» ничего не меняет.</summary>
public partial class ComboRowWindow : Window
{
    public ComboRowWindow() => InitializeComponent();

    private void Save(object sender, RoutedEventArgs e)
    {
        if (DataContext is ComboRowDraft draft && draft.Save())
            DialogResult = true;
    }
}
