using GearFlow.Inventory.Application.Abstractions;
using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GearFlow.Inventory.IntegrationTests.Fixtures;

/// <summary>
/// A API completa (Program.cs real) apontando para o Postgres e o RabbitMQ dos containers. Ambiente "Testing":
/// o Program não migra nem semeia (a fixture já migrou o banco).
/// </summary>
internal sealed class InventoryApiFactory(InfrastructureFixture infrastructure) : WebApplicationFactory<Program>
{
    public const string JwtSecret = "integration-tests-secret-with-more-than-32-chars";

    public CapturingEmailSender Emails { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // UseSetting (e não ConfigureAppConfiguration): o Program lê a configuração cedo, ao registrar os serviços.
        builder.UseSetting("ConnectionStrings:Postgres", infrastructure.PostgresConnectionString);
        builder.UseSetting("RabbitMq:Host", infrastructure.RabbitHost);
        builder.UseSetting("RabbitMq:Port", infrastructure.RabbitPort.ToString());
        builder.UseSetting("RabbitMq:Username", InfrastructureFixture.RabbitUsername);
        builder.UseSetting("RabbitMq:Password", InfrastructureFixture.RabbitPassword);
        builder.UseSetting("RabbitMq:RetryIntervalsMilliseconds:0", "50");
        builder.UseSetting("RabbitMq:RetryIntervalsMilliseconds:1", "100");
        builder.UseSetting("Jwt:Secret", JwtSecret);
        builder.UseSetting("Stock:AlertEmail", "estoque@oficina.test");

        builder.ConfigureServices(services =>
        {
            // Espera o bus conectar e declarar filas/bindings antes de o host terminar de subir:
            // assim o teste só publica quando alguém já está ouvindo.
            services.Configure<MassTransitHostOptions>(options =>
            {
                options.WaitUntilStarted = true;
                options.StartTimeout = TimeSpan.FromSeconds(60);
            });

            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);
        });
    }
}
