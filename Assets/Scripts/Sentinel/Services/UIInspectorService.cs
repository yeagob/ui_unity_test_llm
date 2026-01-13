using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;
using Sentinel.Interfaces;
using TMPro;


namespace Sentinel.Services
{
    /// <summary>
    /// Inspects the UI hierarchy and returns structured data for the agent.
    /// Supports both UI Toolkit (VisualElement) and uGUI (Canvas).
    /// </summary>
    public class UIInspectorService : IUIInspector
    {
        private Func<VisualElement> _rootProvider;
        
        public UIInspectorService(Func<VisualElement> rootProvider)
        {
            _rootProvider = rootProvider;
        }
        
        public string GetUIHierarchy()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{");
            
            List<string> elements = new List<string>();
            bool hasUIToolkit = false;
            bool hasCanvas = false;
            
            // Try UI Toolkit first
            VisualElement root = _rootProvider?.Invoke();
            if (root != null)
            {
                hasUIToolkit = true;
                CollectUIToolkitElements(root, "", elements);
            }
            
            // Also check for Canvas (uGUI)
            Canvas[] canvases = UnityEngine.Object.FindObjectsOfType<Canvas>();
            if (canvases != null && canvases.Length > 0)
            {
                hasCanvas = true;
                foreach (Canvas canvas in canvases)
                {
                    if (canvas.gameObject.activeInHierarchy)
                    {
                        // Get parent path (so the canvas name gets added by CollectCanvasElements)
                        string parentPath = canvas.transform.parent != null 
                            ? GetGameObjectFullPath(canvas.transform.parent) 
                            : "";
                        CollectCanvasElements(canvas.gameObject, parentPath, elements);
                    }
                }
            }
            
            // Build response
            if (!hasUIToolkit && !hasCanvas)
            {
                return "{\"error\": \"No UI found (no UIDocument or Canvas in scene)\"}";
            }
            
            // Count visible vs total elements
            int visibleCount = 0;
            int interactableCount = 0;
            foreach (string elem in elements)
            {
                if (elem.Contains("\"visible\": true"))
                {
                    visibleCount++;
                    if (elem.Contains("\"enabled\": true") && !elem.Contains("\"type\": \"Text\""))
                    {
                        interactableCount++;
                    }
                }
            }
            
            sb.Append($"\"uiType\": \"{(hasUIToolkit && hasCanvas ? "Both" : hasUIToolkit ? "UIToolkit" : "Canvas")}\", ");
            sb.Append($"\"canvasCount\": {canvases?.Length ?? 0}, ");
            sb.Append($"\"totalElements\": {elements.Count}, ");
            sb.Append($"\"visibleElements\": {visibleCount}, ");
            sb.Append($"\"interactableVisible\": {interactableCount}, ");
            sb.Append("\"elements\": [");
            sb.Append(string.Join(",", elements));
            sb.Append("]}");
            
            return sb.ToString();
        }
        
        public bool ElementExists(string elementPath)
        {
            // Check UI Toolkit
            VisualElement root = _rootProvider?.Invoke();
            if (root != null)
            {
                VisualElement element = root.Q(elementPath);
                if (element != null) return true;
            }
            
            // Check Canvas
            GameObject found = GameObject.Find(elementPath);
            return found != null;
        }
        
        public string GetElementState(string elementPath)
        {
            // Check UI Toolkit first
            VisualElement root = _rootProvider?.Invoke();
            if (root != null)
            {
                VisualElement element = root.Q(elementPath);
                if (element != null)
                {
                    return SerializeUIToolkitElementState(element);
                }
            }
            
            // Check Canvas
            GameObject found = GameObject.Find(elementPath);
            if (found != null)
            {
                return SerializeCanvasElementState(found);
            }
            
            return "{\"error\": \"Element not found\", \"path\": \"" + EscapeJson(elementPath) + "\"}";
        }
        
        #region UI Toolkit Collection
        
        private void CollectUIToolkitElements(VisualElement element, string path, List<string> output)
        {
            if (element == null) return;
            
            string currentPath = string.IsNullOrEmpty(element.name) 
                ? path 
                : (string.IsNullOrEmpty(path) ? element.name : path + "/" + element.name);
            
            bool isInteractable = element.pickingMode == PickingMode.Position 
                && element.resolvedStyle.display == DisplayStyle.Flex
                && element.resolvedStyle.visibility == Visibility.Visible;
            
            bool isRelevant = element is UnityEngine.UIElements.Button 
                || element is TextField 
                || element is UnityEngine.UIElements.Toggle 
                || element is ScrollView
                || element is DropdownField
                || element is UnityEngine.UIElements.Slider;
            
            if (isInteractable && isRelevant && !string.IsNullOrEmpty(element.name))
            {
                output.Add(SerializeUIToolkitElement(element, currentPath));
            }
            
            foreach (VisualElement child in element.Children())
            {
                CollectUIToolkitElements(child, currentPath, output);
            }
        }
        
