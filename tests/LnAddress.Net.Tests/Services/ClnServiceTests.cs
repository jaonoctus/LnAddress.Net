using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace LnAddress.Net.Tests.Services;

using LnAddress.Net.Services;

public class ClnServiceTests
{
    private static IConfiguration BuildConfiguration(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    [Theory]
    [InlineData("Cln:CaCert", "Cln CA certificate config is missing")]
    [InlineData("Cln:ClientCert", "Cln client certificate config is missing")]
    [InlineData("Cln:ClientKey", "Cln client key config is missing")]
    public void Given_MissingSetting_When_Constructing_Then_Expect_DescriptiveException(string missingKey, string expectedMessage)
    {
        var settings = new Dictionary<string, string?>
        {
            { "Cln:RpcAddress", "https://localhost:9736" },
            { "Cln:CaCert", "LS0t" },
            { "Cln:ClientCert", "LS0t" },
            { "Cln:ClientKey", "LS0t" }
        };
        settings.Remove(missingKey);

        var exception = Assert.Throws<Exception>(() =>
            new ClnService(BuildConfiguration(settings), new Mock<ILogger<ClnService>>().Object));

        Assert.Equal(expectedMessage, exception.Message);
    }

    [Fact]
    public void Given_NonBase64NonPemSetting_When_Constructing_Then_Expect_DescriptiveException()
    {
        var settings = new Dictionary<string, string?>
        {
            { "Cln:RpcAddress", "https://localhost:9736" },
            { "Cln:CaCert", "not base64 and not pem!" },
            { "Cln:ClientCert", "LS0t" },
            { "Cln:ClientKey", "LS0t" }
        };

        var exception = Assert.Throws<Exception>(() =>
            new ClnService(BuildConfiguration(settings), new Mock<ILogger<ClnService>>().Object));

        Assert.Equal("Cln:CaCert must be PEM text or a base64 encoded PEM file", exception.Message);
    }
}
