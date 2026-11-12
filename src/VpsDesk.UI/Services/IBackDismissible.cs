namespace VpsDesk.Desktop.Services;

/// <summary>
/// Un módulo que puede tener capas abiertas encima de su pantalla (modales, hojas inferiores,
/// vistas previas). El botón/gesto «atrás» de Android pide al módulo de la pantalla activa que
/// cierre la capa más alta; si no había ninguna, devuelve false y la navegación sigue su curso
/// (docs/DESIGN_SYSTEM.md §5). Toda capa nueva debe poder cerrarse desde aquí.
/// </summary>
public interface IBackDismissible
{
    bool TryDismissTopLayer();
}
