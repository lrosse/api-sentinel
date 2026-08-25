using ApiSentinel.Infrastructure;
using ApiSentinel.Infrastructure.Persistence;
using ApiSentinel.Modules.ApiCatalog;
using ApiSentinel.Modules.Identity;
using ApiSentinel.Modules.Incidents;
using ApiSentinel.Modules.Monitoring;
using ApiSentinel.Modules.Monitoring.Scheduling;
using Hangfire;
using Hangfire.Dashboard;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddMonitoringModule(builder.Configuration);
builder.Services.AddIncidentsModule();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false)));

var app = builder.Build();

await app.ApplyDatabaseMigrationsAsync();
await app.SeedDevelopmentDataAsync();
await app.ReconcileMonitorSchedulesAsync();

if (!builder.Environment.IsEnvironment("Testing") &&
    builder.Configuration.GetValue("Hangfire:Enabled", true))
{
    app.UseHangfireDashboard("/hangfire", new DashboardOptions
    {
        Authorization = [new AllowAllDashboardAuthorizationFilter()]
    });
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapIdentityModule();
app.MapApiCatalogModule();
app.MapMonitoringModule();
app.MapIncidentsModule();

app.Run();

internal sealed class AllowAllDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context) => true;
}

public partial class Program;
