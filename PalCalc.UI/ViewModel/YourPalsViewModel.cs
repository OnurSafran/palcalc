using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PalCalc.Model;
using PalCalc.UI;
using PalCalc.UI.Localization;
using PalCalc.UI.Model;
using PalCalc.UI.ViewModel.Mapped;
using PalCalc.UI.ViewModel.PalDerived;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using System.Threading.Tasks;

namespace PalCalc.UI.ViewModel
{
    internal sealed partial class YourPalsViewModel : ObservableObject, IDisposable
    {
        private readonly Dispatcher dispatcher;
        private readonly YourPalsQueryState queryState;
        private readonly SavePalsSessionManager orphanedDocumentManager;
        private readonly Action navigateBack;
        private readonly Action refreshSource;
        private List<YourPalsEntryRowViewModel> allEntries = [];
        private IReadOnlyList<YourPalsGroupFilterOption> groupFilterOptions = [];
        private bool subscribedToLocale;
        private bool disposed;
        private IReadOnlyList<YourPalsAddPalOptionViewModel> addPalOptions = [];

        public static YourPalsViewModel DesignerInstance => new(null, Dispatcher.CurrentDispatcher);

        public YourPalsViewModel(
            SavePalsSession session,
            Dispatcher dispatcher,
            SavePalsSessionManager orphanedDocumentManager = null,
            Action navigateBack = null,
            Action refreshSource = null)
        {
            Session = session;
            this.dispatcher = dispatcher ?? Dispatcher.CurrentDispatcher;
            queryState = session?.QueryState ?? new YourPalsQueryState();
            this.orphanedDocumentManager = orphanedDocumentManager;
            this.navigateBack = navigateBack;
            this.refreshSource = refreshSource;
            useAsSolverSource = Session?.Identity is SaveIdentity identity &&
                AppSettings.Current?.YourPalsSolverSourceBySave?.TryGetValue(identity.CanonicalKey, out var savedPreference) == true &&
                savedPreference;
            BackCommand = new RelayCommand(() => this.navigateBack?.Invoke(), () => this.navigateBack != null);
            RefreshCommand = new RelayCommand(Refresh);
            DiscardChangesAndReloadCommand = new RelayCommand(DiscardChangesAndReload, CanDiscardChangesAndReload);
            CreateDocumentCommand = new RelayCommand(CreateDocument, CanCreateNewDocument);
            ShowAllPalsCommand = new RelayCommand(ShowAllPals);
            ReviewAttentionCommand = new RelayCommand(ReviewAttention);
            ClearQueryCommand = new RelayCommand(ClearQuery);
            ToggleSortDirectionCommand = new RelayCommand(ToggleSortDirection);
            SaveCommand = new AsyncRelayCommand(SaveAsync, CanSave);
            CreateGroupCommand = new RelayCommand(CreateGroup, CanCreateGroup);
            RenameGroupCommand = new RelayCommand(RenameGroup, CanRenameGroup);
            DeleteGroupCommand = new RelayCommand(DeleteGroup, CanDeleteGroup);
            MoveGroupUpCommand = new RelayCommand(MoveGroupUp, CanMoveGroup);
            MoveGroupDownCommand = new RelayCommand(MoveGroupDown, CanMoveGroup);
            AddSelectedSourceCommand = new RelayCommand(AddSelectedSource, CanAddSelectedSource);
            AddManualDefinitionCommand = new RelayCommand(AddManualDefinition, CanAddManualDefinition);
            UpdateManualDefinitionCommand = new RelayCommand(UpdateManualDefinition, CanUpdateManualDefinition);
            RemoveSelectedEntryCommand = new RelayCommand(RemoveSelectedEntry, CanRemoveSelectedEntry);
            AddPalCommand = new RelayCommand(OpenAddPalPicker, CanOpenAddPalPicker);
            CloseOverlayCommand = new RelayCommand(CloseOverlay);
            AddSelectedPalCommand = new RelayCommand(AddSelectedPal, CanAddSelectedPal);
            RepairSelectedEntryCommand = new RelayCommand(OpenSelectedEntryAction, CanOpenSelectedEntryAction);
            RepairEntryCommand = new RelayCommand<YourPalsEntryRowViewModel>(OpenEntryAction, CanOpenEntryAction);
            OpenManualEditorCommand = new RelayCommand(OpenManualEditor, CanOpenManualEditor);
            EditSelectedManualCommand = new RelayCommand(OpenSelectedManualEditor, () => CanEditSelectedManual);
            SaveManualEditorCommand = new RelayCommand(SaveManualEditor, CanSaveManualEditor);
            CancelManualEditorCommand = new RelayCommand(CancelManualEditor);
            CloseDetailsCommand = new RelayCommand(CloseDetails);
            RebindSelectedEntryCommand = new RelayCommand(RebindSelectedEntry, CanRebindSelectedEntry);
            BulkRebindMatchingMembersCommand = new RelayCommand(BulkRebindMatchingMembers, CanBulkRebindMatchingMembers);
            RemoveDuplicateMembersCommand = new RelayCommand(RemoveDuplicateMembers, CanRemoveDuplicateMembers);
            RemoveMissingMembersCommand = new RelayCommand(RemoveMissingMembers, CanRemoveMissingMembers);
            RepairRecoveredDocumentCommand = new RelayCommand(RepairRecoveredDocument, CanRepairRecoveredDocumentCommand);
            RefreshOrphanedDocumentsCommand = new RelayCommand(RefreshOrphanedDocuments);
            DeleteSelectedOrphanedDocumentCommand = new RelayCommand(
                DeleteSelectedOrphanedDocument,
                CanDeleteSelectedOrphanedDocument);
            SetLocalizedQueryOptions();

            if (Session != null)
            {
                Session.Refreshed += Session_Refreshed;
                Translator.LocaleUpdated += Translator_LocaleUpdated;
                subscribedToLocale = true;
                UpdateFromSession();
            }
            else
            {
                SaveDisplayName = Localized(LocalizationCodes.LC_YOUR_PALS_NO_SAVE_SELECTED);
                SaveStateText = Localized(LocalizationCodes.LC_YOUR_PALS_SAVE_STATE_READ_ONLY);
                SessionState = Localized(LocalizationCodes.LC_YOUR_PALS_NO_SESSION);
                SourceState = Localized(LocalizationCodes.LC_YOUR_PALS_SOURCE_UNAVAILABLE_SHORT);
                RecoveryState = Localized(LocalizationCodes.LC_YOUR_PALS_NO_RECOVERY_DETAILS);
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
        public IRelayCommand ShowAllPalsCommand { get; }
        public IRelayCommand ReviewAttentionCommand { get; }
        public IRelayCommand ClearQueryCommand { get; }
        public IRelayCommand ToggleSortDirectionCommand { get; }
        public IAsyncRelayCommand SaveCommand { get; }
        public IRelayCommand CreateGroupCommand { get; }
        public IRelayCommand RenameGroupCommand { get; }
        public IRelayCommand DeleteGroupCommand { get; }
        public IRelayCommand MoveGroupUpCommand { get; }
        public IRelayCommand MoveGroupDownCommand { get; }
        public IRelayCommand AddSelectedSourceCommand { get; }
        public IRelayCommand AddManualDefinitionCommand { get; }
        public IRelayCommand UpdateManualDefinitionCommand { get; }
        public IRelayCommand RemoveSelectedEntryCommand { get; }
        public IRelayCommand AddPalCommand { get; }
        public IRelayCommand CloseOverlayCommand { get; }
        public IRelayCommand AddSelectedPalCommand { get; }
        public IRelayCommand RepairSelectedEntryCommand { get; }
        public IRelayCommand<YourPalsEntryRowViewModel> RepairEntryCommand { get; }
        public IRelayCommand OpenManualEditorCommand { get; }
        public IRelayCommand EditSelectedManualCommand { get; }
        public IRelayCommand SaveManualEditorCommand { get; }
        public IRelayCommand CancelManualEditorCommand { get; }
        public IRelayCommand CloseDetailsCommand { get; }
        public IRelayCommand RebindSelectedEntryCommand { get; }
        public IRelayCommand BulkRebindMatchingMembersCommand { get; }
        public IRelayCommand RemoveDuplicateMembersCommand { get; }
        public IRelayCommand RemoveMissingMembersCommand { get; }
        public IRelayCommand RepairRecoveredDocumentCommand { get; }
        public IRelayCommand RefreshOrphanedDocumentsCommand { get; }
        public IRelayCommand DeleteSelectedOrphanedDocumentCommand { get; }

        public IReadOnlyList<YourPalsStatusFilterOption> StatusFilterOptions { get; private set; } = [];

        public IReadOnlyList<YourPalsSortOption> SortOptions { get; private set; } = [];

        public IReadOnlyList<YourPalsGroupFilterOption> GroupFilterOptions => groupFilterOptions;

        public IReadOnlyList<YourPalsAddPalOptionViewModel> AddPalOptions => addPalOptions;

        public IReadOnlyList<PalViewModel> ManualPalOptions => PalViewModel.All;

        public IReadOnlyList<CustomPalInstanceGender> ManualGenderOptions => CustomPalInstanceGender.Options;

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
        private YourPalsAddPalOptionViewModel selectedAddPal;

        [ObservableProperty]
        private YourPalsGroupSummaryViewModel selectedAddGroup;

        [ObservableProperty]
        private string newGroupName = "";

        [ObservableProperty]
        private string renameGroupName = "";

        [ObservableProperty]
        private string manualInternalName = "";

        [ObservableProperty]
        private PalViewModel selectedManualPal;

        [ObservableProperty]
        private CustomPalInstanceGender selectedManualGender = CustomPalInstanceGender.Male;

        [ObservableProperty]
        private string manualLevelText = "1";

        [ObservableProperty]
        private string manualNickname = "";

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
        private string saveScope = "";

        [ObservableProperty]
        private string sessionState = "";

        [ObservableProperty]
        private string sourceState = "";

        [ObservableProperty]
        private string recoveryState = "";

        [ObservableProperty]
        private string recoveryGuidance = "";

        [ObservableProperty]
        private string saveDisplayName = "";

        [ObservableProperty]
        private string saveStateText = "";

        [ObservableProperty]
        private bool isSaving;

        [ObservableProperty]
        private bool isAddPalPickerOpen;

        [ObservableProperty]
        private bool isManualEditorOpen;

        [ObservableProperty]
        private bool isDetailsOpen;

        [ObservableProperty]
        private bool isAttentionReviewActive;

        [ObservableProperty]
        private bool isRepairMode;

        [ObservableProperty]
        private bool isEditingManualPal;

        [ObservableProperty]
        private string addPalSearchText = "";

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
                OnPropertyChanged(nameof(HasSearchText));
                OnPropertyChanged(nameof(HasActiveQuery));
                ApplyQuery();
            }
        }

