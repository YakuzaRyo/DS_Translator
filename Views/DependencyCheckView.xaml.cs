using Configer.Services;
using Configer.Utilities;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;

namespace Configer.Views
{
    public sealed partial class DependencyCheckView : UserControl
    {
        public ObservableCollection<DependencyCheckResult> Results { get; } = new();

        public DependencyCheckView()
        {
            this.InitializeComponent();
            // Set DataContext so XAML binding can access Results
            this.DataContext = this;
        }

        private async void BtnRunCheck_Click(object sender, RoutedEventArgs e)
        {
            await RunChecksAsync();
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            await RunChecksAsync();
        }

        private void CheckerTile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                if (button.Flyout != null)
                {
                    button.Flyout.ShowAt(button);
                }
                else
                {
                    var attached = FlyoutBase.GetAttachedFlyout(button);
                    attached?.ShowAt(button);
                }
            }
        }

        public async Task RunChecksAsync()
        {
            Results.Clear();

            var rootPath = ProjectRootLocator.Resolve();

            // Discover checkers from DependencyCheckerRegistry
            var checkers = DependencyCheckerRegistry.GetCheckers();
            foreach (var checker in checkers)
            {
                var res = await checker.CheckAsync(rootPath);
                Results.Add(res);
            }
        }
    }
}
