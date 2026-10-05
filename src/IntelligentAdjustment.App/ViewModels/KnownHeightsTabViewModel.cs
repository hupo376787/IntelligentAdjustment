using System.Globalization;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IntelligentAdjustment.App.Services;

namespace IntelligentAdjustment.App.ViewModels;

public partial class KnownHeightsTabViewModel : WorkspaceTabViewModel
{
    private readonly ProjectDocumentViewModel document;
    private readonly IUserDialogService dialogs;

    [ObservableProperty]
    private string newPointName = string.Empty;

    [ObservableProperty]
    private double newHeight;

    public KnownHeightsTabViewModel(ProjectDocumentViewModel document, IUserDialogService dialogs)
        : base("known-heights", LocalizationService.Text("Loc.Nav.KnownHeights"))
    {
        this.document = document;
        this.dialogs = dialogs;
        SelectedRows.CollectionChanged += (_, _) => DeleteSelectedCommand.NotifyCanExecuteChanged();
    }

    public ObservableCollection<KnownHeightRowViewModel> Items => document.KnownHeights;
    public ObservableCollection<KnownHeightRowViewModel> SelectedRows { get; } = new();

    [RelayCommand]
    private void AddKnownHeight()
    {
        string name = NewPointName.Trim();
        if (name.Length == 0)
        {
            dialogs.Info(LocalizationService.Text("Loc.Known.EnterPointName"));
            return;
        }

        if (!double.IsFinite(NewHeight))
        {
            dialogs.Info(LocalizationService.Text("Loc.Known.EnterValidHeight"));
            return;
        }

        if (Items.Any(x => string.Equals(x.PointName.Trim(), name, StringComparison.Ordinal)))
        {
            dialogs.Info(LocalizationService.Format("Loc.Known.DuplicatePoint", name));
            return;
        }

        document.ExecuteUndoable(() =>
        {
            Items.Add(new KnownHeightRowViewModel
            {
                PointName = name,
                Height = NewHeight
            });
        });

        NewPointName = string.Empty;
        NewHeight = 0;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void DeleteSelected()
    {
        KnownHeightRowViewModel[] selected = SelectedRows.ToArray();
        document.ExecuteUndoable(() =>
        {
            foreach (var row in selected)
            {
                Items.Remove(row);
            }
        });

        SelectedRows.Clear();
    }

    public bool PasteRows(string clipboardText)
    {
        if (string.IsNullOrWhiteSpace(clipboardText))
        {
            return false;
        }

        var parsed = new List<KnownHeightRowViewModel>();
        var names = Items
            .Select(x => x.PointName.Trim())
            .Where(x => x.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        foreach (string line in clipboardText
                     .Replace("\r\n", "\n", StringComparison.Ordinal)
                     .Replace('\r', '\n')
                     .Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] cells = line.Split('\t');
            if (cells.Length < 2)
            {
                dialogs.Error(LocalizationService.Text("Loc.Known.PasteFormat"));
                return false;
            }

            string name = cells[0].Trim();
            if (name.Length == 0 ||
                !TryDouble(cells[1], out double height) ||
                !double.IsFinite(height))
            {
                dialogs.Error(LocalizationService.Format("Loc.Known.UnrecognizedRow", line));
                return false;
            }

            if (!names.Add(name))
            {
                dialogs.Error(LocalizationService.Format("Loc.Known.DuplicateBatch", name));
                return false;
            }

            parsed.Add(new KnownHeightRowViewModel
            {
                PointName = name,
                Height = height,
                Comment = cells.Length > 2 ? cells[2].Trim() : null
            });
        }

        if (parsed.Count == 0)
        {
            return false;
        }

        document.ExecuteUndoable(() =>
        {
            foreach (KnownHeightRowViewModel row in parsed)
            {
                Items.Add(row);
            }
        });

        return true;
    }

    private static bool TryDouble(string text, out double value) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        || double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out value);

    private bool HasSelection() => SelectedRows.Count > 0;
}
