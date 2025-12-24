using System.Threading.Tasks;

namespace Sentinel.Interfaces
{
    /// <summary>
    /// Executes UI interactions using Unity UI Toolkit Test Framework.
    /// </summary>
    public interface IUIInteractor
    {
        /// <summary>
        /// Clicks on an element by path.
        /// </summary>
        Task<bool> ClickAsync(string elementPath);
        
        /// <summary>
        /// Types text into an element.
        /// </summary>
        Task<bool> TypeAsync(string elementPath, string text);
        
        /// <summary>
        /// Scrolls an element by delta.
        /// </summary>
        Task<bool> ScrollAsync(string elementPath, float delta);
        
        /// <summary>
        /// Waits for a fixed number of seconds.
        /// </summary>
        Task WaitSecondsAsync(float seconds);
        
        /// <summary>
        /// Waits until an element exists or timeout.
        /// </summary>
        Task<bool> WaitForElementAsync(string elementPath, float timeoutSeconds);
    }
}