        public bool HasSearchText => !string.IsNullOrWhiteSpace(SearchText);

        public bool HasAddPalSearchText => !string.IsNullOrWhiteSpace(AddPalSearchText);

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

        public string SortDirectionText => Localized(IsSortAscending
            ? LocalizationCodes.LC_YOUR_PALS_ASCENDING
            : LocalizationCodes.LC_YOUR_PALS_DESCENDING);

        public string EntryCountText => $"{FilteredEntryCount} / {TotalEntryCount}";

        public string ShowingText => $"{Localized(LocalizationCodes.LC_YOUR_PALS_SHOWING)} {EntryCountText}";

        public string SaveContextText => $"{Localized(LocalizationCodes.LC_YOUR_PALS_SAVE_SCOPE)} {SaveDisplayName}";

        public bool CanEdit => Session?.CanEdit == true;

        public bool CanCreateDocument => Session?.CanCreateDocument == true;

        public bool CanRepairRecoveredDocument => Session?.CanRepairRecoveredDocument == true;

        public string PageDescription => Localized(LocalizationCodes.LC_YOUR_PALS_PURPOSE);

        public string SelectedCollectionTitle => IsAttentionReviewActive
            ? Localized(LocalizationCodes.LC_YOUR_PALS_ATTENTION_VIEW)
            : SelectedGroupSummary?.Name ?? Localized(LocalizationCodes.LC_YOUR_PALS_ALL_PALS);

        public string AddPalDestinationText => Localized(
            LocalizationCodes.LC_YOUR_PALS_ADD_TO_GROUP,
            new { group = SelectedAddGroup?.Name ?? Localized(LocalizationCodes.LC_YOUR_PALS_SELECT_GROUP) });

        public string PickerContextText => IsRepairMode
            ? Localized(
                LocalizationCodes.LC_YOUR_PALS_REPLACE_IN_GROUP,
                new
                {
                    pal = SelectedEntry?.PalName ?? Localized(LocalizationCodes.LC_YOUR_PALS_UNKNOWN_PAL),
                    group = SelectedEntry?.GroupName ?? SelectedAddGroup?.Name ??
                        Localized(LocalizationCodes.LC_YOUR_PALS_SELECT_GROUP),
                })
            : AddPalDestinationText;

        public string ManualEditorTitle => Localized(IsEditingManualPal
            ? LocalizationCodes.LC_YOUR_PALS_EDIT_MANUAL_PAL
            : LocalizationCodes.LC_YOUR_PALS_ADD_MANUAL_PAL);

        public string ManualEditorActionText => Localized(IsEditingManualPal
            ? LocalizationCodes.LC_YOUR_PALS_SAVE_MANUAL_PAL
            : LocalizationCodes.LC_YOUR_PALS_ADD_MANUAL_PAL);

        public string AttentionActionText => SelectedEntry == null
            ? ""
            : SelectedEntry.Status switch
            {
                YourPalsEntryStatus.Stale => Localized(LocalizationCodes.LC_YOUR_PALS_FIND_REPLACEMENT),
                YourPalsEntryStatus.Conflict => Localized(LocalizationCodes.LC_YOUR_PALS_CHOOSE_COPY),
                YourPalsEntryStatus.Unresolved when CanEditSelectedManual => Localized(LocalizationCodes.LC_YOUR_PALS_EDIT_MANUAL_PAL),
                YourPalsEntryStatus.Invalid when CanEditSelectedManual => Localized(LocalizationCodes.LC_YOUR_PALS_EDIT_MANUAL_PAL),
                YourPalsEntryStatus.Unresolved or YourPalsEntryStatus.Invalid => Localized(LocalizationCodes.LC_YOUR_PALS_REVIEW),
                _ => "",
            };

        public string AttentionActionTooltip => SelectedEntry == null
            ? ""
            : SelectedEntry.Status switch
            {
                YourPalsEntryStatus.Stale => Localized(LocalizationCodes.LC_YOUR_PALS_FIND_REPLACEMENT_DESCRIPTION),
                YourPalsEntryStatus.Conflict => Localized(LocalizationCodes.LC_YOUR_PALS_CHOOSE_COPY_DESCRIPTION),
                _ => SelectedEntry.StatusExplanation,
            };

        public string RepairPickerTitle => SelectedEntry?.Status == YourPalsEntryStatus.Conflict
            ? Localized(LocalizationCodes.LC_YOUR_PALS_CHOOSE_COPY)
            : Localized(LocalizationCodes.LC_YOUR_PALS_FIND_REPLACEMENT);

        public string RepairPickerActionText => SelectedEntry?.Status == YourPalsEntryStatus.Conflict
            ? Localized(LocalizationCodes.LC_YOUR_PALS_USE_SELECTED_COPY)
            : Localized(LocalizationCodes.LC_YOUR_PALS_USE_REPLACEMENT);

        public string PickerActionText => IsRepairMode ? RepairPickerActionText : AddPalDestinationText;

        public bool HasAddPalOptions => AddPalOptions.Count > 0;

        public bool HasSelectedAddPal => SelectedAddPal != null;

        public bool CanEditSelectedManual => CanEdit &&
            SelectedEntry?.Member?.KnownKind == YourPalsMemberKind.ManualDefinitionReference;

        public bool IsSelectedEntryProblem => SelectedEntry != null &&
            SelectedEntry.Status != YourPalsEntryStatus.Resolved;

        public int CurrentCollectionEntryCount => SelectedGroupId == null
            ? allEntries.Count
            : allEntries.Count(entry => string.Equals(entry.GroupId, SelectedGroupId, StringComparison.Ordinal));

        public bool HasEmptyCollection => HasGroups &&
            CurrentCollectionEntryCount == 0 &&
            !HasSourceUnavailableCollectionState &&
            !HasNoSourceEntriesCollectionState;

        public bool HasNoQueryMatches => CurrentCollectionEntryCount > 0 &&
            FilteredEntryCount == 0 &&
            (!string.IsNullOrWhiteSpace(SearchText) ||
             SelectedStatusFilter != YourPalsStatusFilter.All ||
             IsAttentionReviewActive);

        public bool HasTextOrStatusQuery =>
            !string.IsNullOrWhiteSpace(SearchText) ||
            SelectedStatusFilter != YourPalsStatusFilter.All ||
            IsAttentionReviewActive;

        public bool HasNoSourceEntries => Session?.IsSourceAvailable == true && !HasSourceEntries;

        public bool IsSourceUnavailable => Session != null && !Session.IsSourceAvailable;

        public bool HasSourceUnavailableCollectionState => HasGroups &&
            CurrentCollectionEntryCount == 0 &&
            !HasTextOrStatusQuery &&
            IsSourceUnavailable;

        public bool HasNoSourceEntriesCollectionState => HasGroups &&
            CurrentCollectionEntryCount == 0 &&
            !HasTextOrStatusQuery &&
            !IsSourceUnavailable &&
            HasNoSourceEntries;

