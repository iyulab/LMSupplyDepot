using System.Text.Json;
using LMSupplyDepots.Host.Models.OpenAI;

namespace LMSupplyDepots.Host.Tests.Verification;

/// <summary>
/// Manual verification helper for testing alias functionality
/// </summary>
public class AliasVerificationHelper
{
    /// <summary>
    /// Simulates the expected HTTP requests to verify alias functionality
    /// This can be used as a reference for manual testing
    /// </summary>
    public static void PrintTestScenarios()
    {
        Console.WriteLine("=== V1 Models Alias Functionality Verification ===");
        Console.WriteLine();

        Console.WriteLine("1. Start the application:");
        Console.WriteLine("   cd d:\\data\\LMSupplyDepot\\src");
        Console.WriteLine("   dotnet run --project LMSupplyDepots.HostApp");
        Console.WriteLine();

        Console.WriteLine("2. Check initial models (should use full IDs if no aliases):");
        Console.WriteLine("   GET http://localhost:12100/v1/models");
        Console.WriteLine();

        Console.WriteLine("3. Set an alias for a model:");
        Console.WriteLine("   PUT http://localhost:12100/api/alias");
        Console.WriteLine("   Content-Type: application/json");
        Console.WriteLine("   {");
        Console.WriteLine("     \"name\": \"hf:DevQuasar/naver-hyperclovax.HyperCLOVAX-SEED-Text-Instruct-0.5B-GGUF/naver-hyperclovax.HyperCLOVAX-SEED-Text-Instruct-0.5B.f16\",");
        Console.WriteLine("     \"alias\": \"hyperclovax\"");
        Console.WriteLine("   }");
        Console.WriteLine();

        Console.WriteLine("4. Check models again (should now show alias as ID):");
        Console.WriteLine("   GET http://localhost:12100/v1/models");
        Console.WriteLine("   Expected result should include:");
        Console.WriteLine("   {");
        Console.WriteLine("     \"object\": \"list\",");
        Console.WriteLine("     \"data\": [");
        Console.WriteLine("       {");
        Console.WriteLine("         \"id\": \"hyperclovax\",  // <-- This should be the alias, not the full ID");
        Console.WriteLine("         \"object\": \"model\",");
        Console.WriteLine("         \"created\": 1234567890,");
        Console.WriteLine("         \"owned_by\": \"local\",");
        Console.WriteLine("         \"type\": \"text-generation\"");
        Console.WriteLine("       }");
        Console.WriteLine("     ]");
        Console.WriteLine("   }");
        Console.WriteLine();

        Console.WriteLine("5. Remove the alias:");
        Console.WriteLine("   PUT http://localhost:12100/api/alias");
        Console.WriteLine("   Content-Type: application/json");
        Console.WriteLine("   {");
        Console.WriteLine("     \"name\": \"hyperclovax\",");
        Console.WriteLine("     \"alias\": null");
        Console.WriteLine("   }");
        Console.WriteLine();

        Console.WriteLine("6. Check models final time (should show full ID again):");
        Console.WriteLine("   GET http://localhost:12100/v1/models");
        Console.WriteLine("   Expected: ID should be back to full model ID");
        Console.WriteLine();

        Console.WriteLine("=== Test Commands (PowerShell/curl) ===");
        Console.WriteLine();

        Console.WriteLine("# Check models");
        Console.WriteLine("Invoke-RestMethod -Uri 'http://localhost:12100/v1/models' -Method Get");
        Console.WriteLine();

        Console.WriteLine("# Set alias");
        Console.WriteLine("$body = @{");
        Console.WriteLine("    name = 'hf:DevQuasar/naver-hyperclovax.HyperCLOVAX-SEED-Text-Instruct-0.5B-GGUF/naver-hyperclovax.HyperCLOVAX-SEED-Text-Instruct-0.5B.f16'");
        Console.WriteLine("    alias = 'hyperclovax'");
        Console.WriteLine("} | ConvertTo-Json");
        Console.WriteLine("Invoke-RestMethod -Uri 'http://localhost:12100/api/alias' -Method Put -Body $body -ContentType 'application/json'");
        Console.WriteLine();

        Console.WriteLine("# Remove alias");
        Console.WriteLine("$body = @{");
        Console.WriteLine("    name = 'hyperclovax'");
        Console.WriteLine("    alias = $null");
        Console.WriteLine("} | ConvertTo-Json");
        Console.WriteLine("Invoke-RestMethod -Uri 'http://localhost:12100/api/alias' -Method Put -Body $body -ContentType 'application/json'");
        Console.WriteLine();
    }

    /// <summary>
    /// Validates that a models response uses aliases correctly
    /// </summary>
    public static bool ValidateModelsResponse(string jsonResponse, string expectedAliasId)
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var response = JsonSerializer.Deserialize<OpenAIModelsResponse>(jsonResponse, options);

            if (response?.Data == null)
            {
                Console.WriteLine("❌ Invalid response format");
                return false;
            }

            var modelWithExpectedId = response.Data.FirstOrDefault(m => m.Id == expectedAliasId);

            if (modelWithExpectedId == null)
            {
                Console.WriteLine($"❌ Model with ID '{expectedAliasId}' not found");
                Console.WriteLine($"   Available IDs: {string.Join(", ", response.Data.Select(m => m.Id))}");
                return false;
            }

            Console.WriteLine($"✅ Found model with expected ID: {expectedAliasId}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error validating response: {ex.Message}");
            return false;
        }
    }
}
