namespace Sentinel.Interfaces
{
    /// <summary>
    /// Inspects the current UI state and returns structured information.
    /// </summary>
    public interface IUIInspector
    {
        /// <summary>
        /// Returns a JSON representation of the visible UI hierarchy.
        /// </summary>
        string GetUIHierarchy();
        
        /// <summary>
        /// Checks if an element exists at the given path.
        /// </summary>
        bool ElementExists(string elementPath);
        
        /// <summary>
        /// Returns the state of an element (enabled, visible, text content).
        /// </summary>
        string GetElementState(string elementPath);
    }
}
