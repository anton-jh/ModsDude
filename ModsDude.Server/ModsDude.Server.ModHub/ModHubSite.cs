using Microsoft.Extensions.Options;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Domain.ModHub;
using System.Diagnostics;

namespace ModsDude.Server.ModHub;

/// <summary>
/// The paced reader behind <see cref="IModHubSite"/>. A singleton, because the pacing is only worth
/// anything if every request in the process goes through the same clock.
/// </summary>
internal sealed class ModHubSite(IHttpClientFactory httpClientFactory, IOptions<ModHubOptions> options)
    : IModHubSite, IDisposable
{
    public const string HttpClientName = "ModHub";

    private readonly SemaphoreSlim _turn = new(1, 1);
    private Stopwatch? _sinceLastRequest;


    public async Task<ModHubListingPage> GetLatestPage(string game, int page, CancellationToken cancellationToken)
    {
        var html = await GetAsync($"mods.php?title={Uri.EscapeDataString(game)}&filter=latest&page={page}", cancellationToken);

        return ModHubParser.ParseLatestPage(html);
    }

    public async Task<ModHubModDetails?> GetMod(string game, int modHubId, CancellationToken cancellationToken)
    {
        var html = await GetAsync(ModPagePath(game, modHubId), cancellationToken);

        return ModHubParser.ParseModPage(html);
    }

    public string GetModPageUrl(string game, int modHubId)
    {
        return new Uri(new Uri(options.Value.BaseUrl), ModPagePath(game, modHubId)).ToString();
    }

    public void Dispose()
    {
        _turn.Dispose();
    }


    private static string ModPagePath(string game, int modHubId)
    {
        return $"mod.php?mod_id={modHubId}&title={Uri.EscapeDataString(game)}";
    }

    private async Task<string> GetAsync(string path, CancellationToken cancellationToken)
    {
        await _turn.WaitAsync(cancellationToken);

        try
        {
            if (_sinceLastRequest is not null && _sinceLastRequest.Elapsed < options.Value.RequestDelay)
            {
                await Task.Delay(options.Value.RequestDelay - _sinceLastRequest.Elapsed, cancellationToken);
            }

            try
            {
                using var response = await httpClientFactory.CreateClient(HttpClientName).GetAsync(path, cancellationToken);

                if (response.IsSuccessStatusCode is false)
                {
                    throw new ModHubUnavailableException($"ModHub answered {(int)response.StatusCode} for '{path}'.");
                }

                return await response.Content.ReadAsStringAsync(cancellationToken);
            }
            catch (HttpRequestException exception)
            {
                throw new ModHubUnavailableException($"ModHub could not be reached for '{path}'.", exception);
            }
            catch (TaskCanceledException exception) when (cancellationToken.IsCancellationRequested is false)
            {
                throw new ModHubUnavailableException($"ModHub did not answer in time for '{path}'.", exception);
            }
            finally
            {
                // Counted from when the request finished, so a slow answer does not let the next one
                // follow it straight away.
                _sinceLastRequest = Stopwatch.StartNew();
            }
        }
        finally
        {
            _turn.Release();
        }
    }
}
