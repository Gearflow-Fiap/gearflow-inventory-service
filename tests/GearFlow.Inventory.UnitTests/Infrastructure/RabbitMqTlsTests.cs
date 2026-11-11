using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using GearFlow.Inventory.Infrastructure.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GearFlow.Inventory.UnitTests.Infrastructure;

/// <summary>
/// AMQPS contra o broker compartilhado: CA privada de laboratório, nome de host sempre conferido. Além das regras do
/// validador, o handshake TLS é feito de verdade num socket local, para provar o comportamento que o .NET reporta.
/// </summary>
public sealed class RabbitMqTlsTests : IDisposable
{
    private const string BrokerHost = "localhost";   // o certificado de teste tem este nome nos SANs

    private readonly List<IDisposable> _disposables = [];
    private readonly List<string> _files = [];

    public void Dispose()
    {
        foreach (var disposable in _disposables) disposable.Dispose();
        foreach (var file in _files) File.Delete(file);
    }

    private X509Certificate2 Track(X509Certificate2 certificate)
    {
        _disposables.Add(certificate);
        return certificate;
    }

    private static X509Certificate2 CreateCa(string name)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        // A CA já existe há 60 dias: assim dá para emitir um certificado de servidor que expirou sem começar antes dela.
        // E ela dura 365 dias, bem mais que os 30 do servidor: o X.509 grava datas em SEGUNDOS, e a geração da chave RSA entre a
        // criação da CA e a do servidor leva tempo. Com os dois em "agora + 30 dias", cruzar a virada de um segundo deixava o servidor
        // expirando 1 s DEPOIS da CA, e o .NET se recusava a emitir (ArgumentException em CertificateRequest.Create; falha intermitente no CI).
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-60), DateTimeOffset.UtcNow.AddDays(365));
    }

    private static X509Certificate2 CreateServerCertificate(
        X509Certificate2 ca, string dnsName, DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN={dnsName}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(dnsName);
        request.CertificateExtensions.Add(san.Build());

        var serial = RandomNumberGenerator.GetBytes(8);
        serial[0] = (byte)((serial[0] & 0x7F) | 0x01);   // positivo e diferente de zero

        using var signed = request.Create(ca, notBefore, notAfter, serial);
        using var withKey = signed.CopyWithPrivateKey(key);
        // O SslStream do Windows precisa de uma chave persistida: reimporta pelo PKCS#12.
        return X509CertificateLoader.LoadPkcs12(withKey.Export(X509ContentType.Pfx), null);
    }

    private string WritePem(X509Certificate2 certificate)
    {
        var path = Path.Combine(Path.GetTempPath(), $"gearflow-ca-{Guid.NewGuid():N}.crt");
        File.WriteAllText(path, certificate.ExportCertificatePem());
        _files.Add(path);
        return path;
    }

    private (X509Certificate2 Ca, string CaPath, X509Certificate2 Server) BrokerPki(string dnsName = BrokerHost)
    {
        var ca = Track(CreateCa("GearFlow Test CA"));
        var server = Track(CreateServerCertificate(ca, dnsName, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30)));
        return (ca, WritePem(ca), server);
    }

    /// <summary>Handshake TLS real, em loopback. Devolve a exceção do lado do cliente, ou null se conectou.</summary>
    private static async Task<Exception?> Handshake(
        X509Certificate2 serverCertificate, string targetHost, RemoteCertificateValidationCallback clientValidator)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var server = Task.Run(async () =>
        {
            try
            {
                using var accepted = await listener.AcceptTcpClientAsync(cts.Token);
                await using var ssl = new SslStream(accepted.GetStream());
                await ssl.AuthenticateAsServerAsync(
                    new SslServerAuthenticationOptions { ServerCertificate = serverCertificate }, cts.Token);
                await ssl.ReadAsync(new byte[1], cts.Token);   // espera o cliente fechar
            }
            catch
            {
                // Se o cliente recusa o certificado, a falha relevante é a do lado dele.
            }
        }, cts.Token);

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port, cts.Token);
            await using var ssl = new SslStream(client.GetStream(), false, clientValidator);
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = targetHost,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            }, cts.Token);
            return null;
        }
        catch (Exception ex) when (ex is AuthenticationException or IOException)
        {
            return ex;
        }
        finally
        {
            listener.Stop();
            await Task.WhenAny(server, Task.Delay(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public async Task A_certificate_from_the_configured_CA_with_the_right_name_is_accepted()
    {
        var (_, caPath, server) = BrokerPki();

        var failure = await Handshake(server, BrokerHost, RabbitMqTls.CreateValidator(caPath));

        failure.Should().BeNull();
    }

    [Fact]
    public async Task A_certificate_from_another_CA_is_rejected()
    {
        var (_, _, server) = BrokerPki();
        var otherCa = Track(CreateCa("Outra CA"));

        var failure = await Handshake(server, BrokerHost, RabbitMqTls.CreateValidator(WritePem(otherCa)));

        failure.Should().NotBeNull("a CA de teste não é a configurada");
    }

    [Fact]
    public async Task The_host_name_is_checked_even_when_the_CA_is_the_right_one()
    {
        var (_, caPath, server) = BrokerPki();

        // Certificado válido da CA certa, mas apresentado para outro nome: é o que um serviço comprometido da mesma CA faria.
        var failure = await Handshake(server, "rabbitmq.outro-servico.local", RabbitMqTls.CreateValidator(caPath));

        failure.Should().NotBeNull("o nome de host incorreto nunca é aceito");
    }

    [Fact]
    public async Task An_expired_certificate_is_rejected()
    {
        var ca = Track(CreateCa("GearFlow Test CA"));
        var expired = Track(CreateServerCertificate(
            ca, BrokerHost, DateTimeOffset.UtcNow.AddDays(-20), DateTimeOffset.UtcNow.AddDays(-10)));

        var failure = await Handshake(expired, BrokerHost, RabbitMqTls.CreateValidator(WritePem(ca)));

        failure.Should().NotBeNull();
    }

    [Fact]
    public async Task Without_a_configured_CA_a_private_CA_certificate_is_rejected()
    {
        var (_, _, server) = BrokerPki();

        var failure = await Handshake(server, BrokerHost, RabbitMqTls.CreateValidator(caCertificatePath: null));

        failure.Should().NotBeNull("sem a CA, só vale a confiança do sistema, que não conhece a CA de teste");
    }

    [Fact]
    public void Name_mismatch_and_missing_certificate_are_never_accepted_by_the_rules()
    {
        var (ca, _, server) = BrokerPki();
        var roots = new X509Certificate2Collection(ca);

        RabbitMqTls.Validate(roots, server, null, SslPolicyErrors.None).Should().BeTrue();
        RabbitMqTls.Validate(roots, server, null, SslPolicyErrors.RemoteCertificateNameMismatch).Should().BeFalse();
        RabbitMqTls.Validate(roots, server, null,
            SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateChainErrors).Should().BeFalse();
        RabbitMqTls.Validate(roots, null, null, SslPolicyErrors.RemoteCertificateNotAvailable).Should().BeFalse();
        RabbitMqTls.Validate(null, server, null, SslPolicyErrors.RemoteCertificateChainErrors).Should().BeFalse();
        RabbitMqTls.Validate(roots, server, null, SslPolicyErrors.RemoteCertificateChainErrors).Should().BeTrue();
    }

    [Fact]
    public void A_missing_CA_file_fails_at_startup_with_a_message_that_says_what_to_mount()
    {
        var act = () => RabbitMqTls.CreateValidator("/etc/gearflow/rabbitmq-ca/nao-existe.crt");

        act.Should().Throw<FileNotFoundException>().WithMessage("*rabbitmq-ca*");
    }

    [Fact]
    public void A_file_without_any_PEM_certificate_is_rejected()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gearflow-vazio-{Guid.NewGuid():N}.crt");
        File.WriteAllText(path, "isto não é um certificado");
        _files.Add(path);

        var act = () => RabbitMqTls.CreateValidator(path);

        act.Should().Throw<InvalidOperationException>().WithMessage("*nenhum certificado*");
    }

    [Fact]
    public void Tls_is_off_by_default_and_binds_from_configuration()
    {
        new RabbitMqOptions().Tls.Should().BeFalse("o desenvolvimento local usa o RabbitMQ do compose, sem TLS");
        new RabbitMqOptions().CaCertificatePath.Should().BeNull();

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RabbitMq:Host"] = "rabbitmq.messaging.svc.cluster.local",
            ["RabbitMq:Port"] = "5671",
            ["RabbitMq:Tls"] = "true",
            ["RabbitMq:CaCertificatePath"] = "/etc/gearflow/rabbitmq-ca/ca.crt",
        }).Build();

        var options = configuration.GetSection(RabbitMqOptions.SectionName).Get<RabbitMqOptions>()!;

        options.Tls.Should().BeTrue();
        options.Port.Should().Be(5671);
        options.CaCertificatePath.Should().Be("/etc/gearflow/rabbitmq-ca/ca.crt");
    }

    [Fact]
    public void The_service_does_not_start_with_tls_on_and_the_CA_file_missing()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RabbitMq:Tls"] = "true",
            ["RabbitMq:CaCertificatePath"] = "/nao/existe/ca.crt",
        }).Build();

        var act = () => new ServiceCollection().AddInventoryMessaging(configuration);

        act.Should().Throw<FileNotFoundException>("a falha tem de aparecer no startup, não numa conexão que nunca fecha");
    }

    [Fact]
    public void The_service_registers_with_tls_on_and_a_valid_CA_file()
    {
        var (_, caPath, _) = BrokerPki();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RabbitMq:Tls"] = "true",
            ["RabbitMq:CaCertificatePath"] = caPath,
        }).Build();

        var act = () => new ServiceCollection().AddInventoryMessaging(configuration);

        act.Should().NotThrow();
    }
}
