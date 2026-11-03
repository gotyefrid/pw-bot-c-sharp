using System.Windows.Controls;

namespace Notes.App;

/// <summary>Выбор сохранённой точки с кнопками «добавить», «изменить», «удалить» (модель — <see cref="Panels.PointPicker"/>).</summary>
public partial class PointPickerView : UserControl
{
    public PointPickerView() => InitializeComponent();
}
