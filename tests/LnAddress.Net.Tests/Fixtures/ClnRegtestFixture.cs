using System.Diagnostics;

namespace LnAddress.Net.Tests.Fixtures;

/// <summary>
/// Starts a regtest bitcoind and a Core Lightning node (with cln-grpc enabled) using
/// tests/Docker/cln/docker-compose.yml, mines enough blocks for the node to sync and
/// exposes the mTLS identity needed to talk to the gRPC plugin.
/// </summary>
// ReSharper disable once ClassNeverInstantiated.Global
public class ClnRegtestFixture : IDisposable
{
    private const string BitcoindContainer = "lnaddress-cln-bitcoind";
    private const string ClnContainer = "lnaddress-cln-node";
    private const string BitcoinCli = "bitcoin-cli -regtest -rpcuser=lnaddress -rpcpassword=lnaddress";

    private static readonly string ComposeDirectory =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../Docker/cln"));

    public string RpcAddress { get; } = "https://127.0.0.1:19736";
    public string CaCertPem { get; private set; } = string.Empty;
    public string ClientCertPem { get; private set; } = string.Empty;
    public string ClientKeyPem { get; private set; } = string.Empty;
    public string NodePubKeyHex { get; private set; } = string.Empty;

    public ClnRegtestFixture()
    {
        SetupNetwork().Wait();
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Run("docker", $"compose -f \"{Path.Combine(ComposeDirectory, "docker-compose.yml")}\" down -v", throwOnError: false).Wait();
    }

    private async Task SetupNetwork()
    {
        var composeFile = Path.Combine(ComposeDirectory, "docker-compose.yml");
        await Run("docker", $"compose -f \"{composeFile}\" down -v", throwOnError: false);
        await Run("docker", $"compose -f \"{composeFile}\" up -d --wait");

        // Give the node a fresh chain tip so bitcoind leaves initial block download
        await Run("docker", $"exec {BitcoindContainer} {BitcoinCli} createwallet miner", throwOnError: false);
        var address = (await Run("docker", $"exec {BitcoindContainer} {BitcoinCli} -rpcwallet=miner getnewaddress")).Trim();
        await Run("docker", $"exec {BitcoindContainer} {BitcoinCli} generatetoaddress 101 {address}");

        await WaitForNodeSync();

        CaCertPem = await Run("docker", $"exec {ClnContainer} cat /root/.lightning/regtest/ca.pem");
        ClientCertPem = await Run("docker", $"exec {ClnContainer} cat /root/.lightning/regtest/client.pem");
        ClientKeyPem = await Run("docker", $"exec {ClnContainer} cat /root/.lightning/regtest/client-key.pem");
    }

    private async Task WaitForNodeSync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (DateTime.UtcNow < deadline)
        {
            var output = await Run("docker", $"exec {ClnContainer} lightning-cli --regtest getinfo", throwOnError: false);
            if (output.Contains("\"blockheight\": 101") && !output.Contains("warning_"))
            {
                NodePubKeyHex = ExtractJsonString(output, "id");
                return;
            }

            await Task.Delay(1_000);
        }

        throw new Exception("Core Lightning node did not sync in time.");
    }

    private static string ExtractJsonString(string json, string key)
    {
        var marker = $"\"{key}\": \"";
        var start = json.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var end = json.IndexOf('"', start);
        return json[start..end];
    }

    private static async Task<string> Run(string fileName, string arguments, bool throwOnError = true)
    {
        var startInfo = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(startInfo) ?? throw new Exception($"Could not start {fileName}");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (throwOnError && process.ExitCode != 0)
        {
            throw new Exception($"{fileName} {arguments} failed ({process.ExitCode}): {await stderr}");
        }

        return await stdout;
    }
}
