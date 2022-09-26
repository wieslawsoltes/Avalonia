using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Markup.Xaml;
using Avalonia.Win32.WinRT.Composition;
using MiniMvvm;

namespace Sandbox
{
    public class MainWindow : Window
    {
        public MainWindow()
        {
            this.InitializeComponent();
            this.AttachDevTools();
            
            this.GetObservable(WindowStateProperty).Subscribe(x =>
            {
                Debug.WriteLine($"[WindowStateProperty] {x}");
            });
            this.GetObservable(WidthProperty).Subscribe(x =>
            {
                Debug.WriteLine($"[WidthProperty] {x}");
            });
            this.GetObservable(HeightProperty).Subscribe(x =>
            {
                Debug.WriteLine($"[HeightProperty] {x}");
            });
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
