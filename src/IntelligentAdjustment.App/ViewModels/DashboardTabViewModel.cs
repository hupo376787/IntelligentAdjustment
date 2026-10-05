using IntelligentAdjustment.App.Services;

namespace IntelligentAdjustment.App.ViewModels;

public sealed class DashboardTabViewModel : WorkspaceTabViewModel
{
    public DashboardTabViewModel(ProjectDocumentViewModel document)
        : base("dashboard", LocalizationService.Text("Loc.Nav.Dashboard"))
    {
        Document = document;
    }

    public ProjectDocumentViewModel Document { get; }
}
