using LLama.Common;
using LLama.Sampling;

namespace LMSupplyDepots.External.LLamaEngine;

public static class ParameterFactory
{
    public static InferenceParams NewInferenceParams(
        int maxTokens = 2048,
        IEnumerable<string>? antiprompt = null,
        float temperature = 0.7f,
        float topP = 0.9f,
        float repeatPenalty = 1.1f)
    {
        // Default LLaMA-3.2 compatible stop sequences if none provided
        var defaultAntiPrompts = new List<string>
        {
            "<|eot_id|>",
            "<|start_header_id|>user<|end_header_id|>",
            "<|start_header_id|>system<|end_header_id|>",
            "\nuser:",
            "\nassistant:",
            "\nsystem:"
        };

        var finalAntiPrompts = antiprompt != null ? [.. antiprompt] : defaultAntiPrompts;

        // Set inference parameters
        return new InferenceParams
        {
            MaxTokens = maxTokens,
            AntiPrompts = finalAntiPrompts,
            SamplingPipeline = new DefaultSamplingPipeline
            {
                Temperature = temperature,
                TopP = topP,
                RepeatPenalty = repeatPenalty
            }
        };
    }
}