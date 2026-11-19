using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace MdViewer;

public enum SaveChoice { Save, Discard, Cancel }

public sealed class ConfirmDialog : Window
{
    SaveChoice _choice = SaveChoice.Cancel;

    ConfirmDialog(string fileName)
    {
        Title = "Cambios sin guardar";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        if (Application.Current!.TryGetResource("EditorBg", ActualThemeVariant, out var bg) && bg is IBrush b) Background = b;
        if (Application.Current.TryGetResource("Fg", ActualThemeVariant, out var fg) && fg is IBrush f) Foreground = f;

        var save = new Button { Content = "Guardar" };
        var discard = new Button { Content = "No guardar" };
        var cancel = new Button { Content = "Cancelar" };
        save.Click += (_, _) => Finish(SaveChoice.Save);
        discard.Click += (_, _) => Finish(SaveChoice.Discard);
        cancel.Click += (_, _) => Finish(SaveChoice.Cancel);

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 18,
            Children =
            {
                new TextBlock
                {
                    Text = $"¿Querés guardar los cambios de \"{fileName}\" antes de continuar?",
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 14
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { save, discard, cancel }
                }
            }
        };
    }

    void Finish(SaveChoice choice)
    {
        _choice = choice;
        Close();
    }

    public static async Task<SaveChoice> ShowAsync(Window owner, string fileName)
    {
        var dialog = new ConfirmDialog(fileName);
        await dialog.ShowDialog(owner);
        return dialog._choice;
    }
}
