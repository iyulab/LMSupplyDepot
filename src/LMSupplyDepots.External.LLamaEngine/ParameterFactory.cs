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
        // Set inference parameters
        return new InferenceParams
        {
            MaxTokens = maxTokens,
            AntiPrompts = antiprompt != null ? [.. antiprompt] : [], // Empty by default
            SamplingPipeline = new DefaultSamplingPipeline
            {
                Temperature = temperature,
                TopP = topP,
                RepeatPenalty = repeatPenalty
            }
        };
    }
}