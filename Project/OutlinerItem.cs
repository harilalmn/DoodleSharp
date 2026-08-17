using System.Collections.ObjectModel;

namespace DoodleSharp.Project;

public class OutlinerItem
{
    public string DisplayName { get; set; } = string.Empty;
    public long Id { get; set; }
    public bool IsShape { get; set; }
    public ObservableCollection<OutlinerItem> Children { get; set; } = new();

    public OutlinerItem()
    {
    }

    public OutlinerItem(string displayName, bool isShape = false, long id = 0)
    {
        DisplayName = displayName;
        IsShape = isShape;
        Id = id;
    }
}
