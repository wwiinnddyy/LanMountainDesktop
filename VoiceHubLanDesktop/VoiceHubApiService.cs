using System.Net.Http;
using System.Text.Json;

namespace VoiceHubLanDesktop;

/// <summary>
/// VoiceHub API 服务
/// </summary>
public sealed class VoiceHubApiService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;

    private const string DefaultApiUrl = "https://voicehub.lao-shui.top/api/songs/public";
    private const int MaxRetryCount = 3;
    private readonly TimeSpan _requestTimeout = TimeSpan.FromSeconds(10);

    public VoiceHubApiService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public async Task<ApiResult<IReadOnlyList<SongItem>>> GetPublicScheduleAsync(
        string? apiUrl = null,
        CancellationToken cancellationToken = default)
    {
        var url = string.IsNullOrWhiteSpace(apiUrl) ? DefaultApiUrl : apiUrl.Trim();

        for (var attempt = 0; attempt < MaxRetryCount; attempt++)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(_requestTimeout);

                var jsonResponse = await _httpClient.GetStringAsync(url, cts.Token);
                var items = JsonSerializer.Deserialize<List<SongItem>>(jsonResponse, _jsonOptions);

                if (items is null)
                {
                    return ApiResult<IReadOnlyList<SongItem>>.Failure("数据解析失败");
                }

                return ApiResult<IReadOnlyList<SongItem>>.Success(items);
            }
            catch (HttpRequestException ex)
            {
                if (attempt == MaxRetryCount - 1)
                {
                    return ApiResult<IReadOnlyList<SongItem>>.Failure($"网络错误: {ex.Message}");
                }
            }
            catch (TaskCanceledException)
            {
                if (attempt == MaxRetryCount - 1)
                {
                    return ApiResult<IReadOnlyList<SongItem>>.Failure("请求超时");
                }
            }
            catch (JsonException ex)
            {
                return ApiResult<IReadOnlyList<SongItem>>.Failure($"数据格式错误: {ex.Message}");
            }
            catch (Exception ex)
            {
                return ApiResult<IReadOnlyList<SongItem>>.Failure($"未知错误: {ex.Message}");
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken);
        }

        return ApiResult<IReadOnlyList<SongItem>>.Failure("获取数据失败");
    }

    public void Dispose() => _httpClient.Dispose();
}

public sealed class ApiResult<T>
{
    public bool IsSuccess { get; }
    public T? Data { get; }
    public string? ErrorMessage { get; }

    private ApiResult(bool isSuccess, T? data, string? errorMessage)
    {
        IsSuccess = isSuccess;
        Data = data;
        ErrorMessage = errorMessage;
    }

    public static ApiResult<T> Success(T data) => new(true, data, null);
    public static ApiResult<T> Failure(string errorMessage) => new(false, default, errorMessage);
}
