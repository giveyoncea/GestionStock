using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Blazored.LocalStorage;
using GestionStock.Web.Models;

namespace GestionStock.Web.Services;

public partial class ApiService : IApiService
{
    private readonly HttpClient _http;
    private readonly ILocalStorageService _storage;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ApiService(HttpClient http, ILocalStorageService storage)
    {
        _http = http;
        _storage = storage;
    }

    private async Task SetAuthHeaderAsync()
    {
        try
        {
            var token = await _storage.GetItemAsStringAsync("authToken");
            _http.DefaultRequestHeaders.Authorization = !string.IsNullOrEmpty(token)
                ? new AuthenticationHeaderValue("Bearer", token)
                : null;
        }
        catch
        {
            _http.DefaultRequestHeaders.Authorization = null;
        }
    }

    private async Task<T?> GetAsync<T>(string url)
    {
        try
        {
            await SetAuthHeaderAsync();
            var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                return default;

            return await response.Content.ReadFromJsonAsync<T>(JsonOpts);
        }
        catch
        {
            return default;
        }
    }

    private async Task<TDocument?> GetDocumentDetailAsync<TDocument>(string url)
        where TDocument : class
    {
        try
        {
            await SetAuthHeaderAsync();
            var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                return default;

            var content = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(content))
                return default;

            var envelope = JsonSerializer.Deserialize<DocumentDetailEnvelope<TDocument>>(content, JsonOpts);
            if (envelope?.Document != null)
                return envelope.Document;

            return JsonSerializer.Deserialize<TDocument>(content, JsonOpts);
        }
        catch
        {
            return default;
        }
    }

    private async Task<ResultDto?> PostAsync<TReq>(string url, TReq body)
    {
        try
        {
            await SetAuthHeaderAsync();
            var response = await _http.PostAsJsonAsync(url, body);
            var content = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(content))
            {
                return new ResultDto(!response.IsSuccessStatusCode == false,
                    response.IsSuccessStatusCode ? "OK" : $"Erreur {(int)response.StatusCode}", null);
            }

            if (content.TrimStart().StartsWith("["))
            {
                var errors = JsonSerializer.Deserialize<string[]>(content, JsonOpts);
                return new ResultDto(false, errors != null ? string.Join(" | ", errors) : content, null);
            }

            return JsonSerializer.Deserialize<ResultDto>(content, JsonOpts);
        }
        catch (Exception ex)
        {
            return new ResultDto(false, ex.Message, null);
        }
    }

    private async Task<ResultDto?> ParseResultAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(content))
        {
            return new ResultDto(response.IsSuccessStatusCode,
                response.IsSuccessStatusCode ? "OK" : $"Erreur {(int)response.StatusCode}", null);
        }

        if (content.TrimStart().StartsWith("["))
        {
            var errors = JsonSerializer.Deserialize<string[]>(content, JsonOpts);
            return new ResultDto(false, errors != null ? string.Join(" | ", errors) : content, null);
        }

        return JsonSerializer.Deserialize<ResultDto>(content, JsonOpts);
    }

    private async Task<ResultDto?> PostEmptyAsync(string url)
    {
        try
        {
            await SetAuthHeaderAsync();
            var response = await _http.PostAsync(url, null);
            return await ParseResultAsync(response);
        }
        catch (Exception ex)
        {
            return new ResultDto(false, ex.Message, null);
        }
    }

    private async Task<ResultDto?> PutAsync<TReq>(string url, TReq body)
    {
        try
        {
            await SetAuthHeaderAsync();
            var response = await _http.PutAsJsonAsync(url, body);
            return await ParseResultAsync(response);
        }
        catch (Exception ex)
        {
            return new ResultDto(false, ex.Message, null);
        }
    }

    private async Task<ResultDto?> DeleteAsync(string url)
    {
        try
        {
            await SetAuthHeaderAsync();
            var response = await _http.DeleteAsync(url);
            return await ParseResultAsync(response);
        }
        catch (Exception ex)
        {
            return new ResultDto(false, ex.Message, null);
        }
    }

    private sealed class DocumentDetailEnvelope<TDocument>
    {
        public TDocument? Document { get; set; }
        public List<LigneDocumentDetailDto>? Lignes { get; set; }
    }
}
