using System.Linq;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ControlCatalog.Pages
{
    public class View
    {
        public string Title { get; set; }
    }

    public class ReproPage : UserControl
    {
        public static readonly AvaloniaProperty ViewsProperty =
            AvaloniaProperty.Register<AutoCompleteBoxPage, AvaloniaList<View>>(nameof(Views));

        public static readonly AvaloniaProperty CurrentViewProperty =
            AvaloniaProperty.Register<AutoCompleteBoxPage, View>(nameof(CurrentView));

        public AvaloniaList<View> Views
        {
            get => (AvaloniaList<View>)GetValue(ViewsProperty);
            set => SetValue(ViewsProperty, value);
        }

        public View CurrentView
        {
            get => (View)GetValue(CurrentViewProperty);
            set => SetValue(CurrentViewProperty, value);
        }

        private int count = 0;

        public ReproPage()
        {
            this.InitializeComponent();
            RestoreViews();
            DataContext = this;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public AvaloniaList<View> CreateViews()
        {
            return new AvaloniaList<View>();
        }

        public void RemoveCurrentView()
        {
            Views.Remove(CurrentView);
            CurrentView = Views.FirstOrDefault();
        }

        public void AddView()
        {
            Views.Add(new View() { Title = $"--{count++}--" });
            CurrentView = Views.LastOrDefault();
        }

        public void RestoreViews()
        {
            Views = CreateViews();
            CurrentView = Views.FirstOrDefault();
        }
    }
}
