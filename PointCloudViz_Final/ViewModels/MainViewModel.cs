using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PointCloudViz_Final.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private int _pointCount;
        public int PointCount { get => _pointCount; set { _pointCount = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
