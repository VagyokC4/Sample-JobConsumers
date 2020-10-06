using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using JobService.Components;
using MassTransit;
using MassTransit.Azure.ServiceBus.Core.Topology;
using MassTransit.Conductor;
using MassTransit.Definition;
using MassTransit.EntityFrameworkCoreIntegration;
using MassTransit.EntityFrameworkCoreIntegration.JobService;
using MassTransit.JobService;
using MassTransit.JobService.Components.StateMachines;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace JobService.Service
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers();

            // services.AddDbContext<JobServiceSagaDbContext>(builder =>
            // builder.UseNpgsql(Configuration.GetConnectionString("JobService"), m =>
            // {
            // m.MigrationsAssembly(Assembly.GetExecutingAssembly().GetName().Name);
            // m.MigrationsHistoryTable($"__{nameof(JobServiceSagaDbContext)}");
            // }));

            services.AddMassTransit(x =>
            {
                x.AddServiceBusMessageScheduler();
                //x.AddRabbitMqMessageScheduler();
                
                x.AddConsumer<ConvertVideoJobConsumer>(typeof(ConvertVideoJobConsumerDefinition));

                x.AddSagaRepository<JobSaga>()
                    .MessageSessionRepository();
                // .EntityFrameworkRepository(r =>
                // {
                // r.ExistingDbContext<JobServiceSagaDbContext>();
                // r.LockStatementProvider = new PostgresLockStatementProvider();
                // });
                x.AddSagaRepository<JobTypeSaga>()
                    .MessageSessionRepository();
                // .EntityFrameworkRepository(r =>
                // {
                // r.ExistingDbContext<JobServiceSagaDbContext>();
                // r.LockStatementProvider = new PostgresLockStatementProvider();
                // });
                x.AddSagaRepository<JobAttemptSaga>()
                    .MessageSessionRepository();
                // .EntityFrameworkRepository(r =>
                // {
                // r.ExistingDbContext<JobServiceSagaDbContext>();
                // r.LockStatementProvider = new PostgresLockStatementProvider();
                // });

                x.AddServiceClient();

                x.AddRequestClient<ConvertVideo>();

                x.SetKebabCaseEndpointNameFormatter();

                x.UsingAzureServiceBus((context, cfg) =>
                    // x.UsingRabbitMq((context, cfg) =>
                {
                    var azureServiceBusCredential = "";
                    cfg.Host(azureServiceBusCredential);

                    // Sessions
                    cfg.RequiresSession = true;
                    cfg.Send<ConvertVideo>(ConfigureSessionIdFormatter);

                    cfg.UseServiceBusMessageScheduler();
                    // cfg.UseRabbitMqMessageScheduler();

                    var options = new ServiceInstanceOptions()
                        .EnableInstanceEndpoint()
                        .SetEndpointNameFormatter(KebabCaseEndpointNameFormatter.Instance);

                    cfg.ServiceInstance(options, instance =>
                    {
                        instance.ConfigureJobServiceEndpoints(js =>
                        {
                            js.FinalizeCompleted = true;

                            js.ConfigureSagaRepositories(context);
                        });

                        instance.ConfigureEndpoints(context);
                    });
                });
            });
            
            services.AddMassTransitHostedService();

            services.AddOpenApiDocument(cfg => cfg.PostProcess = d => d.Info.Title = "Convert Video Service");
        }
        
        public static void ConfigureSessionIdFormatter<T>( IServiceBusMessageSendTopologyConfigurator<T> cfg ) where T : class
        {
            cfg.UseSessionIdFormatter(x => Guid.NewGuid().ToString());
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment()) app.UseDeveloperExceptionPage();

            app.UseHttpsRedirection();

            app.UseRouting();

            app.UseAuthorization();

            app.UseOpenApi();
            app.UseSwaggerUi3();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
                {
                    Predicate = check => check.Tags.Contains("ready"),
                    ResponseWriter = HealthCheckResponseWriter
                });

                endpoints.MapHealthChecks("/health/live",
                    new HealthCheckOptions {ResponseWriter = HealthCheckResponseWriter});

                endpoints.MapControllers();
            });
        }


        static Task HealthCheckResponseWriter(HttpContext context, HealthReport result)
        {
            var json = new JObject(
                new JProperty("status", result.Status.ToString()),
                new JProperty("results", new JObject(result.Entries.Select(entry => new JProperty(entry.Key,
                    new JObject(
                        new JProperty("status", entry.Value.Status.ToString()),
                        new JProperty("description", entry.Value.Description),
                        new JProperty("data", JObject.FromObject(entry.Value.Data))))))));

            context.Response.ContentType = "application/json";

            return context.Response.WriteAsync(json.ToString(Formatting.Indented));
        }
    }
}