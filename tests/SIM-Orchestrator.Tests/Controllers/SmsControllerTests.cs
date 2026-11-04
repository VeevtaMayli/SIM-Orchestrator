using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using SIMOrchestrator.Controllers;
using SIMOrchestrator.Models;
using SIMOrchestrator.Services;
using Xunit;

namespace SIMOrchestrator.Tests.Controllers;

public class SmsControllerTests
{
    private readonly Mock<ISmsStorageService> _storageMock = new();
    private readonly Mock<IDeliveryQueue> _queueMock = new();
    private readonly SmsController _controller;

    public SmsControllerTests()
    {
        _controller = new SmsController(_storageMock.Object, _queueMock.Object, Mock.Of<ILogger<SmsController>>());
    }

    [Fact]
    public async Task ReceiveSms_NewMessage_ReturnsReceivedAndSignalsDispatcher()
    {
        var request = new SmsRequest { Sender = "+1234567890", Text = "Test message" };
        var saved = new SmsMessage { Id = 1, Sender = request.Sender, Text = request.Text };
        _storageMock.Setup(s => s.SaveSmsAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SaveSmsResult(saved, IsDuplicate: false));

        var result = await _controller.ReceiveSms(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Contains("received", ok.Value!.ToString());
        _queueMock.Verify(q => q.Signal(), Times.Once);
    }

    [Fact]
    public async Task ReceiveSms_Duplicate_ReturnsSameIdWithoutSignalling()
    {
        var request = new SmsRequest { Sender = "+1234567890", Text = "Test message" };
        var existing = new SmsMessage { Id = 42, Sender = request.Sender, Text = request.Text };
        _storageMock.Setup(s => s.SaveSmsAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SaveSmsResult(existing, IsDuplicate: true));

        var result = await _controller.ReceiveSms(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = ok.Value!.ToString()!;
        Assert.Contains("duplicate", body);
        Assert.Contains("42", body);
        _queueMock.Verify(q => q.Signal(), Times.Never);
    }

    [Fact]
    public async Task ReceiveSms_StorageFails_PropagatesException()
    {
        var request = new SmsRequest { Sender = "+1234567890", Text = "Test message" };
        _storageMock.Setup(s => s.SaveSmsAsync(request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB error"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _controller.ReceiveSms(request, CancellationToken.None));

        _queueMock.Verify(q => q.Signal(), Times.Never);
    }
}
