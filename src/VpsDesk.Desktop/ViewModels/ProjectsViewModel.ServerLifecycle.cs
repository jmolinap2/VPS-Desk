namespace VpsDesk.Desktop.ViewModels;

public partial class ProjectsViewModel
{
    public void RemoveForServer(Guid serverId)
    {
        _store.RemoveForServer(serverId);
        var activeServer = _serverAccessor();
        if (activeServer?.Id == serverId)
        {
            LoadForServer();
        }
    }
}
