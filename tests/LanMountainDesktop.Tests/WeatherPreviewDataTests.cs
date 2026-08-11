using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class WeatherPreviewDataTests
{
    [Fact]
    public void WeatherSnapshot_DefaultAlerts_IsEmpty()
    {
        var snapshot = new WeatherSnapshot(
            Provider: "Test",
            LocationKey: "test",
            LocationName: "Test City",
            Latitude: 0,
            Longitude: 0,
            FetchedAt: DateTimeOffset.UtcNow,
            ObservationTime: null,
            Current: new WeatherCurrentCondition(24, 25, 58, 42, 12, 180, 1, true, "Partly cloudy"),
            DailyForecasts: [],
            HourlyForecasts: []);

        Assert.NotNull(snapshot.Alerts);
        Assert.Empty(snapshot.Alerts);
    }

    [Fact]
    public async Task XiaomiWeatherService_GetWeatherAsync_ParsesAlerts()
    {
        const string payload = """
        {
          "code": 0,
          "data": {
            "current": {
              "temperature": { "value": 24 },
              "feelsLike": { "value": 26 },
              "humidity": { "value": 58 },
              "weather": { "value": 7, "text": "Light rain" },
              "wind": { "speed": { "value": 12 }, "direction": { "value": 180 } },
              "pubTime": "2026-05-18T10:00:00+08:00"
            },
            "aqi": { "value": 42 },
            "forecastDaily": {
              "temperature": { "value": [{ "from": 20, "to": 28 }] },
              "weather": { "value": [{ "from": 7, "to": 7 }] },
              "sunRiseSet": { "value": [{ "from": "05:42", "to": "18:54" }] },
              "precipitationProbability": { "value": [{ "value": 60 }] }
            },
            "alerts": [
              {
                "title": "Heavy rain warning",
                "detail": "Rain is expected within the next hour.",
                "type": "Rain",
                "level": "Yellow",
                "pubTime": "2026-05-18T09:30:00+08:00",
                "images": { "icon": "https://example.test/rain.webp" }
              }
            ]
          }
        }
        """;

        using var httpClient = new HttpClient(new StubHandler(payload));
        var service = new XiaomiWeatherService(
            new XiaomiWeatherApiOptions { BaseUrl = "https://example.test" },
            httpClient);

        var result = await service.GetWeatherAsync(new WeatherQuery(
            "101010100",
            39.9042,
            116.4074,
            ForecastDays: 3,
            Locale: "en_us",
            ForceRefresh: true));

        Assert.True(result.Success, result.ErrorMessage);
        var alert = Assert.Single(result.Data!.Alerts);
        Assert.Equal("Heavy rain warning", alert.Title);
        Assert.Equal("Rain is expected within the next hour.", alert.Detail);
        Assert.Equal("Rain", alert.Type);
        Assert.Equal("Yellow", alert.Level);
        Assert.Equal("https://example.test/rain.webp", alert.IconUri);
        Assert.NotNull(alert.PublishedAt);
    }

    private sealed class StubHandler(string responseText) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseText)
            });
        }
    }
}
