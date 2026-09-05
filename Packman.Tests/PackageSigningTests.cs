using Packman.Services;
using Xunit;

namespace Packman.Tests;

public sealed class PackageSigningTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("packman-signing-").FullName;

    private string AddScript()
    {
        var app = Directory.CreateDirectory(Path.Combine(_root, "Application"));
        var script = Path.Combine(app.FullName, "Invoke-AppDeployToolkit.ps1");
        File.WriteAllText(script, "Write-Output 'sample'");
        return script;
    }

    [Fact]
    public async Task Disabled_signing_does_not_require_a_certificate()
        => Assert.Null(await PackageSigningService.SignAsync(_root, null));

    [Fact]
    public async Task Enabled_signing_propagates_failure_instead_of_publishing_unsigned_content()
    {
        AddScript();
        var signer = new StubSigner((_, _) => Task.FromResult(new SigningResult { Success = false, ErrorMessage = "Certificate unavailable" }));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => PackageSigningService.SignAsync(_root, signer));
        Assert.Contains("Publishing stopped", error.Message);
        Assert.Contains("Certificate unavailable", error.Message);
    }

    [Fact]
    public async Task Signs_the_deployment_script_and_passes_cancellation_to_the_provider()
    {
        var script = AddScript();
        using var cts = new CancellationTokenSource();
        var signer = new StubSigner((path, token) =>
        {
            Assert.Equal(script, path);
            Assert.Equal(cts.Token, token);
            return Task.FromResult(new SigningResult { FilePath = path, Success = true });
        });
        Assert.Equal(script, await PackageSigningService.SignAsync(_root, signer, cts.Token));
    }

    [Fact]
    public async Task Missing_script_stops_before_calling_the_signer()
    {
        var signer = new StubSigner((_, _) => throw new InvalidOperationException("Should not run"));
        await Assert.ThrowsAsync<FileNotFoundException>(() => PackageSigningService.SignAsync(_root, signer));
    }

    [Fact]
    public async Task Cancellation_is_preserved_even_if_a_provider_returns_success_after_cancellation()
    {
        AddScript();
        using var cts = new CancellationTokenSource();
        var signer = new StubSigner((_, _) =>
        {
            cts.Cancel();
            return Task.FromResult(new SigningResult { Success = true });
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PackageSigningService.SignAsync(_root, signer, cts.Token));
    }

    [Fact]
    public async Task Provider_exceptions_do_not_turn_into_a_successful_build()
    {
        AddScript();
        var signer = new StubSigner((_, _) => throw new IOException("Provider connection failed"));
        await Assert.ThrowsAsync<IOException>(() => PackageSigningService.SignAsync(_root, signer));
    }

    private sealed class StubSigner(Func<string, CancellationToken, Task<SigningResult>> sign) : IFileSigner
    {
        public Task<SigningResult> SignFileAsync(string filePath, CancellationToken cancellationToken = default)
            => sign(filePath, cancellationToken);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
