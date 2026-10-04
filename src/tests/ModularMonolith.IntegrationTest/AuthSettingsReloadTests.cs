using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using ModularMonolith.WebApi;

namespace ModularMonolith.IntegrationTest;

public sealed class AuthSettingsReloadTests
{
    [Fact]
    public void Malformed_runtime_json_preserves_the_previous_settings()
    {
        var directory = Path.Combine(Path.GetTempPath(), "modular-monolith-auth-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "authsettings.json");
        try
        {
            File.WriteAllText(path, """{"Auth":{"Lifetimes":{"AccessTokenMinutes":15}}}""");
            var failures = new ConcurrentQueue<string>();
            using var configuration = (ConfigurationRoot)new ConfigurationBuilder()
                .AddAuthSettingsFile(path, failures.Enqueue).Build();
            File.WriteAllText(path, """{"Auth":{"Lifetimes": """);

            var failure = Record.Exception(configuration.Reload);

            Assert.Null(failure);
            Assert.Equal("15", configuration["Auth:Lifetimes:AccessTokenMinutes"]);
            Assert.Contains(nameof(InvalidDataException), failures);
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(directory);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task File_watching_keeps_settings_on_parse_failure_and_deletion_then_recovers(bool polling)
    {
        var directory = Path.Combine(Path.GetTempPath(), "modular-monolith-auth-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "authsettings.json");
        try
        {
            File.WriteAllText(path, """{"Auth":{"Lifetimes":{"AccessTokenMinutes":15}}}""");
            var failures = Channel.CreateUnbounded<string>();
            using var files = new PhysicalFileProvider(directory)
            {
                UsePollingFileWatcher = polling,
                UseActivePolling = polling
            };
            using var configuration = (ConfigurationRoot)new ConfigurationBuilder().SetFileProvider(files)
                .AddAuthSettingsFile("authsettings.json", failure => failures.Writer.TryWrite(failure)).Build();
            File.WriteAllText(path, """{"Auth":{"Lifetimes": """);
            await WaitForFailureAsync(failures.Reader, nameof(InvalidDataException));
            Assert.Equal("15", configuration["Auth:Lifetimes:AccessTokenMinutes"]);

            File.Delete(path);
            await WaitForFailureAsync(failures.Reader, nameof(FileNotFoundException));
            Assert.Equal("15", configuration["Auth:Lifetimes:AccessTokenMinutes"]);

            var restored = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var subscription = ChangeToken.OnChange(configuration.GetReloadToken, () =>
            {
                if (configuration["Auth:Lifetimes:AccessTokenMinutes"] == "5")
                    restored.TrySetResult();
            });
            File.WriteAllText(path, """{"Auth":{"Lifetimes":{"AccessTokenMinutes":5}}}""");
            await restored.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal("5", configuration["Auth:Lifetimes:AccessTokenMinutes"]);
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(directory);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Missing_or_malformed_initial_file_stops_startup(bool malformed)
    {
        var directory = Path.Combine(Path.GetTempPath(), "modular-monolith-auth-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "authsettings.json");
        try
        {
            if (malformed)
                File.WriteAllText(path, "{invalid");
            var failures = new ConcurrentQueue<string>();
            var builder = new ConfigurationBuilder().AddAuthSettingsFile(path, failures.Enqueue);

            var failure = Record.Exception(() => builder.Build());

            if (malformed)
                Assert.IsType<InvalidDataException>(failure);
            else
                Assert.IsType<FileNotFoundException>(failure);
            Assert.Empty(failures);
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(directory);
        }
    }

    private static async Task WaitForFailureAsync(ChannelReader<string> failures, string expected)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (await failures.ReadAsync(timeout.Token) != expected)
        {
        }
    }
}