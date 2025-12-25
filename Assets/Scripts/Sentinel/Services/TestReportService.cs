using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Sentinel.Interfaces;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Sentinel.Services
{
    /// <summary>
    /// Generates test reports with step logs and screenshots.
    /// Outputs Markdown files for easy review.
    /// Works in both Editor and Play Mode.
    /// </summary>
    public class TestReportService : ITestReporter
    {
        private string _testName;
        private DateTime _startTime;
        private List<TestStep> _steps;
        private string _reportDirectory;
        
        public TestReportService(string reportDirectory = "Assets/TestReports")
        {
            _reportDirectory = reportDirectory;
            _steps = new List<TestStep>();
        }
        
        public void StartTest(string testName)
        {
            _testName = testName;
            _startTime = DateTime.Now;
            _steps.Clear();
            
            // Ensure directory exists
            string fullPath = GetFullPath(_reportDirectory);
            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
            }
            
            Debug.Log($"[Sentinel] Test started: {testName}");
        }
        
        public void LogStep(string action, string result)
        {
            _steps.Add(new TestStep
            {
                Timestamp = DateTime.Now,
                Action = action,
                Result = result
            });
            
            Debug.Log($"[Sentinel] Step: {action} -> {result}");
        }
        
        public string CaptureScreenshot(string label)
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string safeName = SanitizeFilename(label);
            string filename = $"{safeName}_{timestamp}.png";
            string fullPath = GetFullPath(Path.Combine(_reportDirectory, filename));
            
            // Ensure directory exists
            string dir = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            
            try
            {
#if UNITY_EDITOR
                // In Editor, use different approach based on play state
                if (Application.isPlaying)
                {
                    // Use ScreenCapture for Play Mode (requires relative path)
                    string relativePath = Path.Combine(_reportDirectory, filename);
                    ScreenCapture.CaptureScreenshot(relativePath);
                    Debug.Log($"[Sentinel] Screenshot queued (Play Mode): {relativePath}");
                    
                    _steps.Add(new TestStep
                    {
                        Timestamp = DateTime.Now,
                        Action = $"Screenshot: {label}",
                        Result = filename,
                        IsScreenshot = true
                    });
                    
                    return relativePath;
                }
                else
                {
                    // In Edit Mode, capture the Game view or Scene view
                    Texture2D screenshot = CaptureEditorScreenshot();
                    if (screenshot != null)
                    {
                        byte[] bytes = screenshot.EncodeToPNG();
                        File.WriteAllBytes(fullPath, bytes);
                        UnityEngine.Object.DestroyImmediate(screenshot);
                        
                        _steps.Add(new TestStep
                        {
                            Timestamp = DateTime.Now,
                            Action = $"Screenshot: {label}",
                            Result = filename,
                            IsScreenshot = true
                        });
                        
                        Debug.Log($"[Sentinel] Screenshot saved (Edit Mode): {fullPath}");
                        AssetDatabase.Refresh();
                        return fullPath;
                    }
                    else
                    {
                        Debug.LogWarning("[Sentinel] Could not capture Editor screenshot");
                        return null;
                    }
                }
#else
                // Runtime build - use ScreenCapture
                string relativePath = Path.Combine(_reportDirectory, filename);
                ScreenCapture.CaptureScreenshot(relativePath);
                
                _steps.Add(new TestStep
                {
                    Timestamp = DateTime.Now,
                    Action = $"Screenshot: {label}",
                    Result = filename,
                    IsScreenshot = true
                });
                
                Debug.Log($"[Sentinel] Screenshot queued: {relativePath}");
                return relativePath;
#endif
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Sentinel] Screenshot failed: {ex.Message}");
                return null;
            }
        }
        
