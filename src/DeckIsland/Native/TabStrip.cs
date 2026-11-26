using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;

namespace DeckIsland.Native;

// Whether a tab of a window (a browser's or Explorer's tab strip) reaches under the island, read through UI Automation.
// UI Automation calls into the other process and can take a while, so this runs off the UI thread (WindowsHost.CheckTabs).
internal static class TabStrip
{
    private static readonly PropertyCondition Tabs = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem);

    // area in screen physical pixels, as UI Automation reports bounding rectangles.
    public static bool Covers(IntPtr window, Rect area)
    {
        try
        {
            // The root is read outside the cache request: under AutomationElementMode.None it would have no live handle to search from.
            var root = AutomationElement.FromHandle(window);
            var cache = new CacheRequest { AutomationElementMode = AutomationElementMode.None };
            cache.Add(AutomationElement.BoundingRectangleProperty);
            using (cache.Activate())
            {
                foreach (AutomationElement tab in root.FindAll(TreeScope.Descendants, Tabs))
                {
                    var bounds = (Rect)tab.GetCachedPropertyValue(AutomationElement.BoundingRectangleProperty);
                    if (!bounds.IsEmpty && bounds.IntersectsWith(area)) return true;
                }
            }
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or COMException or InvalidOperationException or ArgumentException) { }
        return false;
    }
}
