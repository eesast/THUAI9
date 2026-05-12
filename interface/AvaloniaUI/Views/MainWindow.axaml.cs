using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using System.Linq;
using THUAI9_Avalonia.ViewModels;

namespace THUAI9_Avalonia.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContextChanged += (_, _) => BindMapView();
        }

        private void BindMapView()
        {
            if (DataContext is not MainWindowViewModel vm)
            {
                return;
            }

            var mapView = this.FindControl<MapView>("MapView");
            if (mapView != null)
            {
                vm.SetMapView(mapView);
                mapView.RefreshMap();
            }
        }

        private async void BrowsePlayback_Click(object? sender, RoutedEventArgs e)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.StorageProvider == null)
            {
                return;
            }

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择 THUAI9 回放文件",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("THUAI9 回放文件")
                    {
                        Patterns = ["*.thuaipb"]
                    }
                ]
            });

            var selectedFile = files.FirstOrDefault();
            if (selectedFile == null)
            {
                return;
            }

            string filePath = selectedFile.Path.LocalPath;
            PlaybackPathBox.Text = filePath;

            if (DataContext is MainWindowViewModel vm && vm.LoadPlaybackCommand.CanExecute(filePath))
            {
                vm.LoadPlaybackCommand.Execute(filePath);
            }
        }
    }
}
