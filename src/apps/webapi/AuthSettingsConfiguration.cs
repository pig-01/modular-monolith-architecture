using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Primitives;

namespace ModularMonolith.WebApi;

public static class AuthSettingsConfiguration
{
    public static IConfigurationBuilder AddAuthSettingsFile(this IConfigurationBuilder builder, string path,
        Action<string> reportReloadFailure)
    {
        var source = new AuthSettingsSource(reportReloadFailure) { Path = path, Optional = false };
        source.ResolveFileProvider();
        return builder.Add(source);
    }

    private sealed class AuthSettingsSource(Action<string> reportReloadFailure) : JsonConfigurationSource
    {
        public override IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            EnsureDefaults(builder);
            // The standard watcher clears values on deletion and failed reloads. Own only
            // that watcher, while retaining the framework JSON parser and file provider.
            ReloadOnChange = false;
            return new AuthSettingsProvider(this, reportReloadFailure);
        }
    }

    private sealed class AuthSettingsProvider(JsonConfigurationSource source, Action<string> reportReloadFailure)
        : JsonConfigurationProvider(source)
    {
        private readonly object reloadLock = new();
        private IDisposable? watcher;
        private bool loaded;

        public override void Load()
        {
            lock (reloadLock)
            {
                try
                {
                    // A normal (non-watcher) framework load requires the file and only
                    // replaces Data after a complete JSON parse. Failed reloads keep Data.
                    base.Load();
                    loaded = true;
                }
                catch (Exception exception) when (loaded
                    && exception is IOException or InvalidDataException or UnauthorizedAccessException or FormatException)
                {
                    reportReloadFailure(exception.GetType().Name);
                }
                watcher ??= ChangeToken.OnChange(() => Source.FileProvider!.Watch(Source.Path!), () =>
                {
                    Thread.Sleep(Source.ReloadDelay);
                    Load();
                });
            }
        }

        protected override void Dispose(bool disposing)
        {
            watcher?.Dispose();
            base.Dispose(disposing);
        }
    }
}