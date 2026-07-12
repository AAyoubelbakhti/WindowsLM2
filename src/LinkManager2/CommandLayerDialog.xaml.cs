using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace LinkManager2;

public sealed partial class CommandLayerDialog : ContentDialog
{
    public char Chosen { get; private set; }

    public CommandLayerDialog()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDownHandler), handledEventsToo: true);
    }

    private void OnKeyDownHandler(object sender, KeyRoutedEventArgs e)
    {
        var key = e.Key switch
        {
            VirtualKey.G => 'g',
            VirtualKey.L => 'l',
            VirtualKey.R => 'r',
            VirtualKey.A => 'a',
            VirtualKey.P => 'p',
            VirtualKey.F => 'f',
            VirtualKey.H => 'h',
            _ => '\0',
        };
        if (key == '\0') return;
        Chosen = key;
        e.Handled = true;
        Hide();
    }
}
