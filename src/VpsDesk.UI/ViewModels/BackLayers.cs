using VpsDesk.Desktop.Services;

namespace VpsDesk.Desktop.ViewModels;

// Capas que el botón «atrás» debe poder cerrar en cada módulo (ver IBackDismissible).
// Despliegues/Respaldos lo implementan en DeploymentsViewModel.Layers.cs.

public partial class FilesViewModel : IBackDismissible
{
    public bool TryDismissTopLayer()
    {
        if (HasImagePreview) { CloseImagePreview(); return true; }
        if (HasPendingSave) { CancelPendingSave(); return true; }
        return false;
    }
}

public partial class StorageViewModel : IBackDismissible
{
    public bool TryDismissTopLayer()
    {
        if (!HasPendingCleanup) return false;
        CancelCleanup();
        return true;
    }
}

public partial class ContainersViewModel : IBackDismissible
{
    // La hoja de detalle de un contenedor está abierta mientras haya uno seleccionado.
    public bool TryDismissTopLayer()
    {
        if (!HasSelection) return false;
        SelectedContainer = null;
        return true;
    }
}

public partial class MainWindowViewModel
{
    /// <summary>Cierra la capa más alta de la pantalla activa; false si no hay ninguna abierta.</summary>
    public bool TryDismissTopLayer()
    {
        IBackDismissible? page =
            IsDeploymentsPage || IsBackupsPage ? DeploymentsModule
            : IsFilesPage || IsEnvironmentPage ? FilesModule
            : IsStoragePage ? StorageModule
            : IsContainersPage ? ContainerModule
            : null;
        return page?.TryDismissTopLayer() == true;
    }
}
