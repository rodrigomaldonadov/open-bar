using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using OpenBar.Application.Dock;

namespace OpenBar.Presentation.Dock;

/// <summary>View model for one dock tile.</summary>
public sealed class DockItemViewModel : ObservableObject
{
    private ImageSource? _icon;
    private bool _isRunning;
    private double _iconSize = 48;
    private double _magnification = 1.12;
    private int _animationDurationMs = 180;

    public DockItemViewModel(DockItemInfo info)
    {
        Info = info;
        _isRunning = info.IsRunning;
    }

    public DockItemInfo Info { get; private set; }

    public Guid? PinnedId => Info.PinnedId;

    public string Key => Info.Key;

    public string DisplayName => Info.DisplayName;

    public string? IconPath => Info.IconPath;

    public bool IsPinned => Info.IsPinned;

    public bool IsRunning
    {
        get => _isRunning;
        private set => SetProperty(ref _isRunning, value);
    }

    public ImageSource? Icon
    {
        get => _icon;
        private set
        {
            if (SetProperty(ref _icon, value))
            {
                OnPropertyChanged(nameof(HasIcon));
            }
        }
    }

    public bool HasIcon => _icon is not null;

    public double IconSize
    {
        get => _iconSize;
        private set => SetProperty(ref _iconSize, value);
    }

    public double Magnification
    {
        get => _magnification;
        private set => SetProperty(ref _magnification, value);
    }

    public int AnimationDurationMs
    {
        get => _animationDurationMs;
        private set => SetProperty(ref _animationDurationMs, value);
    }

    public Thickness ItemMargin { get; private set; }

    public void Update(DockItemInfo info)
    {
        Info = info;
        IsRunning = info.IsRunning;
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(IconPath));
        OnPropertyChanged(nameof(PinnedId));
        OnPropertyChanged(nameof(IsPinned));
    }

    public void ApplyLayout(double iconSize, double spacing, double magnification, int animationDurationMs, bool isLast)
    {
        IconSize = iconSize;
        Magnification = magnification;
        AnimationDurationMs = animationDurationMs;
        ItemMargin = new Thickness(0, 0, isLast ? 0 : spacing, 0);
        OnPropertyChanged(nameof(ItemMargin));
    }

    public async Task LoadIconAsync(DockIconService icons, CancellationToken cancellationToken = default)
    {
        var icon = await icons.GetIconAsync(IconPath, IconSize, cancellationToken).ConfigureAwait(true);
        if (icon is not null)
        {
            Icon = icon;
        }
    }
}
