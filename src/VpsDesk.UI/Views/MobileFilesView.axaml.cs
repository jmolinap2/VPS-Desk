using Avalonia.Controls;
using Avalonia.Input;
using VpsDesk.Desktop.ViewModels;
using VpsDesk.Domain.Files;

namespace VpsDesk.Desktop.Views;

public partial class MobileFilesView : UserControl
{
    public MobileFilesView()
    {
        InitializeComponent();
    }

    // Tocar una tarjeta: carpetas y archivos con vista previa se abren; los de texto
    // editables pasan al editor. El botón "Editar" de la tarjeta sigue disponible.
    private void OnEntryTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control { DataContext: RemoteFileEntry entry }) return;
        if (DataContext is not FilesViewModel vm) return;

        if ((entry.IsDirectory || entry.CanPreview) && vm.PreviewEntryCommand.CanExecute(entry))
        {
            vm.PreviewEntryCommand.Execute(entry);
        }
        else if (entry.CanEdit && vm.EditEntryCommand.CanExecute(entry))
        {
            vm.EditEntryCommand.Execute(entry);
        }
    }
    private void OnScrimPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (ReferenceEquals(e.Source, sender))
            (DataContext as VpsDesk.Desktop.ViewModels.FilesViewModel)?.CloseImagePreviewCommand.Execute(null);
    }
}