#if UNITY_EDITOR
        private Texture2D CaptureEditorScreenshot()
        {
            try
            {
                // Try to capture the Game view
                System.Reflection.Assembly assembly = typeof(EditorWindow).Assembly;
                System.Type gameViewType = assembly.GetType("UnityEditor.GameView");
                
                if (gameViewType != null)
                {
                    EditorWindow gameView = EditorWindow.GetWindow(gameViewType, false, null, false);
                    if (gameView != null)
                    {
                        gameView.Repaint();
                        
                        int width = (int)gameView.position.width;
                        int height = (int)gameView.position.height;
                        
                        if (width > 0 && height > 0)
                        {
                            Texture2D screenshot = new Texture2D(width, height, TextureFormat.RGB24, false);
                            // Note: This won't capture the actual game view content in Edit mode
                            // but will at least create a placeholder
                            screenshot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                            screenshot.Apply();
                            return screenshot;
                        }
                    }
                }
                
                // Fallback: Create a simple placeholder
                Texture2D placeholder = new Texture2D(800, 600, TextureFormat.RGB24, false);
                Color[] colors = new Color[800 * 600];
                for (int i = 0; i < colors.Length; i++)
                {
                    colors[i] = new Color(0.2f, 0.2f, 0.3f);
                }
                placeholder.SetPixels(colors);
                placeholder.Apply();
                
                Debug.LogWarning("[Sentinel] Created placeholder screenshot (Edit Mode limitation)");
                return placeholder;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Sentinel] Editor screenshot failed: {ex.Message}");
                return null;
            }
        }
#endif
        
        public string FinishTest(bool success, string summary)
        {
            DateTime endTime = DateTime.Now;
            TimeSpan duration = endTime - _startTime;
            
            string reportFilename = $"TestReport_{SanitizeFilename(_testName)}_{_startTime:yyyyMMdd_HHmmss}.md";
            string fullPath = GetFullPath(Path.Combine(_reportDirectory, reportFilename));
            
            StringBuilder sb = new StringBuilder();
            
            // Header
            sb.AppendLine($"# Test Report: {_testName}");
            sb.AppendLine();
            sb.AppendLine($"**Status**: {(success ? "✅ PASSED" : "❌ FAILED")}");
            sb.AppendLine($"**Start**: {_startTime:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"**End**: {endTime:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"**Duration**: {duration.TotalSeconds:F2}s");
            sb.AppendLine();
            
            // Summary
            sb.AppendLine("## Summary");
            sb.AppendLine(summary);
            sb.AppendLine();
            
            // Steps
            sb.AppendLine("## Steps");
            sb.AppendLine();
            sb.AppendLine("| # | Time | Action | Result |");
            sb.AppendLine("|---|------|--------|--------|");
            
            for (int i = 0; i < _steps.Count; i++)
            {
                TestStep step = _steps[i];
                string time = step.Timestamp.ToString("HH:mm:ss");
                string result = step.IsScreenshot 
                    ? $"![{step.Action}]({step.Result})" 
                    : step.Result;
                
                sb.AppendLine($"| {i + 1} | {time} | {step.Action} | {result} |");
            }
            
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine("*Generated by Sentinel Testing Agent*");
            
            try
            {
                File.WriteAllText(fullPath, sb.ToString());
                Debug.Log($"[Sentinel] Report saved: {fullPath}");
                
#if UNITY_EDITOR
                AssetDatabase.Refresh();
#endif
                
                return fullPath;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Sentinel] Report save failed: {ex.Message}");
                return null;
            }
        }
        
        private string GetFullPath(string relativePath)
        {
            if (Path.IsPathRooted(relativePath))
            {
                return relativePath;
            }
            
            // Convert relative Unity path to absolute
            return Path.Combine(Application.dataPath, "..", relativePath).Replace("\\", "/");
        }
        
        private string SanitizeFilename(string name)
        {
            if (string.IsNullOrEmpty(name)) return "unnamed";
            
            char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder sb = new StringBuilder();
            
            foreach (char c in name)
            {
                if (Array.IndexOf(invalid, c) < 0 && c != ' ')
                {
                    sb.Append(c);
                }
                else if (c == ' ')
                {
                    sb.Append('_');
                }
            }
            
            return sb.ToString();
        }
        
        private struct TestStep
        {
            public DateTime Timestamp;
            public string Action;
            public string Result;
            public bool IsScreenshot;
        }
    }
}
