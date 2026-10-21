using System.Net.Http.Json;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FluentAssertions;
using GearFlow.Inventory.Infrastructure.Email;
using GearFlow.Inventory.IntegrationTests.Fixtures;

namespace GearFlow.Inventory.IntegrationTests.Email;

/// <summary>O envio SMTP real contra um Mailpit (o mesmo que o docker-compose usa em dev).</summary>
public sealed class SmtpEmailSenderTests : IAsyncLifetime
{
    private readonly IContainer _mailpit = new ContainerBuilder("axllent/mailpit:latest")
        .WithPortBinding(1025, true)
        .WithPortBinding(8025, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(8025).ForPath("/livez")))
        .Build();

    public Task InitializeAsync() => _mailpit.StartAsync();

    public async Task DisposeAsync() => await _mailpit.DisposeAsync();

    [Fact]
    public async Task Sends_the_message_over_smtp_with_the_configured_sender()
    {
        var sender = new SmtpEmailSender(new SmtpOptions
        {
            Host = _mailpit.Hostname,
            Port = _mailpit.GetMappedPublicPort(1025),
            From = "estoque-service@gearflow.test",
        });
        var subject = $"Estoque baixo: Pastilha {Guid.NewGuid():N}";

        await sender.SendAsync("estoque@oficina.test", subject, "O item 'Pastilha' (Part) está em 4 (mínimo 5).");

        using var http = new HttpClient { BaseAddress = new Uri($"http://{_mailpit.Hostname}:{_mailpit.GetMappedPublicPort(8025)}/") };
        JsonElement? message = null;
        await Eventually.UntilAsync(async () =>
        {
            var inbox = await http.GetFromJsonAsync<JsonElement>("api/v1/messages");
            message = inbox.GetProperty("messages").EnumerateArray()
                .Cast<JsonElement?>()
                .FirstOrDefault(m => m!.Value.GetProperty("Subject").GetString() == subject);
            return message is not null;
        }, "e-mail chegou no Mailpit");

        message!.Value.GetProperty("To").EnumerateArray().Single().GetProperty("Address").GetString()
            .Should().Be("estoque@oficina.test");
        message.Value.GetProperty("From").GetProperty("Address").GetString().Should().Be("estoque-service@gearflow.test");

        var id = message.Value.GetProperty("ID").GetString();
        var full = await http.GetFromJsonAsync<JsonElement>($"api/v1/message/{id}");
        full.GetProperty("Text").GetString().Should().Contain("está em 4 (mínimo 5)");
    }

    [Fact]
    public async Task An_unreachable_smtp_server_raises_so_the_consumer_can_retry()
    {
        var sender = new SmtpEmailSender(new SmtpOptions { Host = "localhost", Port = 1, From = "x@gearflow.test" });

        var act = () => sender.SendAsync("estoque@oficina.test", "assunto", "corpo");

        await act.Should().ThrowAsync<Exception>();
    }
}
