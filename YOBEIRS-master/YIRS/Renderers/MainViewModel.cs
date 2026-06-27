using CarouselView.FormsPlugin.Abstractions;
using FFImageLoading.Forms;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using Xamarin.Forms;

namespace YIRS.Renderers
{
    public class MainViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public MainViewModel()
        {
            MyItemsSource = new ObservableCollection<View>()
            {
                new CachedImage() { DownsampleToViewSize = true, Source = "IMG_0104.jpg", Aspect = Aspect.AspectFill},
                new CachedImage() { DownsampleToViewSize = true, Source = "yob5.jpg", Aspect = Aspect.AspectFill },
                 new CachedImage() { DownsampleToViewSize = true, Source = "yob2.jpg", Aspect = Aspect.AspectFill},
                new CachedImage() { DownsampleToViewSize = true, Source = "IMG_6194.jpg", Aspect = Aspect.AspectFill },

            };

            PositionSelectedCommand = new Command<PositionSelectedEventArgs>((e) =>
            {
                Debug.Write("Position " + e.NewValue + " selected.");
                Debug.Write(this.SelectedItem);
            });

            ScrolledCommand = new Command<CarouselView.FormsPlugin.Abstractions.ScrolledEventArgs>((e) =>
            {
                Debug.WriteLine("Scrolled to " + e.NewValue + " percent.");
                Debug.WriteLine("Direction = " + e.Direction);
            });
        }

        ObservableCollection<View> _myItemsSource;
        public ObservableCollection<View> MyItemsSource
        {
            set
            {
                _myItemsSource = value;
                OnPropertyChanged("MyItemsSource");
            }
            get
            {
                return _myItemsSource;
            }
        }

        object _selectedItem;
        public object SelectedItem
        {
            set
            {
                _selectedItem = value;
                OnPropertyChanged("SelectedItem");
            }
            get
            {
                return _selectedItem;
            }
        }

        public Command<PositionSelectedEventArgs> PositionSelectedCommand { protected set; get; }

        public Command<CarouselView.FormsPlugin.Abstractions.ScrolledEventArgs> ScrolledCommand { protected set; get; }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
