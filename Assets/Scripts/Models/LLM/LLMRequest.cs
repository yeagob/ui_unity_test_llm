using System;
using System.Collections.Generic;
using ChatSystem.Models.Context;
using ChatSystem.Models.Tools;
using ChatSystem.Enums;

namespace ChatSystem.Models.LLM
{
    /// <summary>
    /// Reasoning effort level for models that support extended thinking (o1, GPT-5.x).
    /// </summary>
    public enum ReasoningEffort
    {
        None,   // No reasoning/thinking (default, fastest)
        Low,    // Minimal reasoning, prioritize speed
        Medium, // Balanced reasoning and speed
        High    // Maximum reasoning depth (slowest, best for planning)
    }
    
    [Serializable]
    public class LLMRequest
    {
        public List<Message> messages;
        public List<ToolConfiguration> tools;
        public int maxTokens;
        public float temperature;
        public string model;
        public ServiceProvider provider;
        
        /// <summary>
        /// Enable extended thinking/reasoning for compatible models (o1, GPT-5.x).
        /// When enabled, the model will use internal chain-of-thought before responding.
        /// </summary>
        public bool enableThinking;
        
        /// <summary>
        /// Level of reasoning effort when thinking is enabled.
        /// Higher values = more thorough reasoning but slower response.
        /// </summary>
        public ReasoningEffort reasoningEffort;
        
        public LLMRequest()
        {
            messages = new List<Message>();
            tools = new List<ToolConfiguration>();
            maxTokens = 2048;
            temperature = 0.7f;
            model = "default";
            provider = ServiceProvider.Custom;
            enableThinking = false;
            reasoningEffort = ReasoningEffort.None;
        }
    }
}