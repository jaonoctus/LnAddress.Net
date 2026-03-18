using Grpc.Core;
using Lnrpc;
using LNUnit.LND;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using NLightning.Domain.Protocol.ValueObjects;
using Routerrpc;
using Invoice = NLightning.Bolt11.Models.Invoice;

namespace LnAddress.Net.Tests.IntegrationTests;

using Fixtures;
using LnAddress.Net.Services;

[Collection("regtest")]
public class LndGrpcIntegrationTest
{
    private readonly LightningRegtestFixture _lightningRegtestFixture;
    private readonly LNDNodeConnection _alice;
    private readonly LndService _lndService;

    public LndGrpcIntegrationTest(LightningRegtestFixture fixture)
    {
        _lightningRegtestFixture = fixture;

        var loggerMock = new Mock<ILogger<LndService>>();

        _alice = _lightningRegtestFixture.Builder?.LNDNodePool?.ReadyNodes.First(x => x.LocalAlias == "alice") ?? throw new Exception("Alice was not ready.");

        var inMemorySettings = new Dictionary<string, string?>{
            { "Lnd:Macaroon", _alice.Settings.MacaroonBase64 },
            { "Lnd:Cert", _alice.Settings.TlsCertBase64 },
            { "Lnd:RpcAddress", _alice.Host }
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        _lndService = new LndService(configuration, loggerMock.Object);
    }

    [Fact]
    public async Task Given_ValidAmount_When_FetchInvoice_Then_Expect_ValidInvoice()
    {
        // Arrange
        const long expectedAmount = 10_000L;
        const string expectedDescription = "payment for nGoline";
        var expectedPubkey = _alice.LocalNodePubKeyBytes;

        // Act
        var invoice = await _lndService.FetchInvoiceAsync(expectedAmount, "nGoline", null);

        // Assert
        var decodedInvoice = Invoice.Decode(invoice, BitcoinNetwork.Regtest);
        Assert.NotNull(decodedInvoice);
        Assert.Equal(expectedPubkey, decodedInvoice.PayeePubKey?.ToBytes());
        Assert.Equal(expectedAmount, (long)decodedInvoice.Amount.MilliSatoshi);
        Assert.Equal(expectedDescription, decodedInvoice.Description);
    }

    [Fact]
    public async Task Given_ValidAmountAndDescription_When_FetchInvoice_Then_Expect_ValidInvoice()
    {
        // Arrange
        const long expectedAmount = 10_000L;
        const string expectedDescription = "LnAddress Payment";
        var expectedPubkey = _alice.LocalNodePubKeyBytes;

        // Act
        var invoice = await _lndService.FetchInvoiceAsync(expectedAmount, "nGoline", expectedDescription);

        // Assert
        var decodedInvoice = Invoice.Decode(invoice, BitcoinNetwork.Regtest);
        Assert.NotNull(decodedInvoice);
        Assert.Equal(expectedPubkey, decodedInvoice.PayeePubKey?.ToBytes());
        Assert.Equal(expectedAmount, (long)decodedInvoice.Amount.MilliSatoshi);
        Assert.Equal(expectedDescription, decodedInvoice.Description);
    }

    [Fact]
    public async Task Given_ValidInvoice_When_Paying_Then_NoError()
    {
        // Arrange
        var invoice = await _lndService.FetchInvoiceAsync(10_000, "nGoline", null);
        var carol = _lightningRegtestFixture.Builder?.LNDNodePool?.ReadyNodes.First(x => x.LocalAlias == "carol") ?? throw new Exception("Bob was not ready.");
        // Wait for a second so channels are synced
        await Task.Delay(1_000, TestContext.Current.CancellationToken);

        // Act
        var streamingCallResponse = carol.RouterClient.SendPaymentV2(new SendPaymentRequest
        {
            PaymentRequest = invoice,
        }, cancellationToken: TestContext.Current.CancellationToken);
        var paymentResponse = await streamingCallResponse
                                   .ResponseStream
                                   .ReadAllAsync(TestContext.Current.CancellationToken)
                                   .FirstOrDefaultAsync(cancellationToken: TestContext.Current.CancellationToken);
        
        // Assert
        Assert.NotNull(paymentResponse);
        Assert.Equal(PaymentFailureReason.FailureReasonNone, paymentResponse.FailureReason);
        Assert.NotNull(paymentResponse.PaymentPreimage);
    }
}