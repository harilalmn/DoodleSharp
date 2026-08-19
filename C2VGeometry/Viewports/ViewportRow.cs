namespace C2VGeometry;

/// <summary>
/// One row of a <see cref="Viewport"/> — what <c>Viewports[row]</c> returns, so that
/// <c>Viewports[row][column]</c> reads the way a grid index should, and so that the row itself can
/// be given a height.
/// </summary>
/// <remarks>
/// <para>
/// A class rather than a struct, which is not a stylistic choice: <c>Viewports[1].Height = "3*"</c>
/// has to compile, and C# refuses to assign to a property of a value returned by an indexer
/// (CS1612, "cannot modify the return value ... because it is not a variable"). A row is indexed a
/// handful of times per layout, so the allocation does not matter.
/// </para>
/// <para>
/// Also deliberately a top-level type rather than nested inside <see cref="Viewport"/>. Project
/// creation and the shadowed-name diagnostic both build the set of names user code must not shadow
/// by reflecting over this assembly's exported types, and <c>Type.Namespace</c> on a nested type
/// reports the <i>enclosing</i> namespace — so a nested <c>Viewport.Row</c> would silently reserve
/// the bare word "Row" for every project.
/// </para>
/// </remarks>
public sealed class ViewportRow
{
    private readonly Viewport _owner;
    private readonly int _row;

    internal ViewportRow(Viewport owner, int row)
    {
        _owner = owner;
        _row = row;
    }

    /// <summary>
    /// The cell at this row and the given column.
    /// </summary>
    /// <exception cref="System.ArgumentOutOfRangeException">
    /// The column does not exist. The message names the viewport's current size.
    /// </exception>
    public Viewport this[int column] => _owner.Cell(_row, column);

    /// <summary>
    /// How tall this row is, in the same spelling XAML uses: <c>"*"</c> for a share of the space,
    /// <c>"3*"</c> for three shares, or a number for a fixed pixel height. Defaults to <c>"*"</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// Viewports.Rows = 3;
    /// Viewports[0].Height = "2*";     // the top row is twice as tall as each of the other two
    /// </code>
    /// </example>
    public string Height
    {
        get => _owner.RowHeightAt(_row).ToString();
        set => _owner.SetRowHeight(_row, ViewportLength.Parse(value));
    }
}
