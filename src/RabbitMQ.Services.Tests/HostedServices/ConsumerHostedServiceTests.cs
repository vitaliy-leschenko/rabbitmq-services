using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.AutoMock;
using RabbitMQ.Client.Exceptions;
using RabbitMQ.Services.HostedServices;
using RabbitMQ.Services.Implementations;
using RabbitMQ.Services.Interfaces;
using RabbitMQ.Services.Settings;
using Xunit;

namespace RabbitMQ.Services.Tests.HostedServices
{
    public class ConsumerHostedServiceTests
    {
        public class T
        {
        }

        private readonly AutoMocker mocker = new();
        private readonly ConsumerHostedService<T> service;

        public ConsumerHostedServiceTests()
        {
            service = mocker.CreateInstance<ConsumerHostedService<T>>();
        }

        private static async Task WaitForAsync(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!condition() && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }

            Assert.True(condition());
        }

        [Fact]
        public void Started_ShouldBeFalseInitially()
        {
            // Assert
            Assert.False(service.Started);
        }

        [Fact]
        public async Task Dispose_ShouldDisposeConsumerAsync()
        {
            // Act
            await service.DisposeAsync();

            // Assert
            mocker.GetMock<IAsyncMessageConsumer<T>>().Verify(t => t.DisposeAsync());
        }

        [Fact]
        public async Task StopAsync_ShouldIgnoreIfNotStarted()
        {
            // Act
            await service.StopAsync(TestContext.Current.CancellationToken);

            // Assert
            mocker.GetMock<IAsyncMessageConsumer<T>>().VerifyNoOtherCalls();
        }

        [Fact]
        public async Task StartAsync_ShouldSetupExecutingTask()
        {
            // Arrange
            mocker.GetMock<IAsyncMessageConsumer<T>>()
                .Setup(t => t.StartAsync())
                .Verifiable();

            mocker.GetMock<IOptions<ConsumerConfiguration<T>>>()
                .SetupGet(t => t.Value)
                .Returns(new ConsumerConfiguration<T>());

            // Act
            await service.StartAsync(TestContext.Current.CancellationToken);

            // Assert
            Assert.NotNull(service.ExecutingTask);
        }

        [Fact]
        public async Task StartAsync_ShouldBeCancelledAfterTimeout()
        {
            // Arrange
            mocker.GetMock<IAsyncMessageConsumer<T>>()
                .Setup(t => t.StartAsync())
                .Throws(new Exception());

            mocker.GetMock<IOptions<ConsumerConfiguration<T>>>()
                .SetupGet(t => t.Value)
                .Returns(new ConsumerConfiguration<T>());

            var source = new CancellationTokenSource(10);

            // Act
            await service.StartAsync(source.Token);

            // Assert
            Assert.NotNull(service.ExecutingTask);
            await Assert.ThrowsAsync<TaskCanceledException>(() => service.ExecutingTask);

            mocker.GetMock<IAsyncMessageConsumer<T>>().Verify(t => t.StartAsync());
        }

        [Fact]
        public async Task StopAsync_ShouldStopConsumer()
        {
            // Arrange
            mocker.GetMock<IAsyncMessageConsumer<T>>()
                .Setup(t => t.StartAsync())
                .Verifiable();

            mocker.GetMock<IOptions<ConsumerConfiguration<T>>>()
                .SetupGet(t => t.Value)
                .Returns(new ConsumerConfiguration<T>());

            await service.StartAsync(TestContext.Current.CancellationToken);
            await WaitForAsync(() => service.Started);

            // Act
            await service.StopAsync(TestContext.Current.CancellationToken);

            // Assert
            Assert.False(service.Started);

            mocker.GetMock<IAsyncMessageConsumer<T>>().Verify(t => t.StopAsync());
        }

        [Fact]
        public async Task Dispose_ShouldCancelExecutingTask()
        {
            // Arrange
            mocker.GetMock<IAsyncMessageConsumer<T>>()
                .Setup(t => t.StartAsync())
                .Throws(new Exception());

            mocker.GetMock<IOptions<ConsumerConfiguration<T>>>()
                .SetupGet(t => t.Value)
                .Returns(new ConsumerConfiguration<T>());

            var source = new CancellationTokenSource(10);
            await service.StartAsync(source.Token);

            // Act
            await service.DisposeAsync();

            // Assert
            mocker.GetMock<IAsyncMessageConsumer<T>>().Verify(t => t.DisposeAsync());

            await Task.Delay(100, TestContext.Current.CancellationToken);
            Assert.NotNull(service.ExecutingTask);
            Assert.True(service.ExecutingTask.IsCanceled);

            Assert.False(service.Started);
        }

