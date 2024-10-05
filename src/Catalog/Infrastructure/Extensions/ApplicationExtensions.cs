using FluentValidation;
using MassTransit.Configuration;
using Microsoft.AspNetCore.Identity;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.Elasticsearch;
using Serilog.Sinks.Fluentd;
using Serilog.Sinks.SystemConsole.Themes;

namespace Catalog.Infrastructure.Extensions;

public static class ApplicationExtensions
{
    public static void AddApplicationServices(this IHostApplicationBuilder builder)
    {
        builder.Services.AddDbContext<CatalogDbContext>(configure =>
        {
            configure.UseSqlServer(builder.Configuration.GetConnectionString(CatalogDbContext.DefaultConnectionStringName));
        });

        builder.Services.AddMassTransit(configure =>
        {
            var brokerConfig = builder.Configuration.GetSection(BrokerOptions.SectionName)
                                                    .Get<BrokerOptions>();
            if (brokerConfig is null)
            {
                throw new ArgumentNullException(nameof(BrokerOptions));
            }
            configure.AddConsumers(Assembly.GetExecutingAssembly());
            configure.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(brokerConfig.Host, hostConfigure =>
                {
                    hostConfigure.Username(brokerConfig.Username);
                    hostConfigure.Password(brokerConfig.Password);
                });

                cfg.UseJsonSerializer();

                cfg.ConfigureEndpoints(context);
            });
        });

        builder.Services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());
        builder.Services.AddOptions<CatalogOptions>()
                        .BindConfiguration(nameof(CatalogOptions));
    }

    public static void AddLoggerConfigs(this WebApplicationBuilder builder)
    {
        builder.Logging.ClearProviders();

        builder.Host.UseSerilog((hostBuilderContext, configureLogger) =>
        {
            configureLogger.ReadFrom.Configuration(hostBuilderContext.Configuration)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("service_name", "Catalog");

            configureLogger.WriteTo.Async(sinkConfigure =>
            {
                sinkConfigure.Console(LogEventLevel.Information, theme: AnsiConsoleTheme.Code);

                sinkConfigure.Elasticsearch(new ElasticsearchSinkOptions(new Uri("http://localhost:9200"))
                {
                    AutoRegisterTemplate = false,
                    ConnectionTimeout = TimeSpan.FromSeconds(5),
                    InlineFields = true,
                    MinimumLogEventLevel = LogEventLevel.Information,
                    IndexDecider = (logEvent, dateTimeOffset) => $"catalog-{dateTimeOffset:yyyy.MM.dd}-{logEvent.Level.ToString().ToLower()}",
                    EmitEventFailure = EmitEventFailureHandling.WriteToSelfLog |
                                        EmitEventFailureHandling.RaiseCallback,
                    FailureCallback = (logEvent, exeption) => Console.WriteLine($"{logEvent}, {exeption.Message}")
                });
            });
        });
    }
}
