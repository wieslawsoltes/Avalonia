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

        public AvaloniaList<View> CreateViews()
        {
            return new AvaloniaList<View>()
            {
                new View() { Title = "--0--" },
                new View() { Title = "--1--" },
                new View() { Title = "--2--" }
            };
        }
        int count = 3;
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

        public void InsertBeforeView()
        {
            int index = Views.IndexOf(CurrentView);
            Views.Insert(index, new View() { Title = $"--{count++}--" });
            CurrentView = Views[index];
        }

        public void InsertAfterView()
        {
            int index = Views.IndexOf(CurrentView) + 1;
            Views.Insert(index, new View() { Title = $"--{count++}--" });
            CurrentView = Views[index];
        }

        public void RestoreViews()
        {
            Views = CreateViews();
            CurrentView = Views.FirstOrDefault();
        }

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
    }
}
