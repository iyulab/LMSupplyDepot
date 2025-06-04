using LMSupplyDepots.Host;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Add host services
builder.Services.AddLMSupplyDepots(options =>
{
    // Configure from appsettings.json
    builder.Configuration.GetSection("LMSupplyDepots").Bind(options);
});

// Configure JSON serialization
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

// Add CORS policy
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", builder =>
    {
        builder.AllowAnyOrigin()
               .AllowAnyMethod()
               .AllowAnyHeader();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/error");
}

// HTTPS 리디렉션 제거 - HTTP와 HTTPS 모두 허용
// app.UseHttpsRedirection();

app.UseCors("AllowAll");

// Simple health check endpoint
app.MapGet("/", () => new { status = "running", service = "LMSupplyDepots API" });

// Global error handler for non-controller code
app.Map("/error", (HttpContext httpContext) =>
{
    var exceptionHandler = httpContext.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
    var exception = exceptionHandler?.Error;

    return Results.Problem(
        title: "An error occurred",
        detail: exception?.Message,
        statusCode: 500
    );
});

// Map controllers
app.MapControllers();

// Version endpoint for compatibility with clients
app.MapGet("/api/version", () => new { version = "0.1.0" });

app.Run();