        public bool IsSaveAttention => Session?.IsDirty == true ||
            Session?.State is SavePalsSessionState.WriteFailed or SavePalsSessionState.ExternalConflict ||
            Session?.IsReadOnly == true;

        public bool IsSaveHealthy => Session != null && !IsSaveAttention && !IsSaving;

        public bool IsAllPalsSelected => !IsAttentionReviewActive && SelectedGroupSummary == null;

        public int AttentionEntryCount => allEntries.Count(entry => entry.Status != YourPalsEntryStatus.Resolved);

        public bool HasAttentionEntries => AttentionEntryCount > 0;

        // "Missing" is specifically a Pal that left the save, which is the only
        // status that bulk removal is safe for; the others may still be repairable.
        public int MissingEntryCount =>
            allEntries.Count(entry => entry.Status == YourPalsEntryStatus.Stale);

        public bool HasMissingEntries => MissingEntryCount > 0;

        public string CollectionSummaryText => Localized(
            LocalizationCodes.LC_YOUR_PALS_COLLECTION_SUMMARY,
            new
            {
                pals = TotalEntryCount,
                groups = Groups.Count,
                attention = AttentionEntryCount,
            });

        public string OrphanedDocumentCountText =>
            Localized(
                LocalizationCodes.LC_YOUR_PALS_ORPHANED_DOCUMENT_COUNT,
                new { count = OrphanedDocuments.Count });

        private YourPalsSolverSourceProjection solverSource = new([], []);

        // Cached rather than rebuilt per access: several bindings read it, and the
        // solver must run against exactly the projection whose counts were shown.
        public YourPalsSolverSourceProjection SolverSource => solverSource;

        public int SolverReadyCount => SolverSource.Entries.Count;

        public int SolverExcludedCount => SolverSource.ExcludedEntries.Count;

        public string SolverSourceSummaryText => Localized(
            LocalizationCodes.LC_YOUR_PALS_SOLVER_READY_COUNT,
            new { count = SolverReadyCount });

        public string SolverSourceExcludedText => Localized(
            LocalizationCodes.LC_YOUR_PALS_SOLVER_EXCLUDED_COUNT,
            new { count = SolverExcludedCount });

        // The solver silently drops references whose Pal is gone. Name that in the
        // panel so short or empty results have a visible explanation.
        public int SolverMissingCount => SolverSource.ExcludedEntries
            .Count(excluded => excluded.Status == YourPalsEntryStatus.Stale);

        public bool HasSolverMissingEntries => UseAsSolverSource && SolverMissingCount > 0;

        public bool IsSolverSourceEmpty => UseAsSolverSource && SolverReadyCount == 0;

        public string SolverSourceMissingText => Localized(
            LocalizationCodes.LC_YOUR_PALS_SOLVER_MISSING_COUNT,
            new { count = SolverMissingCount });

        public string SolverSourceEmptyText =>
            Localized(LocalizationCodes.LC_YOUR_PALS_SOLVER_SOURCE_EMPTY);

        public string SolverSourceStateText => UseAsSolverSource
            ? Localized(LocalizationCodes.LC_YOUR_PALS_SOLVER_INCLUDED)
            : Localized(LocalizationCodes.LC_YOUR_PALS_SOLVER_NOT_INCLUDED);

        private bool useAsSolverSource;

        public bool UseAsSolverSource
        {
            get => useAsSolverSource;
            set
            {
                if (!SetProperty(ref useAsSolverSource, value))
                    return;

                if (Session?.Identity is SaveIdentity identity && AppSettings.Current != null)
                {
                    var preferences = AppSettings.Current.YourPalsSolverSourceBySave ??= new();
                    var hadPrevious = preferences.TryGetValue(identity.CanonicalKey, out var previous);
                    preferences[identity.CanonicalKey] = value;
                    if (!Storage.TrySaveAppSettings(AppSettings.Current))
                    {
                        if (hadPrevious)
                            preferences[identity.CanonicalKey] = previous;
                        else
                            preferences.Remove(identity.CanonicalKey);

                        useAsSolverSource = hadPrevious && previous;
                        OnPropertyChanged(nameof(UseAsSolverSource));
                        EditStatus = Localized(LocalizationCodes.LC_YOUR_PALS_SAVE_STATE_FAILED);
                    }
                }

                OnPropertyChanged(nameof(SolverSourceStateText));
                OnPropertyChanged(nameof(HasSolverMissingEntries));
                OnPropertyChanged(nameof(IsSolverSourceEmpty));
            }
        }

