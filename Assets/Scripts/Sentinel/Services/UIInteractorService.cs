using System;
using System.Collections.Generic;
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
                Debug.Log($"[Sentinel] Attempting to click Canvas element: {obj.name} (active: {obj.activeInHierarchy})");
                
                // Check for Button component
                UnityEngine.UI.Button button = obj.GetComponent<UnityEngine.UI.Button>();
                if (button != null)
                {
                    if (button.interactable)
                    {
                        Debug.Log($"[Sentinel] Found Button component, invoking onClick...");
                        button.onClick.Invoke();
                        await Task.Delay(50);
                        Debug.Log($"[Sentinel] ✓ Clicked Canvas Button: {elementPath}");
                        return true;
                    }
                    else
                    {
                        Debug.LogWarning($"[Sentinel] Button found but NOT interactable: {elementPath}");
                        return false;
                    }
                }
                
                // Check for Toggle
                UnityEngine.UI.Toggle toggle = obj.GetComponent<UnityEngine.UI.Toggle>();
                if (toggle != null)
                {
                    if (toggle.interactable)
                    {
                        toggle.isOn = !toggle.isOn;
                        await Task.Delay(50);
                        Debug.Log($"[Sentinel] ✓ Toggled Canvas Toggle: {elementPath}");
                        return true;
                    }
                    else
                    {
                        Debug.LogWarning($"[Sentinel] Toggle found but NOT interactable: {elementPath}");
                        return false;
                    }
                }
                
                // Try to use EventSystem for generic click
                IPointerClickHandler clickHandler = obj.GetComponent<IPointerClickHandler>();
                if (clickHandler != null)
                {
                    PointerEventData eventData = new PointerEventData(EventSystem.current);
                    clickHandler.OnPointerClick(eventData);
                    await Task.Delay(50);
                    Debug.Log($"[Sentinel] ✓ Clicked Canvas element via IPointerClickHandler: {elementPath}");
                    return true;
                }
                
                // List what components ARE on the object for debugging
                Component[] components = obj.GetComponents<Component>();
                string componentList = "";
                foreach (Component c in components)
                {
                    if (c != null) componentList += c.GetType().Name + ", ";
                }
                Debug.LogWarning($"[Sentinel] No clickable component on '{obj.name}'. Components found: [{componentList}]");
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Sentinel] Canvas click failed with exception: {ex.Message}\n{ex.StackTrace}");
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
            if (string.IsNullOrEmpty(elementPath))
            {
                Debug.LogWarning("[Sentinel] FindCanvasElement: Empty path");
                return null;
            }
            
            // Try exact Unity path first (requires absolute path from root of scene)
            GameObject found = GameObject.Find(elementPath);
            if (found != null)
            {
                Debug.Log($"[Sentinel] Found via GameObject.Find: {elementPath}");
                return found;
            }
            
            // Extract the last part of the path (the element name)
            string elementName = elementPath;
            if (elementPath.Contains("/"))
            {
                string[] parts = elementPath.Split('/');
                elementName = parts[parts.Length - 1];
            }
            
            Debug.Log($"[Sentinel] Searching for element by name: '{elementName}' (from path: {elementPath})");
            
            // Try finding by name in all canvases
            Canvas[] canvases = UnityEngine.Object.FindObjectsOfType<Canvas>();
            Debug.Log($"[Sentinel] Found {canvases.Length} Canvas objects in scene");
            
            foreach (Canvas canvas in canvases)
            {
                if (!canvas.gameObject.activeInHierarchy) continue;
                
                Transform result = FindChildByName(canvas.transform, elementName);
                if (result != null)
                {
                    Debug.Log($"[Sentinel] Found '{elementName}' in Canvas '{canvas.name}' at path: {GetFullPath(result)}");
                    return result.gameObject;
                }
            }
            
            // Try finding in all root objects (in case it's not under a Canvas)
            GameObject[] rootObjects = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
            foreach (GameObject root in rootObjects)
            {
                Transform result = FindChildByName(root.transform, elementName);
                if (result != null)
                {
                    Debug.Log($"[Sentinel] Found '{elementName}' under root '{root.name}'");
                    return result.gameObject;
                }
            }
            
            Debug.LogWarning($"[Sentinel] Element NOT FOUND: '{elementName}' (original path: {elementPath})");
            return null;
        }
        
        private Transform FindChildByName(Transform parent, string name)
        {
            // Check direct match first
            if (parent.name == name) return parent;
            
            // BFS search for the name
            Queue<Transform> queue = new Queue<Transform>();
            queue.Enqueue(parent);
            
            while (queue.Count > 0)
            {
                Transform current = queue.Dequeue();
                
                foreach (Transform child in current)
                {
                    if (child.name == name)
                    {
                        return child;
                    }
                    queue.Enqueue(child);
                }
            }
            
            return null;
        }
        
        private string GetFullPath(Transform t)
        {
            string path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return path;
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
