using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;

namespace M365.SecurityAssessment.Tests;

public sealed class ApiIntegrationTests
{
    [Fact]
    public async Task HealthIsAvailableAndSyntheticPreviewIsDisabledByDefault()
    {
        using var factory = new ApiFactory("Development");
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/synthetic/fixtures")).StatusCode);
    }

    [Fact]
    public void ProductionRejectsSyntheticPreviewConfiguration()
    {
        using var factory = new ApiFactory("Production", previewEnabled: true);
        Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
    }

    [Fact]
    public async Task DevelopmentPreviewRequiresExplicitOptInAndLoopbackClient()
    {
        using var factory = new ApiFactory("Development", previewEnabled: true, overrideRemoteAddress: true);
        using var client = factory.CreateClient();

        using var loopbackRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/synthetic/fixtures");
        loopbackRequest.Headers.Add("X-Test-Remote-Address", "127.0.0.1");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(loopbackRequest)).StatusCode);

        using var externalRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/synthetic/fixtures");
        externalRequest.Headers.Add("X-Test-Remote-Address", "203.0.113.10");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(externalRequest)).StatusCode);
    }

    private sealed class ApiFactory(string environmentName, bool? previewEnabled = null, bool overrideRemoteAddress = false)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environmentName);
            if (previewEnabled is { } enabled)
            {
                builder.UseSetting("Platform:EnableSyntheticPreview", enabled ? "true" : "false");
            }

            if (overrideRemoteAddress)
            {
                builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter, RemoteAddressStartupFilter>());
            }
        }
    }

    private sealed class RemoteAddressStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => application =>
        {
            application.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue("X-Test-Remote-Address", out var value) &&
                    IPAddress.TryParse(value, out var address))
                {
                    context.Connection.RemoteIpAddress = address;
                }

                await nextMiddleware();
            });
            next(application);
        };
    }
}