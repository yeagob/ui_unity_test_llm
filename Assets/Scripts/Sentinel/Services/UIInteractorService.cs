using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;
using UnityEngine.EventSystems;
using Sentinel.Interfaces;
using TMPro;

namespace Sentinel.Services
{
    /// <summary>
    /// Executes UI interactions for both UI Toolkit and Canvas (uGUI).
    /// Uses event simulation for reliable, frame-independent testing.
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
            // Try UI Toolkit first
            VisualElement root = _rootProvider?.Invoke();
            if (root != null)
            {
                VisualElement element = root.Q(elementPath);
                if (element != null)
                {
                    return await ClickUIToolkitElement(element, elementPath);
                }
            }
            
            // Try Canvas/uGUI
            GameObject canvasObj = FindCanvasElement(elementPath);
            if (canvasObj != null)
            {
                return await ClickCanvasElement(canvasObj, elementPath);
            }
            
            Debug.LogWarning($"[Sentinel] Element not found in any UI system: {elementPath}");
            return false;
        }
        
        private async Task<bool> ClickUIToolkitElement(VisualElement element, string elementPath)
        {
            try
            {
                Rect worldBound = element.worldBound;
                Vector2 center = worldBound.center;
                
                SimulatePointerClick(element, center);
                await Task.Delay(50);
                
                Debug.Log($"[Sentinel] Clicked UIToolkit element: {elementPath}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Sentinel] UIToolkit click failed: {ex.Message}");
                return false;
            }
        }
        
        private async Task<bool> ClickCanvasElement(GameObject obj, string elementPath)
        {
            try
            {
                // Check for Button component
                UnityEngine.UI.Button button = obj.GetComponent<UnityEngine.UI.Button>();
                if (button != null && button.interactable)
                {
                    button.onClick.Invoke();
                    await Task.Delay(50);
                    Debug.Log($"[Sentinel] Clicked Canvas Button: {elementPath}");
                    return true;
                }
                
                // Check for Toggle
                UnityEngine.UI.Toggle toggle = obj.GetComponent<UnityEngine.UI.Toggle>();
                if (toggle != null && toggle.interactable)
                {
                    toggle.isOn = !toggle.isOn;
                    await Task.Delay(50);
                    Debug.Log($"[Sentinel] Toggled Canvas Toggle: {elementPath}");
                    return true;
                }
                
                // Try to use EventSystem for generic click
                IPointerClickHandler clickHandler = obj.GetComponent<IPointerClickHandler>();
                if (clickHandler != null)
                {
                    PointerEventData eventData = new PointerEventData(EventSystem.current);
                    clickHandler.OnPointerClick(eventData);
                    await Task.Delay(50);
                    Debug.Log($"[Sentinel] Clicked Canvas element via IPointerClickHandler: {elementPath}");
                    return true;
                }
                
                Debug.LogWarning($"[Sentinel] Canvas element has no clickable component: {elementPath}");
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Sentinel] Canvas click failed: {ex.Message}");
                return false;
            }
        }
        
        public async Task<bool> TypeAsync(string elementPath, string text)
        {
            // Try UI Toolkit first
            VisualElement root = _rootProvider?.Invoke();
            if (root != null)
            {
                VisualElement element = root.Q(elementPath);
                if (element is TextField textField)
                {
                    textField.Focus();
                    await Task.Delay(16);
                    textField.value = text;
                    Debug.Log($"[Sentinel] Typed into UIToolkit TextField: {elementPath}");
                    return true;
                }
            }
            
            // Try Canvas/uGUI
            GameObject canvasObj = FindCanvasElement(elementPath);
            if (canvasObj != null)
            {
                // Try legacy InputField
                InputField inputField = canvasObj.GetComponent<InputField>();
                if (inputField != null)
                {
                    inputField.text = text;
                    inputField.onValueChanged?.Invoke(text);
                    await Task.Delay(50);
                    Debug.Log($"[Sentinel] Typed into Canvas InputField: {elementPath}");
                    return true;
                }
                
                // Try TMP InputField
                TMP_InputField tmpInput = canvasObj.GetComponent<TMP_InputField>();
                if (tmpInput != null)
                {
                    tmpInput.text = text;
                    tmpInput.onValueChanged?.Invoke(text);
                    await Task.Delay(50);
                    Debug.Log($"[Sentinel] Typed into Canvas TMP_InputField: {elementPath}");
                    return true;
                }
            }
            
            Debug.LogWarning($"[Sentinel] No text input found: {elementPath}");
            return false;
        }
        
        public async Task<bool> ScrollAsync(string elementPath, float delta)
        {
            // Try UI Toolkit first
            VisualElement root = _rootProvider?.Invoke();
            if (root != null)
            {
                VisualElement element = root.Q(elementPath);
                if (element is ScrollView scrollView)
                {
                    scrollView.scrollOffset += new Vector2(0, delta);
                    await Task.Delay(16);
                    Debug.Log($"[Sentinel] Scrolled UIToolkit: {elementPath} by {delta}");
                    return true;
                }
            }
            
            // Try Canvas/uGUI
            GameObject canvasObj = FindCanvasElement(elementPath);
            if (canvasObj != null)
            {
                ScrollRect scrollRect = canvasObj.GetComponent<ScrollRect>();
                if (scrollRect != null)
                {
                    scrollRect.verticalNormalizedPosition -= delta / 1000f;
                    await Task.Delay(50);
                    Debug.Log($"[Sentinel] Scrolled Canvas ScrollRect: {elementPath}");
                    return true;
                }
            }
            
            Debug.LogWarning($"[Sentinel] No scrollable element found: {elementPath}");
            return false;
        }
        
        public async Task WaitSecondsAsync(float seconds)
        {
            int ms = Mathf.RoundToInt(seconds * 1000f);
            Debug.Log($"[Sentinel] Waiting {seconds}s...");
            await Task.Delay(ms);
        }
        
        public async Task<bool> WaitForElementAsync(string elementPath, float timeoutSeconds)
        {
            float elapsed = 0f;
            float pollInterval = 0.1f;
            
            while (elapsed < timeoutSeconds)
            {
                // Check UI Toolkit
                VisualElement root = _rootProvider?.Invoke();
                if (root != null)
                {
                    VisualElement element = root.Q(elementPath);
                    if (element != null 
                        && element.resolvedStyle.display == DisplayStyle.Flex
                        && element.resolvedStyle.visibility == Visibility.Visible)
                    {
                        Debug.Log($"[Sentinel] UIToolkit element found: {elementPath}");
                        return true;
                    }
                }
                
                // Check Canvas
                GameObject canvasObj = FindCanvasElement(elementPath);
                if (canvasObj != null && canvasObj.activeInHierarchy)
                {
                    Debug.Log($"[Sentinel] Canvas element found: {elementPath}");
                    return true;
                }
                
                await Task.Delay(Mathf.RoundToInt(pollInterval * 1000f));
                elapsed += pollInterval;
            }
            
            Debug.LogWarning($"[Sentinel] Timeout waiting for: {elementPath}");
            return false;
        }
        
        /// <summary>
        /// Finds a Canvas element by name or path.
        /// Supports: "ButtonName" or "Canvas/Panel/ButtonName"
        /// </summary>
        private GameObject FindCanvasElement(string elementPath)
        {
            // Try exact path first
            GameObject found = GameObject.Find(elementPath);
            if (found != null) return found;
            
            // Try finding by name in all canvases
            Canvas[] canvases = UnityEngine.Object.FindObjectsOfType<Canvas>();
            foreach (Canvas canvas in canvases)
            {
                Transform result = FindChildRecursive(canvas.transform, elementPath);
                if (result != null) return result.gameObject;
            }
            
            return null;
        }
        
        private Transform FindChildRecursive(Transform parent, string name)
        {
            // Check exact name match
            if (parent.name == name) return parent;
            
            // Check if name is part of a path
            foreach (Transform child in parent)
            {
                if (child.name == name) return child;
                
                Transform found = FindChildRecursive(child, name);
                if (found != null) return found;
            }
            
            return null;
        }
        
        private void SimulatePointerClick(VisualElement element, Vector2 position)
        {
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
