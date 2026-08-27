using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using QueueFlow.Infrastructure;
using QueueFlow.Infrastructure.Observability;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "queueflow-worker")
    .WriteTo.Console(new Serilog.Formatting.Json.JsonFormatter()));
builder.Services.AddQueueFlowObservability(builder.Configuration, "queueflow-worker");
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddQueueFlowRealtimeBackplane(builder.Configuration);
builder.Services.AddWorkerBackgroundProcessing();
await builder.Build().RunAsync();
