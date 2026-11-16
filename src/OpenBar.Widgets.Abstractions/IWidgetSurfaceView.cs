namespace OpenBar.Widgets.Abstractions;

/// <summary>
/// Optional contract for widget views that adapt their chrome when hosted in a
/// compact dock surface. Widgets without it keep their normal window view.
/// </summary>
public interface IWidgetSurfaceView
{
    void SetSurfaceMode(bool embedded);
}
