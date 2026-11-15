using System.Globalization;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Graphics;

namespace UsdMxn;

public sealed partial class MainWindow : Window
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private readonly DispatcherQueueTimer _timer;
    private RateSnapshot? _last;
    private DateTime _lastSuccessUtc;
    private DateTime _lastAttemptUtc;
    private bool _busy;
    private bool _minimized;
    private bool _shownOnce;
    private bool _showYear;

    public MainWindow()
    {
        InitializeComponent();

        // Select the default tab before subscribing, so the handler only runs on user changes.
        RangeSelector.SelectedItem = MonthItem;
        RangeSelector.SelectionChanged += (s, _) =>
        {
            _showYear = s.SelectedItem == YearItem;
            DrawChart();
        };

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarArea);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico"));

        double scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        AppWindow.Resize(new SizeInt32((int)(420 * scale), (int)(580 * scale)));

        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = RefreshInterval;
        _timer.Tick += (_, _) => _ = RefreshAsync();
        _timer.Start();

        AppWindow.Changed += OnAppWindowChanged;
        Activated += OnActivated;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        Closed += (_, _) =>
        {
            _timer.Stop();
            NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        };

        _ = RefreshAsync();
    }

    // ---- Refresh -------------------------------------------------------------------------

    private bool IsStale => DateTime.UtcNow - _lastAttemptUtc >= RefreshInterval;

    private async Task RefreshAsync()
    {
        if (_busy) return;
        _busy = true;
        RefreshButton.IsEnabled = false;
        _lastAttemptUtc = DateTime.UtcNow;
        try
        {
            RateSnapshot? snap = await RateService.FetchAsync();
            if (snap is not null)
            {
                // Keep the richer live history if we only got the daily fallback this time.
                if (!snap.Live && _last is { Year.Length: > 0 } old)
                    snap = snap with { Month = old.Month, Year = old.Year, PrevClose = old.PrevClose };
                _last = snap;
                _lastSuccessUtc = DateTime.UtcNow;
                ShowRate(snap);
            }
            ShowStatus(offline: snap is null);
        }
        finally
        {
            _busy = false;
            RefreshButton.IsEnabled = true;
        }
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) => _ = RefreshAsync();

    private void OnActivated(object sender, WindowActivatedEventArgs e)
    {
        // Always open as the compact window, even if the system starts it maximized.
        if (!_shownOnce)
        {
            _shownOnce = true;
            if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized } op)
                op.Restore();
        }

        if (e.WindowActivationState != WindowActivationState.Deactivated && IsStale)
            _ = RefreshAsync();
    }

    // While minimized the timer is stopped, so the process does no work at all.
    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs e)
    {
        bool minimized = sender.Presenter is OverlappedPresenter
        {
            State: OverlappedPresenterState.Minimized
        };
        if (minimized == _minimized) return;
        _minimized = minimized;

        if (minimized)
        {
            _timer.Stop();
        }
        else
        {
            _timer.Start();
            if (IsStale) _ = RefreshAsync();
        }
    }

    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        if (!e.IsAvailable) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            // Only worth an immediate retry if we are currently showing stale/offline data.
            if (!_minimized && _lastSuccessUtc < _lastAttemptUtc) _ = RefreshAsync();
        });
    }

    // ---- Presentation --------------------------------------------------------------------

    private void ShowRate(RateSnapshot s)
    {
        RateText.Text = s.Rate.ToString("0.0000", Inv);
        InverseText.Text = "1 MXN = " + (1 / s.Rate).ToString("0.00000", Inv) + " USD";

        if (s.PrevClose is double prev && prev > 0)
        {
            double diff = s.Rate - prev;
            ChangeText.Text = string.Create(Inv,
                $"{(diff >= 0 ? "▲" : "▼")} {Math.Abs(diff):0.0000} ({diff / prev:+0.00%;-0.00%}) vs previous close");
            string key = diff >= 0 ? "SystemFillColorSuccessBrush" : "SystemFillColorCriticalBrush";
            if (Application.Current.Resources.TryGetValue(key, out object? brush) && brush is Brush b)
                ChangeText.Foreground = b;
        }
        else
        {
            ChangeText.Text = " ";
        }

        // Recalculate the converter field the user is not typing in.
        if (MxnBox.FocusState != FocusState.Unfocused) Convert(MxnBox, UsdBox, 1 / s.Rate);
        else Convert(UsdBox, MxnBox, s.Rate);

        DrawChart();
    }

    private void ShowStatus(bool offline)
    {
        if (_last is null)
        {
            StatusText.Text = "Couldn't reach the rate service. Retrying automatically.";
            return;
        }
        string time = _lastSuccessUtc.ToLocalTime().ToString("HH:mm:ss", Inv);
        StatusText.Text = offline
            ? $"Offline — last updated {time}"
            : $"Updated {time} · {(_last.Live ? "live market rate" : "daily rate")}";
    }

    // ---- Converter -----------------------------------------------------------------------

    private void OnAmountChanged(object sender, TextChangedEventArgs e)
    {
        // Only react to the box the user is typing in; programmatic updates land on the other.
        if (_last is null || sender is not TextBox box || box.FocusState == FocusState.Unfocused)
            return;
        if (ReferenceEquals(box, UsdBox)) Convert(UsdBox, MxnBox, _last.Rate);
        else Convert(MxnBox, UsdBox, 1 / _last.Rate);
    }

    private static void Convert(TextBox from, TextBox to, double factor)
    {
        string text = from.Text.Replace(",", "").Trim();
        to.Text = double.TryParse(text, NumberStyles.Float, Inv, out double value) && double.IsFinite(value)
            ? (value * factor).ToString("#,0.00", Inv)
            : "";
    }

    // ---- Sparkline -----------------------------------------------------------------------

    private void OnChartSizeChanged(object sender, SizeChangedEventArgs e) => DrawChart();

    private void DrawChart()
    {
        double[]? h = _showYear ? _last?.Year : _last?.Month;
        double w = ChartHost.ActualWidth, height = ChartHost.ActualHeight;
        var points = new PointCollection();
        if (h is { Length: >= 2 } && w > 0 && height > 0)
        {
            double min = h.Min(), max = h.Max();
            double span = max - min;
            if (span <= 0) span = 1;
            const double pad = 3;
            for (int i = 0; i < h.Length; i++)
            {
                points.Add(new Point(
                    i * w / (h.Length - 1),
                    pad + (max - h[i]) / span * (height - 2 * pad)));
            }
            RangeText.Text = string.Create(Inv, $"low {min:0.00} · high {max:0.00}");
        }
        else
        {
            RangeText.Text = "";
        }
        Spark.Points = points;
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);
}