        public string SelectedGroupId
        {
            get => queryState.SelectedGroupId;
            private set
            {
                if (string.Equals(queryState.SelectedGroupId, value, StringComparison.Ordinal))
                    return;

                queryState.SelectedGroupId = value;
                if (value != null)
                    IsAttentionReviewActive = false;
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

            refreshSource?.Invoke();
            if (refreshSource == null)
                Session.RefreshCurrent();
        }

        private void ShowAllPals()
        {
            IsAttentionReviewActive = false;
            SelectedGroupId = null;
        }

        private void ReviewAttention()
        {
            IsAttentionReviewActive = true;
            SelectedGroupId = null;
            SelectedStatusFilter = YourPalsStatusFilter.All;
            SearchText = "";
        }

        private void OpenAddPalPicker()
        {
            if (Session == null || Groups.Count == 0)
                return;

            SelectedAddGroup = SelectedGroupSummary ?? Groups[0];
            AddPalSearchText = "";
            SelectedAddPal = null;
            IsManualEditorOpen = false;
            IsAddPalPickerOpen = true;
            UpdateAddPalOptions();
        }

        private void CloseOverlay()
        {
            IsAddPalPickerOpen = false;
            IsManualEditorOpen = false;
            IsRepairMode = false;
            SelectedAddPal = null;
        }

        private void AddSelectedPal()
        {
            if (Session == null || SelectedAddPal == null)
                return;

            if (IsRepairMode)
            {
                if (SelectedEntry == null)
                {
                    EditStatus = Localized(LocalizationCodes.LC_YOUR_PALS_REVIEW);
                    return;
                }

                if (!Session.TryRebindImportedMember(
                        SelectedEntry.GroupId,
                        SelectedEntry.PalEntryKey,
                        SelectedAddPal.SourceEntry,
                        out var rebindError))
                {
                    EditStatus = rebindError;
                    return;
                }

                EditStatus = Localized(
                    LocalizationCodes.LC_YOUR_PALS_REBOUND_ENTRY,
                    new { pal = SelectedAddPal.PalName });
                var repairedKey = SelectedEntry.PalEntryKey;
                CloseOverlay();
                UpdateFromSession(repairedKey);
                return;
            }

            if (SelectedAddGroup == null)
                return;

            if (Session.TryAddImportedMember(
                    SelectedAddGroup.GroupId,
                    SelectedAddPal.SourceEntry,
                    out var entryKey,
                    out var error))
            {
                EditStatus = Localized(
                    LocalizationCodes.LC_YOUR_PALS_ADDED_TO_GROUP,
                    new { pal = SelectedAddPal.PalName, group = SelectedAddGroup.Name });
                IsAddPalPickerOpen = false;
                SelectedAddPal = null;
                UpdateFromSession(entryKey);
            }
            else
            {
                EditStatus = error;
            }
        }

        private void OpenManualEditor()
        {
            if (!CanOpenManualEditor())
                return;

            IsEditingManualPal = false;
            SelectedManualPal = null;
            SelectedManualGender = CustomPalInstanceGender.Male;
            ManualLevelText = "1";
            ManualNickname = "";
            IsManualEditorOpen = true;
            IsAddPalPickerOpen = true;
            IsRepairMode = false;
            OnPropertyChanged(nameof(ManualEditorTitle));
            OnPropertyChanged(nameof(ManualEditorActionText));
            SaveManualEditorCommand.NotifyCanExecuteChanged();
        }

        private void OpenSelectedManualEditor()
        {
            if (!CanEditSelectedManual)
                return;

            var definition = Session?.Document?.ManualDefinitions?.FirstOrDefault(candidate =>
                string.Equals(candidate?.ManualDefinitionId, SelectedEntry?.Member?.ManualDefinitionId, StringComparison.Ordinal));
            var record = Session?.ResolvedMembers?.FirstOrDefault(candidate =>
                string.Equals(candidate?.Member?.PalEntryKey, SelectedEntry?.PalEntryKey, StringComparison.Ordinal))?.ResolvedRecord;
            var internalName = record?.Pal?.InternalName ?? definition?.RawInternalName;
            SelectedManualPal = PalViewModel.All.FirstOrDefault(pal =>
                string.Equals(pal.ModelObject.InternalName, internalName, StringComparison.OrdinalIgnoreCase));
            SelectedManualGender = ManualGenderOptions.FirstOrDefault(option => option.Value == record?.Gender) ??
                ManualGenderOptions.First();
            ManualLevelText = record?.Level.ToString(CultureInfo.InvariantCulture) ?? ReadManualInt(definition, "level", "1");
            ManualNickname = record?.NickName ?? ReadManualString(definition, "nickname");
            IsEditingManualPal = true;
            IsManualEditorOpen = true;
            IsAddPalPickerOpen = true;
            IsRepairMode = false;
            SelectedAddGroup = Groups.FirstOrDefault(group =>
                string.Equals(group.GroupId, SelectedEntry.GroupId, StringComparison.Ordinal));
            OnPropertyChanged(nameof(ManualEditorTitle));
            OnPropertyChanged(nameof(ManualEditorActionText));
            SaveManualEditorCommand.NotifyCanExecuteChanged();
        }

        private void SaveManualEditor()
        {
            if (Session == null || SelectedManualPal == null || !int.TryParse(ManualLevelText, out var level) || level < 1)
            {
                EditStatus = Localized(LocalizationCodes.LC_YOUR_PALS_MANUAL_EDITOR_INVALID);
                return;
            }

            var rawValues = new Dictionary<string, JToken>(StringComparer.OrdinalIgnoreCase)
            {
                ["gender"] = new JValue(SelectedManualGender.Value.ToString()),
                ["level"] = new JValue(level),
            };
            if (IsEditingManualPal || !string.IsNullOrWhiteSpace(ManualNickname))
                rawValues["nickname"] = new JValue(ManualNickname?.Trim() ?? "");

            if (IsEditingManualPal)
            {
                if (Session.TryUpdateManualDefinition(
                        SelectedEntry?.Member?.ManualDefinitionId,
                        SelectedManualPal.ModelObject.InternalName,
                        rawValues,
                        out var error))
                {
                    EditStatus = Localized(LocalizationCodes.LC_YOUR_PALS_MANUAL_UPDATED);
                    CloseOverlay();
                    UpdateFromSession(SelectedEntry?.PalEntryKey);
                }
                else
                {
                    EditStatus = error;
                }

                return;
            }

            if (SelectedAddGroup == null)
            {
                EditStatus = Localized(LocalizationCodes.LC_YOUR_PALS_SELECT_GROUP);
                return;
            }

            if (Session.TryAddManualDefinition(
                    SelectedAddGroup.GroupId,
                    SelectedManualPal.ModelObject.InternalName,
                    rawValues,
                    out _,
                    out var entryKey,
                    out var addError))
            {
                EditStatus = Localized(
                    LocalizationCodes.LC_YOUR_PALS_ADDED_TO_GROUP,
                    new { pal = YourPalsDisplayName.For(SelectedManualPal.ModelObject), group = SelectedAddGroup.Name });
                CloseOverlay();
                UpdateFromSession(entryKey);
            }
            else
            {
                EditStatus = addError;
            }
        }

        private void CancelManualEditor()
        {
            IsManualEditorOpen = false;
            SelectedManualPal = null;
            SaveManualEditorCommand.NotifyCanExecuteChanged();
        }

        private void CloseDetails()
        {
            IsDetailsOpen = false;
            SelectedEntry = null;
        }

        private void OpenSelectedEntryAction()
        {
            if (SelectedEntry == null)
                return;

            OpenEntryAction(SelectedEntry);
        }

        private void OpenEntryAction(YourPalsEntryRowViewModel entry)
        {
            if (entry == null)
                return;

            SelectedEntry = entry;
            if (entry.Member?.KnownKind == YourPalsMemberKind.ManualDefinitionReference &&
                (entry.Status == YourPalsEntryStatus.Unresolved || entry.Status == YourPalsEntryStatus.Invalid))
            {
                OpenSelectedManualEditor();
                return;
            }

            if (entry.Member?.KnownKind == YourPalsMemberKind.ImportedReference && CanEdit)
            {
                SelectedAddGroup = Groups.FirstOrDefault(group =>
                    string.Equals(group.GroupId, entry.GroupId, StringComparison.Ordinal));
                AddPalSearchText = "";
                SelectedAddPal = null;
                IsManualEditorOpen = false;
                IsRepairMode = true;
                IsAddPalPickerOpen = true;
                UpdateAddPalOptions();
            }
        }

        private bool CanOpenSelectedEntryAction() => CanOpenEntryAction(SelectedEntry);

        private bool CanOpenEntryAction(YourPalsEntryRowViewModel entry) =>
            entry != null && entry.Status != YourPalsEntryStatus.Resolved && CanEdit &&
            (entry.Member?.KnownKind == YourPalsMemberKind.ImportedReference ||
             entry.Member?.KnownKind == YourPalsMemberKind.ManualDefinitionReference);

        private bool CanOpenAddPalPicker() => CanEdit && Groups.Count > 0;

        private bool CanAddSelectedPal() => CanEdit &&
            SelectedAddPal != null &&
            (IsRepairMode ? SelectedEntry != null : SelectedAddGroup != null) &&
            !SelectedAddPal.IsAlreadyInSelectedGroup &&
            Session?.CanUseSourceEntry(SelectedAddPal.SourceEntry) == true;

        private bool CanOpenManualEditor() => CanEdit && SelectedAddGroup != null;

        private bool CanSaveManualEditor() => CanEdit && SelectedManualPal != null &&
            int.TryParse(ManualLevelText, out var level) && level >= 1;

        private void UpdateAddPalOptions()
        {
            var selectedGroupId = SelectedAddGroup?.GroupId;
            var existingKeys = allEntries
                .Where(entry => string.Equals(entry.GroupId, selectedGroupId, StringComparison.Ordinal))
                .Where(entry => !IsRepairMode || !string.Equals(
                    entry.PalEntryKey,
                    SelectedEntry?.PalEntryKey,
                    StringComparison.Ordinal))
                .Select(entry => entry.ImportIdentityKey)
                .Where(key => !string.IsNullOrEmpty(key))
                .ToHashSet(StringComparer.Ordinal);
            var search = AddPalSearchText?.Trim() ?? "";
            var culture = QueryCulture();
            var compareInfo = culture.CompareInfo;
            var options = SourceEntries
                .Where(source => Session?.CanUseSourceEntry(source.Entry) == true)
                .Where(source => string.IsNullOrWhiteSpace(search) ||
                    Contains(source.PalName, search, compareInfo, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) ||
                    Contains(source.Nickname, search, compareInfo, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) ||
                    Contains(source.Location, search, compareInfo, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace))
                .Select(source => new YourPalsAddPalOptionViewModel(source, existingKeys.Contains(source.ImportIdentityKey)))
                .ToList();
            addPalOptions = options.AsReadOnly();
            OnPropertyChanged(nameof(AddPalOptions));
            OnPropertyChanged(nameof(HasAddPalOptions));
            AddSelectedPalCommand.NotifyCanExecuteChanged();
        }

        private static string ReadManualString(YourPalsManualDefinition definition, string key) =>
            definition?.RawValues?.FirstOrDefault(pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)).Value?.Type == JTokenType.String
                ? definition.RawValues.First(pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)).Value.Value<string>()
                : "";