        private string SerializeUIToolkitElement(VisualElement element, string path)
        {
            string type = element.GetType().Name;
            string text = "";
            
            if (element is TextElement textElement)
            {
                text = textElement.text ?? "";
            }
            else if (element is TextField textField)
            {
                text = textField.value ?? "";
            }
            
            return $"{{\"name\": \"{EscapeJson(element.name)}\", \"type\": \"{type}\", \"uiSystem\": \"UIToolkit\", \"path\": \"{EscapeJson(path)}\", \"text\": \"{EscapeJson(text)}\", \"enabled\": {element.enabledSelf.ToString().ToLower()}}}";
        }
        
        private string SerializeUIToolkitElementState(VisualElement element)
        {
            string type = element.GetType().Name;
            string text = "";
            
            if (element is TextElement textElement)
            {
                text = textElement.text ?? "";
            }
            else if (element is TextField textField)
            {
                text = textField.value ?? "";
            }
            
            bool isVisible = element.resolvedStyle.display == DisplayStyle.Flex 
                && element.resolvedStyle.visibility == Visibility.Visible;
            
            return $"{{\"name\": \"{EscapeJson(element.name)}\", \"type\": \"{type}\", \"uiSystem\": \"UIToolkit\", \"text\": \"{EscapeJson(text)}\", \"enabled\": {element.enabledSelf.ToString().ToLower()}, \"visible\": {isVisible.ToString().ToLower()}}}";
        }
        
        #endregion
        
        #region Canvas (uGUI) Collection
        
        private void CollectCanvasElements(GameObject obj, string path, List<string> output)
        {
            if (obj == null || !obj.activeInHierarchy) return;
            
            string currentPath = string.IsNullOrEmpty(path) ? obj.name : path + "/" + obj.name;
            
            // Check visibility (alpha, scale, on-screen position)
            bool isVisible = IsCanvasElementVisible(obj);
            string visibleStr = isVisible ? "true" : "false";
            
            // Check for interactable components
            UnityEngine.UI.Button button = obj.GetComponent<UnityEngine.UI.Button>();
            InputField inputField = obj.GetComponent<InputField>();
            TMP_InputField tmpInput = obj.GetComponent<TMP_InputField>();
            UnityEngine.UI.Toggle toggle = obj.GetComponent<UnityEngine.UI.Toggle>();
            UnityEngine.UI.Slider slider = obj.GetComponent<UnityEngine.UI.Slider>();
            Text text = obj.GetComponent<Text>();
            TMP_Text tmpText = obj.GetComponent<TMP_Text>();
            
            if (button != null)
            {
                string btnText = GetButtonText(obj);
                output.Add($"{{\"name\": \"{EscapeJson(obj.name)}\", \"type\": \"Button\", \"uiSystem\": \"Canvas\", \"path\": \"{EscapeJson(currentPath)}\", \"text\": \"{EscapeJson(btnText)}\", \"enabled\": {button.interactable.ToString().ToLower()}, \"visible\": {visibleStr}}}");
            }
            else if (inputField != null)
            {
                output.Add($"{{\"name\": \"{EscapeJson(obj.name)}\", \"type\": \"InputField\", \"uiSystem\": \"Canvas\", \"path\": \"{EscapeJson(currentPath)}\", \"text\": \"{EscapeJson(inputField.text)}\", \"enabled\": {inputField.interactable.ToString().ToLower()}, \"visible\": {visibleStr}}}");
            }
            else if (tmpInput != null)
            {
                output.Add($"{{\"name\": \"{EscapeJson(obj.name)}\", \"type\": \"TMP_InputField\", \"uiSystem\": \"Canvas\", \"path\": \"{EscapeJson(currentPath)}\", \"text\": \"{EscapeJson(tmpInput.text)}\", \"enabled\": {tmpInput.interactable.ToString().ToLower()}, \"visible\": {visibleStr}}}");
            }
            else if (toggle != null)
            {
                output.Add($"{{\"name\": \"{EscapeJson(obj.name)}\", \"type\": \"Toggle\", \"uiSystem\": \"Canvas\", \"path\": \"{EscapeJson(currentPath)}\", \"isOn\": {toggle.isOn.ToString().ToLower()}, \"enabled\": {toggle.interactable.ToString().ToLower()}, \"visible\": {visibleStr}}}");
            }
            else if (slider != null)
            {
                output.Add($"{{\"name\": \"{EscapeJson(obj.name)}\", \"type\": \"Slider\", \"uiSystem\": \"Canvas\", \"path\": \"{EscapeJson(currentPath)}\", \"value\": {slider.value}, \"enabled\": {slider.interactable.ToString().ToLower()}, \"visible\": {visibleStr}}}");
            }
            // Include Text elements for reference (not interactive but useful for context)
            else if (tmpText != null && !string.IsNullOrEmpty(tmpText.text))
            {
                output.Add($"{{\"name\": \"{EscapeJson(obj.name)}\", \"type\": \"Text\", \"uiSystem\": \"Canvas\", \"path\": \"{EscapeJson(currentPath)}\", \"text\": \"{EscapeJson(tmpText.text)}\", \"interactable\": false, \"visible\": {visibleStr}}}");
            }
            else if (text != null && !string.IsNullOrEmpty(text.text))
            {
                output.Add($"{{\"name\": \"{EscapeJson(obj.name)}\", \"type\": \"Text\", \"uiSystem\": \"Canvas\", \"path\": \"{EscapeJson(currentPath)}\", \"text\": \"{EscapeJson(text.text)}\", \"interactable\": false, \"visible\": {visibleStr}}}");
            }
            
            // Recurse children
            foreach (Transform child in obj.transform)
            {
                CollectCanvasElements(child.gameObject, currentPath, output);
            }
        }
        
