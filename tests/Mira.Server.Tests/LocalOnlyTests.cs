using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;

namespace Mira.Server.Tests;

public class LocalOnlyTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        _app = builder.Build();
        _app.UseLocalOnly();
        _app.MapGet("/api/ping", () => "pong");
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    private async Task<int> StatusAsync(string host, string? origin = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/ping");
        request.Headers.Host = host;
        if (origin is not null) request.Headers.TryAddWithoutValidation("Origin", origin);
        using var response = await _client.SendAsync(request);
        return (int)response.StatusCode;
    }

    [Theory]
    [InlineData("localhost:5080")]
    [InlineData("LOCALHOST:5080")]
    [InlineData("127.0.0.1:5080")]
    [InlineData("[::1]:5080")]
    public async Task A_request_addressed_to_a_loopback_host_is_accepted(string host) =>
        Assert.Equal(200, await StatusAsync(host));

    [Theory]
    [InlineData("example.com")]
    [InlineData("evil.localhost.example.com")]
    [InlineData("192.168.1.10:5080")]
    [InlineData("0.0.0.0:5080")]
    public async Task A_request_addressed_to_any_other_host_is_refused_against_dns_rebinding(string host) =>
        Assert.Equal(403, await StatusAsync(host));

    [Theory]
    [InlineData("http://localhost:5173")]
    [InlineData("http://127.0.0.1:5080")]
    [InlineData("http://[::1]:5080")]
    public async Task A_loopback_origin_is_accepted(string origin) =>
        Assert.Equal(200, await StatusAsync("localhost:5080", origin));

    [Theory]
    [InlineData("http://evil.example")]
    [InlineData("https://localhost.evil.example")]
    [InlineData("null")]
    [InlineData("not a url")]
    public async Task A_page_served_from_elsewhere_is_refused_even_when_the_host_is_local(string origin) =>
        Assert.Equal(403, await StatusAsync("localhost:5080", origin));

    [Fact]
    public async Task Without_an_origin_header_only_the_host_matters() =>
        Assert.Equal(200, await StatusAsync("127.0.0.1:5080", origin: null));
}