        private static string ReadManualInt(YourPalsManualDefinition definition, string key, string fallback) =>
            definition?.RawValues?.FirstOrDefault(pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)).Value is JToken token &&
            int.TryParse(token.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value.ToString(CultureInfo.InvariantCulture)
                : fallback;

        private void DiscardChangesAndReload()
        {
            if (Session == null)
                return;

            var confirmation = MessageBox.Show(
                Localized(LocalizationCodes.LC_YOUR_PALS_DISCARD_CONFIRM),
                Localized(LocalizationCodes.LC_YOUR_PALS_DISCARD_TITLE),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirmation != MessageBoxResult.Yes)
                return;

            if (Session.TryDiscardChangesAndReload(out var error))
                EditStatus = Localized(LocalizationCodes.LC_YOUR_PALS_DISCARDED_RELOADED);
            else
                EditStatus = error;
        }

        private bool CanDiscardChangesAndReload() => Session?.CanDiscardChangesAndReload == true;

        private void CreateDocument()
        {
            if (Session == null)
                return;

            if (Session.TryCreateDocument(out var error))
                EditStatus = Localized(LocalizationCodes.LC_YOUR_PALS_DOCUMENT_CREATED);
            else
                EditStatus = error;
        }

        private bool CanCreateNewDocument() => CanCreateDocument;

        private async Task SaveAsync()
        {
            if (Session == null || IsSaving)
                return;

            IsSaving = true;
            SaveStateText = Localized(LocalizationCodes.LC_YOUR_PALS_SAVE_STATE_SAVING);
            OnPropertyChanged(nameof(IsSaveHealthy));
            try
            {
                if (await Task.Run(() => Session.TrySave()))
                {
                    EditStatus = Localized(LocalizationCodes.LC_YOUR_PALS_SAVE_STATE_SAVED);
                }
                else
                {
                    EditStatus = Session.Diagnostics.LastOrDefault(diagnostic =>
                        diagnostic.Code == YourPalsDiagnosticCode.WriteFailed ||
                        diagnostic.Code == YourPalsDiagnosticCode.ExternalConflict)?.Message
                        ?? Localized(LocalizationCodes.LC_YOUR_PALS_SAVE_FAILED);
                }

                UpdateFromSession();
            }
            finally
            {
                IsSaving = false;
                SaveStateText = Session == null
                    ? Localized(LocalizationCodes.LC_YOUR_PALS_SAVE_STATE_READ_ONLY)
                    : BuildSaveStateText(Session);
                OnPropertyChanged(nameof(IsSaveHealthy));
            }
        }

        private void CreateGroup()
        {
            if (Session == null)
                return;

            if (Session.TryCreateGroup(NewGroupName, out _, out var error))
            {
                NewGroupName = "";
                EditStatus = Localized(LocalizationCodes.LC_YOUR_PALS_GROUP_CREATED);
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
                EditStatus = Localized(LocalizationCodes.LC_YOUR_PALS_GROUP_RENAMED);
            else
                EditStatus = error;
        }

        private void DeleteGroup()
        {
            if (Session == null)
                return;

            if (Session.TryDeleteGroup(SelectedGroupSummary?.GroupId, out var error))
            {
                EditStatus = Localized(LocalizationCodes.LC_YOUR_PALS_GROUP_DELETED);
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
                EditStatus = Localized(LocalizationCodes.LC_YOUR_PALS_GROUP_ORDER_CHANGED);
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
                EditStatus = Localized(
                    LocalizationCodes.LC_YOUR_PALS_ADDED_TO_GROUP,
                    new
                    {
                        pal = SelectedSourceEntry?.PalName ?? Localized(LocalizationCodes.LC_YOUR_PALS_UNKNOWN_PAL),
                        group = SelectedGroupSummary?.Name ?? Localized(LocalizationCodes.LC_YOUR_PALS_SELECT_GROUP),
                    });
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
                var manualName = ManualInternalName;
                ManualInternalName = "";
                EditStatus = Localized(
                    LocalizationCodes.LC_YOUR_PALS_ADDED_TO_GROUP,
                    new
                    {
                        pal = manualName,
                        group = SelectedGroupSummary?.Name ?? Localized(LocalizationCodes.LC_YOUR_PALS_SELECT_GROUP),
                    });
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
                EditStatus = Localized(LocalizationCodes.LC_YOUR_PALS_MANUAL_UPDATED);
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

            var confirmation = MessageBox.Show(
                Localized(
                    LocalizationCodes.LC_YOUR_PALS_REMOVE_MEMBER_CONFIRM,
                    new
                    {
                        pal = SelectedEntry?.PalName ?? Localized(LocalizationCodes.LC_YOUR_PALS_UNKNOWN_PAL),
                        group = SelectedEntry?.GroupName ?? Localized(LocalizationCodes.LC_YOUR_PALS_UNNAMED_GROUP),
                    }),
                Localized(LocalizationCodes.LC_YOUR_PALS_REMOVE_FROM_GROUP),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirmation != MessageBoxResult.Yes)
                return;

            if (Session.TryRemoveMember(
                    SelectedEntry?.GroupId,
                    SelectedEntry?.PalEntryKey,
                    out var error))
            {
                EditStatus = Localized(LocalizationCodes.LC_YOUR_PALS_MEMBER_REMOVED);
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
                EditStatus = Localized(
                    LocalizationCodes.LC_YOUR_PALS_REBOUND_ENTRY,
                    new { pal = SelectedSourceEntry?.PalName ?? Localized(LocalizationCodes.LC_YOUR_PALS_UNKNOWN_PAL) });
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
                EditStatus = Localized(
                    LocalizationCodes.LC_YOUR_PALS_REBOUND_MATCHING,
                    new { count = repairedCount });
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
                EditStatus = Localized(
                    LocalizationCodes.LC_YOUR_PALS_DUPLICATES_REMOVED,
                    new { count = summary.RemovedDuplicateMembers });
            }
            else
            {
                EditStatus = error;
            }
        }

        private void RemoveMissingMembers()
        {
            if (Session == null)
                return;

            var confirmation = MessageBox.Show(
                Localized(
                    LocalizationCodes.LC_YOUR_PALS_REMOVE_MISSING_CONFIRM,
                    new { count = MissingEntryCount }),
                Localized(LocalizationCodes.LC_YOUR_PALS_REMOVE_MISSING),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirmation != MessageBoxResult.Yes)
                return;

            if (Session.TryRemoveMissingMembers(out var removedCount, out var error))
            {
                EditStatus = Localized(
                    LocalizationCodes.LC_YOUR_PALS_MISSING_REMOVED,
                    new { count = removedCount });
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
                EditStatus = Localized(
                    LocalizationCodes.LC_YOUR_PALS_RECOVERED_SAVED,
                    new { count = summary.TotalChanges });
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

            // A document whose owner could not be read is still deletable, but say
            // plainly that it could not be verified before removing it.
            var result = MessageBox.Show(
                Localized(
                    SelectedOrphanedDocument.OwnerSaveIdentity.HasValue
                        ? LocalizationCodes.LC_YOUR_PALS_DELETE_ORPHAN_CONFIRM
                        : LocalizationCodes.LC_YOUR_PALS_DELETE_ORPHAN_UNVERIFIED_CONFIRM,
                    new { path = SelectedOrphanedDocument.DocumentPath }),
                Localized(LocalizationCodes.LC_YOUR_PALS_DELETE_ORPHAN_TITLE),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
                return;

            if (orphanedDocumentManager.TryDeleteOrphanedDocument(
                    SelectedOrphanedDocument,
                    out var error))
            {
                EditStatus = Localized(LocalizationCodes.LC_YOUR_PALS_ORPHAN_DELETED);
                UpdateOrphanedDocuments();
            }
            else
            {
                EditStatus = error;
            }
        }

        private void ClearQuery()
        {
            IsAttentionReviewActive = false;
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

        private void UpdateFromSession(string preferredSelectedKey = null)
        {
            if (disposed || Session == null)
                return;

            SetLocalizedQueryOptions();
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

            solverSource = Session.BuildSolverSource();

            HasGroups = Groups.Count > 0;
            HasSourceEntries = SourceEntries.Count > 0;
            SelectedAddGroup = Groups.FirstOrDefault(group =>
                string.Equals(group.GroupId, SelectedAddGroup?.GroupId ?? SelectedGroupId, StringComparison.Ordinal)) ?? Groups.FirstOrDefault();
            UpdateAddPalOptions();
            HasDiagnostics = Diagnostics.Count > 0;
            SaveScope = Session.Identity.CanonicalKey;
            SessionState = BuildSessionStateText(Session);
            SourceState = IsSourceUnavailable
                ? Localized(LocalizationCodes.LC_YOUR_PALS_SOURCE_UNAVAILABLE_SHORT)
                : Localized(LocalizationCodes.LC_YOUR_PALS_SOURCE_AVAILABLE);
            SaveDisplayName = BuildSaveDisplayName(Session);
            OnPropertyChanged(nameof(SaveContextText));
            SaveStateText = IsSaving
                ? Localized(LocalizationCodes.LC_YOUR_PALS_SAVE_STATE_SAVING)
                : BuildSaveStateText(Session);
            RecoveryState = Diagnostics.Count == 0
                ? Localized(LocalizationCodes.LC_YOUR_PALS_NO_RECOVERY_DETAILS)
                : string.Join("; ", Diagnostics.Select(diagnostic => diagnostic.Message));
            RecoveryGuidance = BuildRecoveryGuidance(Session);

            UpdateOrphanedDocuments();
            ApplyQuery(preferredSelectedKey ?? selectedKey);
            OnPropertyChanged(nameof(CanEdit));
            OnPropertyChanged(nameof(CanCreateDocument));
            OnPropertyChanged(nameof(CanRepairRecoveredDocument));
            OnPropertyChanged(nameof(CanDiscardChangesAndReload));
            OnPropertyChanged(nameof(SolverSource));
            OnPropertyChanged(nameof(PageDescription));
            OnPropertyChanged(nameof(SelectedCollectionTitle));
            OnPropertyChanged(nameof(IsAllPalsSelected));
            OnPropertyChanged(nameof(CollectionSummaryText));
            OnPropertyChanged(nameof(CurrentCollectionEntryCount));
            OnPropertyChanged(nameof(HasEmptyCollection));
            OnPropertyChanged(nameof(HasNoQueryMatches));
            OnPropertyChanged(nameof(HasTextOrStatusQuery));
            OnPropertyChanged(nameof(HasNoSourceEntries));
            OnPropertyChanged(nameof(IsSourceUnavailable));
            OnPropertyChanged(nameof(HasSourceUnavailableCollectionState));
            OnPropertyChanged(nameof(HasNoSourceEntriesCollectionState));
            OnPropertyChanged(nameof(IsSaveAttention));
            OnPropertyChanged(nameof(IsSaveHealthy));
            OnPropertyChanged(nameof(AddPalDestinationText));
            OnPropertyChanged(nameof(PickerContextText));
            OnPropertyChanged(nameof(CanEditSelectedManual));
            OnPropertyChanged(nameof(IsSelectedEntryProblem));
            OnPropertyChanged(nameof(HasAttentionEntries));
            OnPropertyChanged(nameof(MissingEntryCount));
            OnPropertyChanged(nameof(HasMissingEntries));
            OnPropertyChanged(nameof(SolverMissingCount));
            OnPropertyChanged(nameof(HasSolverMissingEntries));
            OnPropertyChanged(nameof(IsSolverSourceEmpty));
            OnPropertyChanged(nameof(SolverSourceMissingText));
            OnPropertyChanged(nameof(SolverSourceEmptyText));
            OnPropertyChanged(nameof(SolverReadyCount));
            OnPropertyChanged(nameof(SolverExcludedCount));
            OnPropertyChanged(nameof(SolverSourceSummaryText));
            OnPropertyChanged(nameof(SolverSourceExcludedText));
            OnPropertyChanged(nameof(SolverSourceStateText));
            NotifyEditingCommands();
        }

        private void SetLocalizedQueryOptions()
        {
            StatusFilterOptions = new List<YourPalsStatusFilterOption>
            {
                new(YourPalsStatusFilter.All, Localized(LocalizationCodes.LC_YOUR_PALS_STATUS_ALL)),
                new(YourPalsStatusFilter.Resolved, Localized(LocalizationCodes.LC_YOUR_PALS_STATUS_READY)),
                new(YourPalsStatusFilter.Unresolved, Localized(LocalizationCodes.LC_YOUR_PALS_STATUS_CANNOT_IDENTIFY)),
                new(YourPalsStatusFilter.Stale, Localized(LocalizationCodes.LC_YOUR_PALS_STATUS_NO_LONGER_IN_SAVE)),
                new(YourPalsStatusFilter.Conflict, Localized(LocalizationCodes.LC_YOUR_PALS_STATUS_CONFLICTING_COPIES)),
                new(YourPalsStatusFilter.Invalid, Localized(LocalizationCodes.LC_YOUR_PALS_STATUS_NEEDS_REPAIR)),
            }.AsReadOnly();
            SortOptions = new List<YourPalsSortOption>
            {
                new(YourPalsSortField.PalName, Localized(LocalizationCodes.LC_YOUR_PALS_PAL)),
                new(YourPalsSortField.Status, Localized(LocalizationCodes.LC_YOUR_PALS_STATUS)),
                new(YourPalsSortField.Location, Localized(LocalizationCodes.LC_COMMON_LOCATION)),
            }.AsReadOnly();
            OnPropertyChanged(nameof(StatusFilterOptions));
            OnPropertyChanged(nameof(SortOptions));
        }

        private static string Localized(LocalizationCodes code) => code.Bind().Value;

        private static string Localized(LocalizationCodes code, object formatArgs) => code.Bind(formatArgs).Value;

        private static string BuildSaveDisplayName(SavePalsSession session)
        {
            var playerName = session.CachedSave?.PlayerName;
            var worldName = session.CachedSave?.WorldName;
            if (!string.IsNullOrWhiteSpace(playerName) && !string.IsNullOrWhiteSpace(worldName))
                return $"{playerName} · {worldName}";
            if (!string.IsNullOrWhiteSpace(worldName))
                return worldName;
            return session.Identity.CanonicalKey;
        }

        private static string BuildSaveStateText(SavePalsSession session)
        {
            if (session.State is SavePalsSessionState.WriteFailed or SavePalsSessionState.ExternalConflict)
                return Localized(LocalizationCodes.LC_YOUR_PALS_SAVE_STATE_FAILED);
            if (session.IsReadOnly)
                return Localized(LocalizationCodes.LC_YOUR_PALS_SAVE_STATE_READ_ONLY);
            if (session.IsDirty)
                return Localized(LocalizationCodes.LC_YOUR_PALS_SAVE_STATE_DIRTY);
            return Localized(LocalizationCodes.LC_YOUR_PALS_SAVE_STATE_SAVED);
        }

        private static string BuildSessionStateText(SavePalsSession session) => session.State switch
        {
            SavePalsSessionState.Healthy => Localized(LocalizationCodes.LC_YOUR_PALS_SESSION_HEALTHY),
            SavePalsSessionState.Dirty => Localized(LocalizationCodes.LC_YOUR_PALS_SESSION_DIRTY),
            SavePalsSessionState.ReadOnly => Localized(LocalizationCodes.LC_YOUR_PALS_SESSION_READ_ONLY),
            SavePalsSessionState.Recovery => Localized(LocalizationCodes.LC_YOUR_PALS_SESSION_RECOVERY),
            SavePalsSessionState.SourceUnavailable => Localized(LocalizationCodes.LC_YOUR_PALS_SESSION_SOURCE_UNAVAILABLE),
            SavePalsSessionState.ExternalConflict => Localized(LocalizationCodes.LC_YOUR_PALS_SESSION_EXTERNAL_CONFLICT),
            SavePalsSessionState.WriteFailed => Localized(LocalizationCodes.LC_YOUR_PALS_SESSION_WRITE_FAILED),
            SavePalsSessionState.Orphaned => Localized(LocalizationCodes.LC_YOUR_PALS_SESSION_ORPHANED),
            _ => Localized(LocalizationCodes.LC_YOUR_PALS_SESSION_READ_ONLY),
        };

        private static string BuildRecoveryGuidance(SavePalsSession session)
        {
            if (session.HasUnrecoverableRecoveryData)
            {
                return Localized(LocalizationCodes.LC_YOUR_PALS_RECOVERY_UNRECOVERABLE);
            }

            if (session.IsRecoveredFromBackup)
            {
                return Localized(LocalizationCodes.LC_YOUR_PALS_RECOVERY_BACKUP);
            }

            if (session.CanRepairRecoveredDocument)
            {
                return Localized(LocalizationCodes.LC_YOUR_PALS_RECOVERY_REPAIRABLE);
            }

            if (session.State == SavePalsSessionState.Recovery)
            {
                return Localized(LocalizationCodes.LC_YOUR_PALS_RECOVERY_READ_ONLY);
            }

            return "";
        }

        private void UpdateGroupFilterOptions()
        {
            var selectedGroupId = queryState.SelectedGroupId;
            var options = new List<YourPalsGroupFilterOption>
            {
                new(null, Localized(LocalizationCodes.LC_YOUR_PALS_ALL_PALS)),
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
            OnPropertyChanged(nameof(ShowingText));
            HasEntries = Entries.Count > 0;
            SelectedEntry = Entries.FirstOrDefault(entry => entry.PalEntryKey == selectedKey);
            OnPropertyChanged(nameof(CurrentCollectionEntryCount));
            OnPropertyChanged(nameof(HasEmptyCollection));
            OnPropertyChanged(nameof(HasNoQueryMatches));
            OnPropertyChanged(nameof(HasTextOrStatusQuery));
            OnPropertyChanged(nameof(HasSourceUnavailableCollectionState));
            OnPropertyChanged(nameof(HasNoSourceEntriesCollectionState));
            OnPropertyChanged(nameof(HasActiveQuery));
        }

        private bool MatchesQuery(YourPalsEntryRowViewModel entry)
        {
            if (IsAttentionReviewActive && entry.Status == YourPalsEntryStatus.Resolved)
                return false;

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
                Contains(entry.Nickname, searchText, compareInfo, options) ||
                Contains(entry.Level, searchText, compareInfo, options) ||
                Contains(entry.Gender, searchText, compareInfo, options) ||
                Contains(entry.Location, searchText, compareInfo, options) ||
                Contains(entry.StatusLabel, searchText, compareInfo, options) ||
                Contains(entry.StatusExplanation, searchText, compareInfo, options) ||
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

        private bool CanSave() => !IsSaving && Session?.CanEdit == true && Session.IsDirty;

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

        // Guarded on an available source: when the save cannot be read every
        // imported member looks missing, and removing them would wipe the groups.
        private bool CanRemoveMissingMembers() =>
            CanEdit && Session?.IsSourceAvailable == true && HasMissingEntries;

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
            ShowAllPalsCommand.NotifyCanExecuteChanged();
            ReviewAttentionCommand.NotifyCanExecuteChanged();
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
            RemoveMissingMembersCommand.NotifyCanExecuteChanged();
            RepairRecoveredDocumentCommand.NotifyCanExecuteChanged();
            DeleteSelectedOrphanedDocumentCommand.NotifyCanExecuteChanged();
            AddPalCommand.NotifyCanExecuteChanged();
            AddSelectedPalCommand.NotifyCanExecuteChanged();
            RepairSelectedEntryCommand.NotifyCanExecuteChanged();
            RepairEntryCommand.NotifyCanExecuteChanged();
            OpenManualEditorCommand.NotifyCanExecuteChanged();
            EditSelectedManualCommand.NotifyCanExecuteChanged();
            SaveManualEditorCommand.NotifyCanExecuteChanged();
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
            else
            {
                ManualInternalName = "";
            }

            IsDetailsOpen = value != null;
            OnPropertyChanged(nameof(CanEditSelectedManual));
            OnPropertyChanged(nameof(IsSelectedEntryProblem));
            OnPropertyChanged(nameof(AttentionActionText));
            OnPropertyChanged(nameof(AttentionActionTooltip));
            OnPropertyChanged(nameof(RepairPickerTitle));
            OnPropertyChanged(nameof(RepairPickerActionText));
            OnPropertyChanged(nameof(PickerActionText));
            NotifyEditingCommands();
        }

        partial void OnIsAttentionReviewActiveChanged(bool value)
        {
            OnPropertyChanged(nameof(SelectedCollectionTitle));
            OnPropertyChanged(nameof(HasActiveQuery));
            OnPropertyChanged(nameof(HasNoQueryMatches));
            OnPropertyChanged(nameof(HasTextOrStatusQuery));
            ApplyQuery();
        }

        partial void OnIsRepairModeChanged(bool value)
        {
            OnPropertyChanged(nameof(PickerContextText));
            OnPropertyChanged(nameof(PickerActionText));
        }

        partial void OnIsSavingChanged(bool value)
        {
            OnPropertyChanged(nameof(IsSaveHealthy));
            NotifyEditingCommands();
        }

        partial void OnSelectedGroupSummaryChanged(YourPalsGroupSummaryViewModel value)
        {
            RenameGroupName = value?.Name ?? "";
            if (!string.Equals(SelectedGroupId, value?.GroupId, StringComparison.Ordinal))
                SelectedGroupId = value?.GroupId;
            OnPropertyChanged(nameof(SelectedCollectionTitle));
            OnPropertyChanged(nameof(IsAllPalsSelected));
            OnPropertyChanged(nameof(CurrentCollectionEntryCount));
            OnPropertyChanged(nameof(HasEmptyCollection));
            OnPropertyChanged(nameof(HasNoQueryMatches));
            SelectedAddGroup = Groups.FirstOrDefault(group =>
                string.Equals(group.GroupId, value?.GroupId, StringComparison.Ordinal)) ?? Groups.FirstOrDefault();
            UpdateAddPalOptions();
            OnPropertyChanged(nameof(AddPalDestinationText));
            OnPropertyChanged(nameof(PickerContextText));
            NotifyEditingCommands();
        }

        partial void OnSelectedSourceEntryChanged(YourPalsSourceRowViewModel value) =>
            NotifyEditingCommands();

        partial void OnSelectedAddPalChanged(YourPalsAddPalOptionViewModel value) =>
            AddSelectedPalCommand.NotifyCanExecuteChanged();

        partial void OnSelectedAddGroupChanged(YourPalsGroupSummaryViewModel value)
        {
            UpdateAddPalOptions();
            OnPropertyChanged(nameof(AddPalDestinationText));
            OnPropertyChanged(nameof(PickerContextText));
            AddSelectedPalCommand.NotifyCanExecuteChanged();
            SaveManualEditorCommand.NotifyCanExecuteChanged();
        }

        partial void OnAddPalSearchTextChanged(string value)
        {
            OnPropertyChanged(nameof(HasAddPalSearchText));
            UpdateAddPalOptions();
        }

        partial void OnSelectedManualPalChanged(PalViewModel value) =>
            SaveManualEditorCommand.NotifyCanExecuteChanged();

        partial void OnManualLevelTextChanged(string value) =>
            SaveManualEditorCommand.NotifyCanExecuteChanged();

        partial void OnIsEditingManualPalChanged(bool value)
        {
            OnPropertyChanged(nameof(ManualEditorTitle));
            OnPropertyChanged(nameof(ManualEditorActionText));
        }

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
            SelectedGroupId != null ||
            IsAttentionReviewActive;
    }

    internal sealed record YourPalsStatusFilterOption(YourPalsStatusFilter Value, string Label);

    internal sealed record YourPalsSortOption(YourPalsSortField Value, string Label);

    internal sealed record YourPalsGroupFilterOption(string GroupId, string Label);

    internal sealed class YourPalsGroupSummaryViewModel
    {
        public YourPalsGroupSummaryViewModel(YourPalsResolvedGroup group)
        {
            GroupId = group?.Group?.GroupId ?? "(invalid)";
            Name = string.IsNullOrWhiteSpace(group?.Group?.Name)
                ? Localized(LocalizationCodes.LC_YOUR_PALS_UNNAMED_GROUP)
                : group.Group.Name;
            MemberCount = group?.Members?.Count ?? 0;
            AttentionCount = group?.Members?.Count(member => member.Status != YourPalsEntryStatus.Resolved) ?? 0;
            AttentionText = AttentionCount == 0
                ? ""
                : Localized(
                    LocalizationCodes.LC_YOUR_PALS_ATTENTION_COUNT,
                    new { count = AttentionCount });
            StatusSummary = AttentionText;
        }

        public string GroupId { get; }
        public string Name { get; }
        public int MemberCount { get; }
        public string MemberCountText => $"{Localized(LocalizationCodes.LC_YOUR_PALS_MEMBERS)} {MemberCount}";
        public int AttentionCount { get; }
        public string AttentionText { get; }
        public string StatusSummary { get; }

        private static string Localized(LocalizationCodes code) => code.Bind().Value;

        private static string Localized(LocalizationCodes code, object formatArgs) => code.Bind(formatArgs).Value;
    }

    internal sealed class YourPalsEntryRowViewModel
    {
        public YourPalsEntryRowViewModel(
            YourPalsGroup group,
            YourPalsResolvedMember resolved,
            YourPalsManualDefinition manualDefinition)
        {
            GroupId = group?.GroupId ?? "(invalid)";
            GroupName = string.IsNullOrWhiteSpace(group?.Name)
                ? Localized(LocalizationCodes.LC_YOUR_PALS_UNNAMED_GROUP)
                : group.Name;
            Member = resolved?.Member;
            Status = resolved?.Status ?? YourPalsEntryStatus.Invalid;
            Details = resolved?.Reason ?? Localized(LocalizationCodes.LC_YOUR_PALS_MEMBER_UNINTERPRETED);
            PalEntryKey = resolved?.Member?.PalEntryKey ?? Localized(LocalizationCodes.LC_YOUR_PALS_MISSING_KEY);
            Kind = resolved?.Member?.Kind ?? Localized(LocalizationCodes.LC_YOUR_PALS_MISSING_KIND);
            InstanceId = resolved?.Member?.InstanceId ?? manualDefinition?.ManualDefinitionId ?? "—";
            PalName = DisplayName(resolved, manualDefinition);
            var record = resolved?.SourceEntry?.Record ?? resolved?.ResolvedRecord;
            Nickname = string.IsNullOrWhiteSpace(record?.NickName) ? "—" : record.NickName;
            Level = record?.Level.ToString() ?? "—";
            Gender = record == null ? "—" : record.Gender.Label().Value;
            Icon = ResolveIcon(record?.Pal, resolved?.Member?.LastKnownInternalName);
            SourceScope = DisplaySourceScope(resolved);
            SourceKey = resolved?.SourceEntry?.SourceKey
                ?? resolved?.Member?.SourceKey
                ?? "—";
            var location = resolved?.SourceEntry?.Record ?? resolved?.ResolvedRecord;
            Location = location?.Location == null ? "—" : location.Location.Type.ShortLabel().Value;
            SourceReferenceKey = resolved?.SourceEntry == null
                ? ""
                : YourPalsSourceRowViewModel.GetReferenceKey(resolved.SourceEntry);
            ImportIdentityKey = resolved?.Member?.KnownKind == YourPalsMemberKind.ImportedReference &&
                resolved.Member.SourceIdentity.HasValue &&
                !string.IsNullOrWhiteSpace(resolved.Member.InstanceId)
                ? YourPalsSourceRowViewModel.GetImportIdentityKey(
                    resolved.Member.SourceIdentity.Value,
                    resolved.Member.InstanceId)
                : "";
        }

        public YourPalsMember Member { get; }
        public string GroupId { get; }
        public string GroupName { get; }
        public string PalEntryKey { get; }
        public string Kind { get; }
        public string PalName { get; }
        public string Nickname { get; }
        public string Level { get; }
        public string Gender { get; }
        public ImageSource Icon { get; }
        public string SourceScope { get; }
        public string SourceKey { get; }
        public string InstanceId { get; }
        public string Location { get; }
        public string SourceReferenceKey { get; }
        public string ImportIdentityKey { get; }
        public YourPalsEntryStatus Status { get; }
        public string Details { get; }

        public string StatusLabel => Status switch
        {
            YourPalsEntryStatus.Resolved => Localized(LocalizationCodes.LC_YOUR_PALS_STATUS_READY),
            YourPalsEntryStatus.Unresolved => Localized(LocalizationCodes.LC_YOUR_PALS_STATUS_CANNOT_IDENTIFY),
            YourPalsEntryStatus.Stale => Localized(LocalizationCodes.LC_YOUR_PALS_STATUS_NO_LONGER_IN_SAVE),
            YourPalsEntryStatus.Conflict => Localized(LocalizationCodes.LC_YOUR_PALS_STATUS_CONFLICTING_COPIES),
            YourPalsEntryStatus.Invalid => Localized(LocalizationCodes.LC_YOUR_PALS_STATUS_NEEDS_REPAIR),
            _ => Localized(LocalizationCodes.LC_YOUR_PALS_STATUS_NEEDS_REPAIR),
        };

        public string StatusExplanation => Status switch
        {
            YourPalsEntryStatus.Resolved => Localized(LocalizationCodes.LC_YOUR_PALS_STATUS_READY_EXPLANATION),
            YourPalsEntryStatus.Unresolved => Localized(LocalizationCodes.LC_YOUR_PALS_STATUS_CANNOT_IDENTIFY_EXPLANATION),
            YourPalsEntryStatus.Stale => Localized(LocalizationCodes.LC_YOUR_PALS_STATUS_NO_LONGER_IN_SAVE_EXPLANATION),
            YourPalsEntryStatus.Conflict => Localized(LocalizationCodes.LC_YOUR_PALS_STATUS_CONFLICTING_COPIES_EXPLANATION),
            YourPalsEntryStatus.Invalid => Localized(LocalizationCodes.LC_YOUR_PALS_STATUS_NEEDS_REPAIR_EXPLANATION),
            _ => Details,
        };

        public string AttentionActionText => Status switch
        {
            YourPalsEntryStatus.Stale => Localized(LocalizationCodes.LC_YOUR_PALS_FIND_REPLACEMENT),
            YourPalsEntryStatus.Conflict => Localized(LocalizationCodes.LC_YOUR_PALS_CHOOSE_COPY),
            YourPalsEntryStatus.Unresolved or YourPalsEntryStatus.Invalid when
                Member?.KnownKind == YourPalsMemberKind.ManualDefinitionReference =>
                Localized(LocalizationCodes.LC_YOUR_PALS_EDIT_MANUAL_PAL),
            YourPalsEntryStatus.Unresolved or YourPalsEntryStatus.Invalid when
                Member?.KnownKind == YourPalsMemberKind.ImportedReference =>
                Localized(LocalizationCodes.LC_YOUR_PALS_REVIEW),
            _ => "",
        };

        private static string Localized(LocalizationCodes code) => code.Bind().Value;

        // A member whose Pal has left the save has no live record, but the saved
        // reference still remembers what it was. Showing that Pal's icon keeps the
        // row identifiable while its status explains that it is missing.
        private static ImageSource ResolveIcon(Pal pal, string lastKnownInternalName)
        {
            pal ??= YourPalsDisplayName.FindPalByInternalName(lastKnownInternalName);
            return pal != null && PalIcon.Images.TryGetValue(pal, out var icon)
                ? icon
                : PalIcon.DefaultIcon;
        }

        private static string DisplayName(
            YourPalsResolvedMember resolved,
            YourPalsManualDefinition manualDefinition)
        {
            var record = resolved?.SourceEntry?.Record ?? resolved?.ResolvedRecord;
            if (record?.Pal != null)
            {
                return YourPalsDisplayName.For(record.Pal);
            }

            return manualDefinition?.RawInternalName
                ?? resolved?.Member?.LastKnownDisplayName
                ?? resolved?.Member?.LastKnownInternalName
                ?? Localized(LocalizationCodes.LC_YOUR_PALS_UNKNOWN_PAL);
        }

        private static string DisplaySourceScope(YourPalsResolvedMember resolved)
        {
            if (resolved?.ManualDefinition != null)
                return Localized(LocalizationCodes.LC_YOUR_PALS_MANUAL_DEFINITION);

            var sourceIdentity = resolved?.SourceEntry?.SourceIdentity;
            if (sourceIdentity.HasValue)
                return sourceIdentity.Value.StableKey;

            return resolved?.Member?.SourceIdentity?.StableKey
                ?? Localized(LocalizationCodes.LC_YOUR_PALS_SOURCE_UNAVAILABLE_SHORT);
        }
    }

    internal sealed class YourPalsSourceRowViewModel
    {
        public YourPalsSourceRowViewModel(YourPalsSourceEntry entry)
        {
            Entry = entry;
            var record = entry?.Record;
            PalName = YourPalsDisplayName.For(record?.Pal);
            Nickname = string.IsNullOrWhiteSpace(record?.NickName) ? "—" : record.NickName;
            Icon = record?.Pal == null ? PalIcon.DefaultIcon : PalIcon.Images[record.Pal];
            InstanceId = entry?.InstanceId ?? "—";
            SourceScope = entry?.SourceIdentity.StableKey
                ?? Localized(LocalizationCodes.LC_YOUR_PALS_SOURCE_UNAVAILABLE_SHORT);
            SourceKey = entry?.SourceKey ?? "—";
            Location = record?.Location == null ? "—" : record.Location.Type.ShortLabel().Value;
            Level = record?.Level.ToString() ?? "—";
            Gender = record == null ? "—" : record.Gender.Label().Value;
        }

        public YourPalsSourceEntry Entry { get; }
        public string PalName { get; }
        public string Nickname { get; }
        public ImageSource Icon { get; }
        public string InstanceId { get; }
        public string SourceScope { get; }
        public string SourceKey { get; }
        public string Location { get; }
        public string Level { get; }
        public string Gender { get; }
        public string ReferenceKey => GetReferenceKey(Entry);
        public string ImportIdentityKey => GetImportIdentityKey(Entry);

        public static string GetImportIdentityKey(YourPalsSourceEntry entry) => entry == null
            ? ""
            : GetImportIdentityKey(entry.SourceIdentity, entry.InstanceId);

        public static string GetImportIdentityKey(SourceIdentity sourceIdentity, string instanceId) =>
            string.Concat(StablePart(sourceIdentity.StableKey), StablePart(instanceId));

        public static string GetReferenceKey(YourPalsSourceEntry entry) => entry == null
            ? Localized(LocalizationCodes.LC_YOUR_PALS_SOURCE_UNAVAILABLE_SHORT)
            : string.Concat(
                StablePart(entry.SourceIdentity.StableKey),
                StablePart(entry.InstanceId),
                StablePart(entry.SourceKey),
                StablePart(entry.ContentFingerprint));

        private static string Localized(LocalizationCodes code) => code.Bind().Value;

        private static string StablePart(string value) =>
            $"{value?.Length ?? -1}:{value}";
    }

    internal sealed class YourPalsAddPalOptionViewModel
    {
        public YourPalsAddPalOptionViewModel(YourPalsSourceRowViewModel source, bool isAlreadyInSelectedGroup)
        {
            Source = source;
            IsAlreadyInSelectedGroup = isAlreadyInSelectedGroup;
        }

        public YourPalsSourceRowViewModel Source { get; }
        public YourPalsSourceEntry SourceEntry => Source.Entry;
        public string PalName => Source.PalName;
        public string Nickname => Source.Nickname;
        public string Level => Source.Level;
        public string Gender => Source.Gender;
        public string Location => Source.Location;
        public ImageSource Icon => Source.Icon;
        public bool IsAlreadyInSelectedGroup { get; }
        public string AlreadyInGroupText => IsAlreadyInSelectedGroup ?
            Localized(LocalizationCodes.LC_YOUR_PALS_ALREADY_IN_GROUP) : "";
        public string ReferenceKey => Source.ReferenceKey;

        private static string Localized(LocalizationCodes code) => code.Bind().Value;
    }

    internal static class YourPalsDisplayName
    {
        private static Dictionary<string, Pal> palsByInternalName;

        internal static Pal FindPalByInternalName(string internalName)
        {
            if (string.IsNullOrWhiteSpace(internalName))
                return null;

            palsByInternalName ??= PalDB.LoadEmbedded().Pals
                .GroupBy(pal => pal.InternalName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            return palsByInternalName.GetValueOrDefault(internalName);
        }

        public static string For(Pal pal)
        {
            if (pal == null)
                return Localized(LocalizationCodes.LC_YOUR_PALS_UNKNOWN_PAL);

            if (pal.LocalizedNames != null)
            {
                var localizedName = pal.LocalizedNames.GetValueOrElse(
                    Translator.CurrentLocale.ToFormalName(),
                    pal.Name);
                if (!string.IsNullOrWhiteSpace(localizedName))
                    return localizedName;
            }

            return pal.Name ?? pal.InternalName ?? Localized(LocalizationCodes.LC_YOUR_PALS_UNKNOWN_PAL);
        }

        private static string Localized(LocalizationCodes code) => code.Bind().Value;
    }
}
