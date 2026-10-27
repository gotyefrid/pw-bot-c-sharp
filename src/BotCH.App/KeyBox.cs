using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BotCH.Core.Actions;

namespace BotCH.App;

/// <summary>
/// Поле «нажмите клавишу»: щёлкнуть и нажать — в поле её имя («F1», «Ctrl+2»), оно же уходит в <see cref="Stroke"/>.
/// Ctrl/Alt/Shift сами по себе не берутся — ждём клавишу с ними. Alt+F4 и другие сочетания Windows поле не перехватывает.
/// </summary>
public sealed class KeyBox : TextBox
{
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(string), typeof(KeyBox),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, e) => ((KeyBox)d).Text = e.NewValue as string ?? ""));

    public KeyBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        IsUndoEnabled = false;
        Cursor = Cursors.Hand;
    }

    /// <summary>Выбранная клавиша строкой (<see cref="KeyStroke"/>); пусто — не выбрана.</summary>
    public string Stroke
    {
        get => (string)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            _ => e.Key,
        };
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift)
        {
            e.Handled = true;
            return;
        }

        var modifiers = Keyboard.Modifiers;
        var stroke = new KeyStroke(KeyInterop.VirtualKeyFromKey(key), modifiers.HasFlag(ModifierKeys.Control),
            modifiers.HasFlag(ModifierKeys.Alt), modifiers.HasFlag(ModifierKeys.Shift));
        if (!KeyStroke.Known(stroke.Key) || stroke.Forbidden is not null)
        {
            base.OnPreviewKeyDown(e);
            return;
        }

        e.Handled = true;
        Stroke = stroke.ToString();
    }
}
