using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PalCalc.Model;
using PalCalc.UI.Localization;
using PalCalc.UI.Model;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace PalCalc.UI.ViewModel
{
    internal sealed partial class YourPalsViewModel : ObservableObject, IDisposable
    {
        private readonly Dispatcher dispatcher;
        private readonly YourPalsQueryState queryState;
        private readonly SavePalsSessionManager orphanedDocumentManager;
        private readonly Action navigateBack;
        private List<YourPalsEntryRowViewModel> allEntries = [];
        private IReadOnlyList<YourPalsGroupFilterOption> groupFilterOptions = [];
        private bool subscribedToLocale;
        private bool disposed;

        public static YourPalsViewModel DesignerInstance => new(null, Dispatcher.CurrentDispatcher);

        public YourPalsViewModel(
            SavePalsSession session,
            Dispatcher dispatcher,
            SavePalsSessionManager orphanedDocumentManager = null,
            Action navigateBack = null)
        {
            Session = session;
            this.dispatcher = dispatcher ?? Dispatcher.CurrentDispatcher;
            queryState = session?.QueryState ?? new YourPalsQueryState();
            this.orphanedDocumentManager = orphanedDocumentManager;
            this.navigateBack = navigateBack;
            BackCommand = new RelayCommand(() => this.navigateBack?.Invoke(), () => this.navigateBack != null);
            RefreshCommand = new RelayCommand(Refresh);
            DiscardChangesAndReloadCommand = new RelayCommand(DiscardChangesAndReload, CanDiscardChangesAndReload);
            CreateDocumentCommand = new RelayCommand(CreateDocument, CanCreateNewDocument);
            ClearQueryCommand = new RelayCommand(ClearQuery);
            ToggleSortDirectionCommand = new RelayCommand(ToggleSortDirection);
            SaveCommand = new RelayCommand(Save, CanSave);
            CreateGroupCommand = new RelayCommand(CreateGroup, CanCreateGroup);
            RenameGroupCommand = new RelayCommand(RenameGroup, CanRenameGroup);
            DeleteGroupCommand = new RelayCommand(DeleteGroup, CanDeleteGroup);
            MoveGroupUpCommand = new RelayCommand(MoveGroupUp, CanMoveGroup);
            MoveGroupDownCommand = new RelayCommand(MoveGroupDown, CanMoveGroup);
            AddSelectedSourceCommand = new RelayCommand(AddSelectedSource, CanAddSelectedSource);
            AddManualDefinitionCommand = new RelayCommand(AddManualDefinition, CanAddManualDefinition);
            UpdateManualDefinitionCommand = new RelayCommand(UpdateManualDefinition, CanUpdateManualDefinition);
            RemoveSelectedEntryCommand = new RelayCommand(RemoveSelectedEntry, CanRemoveSelectedEntry);
            RebindSelectedEntryCommand = new RelayCommand(RebindSelectedEntry, CanRebindSelectedEntry);
            BulkRebindMatchingMembersCommand = new RelayCommand(BulkRebindMatchingMembers, CanBulkRebindMatchingMembers);
            RemoveDuplicateMembersCommand = new RelayCommand(RemoveDuplicateMembers, CanRemoveDuplicateMembers);
            RepairRecoveredDocumentCommand = new RelayCommand(RepairRecoveredDocument, CanRepairRecoveredDocumentCommand);
            RefreshOrphanedDocumentsCommand = new RelayCommand(RefreshOrphanedDocuments);
            DeleteSelectedOrphanedDocumentCommand = new RelayCommand(
                DeleteSelectedOrphanedDocument,
                CanDeleteSelectedOrphanedDocument);

            if (Session != null)
            {
                Session.Refreshed += Session_Refreshed;
                Translator.LocaleUpdated += Translator_LocaleUpdated;
                subscribedToLocale = true;
                UpdateFromSession();
            }
            else
            {
                UpdateOrphanedDocuments();
            }

            if (this.orphanedDocumentManager != null)
                this.orphanedDocumentManager.OrphanedDocumentsChanged += OrphanedDocumentsChanged;
        }

        public SavePalsSession Session { get; }
        public IRelayCommand BackCommand { get; }

        public IRelayCommand RefreshCommand { get; }
        public IRelayCommand DiscardChangesAndReloadCommand { get; }
        public IRelayCommand CreateDocumentCommand { get; }
        public IRelayCommand ClearQueryCommand { get; }
        public IRelayCommand ToggleSortDirectionCommand { get; }
        public IRelayCommand SaveCommand { get; }
        public IRelayCommand CreateGroupCommand { get; }
        public IRelayCommand RenameGroupCommand { get; }
        public IRelayCommand DeleteGroupCommand { get; }
        public IRelayCommand MoveGroupUpCommand { get; }
        public IRelayCommand MoveGroupDownCommand { get; }
        public IRelayCommand AddSelectedSourceCommand { get; }
        public IRelayCommand AddManualDefinitionCommand { get; }
        public IRelayCommand UpdateManualDefinitionCommand { get; }
        public IRelayCommand RemoveSelectedEntryCommand { get; }
        public IRelayCommand RebindSelectedEntryCommand { get; }
        public IRelayCommand BulkRebindMatchingMembersCommand { get; }
        public IRelayCommand RemoveDuplicateMembersCommand { get; }
        public IRelayCommand RepairRecoveredDocumentCommand { get; }
        public IRelayCommand RefreshOrphanedDocumentsCommand { get; }
        public IRelayCommand DeleteSelectedOrphanedDocumentCommand { get; }

        public IReadOnlyList<YourPalsStatusFilterOption> StatusFilterOptions { get; } =
        [
            new(YourPalsStatusFilter.All, "All statuses"),
            new(YourPalsStatusFilter.Resolved, "Resolved"),
            new(YourPalsStatusFilter.Unresolved, "Unresolved"),
            new(YourPalsStatusFilter.Stale, "Stale"),
            new(YourPalsStatusFilter.Conflict, "Conflict"),
            new(YourPalsStatusFilter.Invalid, "Invalid"),
        ];

        public IReadOnlyList<YourPalsSortOption> SortOptions { get; } =
        [
            new(YourPalsSortField.Group, "Group"),
            new(YourPalsSortField.PalName, "Pal name"),
            new(YourPalsSortField.Status, "Status"),
            new(YourPalsSortField.Source, "Source"),
            new(YourPalsSortField.Instance, "Instance"),
            new(YourPalsSortField.Location, "Location"),
            new(YourPalsSortField.Key, "Entry key"),
        ];

        public IReadOnlyList<YourPalsGroupFilterOption> GroupFilterOptions => groupFilterOptions;

        [ObservableProperty]
        private IReadOnlyList<YourPalsGroupSummaryViewModel> groups = [];

        [ObservableProperty]
        private IReadOnlyList<YourPalsEntryRowViewModel> entries = [];

        [ObservableProperty]
        private IReadOnlyList<YourPalsSourceRowViewModel> sourceEntries = [];

        [ObservableProperty]
        private IReadOnlyList<YourPalsDiagnostic> diagnostics = [];

        [ObservableProperty]
        private YourPalsEntryRowViewModel selectedEntry;

        [ObservableProperty]
        private YourPalsGroupSummaryViewModel selectedGroupSummary;

        [ObservableProperty]
        private YourPalsSourceRowViewModel selectedSourceEntry;

        [ObservableProperty]
        private string newGroupName = "";

        [ObservableProperty]
        private string renameGroupName = "";

        [ObservableProperty]
        private string manualInternalName = "";

        [ObservableProperty]
        private string editStatus = "";

        [ObservableProperty]
        private bool hasGroups;

        [ObservableProperty]
        private bool hasEntries;

        [ObservableProperty]
        private bool hasSourceEntries;

        [ObservableProperty]
        private bool hasDiagnostics;

        [ObservableProperty]
        private IReadOnlyList<YourPalsOrphanedDocument> orphanedDocuments = [];

        [ObservableProperty]
        private YourPalsOrphanedDocument selectedOrphanedDocument;

        [ObservableProperty]
        private bool hasOrphanedDocuments;

        [ObservableProperty]
        private string saveScope = "No save selected";

        [ObservableProperty]
        private string sessionState = "No session";

        [ObservableProperty]
        private string sourceState = "Unavailable";

        [ObservableProperty]
        private string recoveryState = "No recovery details";

        [ObservableProperty]
        private string recoveryGuidance = "";

        private YourPalsGroupFilterOption selectedGroupFilter;

        [ObservableProperty]
        private int filteredEntryCount;

        [ObservableProperty]
        private int totalEntryCount;

        public string SearchText
        {
            get => queryState.SearchText;
            set
            {
                value ??= "";
                if (string.Equals(queryState.SearchText, value, StringComparison.Ordinal))
                    return;

                queryState.SearchText = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasActiveQuery));
                ApplyQuery();
            }
        }

        public YourPalsStatusFilter SelectedStatusFilter
        {
            get => queryState.StatusFilter;
            set
            {
                if (queryState.StatusFilter == value)
                    return;

                queryState.StatusFilter = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasActiveQuery));
                ApplyQuery();
            }
        }

        public YourPalsSortField SelectedSortField
        {
            get => queryState.SortField;
            set
            {
                if (queryState.SortField == value)
                    return;

                queryState.SortField = value;
                OnPropertyChanged();
                ApplyQuery();
            }
        }

        public bool IsSortAscending
        {
            get => queryState.IsSortAscending;
            private set
            {
                if (queryState.IsSortAscending == value)
                    return;

                queryState.IsSortAscending = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SortDirectionText));
                ApplyQuery();
            }
        }

        public string SortDirectionText => IsSortAscending ? "Ascending" : "Descending";

        public string EntryCountText => $"{FilteredEntryCount} / {TotalEntryCount}";

        public bool CanEdit => Session?.CanEdit == true;

        public bool CanCreateDocument => Session?.CanCreateDocument == true;

        public bool CanRepairRecoveredDocument => Session?.CanRepairRecoveredDocument == true;

        public string OrphanedDocumentCountText =>
            $"Orphaned documents: {OrphanedDocuments.Count}";

        public YourPalsSolverSourceProjection SolverSource =>
            Session?.BuildSolverSource() ?? new YourPalsSolverSourceProjection([], []);

        [ObservableProperty]
        private bool useAsSolverSource;

        public string SelectedGroupId
        {
            get => queryState.SelectedGroupId;
            private set
            {
                if (string.Equals(queryState.SelectedGroupId, value, StringComparison.Ordinal))
                    return;

                queryState.SelectedGroupId = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasActiveQuery));
                var selectedOption = groupFilterOptions.FirstOrDefault(option =>
                    string.Equals(option.GroupId, value, StringComparison.Ordinal));
                if (!ReferenceEquals(selectedGroupFilter, selectedOption))
                {
                    selectedGroupFilter = selectedOption;
                    OnPropertyChanged(nameof(SelectedGroupFilter));
                }
                if (value != null)
                {
                    SelectedGroupSummary = Groups.FirstOrDefault(group =>
                        string.Equals(group.GroupId, value, StringComparison.Ordinal));
                }
                else
                {
                    SelectedGroupSummary = null;
                }
                ApplyQuery();
            }
        }

        public YourPalsGroupFilterOption SelectedGroupFilter
        {
            get => selectedGroupFilter;
            set
            {
                if (!SetProperty(ref selectedGroupFilter, value))
                    return;

                SelectedGroupId = value?.GroupId;
            }
        }

        private void Refresh()
        {
            if (Session == null)
                return;

            Session.RefreshCurrent();
        }

        private void DiscardChangesAndReload()
        {
            if (Session == null)
                return;

            if (Session.TryDiscardChangesAndReload(out var error))
                EditStatus = "Local changes discarded and document reloaded.";
            else
                EditStatus = error;
        }

        private bool CanDiscardChangesAndReload() => Session?.CanDiscardChangesAndReload == true;

        private void CreateDocument()
        {
            if (Session == null)
                return;

            if (Session.TryCreateDocument(out var error))
                EditStatus = "Your Pals document created. Save to persist changes.";
            else
                EditStatus = error;
        }

        private bool CanCreateNewDocument() => CanCreateDocument;

        private void Save()
        {
            if (Session == null)
                return;

            if (Session.TrySave())
            {
                EditStatus = "Saved";
                UpdateFromSession();
            }
            else
            {
                EditStatus = Session.Diagnostics.LastOrDefault(diagnostic =>
                    diagnostic.Code == YourPalsDiagnosticCode.WriteFailed ||
                    diagnostic.Code == YourPalsDiagnosticCode.ExternalConflict)?.Message
                    ?? "The Your Pals document could not be saved.";
                UpdateFromSession();
            }
        }

        private void CreateGroup()
        {
            if (Session == null)
                return;

            if (Session.TryCreateGroup(NewGroupName, out _, out var error))
            {
                NewGroupName = "";
                EditStatus = "Group created. Save to persist changes.";
            }
            else
            {
                EditStatus = error;
            }
        }

        private void RenameGroup()
        {
            if (Session == null)
                return;

            if (Session.TryRenameGroup(SelectedGroupSummary?.GroupId, RenameGroupName, out var error))
                EditStatus = "Group renamed. Save to persist changes.";
            else
                EditStatus = error;
        }

        private void DeleteGroup()
        {
            if (Session == null)
                return;

            if (Session.TryDeleteGroup(SelectedGroupSummary?.GroupId, out var error))
            {
                EditStatus = "Group deleted. Save to persist changes.";
                SelectedGroupSummary = null;
            }
            else
            {
                EditStatus = error;
            }
        }

        private void MoveGroupUp() => MoveGroup(-1);

        private void MoveGroupDown() => MoveGroup(1);

        private void MoveGroup(int offset)
        {
            if (Session == null)
                return;

            if (Session.TryMoveGroup(SelectedGroupSummary?.GroupId, offset, out var error))
                EditStatus = "Group order changed. Save to persist changes.";
            else
                EditStatus = error;
        }

        private void AddSelectedSource()
        {
            if (Session == null)
                return;

            if (Session.TryAddImportedMember(
                    SelectedGroupSummary?.GroupId,
                    SelectedSourceEntry?.Entry,
                    out _,
                    out var error))
            {
                EditStatus = "Pal added to the group. Save to persist changes.";
            }
            else
            {
                EditStatus = error;
            }
        }

        private void AddManualDefinition()
        {
            if (Session == null)
                return;

            if (Session.TryAddManualDefinition(
                    SelectedGroupSummary?.GroupId,
                    ManualInternalName,
                    null,
                    out _,
                    out _,
                    out var error))
            {
                ManualInternalName = "";
                EditStatus = "Manual Pal added. Save to persist changes.";
            }
            else
            {
                EditStatus = error;
            }
        }

        private void UpdateManualDefinition()
        {
            if (Session == null)
                return;

            if (Session.TryUpdateManualDefinition(
                    SelectedEntry?.Member?.ManualDefinitionId,
                    ManualInternalName,
                    null,
                    out var error))
            {
                EditStatus = "Manual Pal updated. Save to persist changes.";
            }
            else
            {
                EditStatus = error;
            }
        }

        private void RemoveSelectedEntry()
        {
            if (Session == null)
                return;

            if (Session.TryRemoveMember(
                    SelectedEntry?.GroupId,
                    SelectedEntry?.PalEntryKey,
                    out var error))
            {
                EditStatus = "Member removed from the group. Save to persist changes.";
                SelectedEntry = null;
            }
            else
            {
                EditStatus = error;
            }
        }

        private void RebindSelectedEntry()
        {
            if (Session == null)
                return;

            if (Session.TryRebindImportedMember(
                    SelectedEntry?.GroupId,
                    SelectedEntry?.PalEntryKey,
                    SelectedSourceEntry?.Entry,
                    out var error))
            {
                EditStatus = "Member rebound to the selected source Pal. Save to persist changes.";
            }
            else
            {
                EditStatus = error;
            }
        }

        private void BulkRebindMatchingMembers()
        {
            if (Session == null)
                return;

            if (Session.TryBulkRebindMatchingMembers(
                    SelectedSourceEntry?.Entry,
                    out var repairedCount,
                    out var error))
            {
                EditStatus = $"Rebound {repairedCount} matching member(s). Save to persist changes.";
            }
            else
            {
                EditStatus = error;
            }
        }

        private void RemoveDuplicateMembers()
        {
            if (Session == null)
                return;

            if (Session.TryRemoveDuplicateMembers(out var summary, out var error))
            {
                EditStatus = $"Removed {summary.RemovedDuplicateMembers} duplicate member(s). Save to persist changes.";
            }
            else
            {
                EditStatus = error;
            }
        }

        private void RepairRecoveredDocument()
        {
            if (Session == null)
                return;

            if (Session.TryRepairRecoveredDocument(out var summary, out var error))
            {
                EditStatus = $"Recovered and saved Your Pals ({summary.TotalChanges} repair change(s)).";
            }
            else
            {
                EditStatus = error;
                UpdateFromSession();
            }
        }

        private void RefreshOrphanedDocuments() => UpdateOrphanedDocuments();

        private void DeleteSelectedOrphanedDocument()
        {
            if (orphanedDocumentManager == null || SelectedOrphanedDocument == null)
                return;

            var result = MessageBox.Show(
                $"Delete the orphaned Your Pals document?\n\n{SelectedOrphanedDocument.DocumentPath}\n\nIts document and backup will be removed.",
                "Delete orphaned document",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
                return;

            if (orphanedDocumentManager.TryDeleteOrphanedDocument(
                    SelectedOrphanedDocument,
                    out var error))
            {
                EditStatus = "Orphaned document deleted.";
                UpdateOrphanedDocuments();
            }
            else
            {
                EditStatus = error;
            }
        }

        private void ClearQuery()
        {
            SearchText = "";
            SelectedStatusFilter = YourPalsStatusFilter.All;
            SelectedGroupId = null;
        }

        private void ToggleSortDirection()
        {
            IsSortAscending = !IsSortAscending;
        }

        private void Session_Refreshed(object sender, EventArgs e)
        {
            if (dispatcher.CheckAccess())
            {
                UpdateFromSession();
                return;
            }

            dispatcher.BeginInvoke(UpdateFromSession, DispatcherPriority.DataBind);
        }

        private void Translator_LocaleUpdated()
        {
            if (dispatcher.CheckAccess())
            {
                UpdateFromSession();
                return;
            }

            dispatcher.BeginInvoke(UpdateFromSession, DispatcherPriority.DataBind);
        }

        private void UpdateFromSession()
        {
            if (disposed || Session == null)
                return;

            var selectedKey = SelectedEntry?.PalEntryKey;
            var selectedGroupId = SelectedGroupSummary?.GroupId;
            var selectedSourceReferenceKey = SelectedSourceEntry?.ReferenceKey;
            var manualDefinitions = (Session.Document?.ManualDefinitions ?? [])
                .Where(definition => definition != null && !string.IsNullOrWhiteSpace(definition.ManualDefinitionId))
                .GroupBy(definition => definition.ManualDefinitionId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

            Groups = (Session.ResolvedGroups ?? [])
                .Select(group => new YourPalsGroupSummaryViewModel(group))
                .ToList()
                .AsReadOnly();
            SelectedGroupSummary = Groups.FirstOrDefault(group =>
                string.Equals(group.GroupId, selectedGroupId, StringComparison.Ordinal));

            allEntries = (Session.ResolvedGroups ?? [])
                .SelectMany(group => group.Members.Select(member => new YourPalsEntryRowViewModel(
                    group.Group,
                    member,
                    member.Member?.ManualDefinitionId != null &&
                    manualDefinitions.TryGetValue(member.Member.ManualDefinitionId, out var definition)
                        ? definition
                        : null)))
                .ToList();

            TotalEntryCount = allEntries.Count;
            OnPropertyChanged(nameof(EntryCountText));
            UpdateGroupFilterOptions();

            SourceEntries = (Session.SourceSnapshot?.Entries ?? [])
                .Select(entry => new YourPalsSourceRowViewModel(entry))
                .ToList()
                .AsReadOnly();
            SelectedSourceEntry = SourceEntries.FirstOrDefault(entry =>
                string.Equals(entry.ReferenceKey, selectedSourceReferenceKey, StringComparison.Ordinal));

            Diagnostics = (Session.Diagnostics ?? [])
                .ToList()
                .AsReadOnly();

            HasGroups = Groups.Count > 0;
            HasSourceEntries = SourceEntries.Count > 0;
            HasDiagnostics = Diagnostics.Count > 0;
            SaveScope = Session.Identity.CanonicalKey;
            SessionState = Session.State.ToString();
            SourceState = Session.SourceSnapshot?.IsAvailable == true ? "Available" : "Unavailable";
            RecoveryState = Diagnostics.Count == 0
                ? "No recovery details"
                : string.Join("; ", Diagnostics.Select(diagnostic => diagnostic.Message));
            RecoveryGuidance = BuildRecoveryGuidance(Session);

            UpdateOrphanedDocuments();
            ApplyQuery(selectedKey);
            OnPropertyChanged(nameof(CanEdit));
            OnPropertyChanged(nameof(CanCreateDocument));
            OnPropertyChanged(nameof(CanRepairRecoveredDocument));
            OnPropertyChanged(nameof(CanDiscardChangesAndReload));
            OnPropertyChanged(nameof(SolverSource));
            NotifyEditingCommands();
        }

        private static string BuildRecoveryGuidance(SavePalsSession session)
        {
            if (session.HasUnrecoverableRecoveryData)
            {
                return "Repair is disabled because one or more whole records could not be recovered safely. The original file is preserved; use the backup or recover the missing records manually.";
            }

            if (session.IsRecoveredFromBackup)
            {
                return "A backup was loaded read-only. Use Repair recovered to restore the primary document. If repair fails, correct the file problem and retry; the backup remains preserved.";
            }

            if (session.CanRepairRecoveredDocument)
            {
                return "Some document data needs repair. Review the recovery details, then use Repair recovered to write the repaired projection.";
            }

            if (session.State == SavePalsSessionState.Recovery)
            {
                return "This document is read-only because it could not be loaded safely. Review the recovery details; no empty replacement will be saved automatically.";
            }

            return "";
        }

        private void UpdateGroupFilterOptions()
        {
            var selectedGroupId = queryState.SelectedGroupId;
            var options = new List<YourPalsGroupFilterOption>
            {
                new(null, "All groups"),
            };
            options.AddRange(Groups.Select(group => new YourPalsGroupFilterOption(
                group.GroupId,
                group.Name)));

            if (selectedGroupId != null &&
                !options.Any(option => string.Equals(option.GroupId, selectedGroupId, StringComparison.Ordinal)))
            {
                queryState.SelectedGroupId = null;
                selectedGroupId = null;
                SelectedGroupSummary = null;
                OnPropertyChanged(nameof(SelectedGroupId));
                OnPropertyChanged(nameof(HasActiveQuery));
            }

            groupFilterOptions = options.AsReadOnly();
            OnPropertyChanged(nameof(GroupFilterOptions));
            SelectedGroupFilter = options.FirstOrDefault(option =>
                string.Equals(option.GroupId, selectedGroupId, StringComparison.Ordinal));
        }

        private void ApplyQuery(string preferredSelectedKey = null)
        {
            var selectedKey = preferredSelectedKey ?? SelectedEntry?.PalEntryKey;
            var filtered = allEntries.Where(MatchesQuery);
            var comparer = DisplayComparer();

            var ordered = queryState.SortField switch
            {
                YourPalsSortField.Group => queryState.IsSortAscending
                    ? filtered.OrderBy(entry => entry.GroupName, comparer)
                    : filtered.OrderByDescending(entry => entry.GroupName, comparer),
                YourPalsSortField.Status => queryState.IsSortAscending
                    ? filtered.OrderBy(entry => entry.Status)
                    : filtered.OrderByDescending(entry => entry.Status),
                YourPalsSortField.Source => queryState.IsSortAscending
                    ? filtered.OrderBy(entry => entry.SourceScope, comparer)
                    : filtered.OrderByDescending(entry => entry.SourceScope, comparer),
                YourPalsSortField.Instance => queryState.IsSortAscending
                    ? filtered.OrderBy(entry => entry.InstanceId, comparer)
                    : filtered.OrderByDescending(entry => entry.InstanceId, comparer),
                YourPalsSortField.Location => queryState.IsSortAscending
                    ? filtered.OrderBy(entry => entry.Location, comparer)
                    : filtered.OrderByDescending(entry => entry.Location, comparer),
                YourPalsSortField.Key => queryState.IsSortAscending
                    ? filtered.OrderBy(entry => entry.PalEntryKey, StringComparer.Ordinal)
                    : filtered.OrderByDescending(entry => entry.PalEntryKey, StringComparer.Ordinal),
                _ => queryState.IsSortAscending
                    ? filtered.OrderBy(entry => entry.PalName, comparer)
                    : filtered.OrderByDescending(entry => entry.PalName, comparer),
            };

            Entries = ordered
                .ThenBy(entry => entry.PalEntryKey, StringComparer.Ordinal)
                .ToList()
                .AsReadOnly();
            FilteredEntryCount = Entries.Count;
            OnPropertyChanged(nameof(EntryCountText));
            HasEntries = Entries.Count > 0;
            SelectedEntry = Entries.FirstOrDefault(entry => entry.PalEntryKey == selectedKey);
            OnPropertyChanged(nameof(HasActiveQuery));
        }

        private bool MatchesQuery(YourPalsEntryRowViewModel entry)
        {
            if (queryState.SelectedGroupId != null &&
                !string.Equals(entry.GroupId, queryState.SelectedGroupId, StringComparison.Ordinal))
            {
                return false;
            }

            if (!MatchesStatusFilter(entry.Status))
            {
                return false;
            }

            var searchText = queryState.SearchText.Trim();
            if (searchText.Length == 0)
                return true;

            var culture = QueryCulture();
            var compareInfo = culture.CompareInfo;
            var options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
            return Contains(entry.GroupName, searchText, compareInfo, options) ||
                Contains(entry.PalName, searchText, compareInfo, options) ||
                Contains(entry.Status.ToString(), searchText, compareInfo, options) ||
                Contains(entry.SourceScope, searchText, compareInfo, options) ||
                Contains(entry.SourceKey, searchText, compareInfo, options) ||
                Contains(entry.InstanceId, searchText, compareInfo, options) ||
                Contains(entry.PalEntryKey, searchText, compareInfo, options) ||
                Contains(entry.Details, searchText, compareInfo, options);
        }

        private bool MatchesStatusFilter(YourPalsEntryStatus status) => queryState.StatusFilter switch
        {
            YourPalsStatusFilter.All => true,
            YourPalsStatusFilter.Resolved => status == YourPalsEntryStatus.Resolved,
            YourPalsStatusFilter.Unresolved => status == YourPalsEntryStatus.Unresolved,
            YourPalsStatusFilter.Stale => status == YourPalsEntryStatus.Stale,
            YourPalsStatusFilter.Conflict => status == YourPalsEntryStatus.Conflict,
            YourPalsStatusFilter.Invalid => status == YourPalsEntryStatus.Invalid,
            _ => false,
        };

        private static bool Contains(
            string value,
            string searchText,
            CompareInfo compareInfo,
            CompareOptions options) =>
            value != null && compareInfo.IndexOf(value, searchText, options) >= 0;

        private static StringComparer DisplayComparer() =>
            StringComparer.Create(QueryCulture(), ignoreCase: true);

        private static CultureInfo QueryCulture()
        {
            try
            {
                return CultureInfo.GetCultureInfo(Translator.CurrentLocale.ToFormalName());
            }
            catch (CultureNotFoundException)
            {
                return CultureInfo.InvariantCulture;
            }
        }

        private bool CanSave() => Session?.CanEdit == true && Session.IsDirty;

        private bool CanCreateGroup() => CanEdit && !string.IsNullOrWhiteSpace(NewGroupName);

        private bool CanRenameGroup() => CanEdit &&
            SelectedGroupSummary != null &&
            !string.IsNullOrWhiteSpace(RenameGroupName);

        private bool CanDeleteGroup() => CanEdit && SelectedGroupSummary != null;

        private bool CanMoveGroup() => CanEdit && SelectedGroupSummary != null;

        private bool CanAddSelectedSource() => CanEdit &&
            SelectedGroupSummary != null &&
            Session?.CanUseSourceEntry(SelectedSourceEntry?.Entry) == true;

        private bool CanAddManualDefinition() => CanEdit &&
            SelectedGroupSummary != null &&
            !string.IsNullOrWhiteSpace(ManualInternalName);

        private bool CanUpdateManualDefinition() => CanEdit &&
            SelectedEntry?.Member?.KnownKind == YourPalsMemberKind.ManualDefinitionReference &&
            !string.IsNullOrWhiteSpace(ManualInternalName);

        private bool CanRemoveSelectedEntry() => CanEdit && SelectedEntry != null;

        private bool CanRebindSelectedEntry() => CanEdit &&
            SelectedEntry?.Member?.KnownKind == YourPalsMemberKind.ImportedReference &&
            Session?.CanUseSourceEntry(SelectedSourceEntry?.Entry) == true;

        private bool CanBulkRebindMatchingMembers() => CanEdit &&
            Session?.CanUseSourceEntry(SelectedSourceEntry?.Entry) == true;

        private bool CanRemoveDuplicateMembers() => CanEdit;

        private bool CanRepairRecoveredDocumentCommand() => Session?.CanRepairRecoveredDocument == true;

        private bool CanDeleteSelectedOrphanedDocument() =>
            orphanedDocumentManager != null && SelectedOrphanedDocument != null;

        private void UpdateOrphanedDocuments()
        {
            var selectedPath = SelectedOrphanedDocument?.DocumentPath;
            OrphanedDocuments = orphanedDocumentManager?.OrphanedDocuments ?? [];
            HasOrphanedDocuments = OrphanedDocuments.Count > 0;
            SelectedOrphanedDocument = OrphanedDocuments.FirstOrDefault(orphan =>
                string.Equals(orphan.DocumentPath, selectedPath, StringComparison.Ordinal));
            OnPropertyChanged(nameof(OrphanedDocumentCountText));
            DeleteSelectedOrphanedDocumentCommand.NotifyCanExecuteChanged();
        }

        private void OrphanedDocumentsChanged(object sender, EventArgs e)
        {
            if (dispatcher.CheckAccess())
            {
                UpdateOrphanedDocuments();
                return;
            }

            dispatcher.BeginInvoke(UpdateOrphanedDocuments, DispatcherPriority.DataBind);
        }

        private void NotifyEditingCommands()
        {
            CreateDocumentCommand.NotifyCanExecuteChanged();
            DiscardChangesAndReloadCommand.NotifyCanExecuteChanged();
            SaveCommand.NotifyCanExecuteChanged();
            CreateGroupCommand.NotifyCanExecuteChanged();
            RenameGroupCommand.NotifyCanExecuteChanged();
            DeleteGroupCommand.NotifyCanExecuteChanged();
            MoveGroupUpCommand.NotifyCanExecuteChanged();
            MoveGroupDownCommand.NotifyCanExecuteChanged();
            AddSelectedSourceCommand.NotifyCanExecuteChanged();
            AddManualDefinitionCommand.NotifyCanExecuteChanged();
            UpdateManualDefinitionCommand.NotifyCanExecuteChanged();
            RemoveSelectedEntryCommand.NotifyCanExecuteChanged();
            RebindSelectedEntryCommand.NotifyCanExecuteChanged();
            BulkRebindMatchingMembersCommand.NotifyCanExecuteChanged();
            RemoveDuplicateMembersCommand.NotifyCanExecuteChanged();
            RepairRecoveredDocumentCommand.NotifyCanExecuteChanged();
            DeleteSelectedOrphanedDocumentCommand.NotifyCanExecuteChanged();
        }

        partial void OnSelectedEntryChanged(YourPalsEntryRowViewModel value)
        {
            if (value?.Member?.KnownKind == YourPalsMemberKind.ManualDefinitionReference)
            {
                ManualInternalName = Session?.Document?.ManualDefinitions?
                    .FirstOrDefault(definition => string.Equals(
                        definition.ManualDefinitionId,
                        value.Member.ManualDefinitionId,
                        StringComparison.Ordinal))?.RawInternalName ?? "";
            }
            else if (value == null)
            {
                ManualInternalName = "";
            }

            NotifyEditingCommands();
        }

        partial void OnSelectedGroupSummaryChanged(YourPalsGroupSummaryViewModel value)
        {
            RenameGroupName = value?.Name ?? "";
            NotifyEditingCommands();
        }

        partial void OnSelectedSourceEntryChanged(YourPalsSourceRowViewModel value) =>
            NotifyEditingCommands();

        partial void OnNewGroupNameChanged(string value) => NotifyEditingCommands();

        partial void OnRenameGroupNameChanged(string value) => NotifyEditingCommands();

        partial void OnManualInternalNameChanged(string value) => NotifyEditingCommands();

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            if (Session != null)
                Session.Refreshed -= Session_Refreshed;
            if (subscribedToLocale)
                Translator.LocaleUpdated -= Translator_LocaleUpdated;
            if (orphanedDocumentManager != null)
                orphanedDocumentManager.OrphanedDocumentsChanged -= OrphanedDocumentsChanged;
        }

        public bool HasActiveQuery =>
            !string.IsNullOrWhiteSpace(SearchText) ||
            SelectedStatusFilter != YourPalsStatusFilter.All ||
            SelectedGroupId != null;
    }

    internal sealed record YourPalsStatusFilterOption(YourPalsStatusFilter Value, string Label);

    internal sealed record YourPalsSortOption(YourPalsSortField Value, string Label);

    internal sealed record YourPalsGroupFilterOption(string GroupId, string Label);

    internal sealed class YourPalsGroupSummaryViewModel
    {
        public YourPalsGroupSummaryViewModel(YourPalsResolvedGroup group)
        {
            GroupId = group?.Group?.GroupId ?? "(invalid)";
            Name = string.IsNullOrWhiteSpace(group?.Group?.Name) ? "(unnamed group)" : group.Group.Name;
            MemberCount = group?.Members?.Count ?? 0;
            StatusSummary = BuildStatusSummary(group?.Members);
        }

        public string GroupId { get; }
        public string Name { get; }
        public int MemberCount { get; }
        public string StatusSummary { get; }

        private static string BuildStatusSummary(IReadOnlyList<YourPalsResolvedMember> members)
        {
            if (members == null || members.Count == 0)
                return "Empty";

            return string.Join(
                ", ",
                members
                    .GroupBy(member => member.Status)
                    .OrderBy(group => group.Key)
                    .Select(group => $"{group.Key}: {group.Count()}"));
        }
    }

    internal sealed class YourPalsEntryRowViewModel
    {
        public YourPalsEntryRowViewModel(
            YourPalsGroup group,
            YourPalsResolvedMember resolved,
            YourPalsManualDefinition manualDefinition)
        {
            GroupId = group?.GroupId ?? "(invalid)";
            GroupName = string.IsNullOrWhiteSpace(group?.Name) ? "(unnamed group)" : group.Name;
            Member = resolved?.Member;
            Status = resolved?.Status ?? YourPalsEntryStatus.Invalid;
            Details = resolved?.Reason ?? "The member could not be interpreted.";
            PalEntryKey = resolved?.Member?.PalEntryKey ?? "(missing key)";
            Kind = resolved?.Member?.Kind ?? "(missing kind)";
            InstanceId = resolved?.Member?.InstanceId ?? manualDefinition?.ManualDefinitionId ?? "—";
            PalName = DisplayName(resolved, manualDefinition);
            SourceScope = DisplaySourceScope(resolved);
            SourceKey = resolved?.SourceEntry?.SourceKey
                ?? resolved?.Member?.SourceKey
                ?? "—";
            Location = (resolved?.SourceEntry?.Record ?? resolved?.ResolvedRecord)?.Location?.Type.ToString() ?? "—";
        }

        public YourPalsMember Member { get; }
        public string GroupId { get; }
        public string GroupName { get; }
        public string PalEntryKey { get; }
        public string Kind { get; }
        public string PalName { get; }
        public string SourceScope { get; }
        public string SourceKey { get; }
        public string InstanceId { get; }
        public string Location { get; }
        public YourPalsEntryStatus Status { get; }
        public string Details { get; }

        private static string DisplayName(
            YourPalsResolvedMember resolved,
            YourPalsManualDefinition manualDefinition)
        {
            var record = resolved?.SourceEntry?.Record ?? resolved?.ResolvedRecord;
            if (record?.Pal != null)
            {
                if (!string.IsNullOrWhiteSpace(record.NickName))
                    return $"{record.NickName} ({YourPalsDisplayName.For(record.Pal)})";

                return YourPalsDisplayName.For(record.Pal);
            }

            return manualDefinition?.RawInternalName
                ?? resolved?.Member?.LastKnownDisplayName
                ?? resolved?.Member?.LastKnownInternalName
                ?? "Unknown Pal";
        }

        private static string DisplaySourceScope(YourPalsResolvedMember resolved)
        {
            if (resolved?.ManualDefinition != null)
                return "Manual definition";

            var sourceIdentity = resolved?.SourceEntry?.SourceIdentity;
            if (sourceIdentity.HasValue)
                return sourceIdentity.Value.StableKey;

            return resolved?.Member?.SourceIdentity?.StableKey ?? "Unavailable";
        }
    }

    internal sealed class YourPalsSourceRowViewModel
    {
        public YourPalsSourceRowViewModel(YourPalsSourceEntry entry)
        {
            Entry = entry;
            var record = entry?.Record;
            PalName = YourPalsDisplayName.For(record?.Pal);
            InstanceId = entry?.InstanceId ?? "—";
            SourceScope = entry?.SourceIdentity.StableKey ?? "Unavailable";
            SourceKey = entry?.SourceKey ?? "—";
            Location = record?.Location?.Type.ToString() ?? "—";
            Level = record?.Level.ToString() ?? "—";
            Gender = record?.Gender.ToString() ?? "—";
        }

        public YourPalsSourceEntry Entry { get; }
        public string PalName { get; }
        public string InstanceId { get; }
        public string SourceScope { get; }
        public string SourceKey { get; }
        public string Location { get; }
        public string Level { get; }
        public string Gender { get; }
        public string ReferenceKey => Entry == null
            ? "Unavailable"
            : string.Concat(
                StablePart(Entry.SourceIdentity.StableKey),
                StablePart(Entry.InstanceId),
                StablePart(Entry.SourceKey),
                StablePart(Entry.ContentFingerprint));

        private static string StablePart(string value) =>
            $"{value?.Length ?? -1}:{value}";
    }

    internal static class YourPalsDisplayName
    {
        public static string For(Pal pal)
        {
            if (pal == null)
                return "Unknown Pal";

            if (pal.LocalizedNames != null)
            {
                var localizedName = pal.LocalizedNames.GetValueOrElse(
                    Translator.CurrentLocale.ToFormalName(),
                    pal.Name);
                if (!string.IsNullOrWhiteSpace(localizedName))
                    return localizedName;
            }

            return pal.Name ?? pal.InternalName ?? "Unknown Pal";
        }
    }
}
