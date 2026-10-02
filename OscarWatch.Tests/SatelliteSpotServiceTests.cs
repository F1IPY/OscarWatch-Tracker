using System.Net;
using System.Text;
using OscarWatch.Core.Models;
using OscarWatch.Core.Services;

namespace OscarWatch.Tests;

public sealed class SatelliteSpotServiceTests
{
    private static readonly DateTime HeardAt = new(2026, 10, 1, 17, 47, 3, DateTimeKind.Utc);

    private static SatelliteStatusSettings Settings => new()
    {
        BaseUrl = "https://oscarwatch.org",
        ApiToken = "test-token"
    };

    private static OscarWatchSatelliteSpot Spot(string? grid = "JN11", long? uplink = 145_995_635, long? downlink = 435_612_000) =>
        new(
            "RS-44",
            "EA3ZZ",
            grid,
            -9,
            uplink,
            downlink,
            HeardAt,
            "CQ EA3ZZ JN11",
            "OscarWatch-Tracker/1.6.0");

    [Fact]
    public async Task Submit_posts_snake_case_spot_and_accepts_202()
    {
        var handler = new StubHandler("""{"accepted":true}""", HttpStatusCode.Accepted);
        var service = new SatelliteSpotService(new HttpClient(handler));

        var result = await service.SubmitAsync(Settings, Spot());

        Assert.True(result.Accepted);
        Assert.Equal(202, result.HttpStatusCode);
        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal("https://oscarwatch.org/api/v1/spots", handler.LastUri?.ToString());
        Assert.Equal("Bearer", handler.LastAuthScheme);
        Assert.Equal("test-token", handler.LastAuthParameter);
        Assert.Contains("\"satellite\":\"RS-44\"", handler.LastBody, StringComparison.Ordinal);
        Assert.Contains("\"mode\":\"FT4\"", handler.LastBody, StringComparison.Ordinal);
        Assert.Contains("\"heard_callsign\":\"EA3ZZ\"", handler.LastBody, StringComparison.Ordinal);
        Assert.Contains("\"heard_grid\":\"JN11\"", handler.LastBody, StringComparison.Ordinal);
        Assert.Contains("\"snr_db\":-9", handler.LastBody, StringComparison.Ordinal);
        Assert.Contains("\"uplink_hz\":145995635", handler.LastBody, StringComparison.Ordinal);
        Assert.Contains("\"downlink_hz\":435612000", handler.LastBody, StringComparison.Ordinal);
        Assert.Contains("\"heard_at\":\"2026-10-01T17:47:03Z\"", handler.LastBody, StringComparison.Ordinal);
        Assert.Contains("\"message\":\"CQ EA3ZZ JN11\"", handler.LastBody, StringComparison.Ordinal);
        Assert.Contains("\"client\":\"OscarWatch-Tracker/1.6.0\"", handler.LastBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Submit_omits_empty_optional_fields()
    {
        var handler = new StubHandler("{}", HttpStatusCode.Accepted);
        var service = new SatelliteSpotService(new HttpClient(handler));

        await service.SubmitAsync(Settings, Spot(grid: null, uplink: null, downlink: 0));

        Assert.DoesNotContain("heard_grid", handler.LastBody, StringComparison.Ordinal);
        Assert.DoesNotContain("uplink_hz", handler.LastBody, StringComparison.Ordinal);
        Assert.DoesNotContain("downlink_hz", handler.LastBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Submit_requires_token()
    {
        var handler = new StubHandler("{}");
        var service = new SatelliteSpotService(new HttpClient(handler));

        var result = await service.SubmitAsync(new SatelliteStatusSettings { BaseUrl = "https://oscarwatch.org" }, Spot());

        Assert.False(result.Accepted);
        Assert.Equal(0, result.HttpStatusCode);
        Assert.Null(handler.LastUri);
    }

    [Fact]
    public async Task Submit_surfaces_401()
    {
        var handler = new StubHandler("""{"message":"Unauthenticated."}""", HttpStatusCode.Unauthorized);
        var service = new SatelliteSpotService(new HttpClient(handler));

        var result = await service.SubmitAsync(Settings, Spot());

        Assert.False(result.Accepted);
        Assert.Equal(401, result.HttpStatusCode);
        Assert.Contains("Unauthorised", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Submit_surfaces_422()
    {
        var handler = new StubHandler(
            """{"message":"The given data was invalid.","errors":{"heard_callsign":["You cannot spot yourself."]}}""",
            HttpStatusCode.UnprocessableEntity);
        var service = new SatelliteSpotService(new HttpClient(handler));

        var result = await service.SubmitAsync(Settings, Spot());

        Assert.False(result.Accepted);
        Assert.Equal(422, result.HttpStatusCode);
        Assert.Contains("Validation failed", result.Message, StringComparison.Ordinal);
        Assert.Contains("You cannot spot yourself", result.Message, StringComparison.Ordinal);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _body;
        private readonly HttpStatusCode _status;

        public StubHandler(string body, HttpStatusCode status = HttpStatusCode.OK)
        {
            _body = body;
            _status = status;
        }

        public HttpMethod? LastMethod { get; private set; }
        public Uri? LastUri { get; private set; }
        public string? LastAuthScheme { get; private set; }
        public string? LastAuthParameter { get; private set; }
        public string LastBody { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastMethod = request.Method;
            LastUri = request.RequestUri;
            LastAuthScheme = request.Headers.Authorization?.Scheme;
            LastAuthParameter = request.Headers.Authorization?.Parameter;
            if (request.Content is not null)
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            };
        }
    }
}
