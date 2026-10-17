using Azure.Monitor.OpenTelemetry.AspNetCore;
using Expovice.Api.Endpoints;
using Expovice.Api.Infrastructure;
using Expovice.Contracts;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

if (!string.IsNullOrEmpty(config["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor();
}

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    // App Service terminates TLS in front of us; trust its X-Forwarded-* headers.
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});
builder.Services
    .AddExpoviceData(config)
    .AddExpoviceAuth(config, builder.Environment)
    .AddExpoviceMessaging(config)
    .AddExpoviceStorage(config);

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks(ApiRoutes.Health).AllowAnonymous();
app.MapAuthEndpoints()
    .MapMeEndpoints()
    .MapProviderEndpoints()
    .MapRequestEndpoints()
    .MapWorkEndpoints();

await app.InitialiseDatabaseAsync();
await app.RunAsync();

/// <summary>Exposed for WebApplicationFactory in integration tests.</summary>
public partial class Program;
