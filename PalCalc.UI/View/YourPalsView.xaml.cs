using System.Windows.Controls;
using System;
using System.Linq;
using PalCalc.UI.Localization;
using PalCalc.UI.View.Utils;
using PalCalc.UI.ViewModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace PalCalc.UI.View
{
    public partial class YourPalsView : UserControl
    {
        private const double CompactBreakpoint = 980;
        private bool isCompactLayout;
        private UIElement focusBeforeDetails;
        private UIElement focusBeforePicker;
        private UIElement focusBeforeManualEditor;

        public YourPalsView()
        {
            InitializeComponent();
        }

        private YourPalsViewModel ViewModel => DataContext as YourPalsViewModel;

        private void YourPalsView_Loaded(object sender, RoutedEventArgs e) =>
            ApplyResponsiveLayout(ActualWidth);

        private void YourPalsView_SizeChanged(object sender, SizeChangedEventArgs e) =>
            ApplyResponsiveLayout(e.NewSize.Width);

        private void ApplyResponsiveLayout(double width)
        {
            var compact = width > 0 && width < CompactBreakpoint;
            if (compact == isCompactLayout)
                return;

            isCompactLayout = compact;
            if (compact)
            {
                // Keep the collection usable at the app's 480px minimum width;
                // secondary fields remain available in the details panel.
                PalColumn.Width = 145;
                LevelColumn.Width = 45;
                GenderColumn.Width = 60;
                LocationColumn.Width = 0;
                StatusColumn.Width = 180;
                GroupColumn.Width = 0;

                CollectionLayoutGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
                CollectionLayoutGrid.ColumnDefinitions[0].MinWidth = 0;
                CollectionLayoutGrid.ColumnDefinitions[1].Width = new GridLength(0);
                CollectionLayoutGrid.ColumnDefinitions[2].Width = new GridLength(0);
                CollectionLayoutGrid.ColumnDefinitions[2].MinWidth = 0;

                Grid.SetRow(GroupPanel, 0);
                Grid.SetColumn(GroupPanel, 0);
                Grid.SetRow(GroupCollectionSplitter, 1);
                Grid.SetColumn(GroupCollectionSplitter, 0);
                Grid.SetRow(CollectionPanel, 2);
                Grid.SetColumn(CollectionPanel, 0);

                GroupPanel.Height = 155;
                GroupPanel.MaxHeight = 155;
                GroupPanel.VerticalAlignment = VerticalAlignment.Top;
                GroupCollectionSplitter.Width = double.NaN;
                GroupCollectionSplitter.Height = 5;
                GroupCollectionSplitter.HorizontalAlignment = HorizontalAlignment.Stretch;
                GroupCollectionSplitter.VerticalAlignment = VerticalAlignment.Center;
            }
            else
            {
                PalColumn.Width = 185;
                LevelColumn.Width = 55;
                GenderColumn.Width = 75;
                LocationColumn.Width = 120;
                StatusColumn.Width = 230;
                GroupColumn.Width = 125;

                CollectionLayoutGrid.ColumnDefinitions[0].Width = new GridLength(170);
                CollectionLayoutGrid.ColumnDefinitions[0].MinWidth = 125;
                CollectionLayoutGrid.ColumnDefinitions[1].Width = new GridLength(5);
                CollectionLayoutGrid.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
                CollectionLayoutGrid.ColumnDefinitions[2].MinWidth = 240;

                Grid.SetRow(GroupPanel, 0);
                Grid.SetColumn(GroupPanel, 0);
                Grid.SetRow(GroupCollectionSplitter, 0);
                Grid.SetColumn(GroupCollectionSplitter, 1);
                Grid.SetRow(CollectionPanel, 0);
                Grid.SetColumn(CollectionPanel, 2);

                GroupPanel.ClearValue(FrameworkElement.HeightProperty);
                GroupPanel.ClearValue(FrameworkElement.MaxHeightProperty);
                GroupPanel.VerticalAlignment = VerticalAlignment.Stretch;
                GroupCollectionSplitter.Width = 5;
                GroupCollectionSplitter.ClearValue(FrameworkElement.HeightProperty);
                GroupCollectionSplitter.HorizontalAlignment = HorizontalAlignment.Center;
                GroupCollectionSplitter.VerticalAlignment = VerticalAlignment.Stretch;
            }
        }

        private void YourPalsView_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var viewModel = ViewModel;
            if (viewModel == null)
                return;

            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                SearchTextBox.Focus();
                SearchTextBox.SelectAll();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (viewModel.SaveCommand.CanExecute(null))
                    viewModel.SaveCommand.Execute(null);
                e.Handled = true;
                return;
            }

            if (e.Key != Key.Escape)
                return;

            if (viewModel.IsAddPalPickerOpen)
                viewModel.CloseOverlayCommand.Execute(null);
            else if (viewModel.IsDetailsOpen)
                viewModel.CloseDetailsCommand.Execute(null);
            else
                return;

            e.Handled = true;
        }

        private void PageScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var outerScrollViewer = (ScrollViewer)sender;
            var innerScrollViewer = FindScrollViewer(e.OriginalSource as DependencyObject);

            if (innerScrollViewer == null ||
                ReferenceEquals(innerScrollViewer, outerScrollViewer) ||
                !IsAtVerticalScrollBoundary(innerScrollViewer, e.Delta) ||
                !CanScrollVertically(outerScrollViewer, e.Delta))
            {
                return;
            }

            e.Handled = true;
            outerScrollViewer.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
            });
        }

        private static bool IsAtVerticalScrollBoundary(ScrollViewer scrollViewer, int delta)
        {
            const double epsilon = 0.5;
            return delta > 0
                ? scrollViewer.VerticalOffset <= epsilon
                : scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight - epsilon;
        }

        private static bool CanScrollVertically(ScrollViewer scrollViewer, int delta)
        {
            const double epsilon = 0.5;
            return delta > 0
                ? scrollViewer.VerticalOffset > epsilon
                : scrollViewer.VerticalOffset < scrollViewer.ScrollableHeight - epsilon;
        }

        private static ScrollViewer FindScrollViewer(DependencyObject source)
        {
            while (source != null)
            {
                if (source is ScrollViewer scrollViewer)
                    return scrollViewer;

                source = source is Visual || source is Visual3D
                    ? VisualTreeHelper.GetParent(source)
                    : LogicalTreeHelper.GetParent(source);
            }

            return null;
        }

        private void DetailsOverlay_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is true)
            {
                focusBeforeDetails = Keyboard.FocusedElement as UIElement;
                FocusWhenVisible(DetailsCloseButton);
            }
            else
            {
                RestoreFocus(focusBeforeDetails);
                focusBeforeDetails = null;
            }
        }

        private void PickerOverlay_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is true)
            {
                focusBeforePicker = Keyboard.FocusedElement as UIElement;
                FocusWhenVisible(PickerCloseButton);
            }
            else
            {
                RestoreFocus(focusBeforePicker);
                focusBeforePicker = null;
            }
        }

        private void ManualEditor_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is true)
            {
                focusBeforeManualEditor = Keyboard.FocusedElement as UIElement;
                FocusWhenVisible(ManualCloseButton);
            }
            else
            {
                RestoreFocus(focusBeforeManualEditor);
                focusBeforeManualEditor = null;
            }
        }

        private void FocusWhenVisible(UIElement element)
        {
            Dispatcher.BeginInvoke(
                DispatcherPriority.Input,
                new Action(() => element.Focus()));
        }

        private void RestoreFocus(UIElement element)
        {
            if (element == null)
                return;

            Dispatcher.BeginInvoke(
                DispatcherPriority.Input,
                new Action(() =>
                {
                    if (element.IsVisible && element.IsEnabled && element.Focusable)
                        element.Focus();
                }));
        }

        private void NewGroup_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var viewModel = ViewModel;
            if (viewModel == null)
                return;

            var dialog = new SimpleTextInputWindow
            {
                Title = Localized(LocalizationCodes.LC_YOUR_PALS_NEW_GROUP),
                InputLabel = Localized(LocalizationCodes.LC_YOUR_PALS_GROUP_NAME),
                Validator = name => !string.IsNullOrWhiteSpace(name) &&
                    !viewModel.Groups.Any(group => string.Equals(group.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)),
                Owner = App.ActiveWindow,
            };
            if (dialog.ShowDialog() == true)
            {
                viewModel.NewGroupName = dialog.Result;
                viewModel.CreateGroupCommand.Execute(null);
            }
        }

        private void RenameGroup_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var group = (sender as System.Windows.Controls.MenuItem)?.DataContext as YourPalsGroupSummaryViewModel;
            var viewModel = ViewModel;
            if (viewModel == null || group == null)
                return;

            viewModel.SelectedGroupSummary = group;
            var dialog = new SimpleTextInputWindow(group.Name)
            {
                Title = Localized(LocalizationCodes.LC_YOUR_PALS_RENAME_GROUP),
                InputLabel = Localized(LocalizationCodes.LC_YOUR_PALS_GROUP_NAME),
                Validator = name => !string.IsNullOrWhiteSpace(name) &&
                    !viewModel.Groups.Any(candidate =>
                        !string.Equals(candidate.GroupId, group.GroupId, StringComparison.Ordinal) &&
                        string.Equals(candidate.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)),
                Owner = App.ActiveWindow,
            };
            if (dialog.ShowDialog() == true)
            {
                viewModel.RenameGroupName = dialog.Result;
                viewModel.RenameGroupCommand.Execute(null);
            }
        }

        private void MoveGroupUp_Click(object sender, System.Windows.RoutedEventArgs e) =>
            ExecuteGroupCommand(sender, ViewModel?.MoveGroupUpCommand);

        private void MoveGroupDown_Click(object sender, System.Windows.RoutedEventArgs e) =>
            ExecuteGroupCommand(sender, ViewModel?.MoveGroupDownCommand);

        private void DeleteGroup_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var group = (sender as System.Windows.Controls.MenuItem)?.DataContext as YourPalsGroupSummaryViewModel;
            var viewModel = ViewModel;
            if (viewModel == null || group == null)
                return;

            viewModel.SelectedGroupSummary = group;
            var result = System.Windows.MessageBox.Show(
                Localized(LocalizationCodes.LC_YOUR_PALS_DELETE_GROUP_CONFIRM, new { group = group.Name }),
                Localized(LocalizationCodes.LC_YOUR_PALS_DELETE_GROUP),
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);
            if (result == System.Windows.MessageBoxResult.Yes &&
                viewModel.DeleteGroupCommand.CanExecute(null))
            {
                viewModel.DeleteGroupCommand.Execute(null);
            }
        }

        private void ExecuteGroupCommand(object sender, System.Windows.Input.ICommand command)
        {
            var group = (sender as System.Windows.Controls.MenuItem)?.DataContext as YourPalsGroupSummaryViewModel;
            if (ViewModel == null || group == null || command == null)
                return;

            ViewModel.SelectedGroupSummary = group;
            if (command.CanExecute(null))
                command.Execute(null);
        }

        private static string Localized(LocalizationCodes code) => code.Bind().Value;

        private static string Localized(LocalizationCodes code, object formatArgs) => code.Bind(formatArgs).Value;
    }
}
