using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WindowsWhisker.Services;
using WindowsWhisker.Views;

namespace WindowsWhisker.Controls;

public sealed class CpuGraphView : FrameworkElement
{
    const int Capacity = 52;
    readonly double[] _samples = new double[Capacity];
    readonly DispatcherTimer _timer;
    readonly bool _vertical;
    readonly PanelActions _actions;
    double _current;

    public CpuGraphView(PanelActions actions, bool vertical)
    {
        _actions = actions;
        _vertical = vertical;
        Width = vertical ? double.NaN : 86;
        Height = vertical ? 86 : double.NaN;
        MinWidth = vertical ? 18 : 72;
        MinHeight = vertical ? 72 : 18;
        Margin = new Thickness(4, 5, 4, 5);
        SnapsToDevicePixels = true;
        ClipToBounds = true;
        Cursor = Cursors.Hand;
        ToolTip = "CPU usage: 0.0%";
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _timer.Tick += (_, _) => Sample();
        Loaded += (_, _) => { Sample(); _timer.Start(); };
        Unloaded += (_, _) => _timer.Stop();
        MouseLeftButtonUp += (_, _) => OpenTaskManager();
    }

    void Sample()
    {
        Array.Copy(_samples, 1, _samples, 0, Capacity - 1);
        _current = CpuSampler.Read();
        _samples[^1] = _current;
        ToolTip = $"CPU usage: {_current:0.0}%";
        InvalidateVisual();
    }

    void OpenTaskManager()
    {
        try
        {
            Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Dialogs.Alert(_actions.Owner(), "Couldn't open Task Manager.\n" + ex.Message);
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = Math.Max(1, ActualWidth);
        double h = Math.Max(1, ActualHeight);
        var bounds = new Rect(0, 0, w, h);
        dc.DrawRectangle(TrackBrush(), null, bounds);

        var fill = Ui.Accent();
        int count = _samples.Length;
        if (_vertical)
        {
            double row = h / count;
            for (int i = 0; i < count; i++)
            {
                double t = _samples[i] / 100.0;
                if (t <= 0) continue;
                double barH = Math.Max(1, row);
                dc.DrawRectangle(fill, null, new Rect(0, i * row, w * t, barH));
            }
        }
        else
        {
            double col = w / count;
            for (int i = 0; i < count; i++)
            {
                double t = _samples[i] / 100.0;
                if (t <= 0) continue;
                double barW = Math.Max(1, col);
                double barH = Math.Max(1, h * t);
                dc.DrawRectangle(fill, null, new Rect(i * col, h - barH, barW, barH));
            }
        }
    }

    static SolidColorBrush TrackBrush()
    {
        var color = Ui.PanelBackground().Color;
        return Ui.Brush($"#{Math.Max(0, color.R - 18):X2}{Math.Max(0, color.G - 16):X2}{Math.Max(0, color.B - 12):X2}");
    }
}
