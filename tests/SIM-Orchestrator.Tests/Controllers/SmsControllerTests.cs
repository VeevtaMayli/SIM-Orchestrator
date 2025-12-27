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
    private readonly Mock<ISmsStorageService> _storageMock;
    private readonly Mock<ITelegramService> _telegramMock;
    private readonly Mock<ILogger<SmsController>> _loggerMock;
    private readonly SmsController _controller;

    public SmsControllerTests()
    {
        _storageMock = new Mock<ISmsStorageService>();
        _telegramMock = new Mock<ITelegramService>();
        _loggerMock = new Mock<ILogger<SmsController>>();
        _controller = new SmsController(_storageMock.Object, _telegramMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task ReceiveSms_ValidRequest_ReturnsOk()
    {
        var request = new SmsRequest { Sender = "+1234567890", Text = "Test message" };
        var savedSms = new SmsMessage { Id = 1, Sender = request.Sender, Text = request.Text };

        _storageMock.Setup(s => s.SaveSmsAsync(request)).ReturnsAsync(savedSms);
        _telegramMock.Setup(t => t.SendSmsToTelegramAsync(savedSms)).ReturnsAsync(true);

        var result = await _controller.ReceiveSms(request);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
        _storageMock.Verify(s => s.MarkAsSentAsync(1), Times.Once);
    }

    [Fact]
    public async Task ReceiveSms_TelegramFails_StillReturnsOk()
    {
        var request = new SmsRequest { Sender = "+1234567890", Text = "Test message" };
        var savedSms = new SmsMessage { Id = 1, Sender = request.Sender, Text = request.Text };

        _storageMock.Setup(s => s.SaveSmsAsync(request)).ReturnsAsync(savedSms);
        _telegramMock.Setup(t => t.SendSmsToTelegramAsync(savedSms)).ReturnsAsync(false);

        var result = await _controller.ReceiveSms(request);

        var okResult = Assert.IsType<OkObjectResult>(result);
        _storageMock.Verify(s => s.MarkAsSentAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ReceiveSms_StorageFails_ThrowsException()
    {
        var request = new SmsRequest { Sender = "+1234567890", Text = "Test message" };
        _storageMock.Setup(s => s.SaveSmsAsync(request)).ThrowsAsync(new Exception("DB error"));

        await Assert.ThrowsAsync<Exception>(() => _controller.ReceiveSms(request));
    }
}
