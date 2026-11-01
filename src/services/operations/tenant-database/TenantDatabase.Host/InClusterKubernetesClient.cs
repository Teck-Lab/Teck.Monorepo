using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

/// <summary>Builds an in-cluster Kubernetes API client with the bound ServiceAccount identity and verified CA.</summary>
public static class InClusterKubernetesClient
{
    /// <summary>Creates an authenticated, TLS-validated Kubernetes HTTP client.</summary>
    /// <param name="config">Kubernetes API address and ServiceAccount file settings.</param>
    /// <returns>A TLS-validated HTTP client with the Kubernetes ServiceAccount bearer token.</returns>
    public static HttpClient Create(IConfiguration config)
    {
        string root = config["Kubernetes:ServiceAccountPath"] ?? "/var/run/secrets/kubernetes.io/serviceaccount";
        string jwt = File.ReadAllText(Path.Combine(root, "token")).Trim();
        if (string.IsNullOrWhiteSpace(jwt))
        {
            throw new InvalidOperationException("Kubernetes ServiceAccount token is required.");
        }

        var ca = X509Certificate2.CreateFromPemFile(Path.Combine(root, "ca.crt"));
        var handler = new HttpClientHandler();
        handler.ServerCertificateCustomValidationCallback = (_, certificate, _, errors) =>
        {
            if (errors == SslPolicyErrors.None)
            {
                return true;
            }

            if (certificate is null || (errors & ~SslPolicyErrors.RemoteCertificateChainErrors) != 0)
            {
                return false;
            }

            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(ca);
            return chain.Build(certificate);
        };
        var http = new HttpClient(handler) { BaseAddress = new Uri(config["Kubernetes:ApiUrl"] ?? "https://kubernetes.default.svc/") };
        if (http.BaseAddress.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Kubernetes API requires HTTPS.");
        }

        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        return http;
    }
}
