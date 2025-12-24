using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using Sentinel.Interfaces;

namespace Sentinel.Services
{
    /// <summary>
    /// Inspects the UI hierarchy and returns structured data for the agent.
    /// Uses UI Toolkit's Query system for element discovery.
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
            VisualElement root = _rootProvider?.Invoke();
            if (root == null)
            {
                return "{\"error\": \"No UI root available\"}";
            }
            
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"elements\": [");
            
            List<string> elements = new List<string>();
            CollectInteractableElements(root, "", elements);
            
            sb.Append(string.Join(",", elements));
            sb.Append("]}");
            
            return sb.ToString();
        }
        
        public bool ElementExists(string elementPath)
        {
            VisualElement root = _rootProvider?.Invoke();
            if (root == null) return false;
            
            VisualElement element = root.Q(elementPath);
            return element != null;
        }
        
        public string GetElementState(string elementPath)
        {
            VisualElement root = _rootProvider?.Invoke();
            if (root == null)
            {
                return "{\"error\": \"No UI root available\"}";
            }
            
            VisualElement element = root.Q(elementPath);
            if (element == null)
            {
                return "{\"error\": \"Element not found\", \"path\": \"" + elementPath + "\"}";
            }
            
            return SerializeElementState(element);
        }
        
        private void CollectInteractableElements(VisualElement element, string path, List<string> output)
        {
            if (element == null) return;
            
            string currentPath = string.IsNullOrEmpty(element.name) 
                ? path 
                : (string.IsNullOrEmpty(path) ? element.name : path + "/" + element.name);
            
            bool isInteractable = element.pickingMode == PickingMode.Position 
                && element.resolvedStyle.display == DisplayStyle.Flex
                && element.resolvedStyle.visibility == Visibility.Visible;
            
            bool isRelevant = element is Button 
                || element is TextField 
                || element is Toggle 
                || element is ScrollView
                || element is DropdownField
                || element is Slider;
            
            if (isInteractable && isRelevant && !string.IsNullOrEmpty(element.name))
            {
                output.Add(SerializeElement(element, currentPath));
            }
            
            foreach (VisualElement child in element.Children())
            {
                CollectInteractableElements(child, currentPath, output);
            }
        }
        
        private string SerializeElement(VisualElement element, string path)
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
            
            return $"{{\"name\": \"{element.name}\", \"type\": \"{type}\", \"path\": \"{path}\", \"text\": \"{EscapeJson(text)}\", \"enabled\": {element.enabledSelf.ToString().ToLower()}}}";
        }
        
        private string SerializeElementState(VisualElement element)
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
            
            return $"{{\"name\": \"{element.name}\", \"type\": \"{type}\", \"text\": \"{EscapeJson(text)}\", \"enabled\": {element.enabledSelf.ToString().ToLower()}, \"visible\": {isVisible.ToString().ToLower()}}}";
        }
        
        private string EscapeJson(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }
    }
}
