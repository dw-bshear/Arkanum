using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Arkanum.Go.Proof.Protocol;
using Microsoft.AspNetCore.Server.Kestrel.Https;

namespace Arkanum.Go.Proof.Server;

public static class ProofHost
{
    public static WebApplication Build(ProofConfiguration configuration, X509Certificate2 certificate, RSA signer, int port,
        IPAddress? listenAddress = null)
    {
        configuration.ValidateLease();
        configuration.ValidateEnrollmentDirectory();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], ApplicationName = typeof(ProofHost).Assembly.FullName });
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = 4096;
            options.Listen(listenAddress ?? IPAddress.Loopback, port, endpoint => endpoint.UseHttps(https =>
            {
                https.ServerCertificate = certificate;
                https.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
                https.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
                https.CheckCertificateRevocation = true;
            }));
        });
        var service = new ProofService(configuration, signer);
        var app = builder.Build();
        // Deliberately no certificate -> ClaimsPrincipal authentication handler and no portal routes.
        app.MapPost("/sprint1/sync", async (HttpContext context, ProofSyncRequest request) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var client = await context.Connection.GetClientCertificateAsync(context.RequestAborted);
            if (client is null) return Results.StatusCode(403);
            try { return Results.Json(service.Sync(request, client)); }
            catch (UnauthorizedAccessException) { return Results.StatusCode(403); }
            catch (ArgumentException) { return Results.BadRequest(); }
        });
        return app;
    }
}
