using Cadder.CaddyShim;
using Cadder.Contracts;

namespace Cadder.Daemon.Tests;

public sealed class ShimCommandParserTests
{
    [Fact]
    public void Parse_RunWithoutConfig_UsesCaddyfileInWorkingDirectory()
    {
        var workingDirectory = Path.Combine(Path.GetTempPath(), "cadder-shim-parser");

        var result = ShimCommandParser.Parse(["run"], workingDirectory);

        Assert.True(result.Succeeded);
        Assert.Equal(ShimCommandKind.Run, result.CommandKind);
        Assert.NotNull(result.RunCommand);
        Assert.Equal("Caddyfile", result.RunCommand.RawConfigPath);
        Assert.Equal(Path.GetFullPath(Path.Combine(workingDirectory, "Caddyfile")), result.RunCommand.CanonicalConfigPath);
        Assert.Equal(workingDirectory, result.RunCommand.WorkingDirectory);
        Assert.Null(result.RunCommand.Adapter);
        Assert.Equal(["run"], result.RunCommand.RawArguments);
    }

    [Theory]
    [InlineData("--config", "Caddyfile", "--adapter", "caddyfile")]
    [InlineData("--config=Caddyfile", null, "--adapter=caddyfile", null)]
    public void Parse_RunWithConfigAndAdapter_SupportsSeparatedAndEqualsForms(
        string configFlag,
        string? configValue,
        string adapterFlag,
        string? adapterValue)
    {
        var workingDirectory = Path.Combine(Path.GetTempPath(), "cadder-shim-parser");
        var args = BuildArgs(configFlag, configValue, adapterFlag, adapterValue);

        var result = ShimCommandParser.Parse(args, workingDirectory);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.RunCommand);
        Assert.Equal("Caddyfile", result.RunCommand.RawConfigPath);
        Assert.Equal("caddyfile", result.RunCommand.Adapter);
        Assert.Equal(args, result.RunCommand.RawArguments);
    }

    [Fact]
    public void Parse_RunWithMissingOptionValue_FailsClearly()
    {
        var result = ShimCommandParser.Parse(["run", "--config"], Directory.GetCurrentDirectory());

        Assert.False(result.Succeeded);
        Assert.Contains("--config option requires", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RunWithNextFlagAsOptionValue_FailsClearly()
    {
        var result = ShimCommandParser.Parse(
            ["run", "--config", "--adapter", "caddyfile"],
            Directory.GetCurrentDirectory());

        Assert.False(result.Succeeded);
        Assert.Contains("--config option requires", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_UnsupportedCommand_NamesSupportedCommandSet()
    {
        var result = ShimCommandParser.Parse(["version"], Directory.GetCurrentDirectory());

        Assert.False(result.Succeeded);
        Assert.Contains("Unsupported caddy command 'version'", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("caddy run", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("--cadder-shim-info", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void RegistrationFactory_CapturesShimRunSessionFields()
    {
        var workingDirectory = Path.Combine(Path.GetTempPath(), "cadder-shim-parser");
        var command = ShimCommandParser.Parse(
            ["run", "--config", "Caddyfile", "--adapter", "caddyfile"],
            workingDirectory).RunCommand;
        Assert.NotNull(command);

        var registration = ShimRegistrationFactory.Create(
            command,
            new ShimProcessIdentity(1234, DateTimeOffset.Parse("2026-06-09T12:00:00Z"), "C:\\tools\\caddy.exe"),
            DateTimeOffset.Parse("2026-06-09T12:00:01Z"),
            "nonce-1");

        Assert.Equal("shim-nonce-1", registration.RegistrationId);
        Assert.Equal(ActivationState.Registered, registration.ActivationState);
        Assert.Equal(workingDirectory, registration.SourceWorkingDirectory.Raw);
        Assert.Equal(Path.GetFullPath(workingDirectory), registration.SourceWorkingDirectory.Canonical);
        Assert.Equal("Caddyfile", registration.SourceConfigPath.Raw);
        Assert.NotNull(registration.ShimRun);
        Assert.Equal("caddyfile", registration.ShimRun.Adapter);
        Assert.Equal(["run", "--config", "Caddyfile", "--adapter", "caddyfile"], registration.ShimRun.RawArguments);
        Assert.Equal(1234, registration.OwnerProcess.ProcessId);
        Assert.Equal("nonce-1", registration.OwnerProcess.ShimSessionNonce);
    }

    private static string[] BuildArgs(
        string configFlag,
        string? configValue,
        string adapterFlag,
        string? adapterValue)
    {
        List<string> args = ["run", configFlag];

        if (configValue is not null)
        {
            args.Add(configValue);
        }

        args.Add(adapterFlag);

        if (adapterValue is not null)
        {
            args.Add(adapterValue);
        }

        return [.. args];
    }
}
