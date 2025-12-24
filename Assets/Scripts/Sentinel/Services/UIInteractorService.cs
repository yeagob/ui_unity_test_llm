using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using Sentinel.Interfaces;

namespace Sentinel.Services
{
    /// <summary>
    /// Executes UI interactions using Unity UI Toolkit Test Framework patterns.
    /// Uses Pointer and Keyboard simulation for reliable, frame-independent testing.
    /// </summary>
    public class UIInteractorService : IUIInteractor
    {
        private Func<VisualElement> _rootProvider;
        
        public UIInteractorService(Func<VisualElement> rootProvider)
        {
            _rootProvider = rootProvider;
        }
        
        public async Task<bool> ClickAsync(string elementPath)
        {
            VisualElement root = _rootProvider?.Invoke();
            if (root == null)
            {
                Debug.LogWarning("[Sentinel] No UI root available for click");
                return false;
            }
            
            VisualElement element = root.Q(elementPath);
            if (element == null)
            {
                Debug.LogWarning($"[Sentinel] Element not found: {elementPath}");
                return false;
            }
            
            try
            {
                // Calculate center position using worldBound (as per UI Toolkit Test Framework)
                Rect worldBound = element.worldBound;
                Vector2 center = worldBound.center;
                
                // Simulate pointer event sequence: Move -> Down -> Up
                SimulatePointerClick(element, center);
                
                // Wait one frame for callbacks to execute
                await Task.Delay(16);
                
                Debug.Log($"[Sentinel] Clicked: {elementPath}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Sentinel] Click failed: {ex.Message}");
                return false;
            }
        }
        
        public async Task<bool> TypeAsync(string elementPath, string text)
        {
            VisualElement root = _rootProvider?.Invoke();
            if (root == null)
            {
                Debug.LogWarning("[Sentinel] No UI root available for type");
                return false;
            }
            
            VisualElement element = root.Q(elementPath);
            if (element == null)
            {
                Debug.LogWarning($"[Sentinel] Element not found: {elementPath}");
                return false;
            }
            
            try
            {
                // Focus the element first
                element.Focus();
                await Task.Delay(16);
                
                // If it's a TextField, set value directly
                if (element is TextField textField)
                {
                    textField.value = text;
                    Debug.Log($"[Sentinel] Typed into {elementPath}: {text}");
                    return true;
                }
                
                Debug.LogWarning($"[Sentinel] Element is not a text input: {elementPath}");
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Sentinel] Type failed: {ex.Message}");
                return false;
            }
        }
        
        public async Task<bool> ScrollAsync(string elementPath, float delta)
        {
            VisualElement root = _rootProvider?.Invoke();
            if (root == null)
            {
                Debug.LogWarning("[Sentinel] No UI root available for scroll");
                return false;
            }
            
            VisualElement element = root.Q(elementPath);
            if (element == null)
            {
                Debug.LogWarning($"[Sentinel] Element not found: {elementPath}");
                return false;
            }
            
            try
            {
                if (element is ScrollView scrollView)
                {
                    scrollView.scrollOffset += new Vector2(0, delta);
                    await Task.Delay(16);
                    Debug.Log($"[Sentinel] Scrolled {elementPath} by {delta}");
                    return true;
                }
                
                Debug.LogWarning($"[Sentinel] Element is not scrollable: {elementPath}");
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Sentinel] Scroll failed: {ex.Message}");
                return false;
            }
        }
        
        public async Task WaitSecondsAsync(float seconds)
        {
            int ms = Mathf.RoundToInt(seconds * 1000f);
            Debug.Log($"[Sentinel] Waiting {seconds}s...");
            await Task.Delay(ms);
        }
        
        public async Task<bool> WaitForElementAsync(string elementPath, float timeoutSeconds)
        {
            VisualElement root = _rootProvider?.Invoke();
            if (root == null)
            {
                Debug.LogWarning("[Sentinel] No UI root available");
                return false;
            }
            
            float elapsed = 0f;
            float pollInterval = 0.1f;
            
            while (elapsed < timeoutSeconds)
            {
                VisualElement element = root.Q(elementPath);
                if (element != null 
                    && element.resolvedStyle.display == DisplayStyle.Flex
                    && element.resolvedStyle.visibility == Visibility.Visible)
                {
                    Debug.Log($"[Sentinel] Element found: {elementPath}");
                    return true;
                }
                
                await Task.Delay(Mathf.RoundToInt(pollInterval * 1000f));
                elapsed += pollInterval;
            }
            
            Debug.LogWarning($"[Sentinel] Timeout waiting for: {elementPath}");
            return false;
        }
        
        private void SimulatePointerClick(VisualElement element, Vector2 position)
        {
            // Create and dispatch pointer events following UI Toolkit Test Framework pattern
            // PointerDown -> PointerUp -> Click
            
            using (PointerDownEvent downEvent = PointerDownEvent.GetPooled())
            {
                downEvent.target = element;
                element.SendEvent(downEvent);
            }
            
            using (PointerUpEvent upEvent = PointerUpEvent.GetPooled())
            {
                upEvent.target = element;
                element.SendEvent(upEvent);
            }
            
            using (ClickEvent clickEvent = ClickEvent.GetPooled())
            {
                clickEvent.target = element;
                element.SendEvent(clickEvent);
            }
        }
    }
}