        [Fact]
        public async Task StartAsync_ShouldMaskUrlBeforeLogging()
        {
            // Arrange
            const string Url = "amqp://user:secret@localhost/vhost/queue";

            mocker.GetMock<IAsyncMessageConsumer<T>>()
                .Setup(t => t.StartAsync())
                .Verifiable();

            mocker.GetMock<IOptions<ConsumerConfiguration<T>>>()
                .SetupGet(t => t.Value)
                .Returns(new ConsumerConfiguration<T> { Url = Url });

            mocker.GetMock<IUriMasker>()
                .Setup(t => t.Mask(Url))
                .Returns("masked")
                .Verifiable();

            // Act
            await service.StartAsync(TestContext.Current.CancellationToken);
            await WaitForAsync(() => service.Started);

            // Assert
            mocker.GetMock<IUriMasker>().Verify(t => t.Mask(Url), Times.AtLeastOnce);

            await service.StopAsync(TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task StartAsync_ShouldNotLogCredentials()
        {
            // Arrange
            const string Url = "amqp://user:secret@localhost/vhost/queue";
            const string MaskedUrl = "amqp://***:***@localhost/vhost/queue";

            var mocker = new AutoMocker();
            mocker.Use<IUriMasker>(new UriMasker());

            mocker.GetMock<IAsyncMessageConsumer<T>>()
                .Setup(t => t.StartAsync())
                .Verifiable();

            mocker.GetMock<IOptions<ConsumerConfiguration<T>>>()
                .SetupGet(t => t.Value)
                .Returns(new ConsumerConfiguration<T> { Url = Url });

            var service = mocker.CreateInstance<ConsumerHostedService<T>>();

            // Act
            await service.StartAsync(TestContext.Current.CancellationToken);
            await WaitForAsync(() => service.Started);

            // Assert
#pragma warning disable CA1873 // Avoid potentially expensive logging
            mocker.GetMock<ILogger<ConsumerHostedService<T>>>()
                .Verify(t => t.Log(LogLevel.Information, It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains(MaskedUrl)),
                    It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Exactly(2));
            mocker.GetMock<ILogger<ConsumerHostedService<T>>>()
                .Verify(t => t.Log(It.IsAny<LogLevel>(), It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains("secret")),
                    It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Never);
#pragma warning restore CA1873 // Avoid potentially expensive logging

            await service.StopAsync(TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task ExecuteAsync_ShouldReconnect_WhenTheConnectionIsLost()
        {
            // Arrange
            var starts = 0;
            var consumer = mocker.GetMock<IAsyncMessageConsumer<T>>();
            consumer.Setup(t => t.StartAsync())
                .Returns(() =>
                {
                    Interlocked.Increment(ref starts);
                    return Task.CompletedTask;
                });

            mocker.GetMock<IOptions<ConsumerConfiguration<T>>>()
                .SetupGet(t => t.Value)
                .Returns(new ConsumerConfiguration<T>());

            await service.StartAsync(TestContext.Current.CancellationToken);
            await WaitForAsync(() => service.Started);

            // Act
            consumer.Raise(t => t.ConnectionLost += null, EventArgs.Empty);

            // Assert
            await WaitForAsync(() => Volatile.Read(ref starts) == 2 && service.Started);
            consumer.Verify(t => t.StopAsync(), Times.Once);

            await service.StopAsync(TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task ExecuteAsync_ShouldKeepRetrying_WhenTheBrokerIsStillDownAfterTheLoss()
        {
            // Arrange
            var starts = 0;
            var consumer = mocker.GetMock<IAsyncMessageConsumer<T>>();
            consumer.Setup(t => t.StartAsync())
                .Returns(() =>
                {
                    // The first start succeeds; the reconnect right after the loss fails, because the
                    // broker is still coming up. That second failure used to kill the consumer for good.
                    var attempt = Interlocked.Increment(ref starts);
                    return attempt == 2
                        ? Task.FromException(new BrokerUnreachableException(new Exception()))
                        : Task.CompletedTask;
                });

            mocker.GetMock<IOptions<ConsumerConfiguration<T>>>()
                .SetupGet(t => t.Value)
                .Returns(new ConsumerConfiguration<T>());

            await service.StartAsync(TestContext.Current.CancellationToken);
            await WaitForAsync(() => service.Started);

            // Act
            consumer.Raise(t => t.ConnectionLost += null, EventArgs.Empty);

            // Assert
            await WaitForAsync(() => Volatile.Read(ref starts) >= 3 && service.Started);

            await service.StopAsync(TestContext.Current.CancellationToken);
        }
    }
}
