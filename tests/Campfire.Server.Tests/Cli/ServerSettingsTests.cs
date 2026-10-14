using Campfire.Server.Cli;
using Microsoft.Extensions.Logging;

namespace Campfire.Server.Tests.Cli;

public sealed class ServerSettingsTests : IDisposable
{
    readonly TestRoot root = new();

    public void Dispose() => root.Dispose();

    [Fact]
    public void Defaults_are_the_reference_images()
    {
        var settings = root.Settings();

        Assert.Equal("production", settings.RailsEnv);
        Assert.Equal(System.IO.Path.Combine(root.Path, "storage", "db", "production.sqlite3"), settings.DatabasePath);
        Assert.Equal(System.IO.Path.Combine(root.Path, "storage", "files"), settings.FilesPath);
        Assert.Equal(System.IO.Path.Combine(root.Path, "storage", "backups"), settings.BackupsPath);
        Assert.Equal(System.IO.Path.Combine(root.Path, "artifacts", "assets"), settings.AssetsDirectory);
        Assert.Equal(3000, settings.Port);
        Assert.Equal(10, settings.MaxThreads);
        Assert.True(settings.Ssl);
        Assert.Equal("0", settings.AppVersion);
        Assert.Null(settings.SecretKeyBase);
        Assert.Null(settings.VapidPublicKey);
        Assert.Null(settings.TlsDomain);
    }

    [Fact]
    public void Reads_the_environment()
    {
        var settings = root.Settings(
            ("RAILS_ENV", "development"),
            ("SECRET_KEY_BASE", "secret"),
            ("VAPID_PUBLIC_KEY", "public"),
            ("VAPID_PRIVATE_KEY", "private"),
            ("TLS_DOMAIN", "chat.example.com"),
            ("CAMPFIRE_STORAGE_PATH", "/data"),
            ("PORT", "4000"),
            ("RAILS_MAX_THREADS", "3"),
            ("GIT_REVISION", "abc123"));

        Assert.Equal("development", settings.RailsEnv);
        Assert.Equal("secret", settings.SecretKeyBase);
        Assert.Equal("public", settings.VapidPublicKey);
        Assert.Equal("private", settings.VapidPrivateKey);
        Assert.Equal("chat.example.com", settings.TlsDomain);
        Assert.Equal("/data/db/development.sqlite3", settings.DatabasePath);
        Assert.Equal("/data/files", settings.FilesPath);
        Assert.Equal("/data/backups", settings.BackupsPath);
        Assert.Equal(4000, settings.Port);
        Assert.Equal(3, settings.MaxThreads);
        Assert.Equal("abc123", settings.AppVersion);
    }

    [Fact]
    public void The_database_path_can_be_set_on_its_own()
    {
        Assert.Equal("/elsewhere/campfire.db", root.Settings(("CAMPFIRE_DATABASE_PATH", "/elsewhere/campfire.db")).DatabasePath);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("  ", true)]
    [InlineData("1", false)]
    [InlineData("false", false)]
    public void Ssl_is_on_while_disable_ssl_is_blank(string? disableSsl, bool ssl)
    {
        Assert.Equal(ssl, root.Settings(("DISABLE_SSL", disableSsl)).Ssl);
    }

    [Fact]
    public void App_version_prefers_app_version_then_git_revision()
    {
        Assert.Equal("1.2", root.Settings(("APP_VERSION", "1.2"), ("GIT_REVISION", "abc")).AppVersion);
        Assert.Equal("abc", root.Settings(("APP_VERSION", ""), ("GIT_REVISION", "abc")).AppVersion);
    }

    [Theory]
    [InlineData("PORT", "http")]
    [InlineData("PORT", "-1")]
    [InlineData("RAILS_MAX_THREADS", "0")]
    public void Malformed_numbers_are_rejected(string name, string value)
    {
        var error = Assert.Throws<SettingsException>(() => root.Settings((name, value)));
        Assert.Contains(name, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_log_level_is_info_like_the_reference()
    {
        Assert.Equal(LogLevel.Information, root.Settings().LogLevel);
        Assert.Equal(LogLevel.Information, root.Settings(("RAILS_LOG_LEVEL", "")).LogLevel);
    }

    // Logger::Severity's names in any case; `unknown` is above `fatal`, so it logs nothing.
    [Theory]
    [InlineData("debug", LogLevel.Debug)]
    [InlineData("info", LogLevel.Information)]
    [InlineData("warn", LogLevel.Warning)]
    [InlineData("WARN", LogLevel.Warning)]
    [InlineData("error", LogLevel.Error)]
    [InlineData("fatal", LogLevel.Critical)]
    [InlineData("unknown", LogLevel.None)]
    public void Reads_rails_log_level(string value, LogLevel expected)
    {
        Assert.Equal(expected, root.Settings(("RAILS_LOG_LEVEL", value)).LogLevel);
    }

    [Fact]
    public void An_unknown_log_level_is_rejected_as_rails_rejects_it()
    {
        var error = Assert.Throws<SettingsException>(() => root.Settings(("RAILS_LOG_LEVEL", "loud")));
        Assert.Contains("RAILS_LOG_LEVEL", error.Message, StringComparison.Ordinal);
    }
}
