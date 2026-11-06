using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace GearFlow.Inventory.Infrastructure.Messaging;

/// <summary>
/// Validação do certificado do broker (AMQPS). O broker do <c>gearflow-infra-broker</c> usa uma CA privada de laboratório:
/// a cadeia não fecha na confiança do sistema, então o certificado é revalidado contra a CA informada em
/// <see cref="RabbitMqOptions.CaCertificatePath"/>. As regras do README do broker valem aqui: CA desconhecida e nome de host
/// incorreto são <b>rejeitados</b>. Não existe atalho de "aceitar qualquer certificado".
/// </summary>
internal static class RabbitMqTls
{
    /// <summary>
    /// Monta o callback do <see cref="SslStream"/>. Falha na hora (no startup) se o arquivo da CA não existe ou não tem
    /// certificado, em vez de subir o serviço com uma conexão que nunca vai fechar.
    /// </summary>
    public static RemoteCertificateValidationCallback CreateValidator(string? caCertificatePath)
    {
        X509Certificate2Collection? roots = null;

        if (!string.IsNullOrWhiteSpace(caCertificatePath))
        {
            if (!File.Exists(caCertificatePath))
                throw new FileNotFoundException(
                    $"RabbitMq__CaCertificatePath aponta para um arquivo que não existe: {caCertificatePath}. " +
                    "Monte o ca.crt do broker (ConfigMap rabbitmq-ca, veja k8s/README.md).", caCertificatePath);

            roots = [];
            roots.ImportFromPemFile(caCertificatePath);
            if (roots.Count == 0)
                throw new InvalidOperationException($"O arquivo {caCertificatePath} não contém nenhum certificado PEM.");
        }

        return (_, certificate, presentedChain, errors) => Validate(roots, certificate, presentedChain, errors);
    }

    internal static bool Validate(
        X509Certificate2Collection? trustedRoots,
        X509Certificate? certificate,
        X509Chain? presentedChain,
        SslPolicyErrors errors)
    {
        // Cadeia e nome corretos pela confiança do sistema (ex.: broker com certificado de uma CA pública).
        if (errors == SslPolicyErrors.None) return true;

        // Nome errado ou sem certificado: nunca se aceita, nem com a CA certa. É o que impede um certificado válido de
        // outro serviço da mesma CA de se passar pelo broker.
        if ((errors & (SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateNotAvailable)) != 0)
            return false;

        // Sobrou só erro de cadeia (a CA privada não é conhecida do sistema): revalida contra a CA configurada.
        if (trustedRoots is null || certificate is null) return false;

        using var leaf = X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.AddRange(trustedRoots);
        // A CA de laboratório não publica CRL nem OCSP: sem isso a verificação falharia por não alcançar o ponto de revogação.
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

        if (presentedChain is not null)
            foreach (var element in presentedChain.ChainElements)
                chain.ChainPolicy.ExtraStore.Add(element.Certificate);

        // Build valida validade (datas) e se a cadeia termina em uma das CAs configuradas.
        return chain.Build(leaf);
    }
}
