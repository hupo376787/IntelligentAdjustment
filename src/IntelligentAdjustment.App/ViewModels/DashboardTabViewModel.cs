namespace IntelligentAdjustment.App.ViewModels;

public sealed class DashboardTabViewModel : WorkspaceTabViewModel
{
    public DashboardTabViewModel(ProjectDocumentViewModel document)
        : base("dashboard", "工程概览")
    {
        Document = document;
    }

    public ProjectDocumentViewModel Document { get; }
}
