using Microsoft.Extensions.Configuration;
using System.Text;

namespace LMSupplyDepots.External.OpenAI.SampleConsoleApp.ChatSample;

class Program
{
    private static string ApiKey;
    private static OpenAIService _openAI;
    private static List<(string role, string content)> _conversation = new();
}
