using System.Windows;
using System.Windows.Controls;

namespace FilesCollector.App.Controls;

public partial class SizeHistogram : UserControl
{
    private double _maxKiB = 1;

    public static readonly DependencyProperty MaxKiBProperty = DependencyProperty.Register(
        nameof(MaxKiB),
        typeof(double),
        typeof(SizeHistogram),
        new PropertyMetadata(1.0, OnValueOrRangeChanged));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value),
        typeof(double),
        typeof(SizeHistogram),
        new FrameworkPropertyMetadata(5120.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueOrRangeChanged));

    public static readonly DependencyProperty BucketsProperty = DependencyProperty.Register(
        nameof(Buckets),
        typeof(IReadOnlyList<SizeBucket>),
        typeof(SizeHistogram),
        new PropertyMetadata(null, OnBucketsChanged));

    public SizeHistogram()
    {
        InitializeComponent();
        ThresholdSlider.ValueChanged += (_, _) => OnValueOrRangeChanged(this, new DependencyPropertyChangedEventArgs(ValueProperty, null, null));
        SizeChanged += (_, _) => PositionThresholdLine();
    }

    public double MaxKiB
    {
        get => (double)GetValue(MaxKiBProperty);
        set => SetValue(MaxKiBProperty, value);
    }

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public IReadOnlyList<SizeBucket>? Buckets
    {
        get => (IReadOnlyList<SizeBucket>?)GetValue(BucketsProperty);
        set => SetValue(BucketsProperty, value);
    }

    public static readonly DependencyProperty BucketCountProperty = DependencyProperty.Register(
        nameof(BucketCount),
        typeof(int),
        typeof(SizeHistogram),
        new PropertyMetadata(0));

    public int BucketCount
    {
        get => (int)GetValue(BucketCountProperty);
        set => SetValue(BucketCountProperty, value);
    }

    private static void OnValueOrRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (SizeHistogram)d;
        control._maxKiB = Math.Max(1, control.MaxKiB);
        control.ThresholdSlider.Maximum = control._maxKiB;
        control.ThresholdSlider.Value = Math.Clamp(control.Value, 1, control._maxKiB);
        control.ValueLabel.Text = FormatValue(control.Value);
        control.PositionThresholdLine();
    }

    private static void OnBucketsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (SizeHistogram)d;
        var buckets = (IReadOnlyList<SizeBucket>?)e.NewValue;
        control.BarsHost.ItemsSource = buckets;
        control.BucketCount = buckets?.Count ?? 0;
        control.PositionThresholdLine();
    }

    private void PositionThresholdLine()
    {
        if (Buckets is not { Count: > 0 } || BarsHost.ActualWidth <= 0)
        {
            return;
        }

        var ratio = _maxKiB <= 0 ? 0 : Math.Clamp(Value / _maxKiB, 0, 1);
        var x = ratio * BarsHost.ActualWidth;
        Canvas.SetLeft(ThresholdLine, x);
        ThresholdLine.X1 = x;
        ThresholdLine.X2 = x;
    }

    private static string FormatValue(double valueKiB)
    {
        if (valueKiB < 1024)
        {
            return $"{valueKiB:0} KB";
        }

        return $"{valueKiB / 1024d:0.#} MB";
    }
}