        /// <summary>
        /// Determines if a Canvas element is actually visible to the user.
        /// Checks: CanvasGroup alpha, RectTransform scale, and screen position.
        /// </summary>
        private bool IsCanvasElementVisible(GameObject obj)
        {
            // Check all parent CanvasGroups for alpha
            Transform current = obj.transform;
            while (current != null)
            {
                CanvasGroup cg = current.GetComponent<CanvasGroup>();
                if (cg != null && cg.alpha < 0.1f)
                {
                    return false; // Hidden by CanvasGroup alpha
                }
                current = current.parent;
            }
            
            // Check scale (if scale is 0, element is invisible)
            Vector3 scale = obj.transform.lossyScale;
            if (Mathf.Approximately(scale.x, 0f) || Mathf.Approximately(scale.y, 0f))
            {
                return false;
            }
            
            // Check if RectTransform is on screen
            RectTransform rectTransform = obj.GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                // Get the canvas this element belongs to
                Canvas canvas = obj.GetComponentInParent<Canvas>();
                if (canvas != null && canvas.worldCamera != null)
                {
                    Vector3[] corners = new Vector3[4];
                    rectTransform.GetWorldCorners(corners);
                    
                    // Check if any corner is within screen bounds
                    Camera cam = canvas.worldCamera;
                    bool anyCornerOnScreen = false;
                    foreach (Vector3 corner in corners)
                    {
                        Vector3 screenPoint = cam.WorldToScreenPoint(corner);
                        if (screenPoint.x >= 0 && screenPoint.x <= Screen.width &&
                            screenPoint.y >= 0 && screenPoint.y <= Screen.height &&
                            screenPoint.z > 0)
                        {
                            anyCornerOnScreen = true;
                            break;
                        }
                    }
                    
                    if (!anyCornerOnScreen)
                    {
                        return false; // Off screen
                    }
                }
            }
            
            return true;
        }
        
        private string GetButtonText(GameObject buttonObj)
        {
            // Try to find text in children
            Text textComp = buttonObj.GetComponentInChildren<Text>();
            if (textComp != null) return textComp.text ?? "";
            
            TMP_Text tmpText = buttonObj.GetComponentInChildren<TMP_Text>();
            if (tmpText != null) return tmpText.text ?? "";
            
            return "";
        }
        
        private string SerializeCanvasElementState(GameObject obj)
        {
            bool isActive = obj.activeInHierarchy;
            string type = "GameObject";
            bool enabled = true;
            string text = "";
            
            UnityEngine.UI.Button button = obj.GetComponent<UnityEngine.UI.Button>();
            if (button != null)
            {
                type = "Button";
                enabled = button.interactable;
                text = GetButtonText(obj);
            }
            
            InputField inputField = obj.GetComponent<InputField>();
            if (inputField != null)
            {
                type = "InputField";
                enabled = inputField.interactable;
                text = inputField.text;
            }
            
            TMP_InputField tmpInput = obj.GetComponent<TMP_InputField>();
            if (tmpInput != null)
            {
                type = "TMP_InputField";
                enabled = tmpInput.interactable;
                text = tmpInput.text;
            }
            
            return $"{{\"name\": \"{EscapeJson(obj.name)}\", \"type\": \"{type}\", \"uiSystem\": \"Canvas\", \"text\": \"{EscapeJson(text)}\", \"enabled\": {enabled.ToString().ToLower()}, \"visible\": {isActive.ToString().ToLower()}}}";
        }
        
        #endregion
        
        private string EscapeJson(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }
        
        /// <summary>
        /// Gets the full hierarchy path from scene root (for GameObject.Find compatibility).
        /// Example: "Main_Menu/Canv_Main/MAIN/VerticalLayout/Btn_Settings"
        /// </summary>
        private string GetGameObjectFullPath(Transform t)
        {
            string path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return path;
        }
    }
}
