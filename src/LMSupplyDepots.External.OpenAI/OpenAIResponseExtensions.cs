using OpenAI.Responses;

namespace LMSupplyDepots.External.OpenAI;

/// <summary>
/// Extension methods for OpenAIResponse
/// </summary>
public static class OpenAIResponseExtensions
{
    /// <summary>
    /// Extracts output text from the response
    /// </summary>
    public static string GetOutputText(this OpenAIResponse response)
    {
        string text = string.Empty;

        // Extract output text from message items in the response
        foreach (var item in response.OutputItems)
        {
            if (item is MessageResponseItem message)
            {
                foreach (var part in message.Content)
                {
                    // Access text content through the Text property
                    if (part.Kind == ResponseContentPartKind.OutputText)
                    {
                        text += part.Text;
                    }
                }
            }
        }

        return text;
    }

    /// <summary>
    /// Extracts file citation annotations from the response
    /// </summary>
    public static List<ResponseMessageAnnotation> FileAnnotations(this OpenAIResponse response)
    {
        var annotations = new List<ResponseMessageAnnotation>();

        // Extract annotations from message items in the response
        foreach (var item in response.OutputItems)
        {
            if (item is MessageResponseItem message)
            {
                foreach (var part in message.Content)
                {
                    if (part.Kind == ResponseContentPartKind.OutputText)
                    {
                        // Access annotations through OutputTextAnnotations property
                        var outputTextAnnotations = part.OutputTextAnnotations;
                        if (outputTextAnnotations != null)
                        {
                            foreach (var annotation in outputTextAnnotations)
                            {
                                // Add only file citation annotations
                                if (annotation.Kind == ResponseMessageAnnotationKind.FileCitation)
                                {
                                    annotations.Add(annotation);
                                }
                            }
                        }
                    }
                }
            }
        }

        return annotations;
    }
}