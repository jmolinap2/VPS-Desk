namespace VpsDesk.Desktop.ViewModels;

public partial class ProjectsViewModel
{
    public void RemoveForServer(Guid serverId)
    {
        _store.RemoveForServer(serverId);
        var activeServer = _serverAccessor();
        if (activeServer?.Id != serverId) return;

        _suppressSelection = true;
        try
        {
            AssociatedProjects.Clear();
            DiscoveredProjects.Clear();
            SelectedProject = null;
            SelectedDiscovery = null;
            StatusMessage = "El servidor se eliminó. Sus asociaciones locales de proyectos también se retiraron.";
        }
        finally
        {
            _suppressSelection = false;
            NotifyProjectState();
        }
    }
}
