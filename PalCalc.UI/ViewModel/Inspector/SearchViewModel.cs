using AdonisUI.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PalCalc.Model;
using PalCalc.UI.Localization;
using PalCalc.UI.Model;
using PalCalc.UI.View;
using PalCalc.UI.View.Utils;
using PalCalc.UI.ViewModel.Inspector.Search;
using PalCalc.UI.ViewModel.Inspector.Search.Container;
using PalCalc.UI.ViewModel.Inspector.Search.Grid;
using PalCalc.UI.ViewModel.Mapped;
using PalCalc.UI.ViewModel.PalDerived;
using PalCalc.UI.ViewModel.SaveSelection;
using QuickGraph.Graphviz;
using QuickGraph.Serialization;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace PalCalc.UI.ViewModel.Inspector
{
    public partial class SearchViewModel : ObservableObject
    {
        private static SearchViewModel designerInstance = null;
        public static SearchViewModel DesignerInstance => designerInstance ??= new SearchViewModel(
            SaveGameViewModel.DesignerInstance,
            CachedSaveGame.SampleForDesignerView,
            GameSettings.Defaults
        );

        private GameSettings settings;
        private CachedSaveGame cachedSave;
        private readonly SaveGameViewModel saveGameViewModel;
        private readonly SaveCustomizationsViewModel customizations;

        [ObservableProperty]
        private OwnerTreeViewModel ownerTree;

        public int TotalMatches => OwnerTree.AllContainerSources.SelectMany(c => c.Container.Grids).SelectMany(g => g.Slots).Count(slot => slot.Matches);

        public SearchSettingsViewModel SearchSettings { get; }

        public bool CanEditCustomizations => customizations?.CanPersist == true;

        public bool HasCustomizationsLoadError => !string.IsNullOrWhiteSpace(customizations?.LoadError);

        private ICommand newCustomContainerCommand;
        public IRelayCommand<ISearchableContainerViewModel> DeleteContainerCommand { get; }

        public IRelayCommand<ISearchableContainerViewModel> RenameContainerCommand { get; }

        public IRelayCommand<IContainerGridSlotViewModel> DeleteSlotCommand { get; }

        private static bool IsValidCustomLabel(SaveGameViewModel context, string label) =>
            label.Length > 0 && !context.Customizations.CustomContainers.Any(c => c.Label == label);

        public SearchViewModel(SaveGameViewModel sgvm, CachedSaveGame cachedSave, GameSettings settings)
        {
            saveGameViewModel = sgvm;
            customizations = sgvm.Customizations;
            this.settings = settings;
            this.cachedSave = cachedSave;

            newCustomContainerCommand = new RelayCommand(() =>
            {
                var nameModal = new SimpleTextInputWindow()
                {
                    Title = LocalizationCodes.LC_CUSTOM_CONTAINER_NEW_TITLE.Bind().Value,
                    InputLabel = LocalizationCodes.LC_CUSTOM_CONTAINER_NEW_FIELD.Bind().Value,
                    Validator = label => IsValidCustomLabel(saveGameViewModel, label),
                    Owner = App.ActiveWindow,
                };

                if (nameModal.ShowDialog() == true)
                {
                    var container = new CustomContainer() { Label = nameModal.Result };
                    customizations.CustomContainers.Add(new CustomContainerViewModel(container));
                }
            }, () => CanEditCustomizations);

            RenameContainerCommand = new RelayCommand<ISearchableContainerViewModel>(
                container =>
                {
                    var customContainer = container as CustomSearchableContainerViewModel;
                    if (customContainer == null || !CanEditCustomizations) return;

                    var nameModal = new SimpleTextInputWindow(customContainer.Label)
                    {
                        Title = LocalizationCodes.LC_CUSTOM_CONTAINER_RENAME_TITLE.Bind().Value,
                        InputLabel = LocalizationCodes.LC_CUSTOM_CONTAINER_RENAME_FIELD.Bind().Value,
                        Validator = label => IsValidCustomLabel(saveGameViewModel, label),
                        Owner = App.ActiveWindow,
                    };

                    if (nameModal.ShowDialog() == true)
                    {
                        customContainer.Value.Label = nameModal.Result;
                    }
                },
                container => CanEditCustomizations && container is CustomSearchableContainerViewModel
            );

            DeleteContainerCommand = new RelayCommand<ISearchableContainerViewModel>(
                container =>
                {
                    var customContainer = container as CustomSearchableContainerViewModel;
                    if (customContainer == null || !CanEditCustomizations) return;

                    if (MessageBox.Show(LocalizationCodes.LC_REMOVE_CUSTOM_CONTAINER_DESCRIPTION.Bind(customContainer.Label).Value, "", MessageBoxButton.OKCancel) != MessageBoxResult.OK)
                        return;

                    customizations.CustomContainers.Remove(customContainer.Value);
                },
                container => CanEditCustomizations && container is CustomSearchableContainerViewModel
            );

            DeleteSlotCommand = new RelayCommand<IContainerGridSlotViewModel>(
                slot =>
                {
                    var subCommands = OwnerTree.AllContainerSources
                        .SelectMany(s => s.Container.Grids)
                        .Select(g => g.DeleteSlotCommand)
                        .SkipNull()
                        .Where(cmd => cmd.CanExecute(slot))
                        .ToList();

                    foreach (var cmd in subCommands)
                        cmd.Execute(slot);
                },
                slot => CanEditCustomizations && slot != null
            );

            CollectionChangedEventManager.AddHandler(
                customizations.CustomContainers,
                (_, _) => BuildContainerTree()
            );

            BuildContainerTree();
            SearchSettings = new SearchSettingsViewModel();

            SearchSettings.PropertyChanged += SearchSettings_PropertyChanged;
        }

        private void BuildContainerTree()
        {
            var csg = cachedSave;
            var palsByContainerId = csg.OwnedPals.GroupBy(p => p.Location.ContainerId).ToDictionary(g => g.Key, g => g.ToList());

            var containers = csg.PalContainers
                .Where(c => palsByContainerId.ContainsKey(c.Id))
                .Select(c => new DefaultSearchableContainerViewModel(settings, c, palsByContainerId[c.Id]))
                .Cast<ISearchableContainerViewModel>()
                .Concat(
                    customizations.CustomContainers.Select(c =>
                        new CustomSearchableContainerViewModel(settings, c)
                        {
                            RenameCommand = RenameContainerCommand,
                            DeleteCommand = DeleteContainerCommand,
                        }
                    )
                );

            OwnerTree = new OwnerTreeViewModel(csg, containers.ToList())
            {
                CreateCustomContainerCommand = newCustomContainerCommand
            };
        }

        private void ApplySearchSettings()
        {
            foreach (var source in OwnerTree.AllContainerSources)
                source.SearchCriteria = SearchSettings.AsCriteria;
        }

        private void SearchSettings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SearchSettings.AsCriteria))
            {
                ApplySearchSettings();
                OnPropertyChanged(nameof(TotalMatches));
            }
        }

    }
}
