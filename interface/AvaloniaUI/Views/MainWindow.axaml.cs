using Avalonia.Controls;
using Avalonia.Markup.Xaml;
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
    }
}
