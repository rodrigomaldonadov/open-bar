using OpenBar.Widgets.Abstractions;

namespace OpenBar.WidgetRuntime.Tests.Fakes;

/// <summary>Creates recording widgets and keeps references for assertions.</summary>
internal sealed class RecordingWidgetFactory
{
    private readonly List<RecordingWidget> _created = [];

    public IReadOnlyList<RecordingWidget> Created => _created;

    public RecordingWidget Last => _created[^1];

    public IWidget Create()
    {
        var widget = new RecordingWidget();
        _created.Add(widget);
        return widget;
    }
}
