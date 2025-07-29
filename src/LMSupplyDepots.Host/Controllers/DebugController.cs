using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using LMSupplyDepots.Interfaces;
using LMSupplyDepots.Inference.Adapters;

namespace LMSupplyDepots.Host.Controllers;

/// <summary>
/// Debug controller for troubleshooting DI registrations
/// </summary>
[ApiController]
[Route("api/debug")]
public class DebugController : ControllerBase
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DebugController> _logger;

    public DebugController(IServiceProvider serviceProvider, ILogger<DebugController> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Gets information about registered services
    /// </summary>
    [HttpGet("services")]
    public ActionResult<object> GetServiceInfo()
    {
        try
        {
            // Check IModelLoader registration
            var modelLoader = _serviceProvider.GetService<IModelLoader>();
            var modelLoaderType = modelLoader?.GetType();

            // Check RepositoryModelLoaderService registration  
            var repositoryModelLoader = _serviceProvider.GetService<LMSupplyDepots.Inference.Services.RepositoryModelLoaderService>();
            var repositoryModelLoaderType = repositoryModelLoader?.GetType();

            // Check BaseModelAdapter registrations
            var adapters = _serviceProvider.GetServices<BaseModelAdapter>().ToList();

            // Check LLama-specific services
            var llamaModelManager = _serviceProvider.GetService<LMSupplyDepots.External.LLamaEngine.Services.ILLamaModelManager>();
            var llamaBackendService = _serviceProvider.GetService<LMSupplyDepots.External.LLamaEngine.Services.ILLamaBackendService>();
            var llmService = _serviceProvider.GetService<LMSupplyDepots.External.LLamaEngine.Services.ILLMService>();

            var result = new
            {
                ModelLoader = new
                {
                    Type = modelLoaderType?.FullName,
                    Assembly = modelLoaderType?.Assembly.GetName().Name,
                    IsNull = modelLoader == null
                },
                RepositoryModelLoader = new
                {
                    Type = repositoryModelLoaderType?.FullName,
                    Assembly = repositoryModelLoaderType?.Assembly.GetName().Name,
                    IsNull = repositoryModelLoader == null
                },
                Adapters = adapters.Select(a => new
                {
                    Type = a.GetType().FullName,
                    AdapterName = a.AdapterName,
                    SupportedFormats = a.SupportedFormats,
                    SupportedModelTypes = a.SupportedModelTypes.Select(t => t.ToString()).ToList()
                }).ToList(),
                AdapterCount = adapters.Count,
                LLamaServices = new
                {
                    ModelManager = new
                    {
                        Type = llamaModelManager?.GetType().FullName,
                        IsRegistered = llamaModelManager != null
                    },
                    BackendService = new
                    {
                        Type = llamaBackendService?.GetType().FullName,
                        IsRegistered = llamaBackendService != null
                    },
                    LLMService = new
                    {
                        Type = llmService?.GetType().FullName,
                        IsRegistered = llmService != null
                    }
                }
            };

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting service info");
            return StatusCode(500, new { Error = ex.Message, StackTrace = ex.StackTrace });
        }
    }
}
