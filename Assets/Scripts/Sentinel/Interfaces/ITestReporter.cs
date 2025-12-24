namespace Sentinel.Interfaces
{
    /// <summary>
    /// Generates test reports with screenshots and step logging.
    /// </summary>
    public interface ITestReporter
    {
        /// <summary>
        /// Starts a new test with the given name.
        /// </summary>
        void StartTest(string testName);
        
        /// <summary>
        /// Logs a step in the current test.
        /// </summary>
        void LogStep(string action, string result);
        
        /// <summary>
        /// Captures a screenshot with a label.
        /// </summary>
        string CaptureScreenshot(string label);
        
        /// <summary>
        /// Finishes the test and returns the report path.
        /// </summary>
        string FinishTest(bool success, string summary);
    }
}
