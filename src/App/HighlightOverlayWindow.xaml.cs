using System;
using System.Windows;
using System.Windows.Shapes;
using System.Windows.Controls;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace PopupGuard;

public partial class HighlightOverlayWindow : Window
{
    public HighlightOverlayWindow()
    {
        InitializeComponent();
    }

    public void ShowBounds(Rect rect, bool purple = false)
    {
        if (rect.Width < 0 || rect.Height < 0)
        {
            return;
        }

        // Adjust for per-monitor DPI: convert from physical pixels to WPF DUs for the target screen
        var hwnd = new WindowInteropHelper(this).Handle;
        var dpi = GetDpiForWindow(hwnd);
        double scale = dpi / 96.0;

        Canvas.SetLeft(HighlightRect, rect.Left / scale);
        Canvas.SetTop(HighlightRect, rect.Top / scale);
        HighlightRect.Width = rect.Width / scale;
        HighlightRect.Height = rect.Height / scale;
        HighlightRect.Stroke = purple ? System.Windows.Media.Brushes.MediumPurple : System.Windows.Media.Brushes.LimeGreen;
        if (!IsVisible)
        {
            Show();
        }
    }

    public void HideBounds()
    {
        HighlightRect.Width = 0;
        HighlightRect.Height = 0;
        if (IsVisible)
        {
            Hide();
        }
    }

    [DllImport("user32.dll")]
    private static extern int GetDpiForWindow(IntPtr hWnd);
}


