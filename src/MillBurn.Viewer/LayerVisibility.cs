namespace MillBurn.Viewer;

/// <summary>
/// Which layers a scene should hide when it is rebuilt, and — the part that went wrong — where the
/// answer comes from.
///
/// There are two possible sources and they are not interchangeable. What the operator has hidden on
/// screen is the live answer, and it wins whenever the window holds rows for the project being
/// drawn. What the project was *saved* with is the answer only when there is nothing on screen to
/// ask: the first build of a project, or the first build after a different project replaced it.
///
/// 6.27 was those two being told apart by counting. An empty set of hidden rows was read as "nobody
/// has an opinion", so the saved state was consulted instead — and it had one. Opening a second
/// project over a first showed every layer ticked, because the rows still on screen belonged to the
/// project being replaced and none of their ids matched; then the first Preview found nothing
/// hidden, fell back to the saved state, and unticked four layers in front of the operator. The
/// same confusion ran the other way inside one project: reveal every layer, change any setting, and
/// the saved state hid them again.
///
/// So the distinction is carried by null rather than by a count. Null means there is nobody to ask.
/// An empty set is an answer, and it means everything is showing.
/// </summary>
public static class LayerVisibility
{
    /// <summary>The ids to hide, or null to leave the scene's own defaults alone.</summary>
    /// <param name="onScreen">
    /// The ids hidden in the window now, or null when the window holds no rows for this project —
    /// which includes the rebuild that follows a different project being opened.
    /// </param>
    /// <param name="saved">What the project was saved with.</param>
    public static IReadOnlySet<string>? Hidden(IReadOnlySet<string>? onScreen, IEnumerable<string> saved)
    {
        if (onScreen is not null)
        {
            return onScreen;
        }

        ArgumentNullException.ThrowIfNull(saved);

        var fromFile = saved.ToHashSet(StringComparer.Ordinal);
        return fromFile.Count == 0 ? null : fromFile;
    }

    /// <summary>Applies that answer to a scene. A null answer leaves every layer as it was built.</summary>
    public static void Apply(BoardScene scene, IReadOnlySet<string>? hidden)
    {
        ArgumentNullException.ThrowIfNull(scene);

        if (hidden is null)
        {
            return;
        }

        foreach (var layer in scene.Layers)
        {
            layer.Visible = !hidden.Contains(layer.Id);
        }
    }
}
