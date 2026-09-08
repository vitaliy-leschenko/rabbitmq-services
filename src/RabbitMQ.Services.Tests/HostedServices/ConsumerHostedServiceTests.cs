using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
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
            mocker.Use<TimeProvider>(TimeProvider.System);
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

        /// <summary>
        /// Drives the fake clock forward in steps until the condition holds: the timers behind the
        /// service's delays only exist once the loop has reached them, so a single Advance is not enough.
        /// </summary>
        private static async Task AdvanceUntilAsync(FakeTimeProvider timeProvider, TimeSpan step, Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!condition() && DateTime.UtcNow < deadline)
            {
                timeProvider.Advance(step);
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }

            Assert.True(condition());
        }

        private static (AutoMocker Mocker, ConsumerHostedService<T> Service) Create(TimeProvider timeProvider)
        {
            var mocker = new AutoMocker();
            mocker.Use<TimeProvider>(timeProvider);

            mocker.GetMock<IOptions<ConsumerConfiguration<T>>>()
                .SetupGet(t => t.Value)
                .Returns(new ConsumerConfiguration<T>());

            return (mocker, mocker.CreateInstance<ConsumerHostedService<T>>());
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
                .Setup(t => t.StartAsync(It.IsAny<CancellationToken>()))
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
                .Setup(t => t.StartAsync(It.IsAny<CancellationToken>()))
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

            mocker.GetMock<IAsyncMessageConsumer<T>>().Verify(t => t.StartAsync(It.IsAny<CancellationToken>()));
        }

        [Fact]
        public async Task StopAsync_ShouldStopConsumer()
        {
            // Arrange
            mocker.GetMock<IAsyncMessageConsumer<T>>()
                .Setup(t => t.StartAsync(It.IsAny<CancellationToken>()))
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

            mocker.GetMock<IAsyncMessageConsumer<T>>().Verify(t => t.StopAsync(It.IsAny<CancellationToken>()));
        }

        [Fact]
        public async Task Dispose_ShouldCancelExecutingTask()
        {
            // Arrange
            mocker.GetMock<IAsyncMessageConsumer<T>>()
                .Setup(t => t.StartAsync(It.IsAny<CancellationToken>()))
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
                .Setup(t => t.StartAsync(It.IsAny<CancellationToken>()))
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
            mocker.Use<TimeProvider>(TimeProvider.System);

            mocker.GetMock<IAsyncMessageConsumer<T>>()
                .Setup(t => t.StartAsync(It.IsAny<CancellationToken>()))
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
            consumer.Setup(t => t.StartAsync(It.IsAny<CancellationToken>()))
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
            consumer.Verify(t => t.StopAsync(It.IsAny<CancellationToken>()), Times.Once);

            await service.StopAsync(TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task ExecuteAsync_ShouldKeepRetrying_WhenTheBrokerIsStillDownAfterTheLoss()
        {
            // Arrange
            var starts = 0;
            var consumer = mocker.GetMock<IAsyncMessageConsumer<T>>();
            consumer.Setup(t => t.StartAsync(It.IsAny<CancellationToken>()))
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

        [Fact]
        public async Task StartConsumerAsync_ShouldAbandonHungStart_AndRetry()
        {
            // Arrange
            var timeProvider = new FakeTimeProvider();
            var (mocker, service) = Create(timeProvider);

            var starts = 0;
            var hungStart = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            mocker.GetMock<IAsyncMessageConsumer<T>>()
                .Setup(t => t.StartAsync(It.IsAny<CancellationToken>()))
                .Returns((CancellationToken token) =>
                {
                    if (Interlocked.Increment(ref starts) == 1)
                    {
                        hungStart.TrySetResult();

                        // Stuck in the client: ends only when the supervisor gives up on the attempt.
                        return Task.Delay(Timeout.Infinite, token);
                    }

                    return Task.CompletedTask;
                });

            await service.StartAsync(TestContext.Current.CancellationToken);
            await hungStart.Task;
            Assert.False(service.Started);

            // Act
            timeProvider.Advance(ConsumerHostedService<T>.StartAttemptTimeout);

            // Assert
            await AdvanceUntilAsync(timeProvider, ConsumerHostedService<T>.RetryDelay,
                () => Volatile.Read(ref starts) == 2 && service.Started);

            await service.StopAsync(TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task ExecuteAsync_ShouldRestartConsumer_WhenWatchdogSeesDisconnectedConsumer()
        {
            // Arrange
            var timeProvider = new FakeTimeProvider();
            var (mocker, service) = Create(timeProvider);

            var starts = 0;
            var consumer = mocker.GetMock<IAsyncMessageConsumer<T>>();
            consumer.Setup(t => t.StartAsync(It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    Interlocked.Increment(ref starts);
                    return Task.CompletedTask;
                });

            // The connection is gone, but no ConnectionLost event was ever raised.
            consumer.SetupGet(t => t.IsConnected).Returns(false);

            await service.StartAsync(TestContext.Current.CancellationToken);
            await WaitForAsync(() => service.Started);

            // Act + Assert
            await AdvanceUntilAsync(timeProvider, ConsumerHostedService<T>.WatchdogInterval,
                () => Volatile.Read(ref starts) >= 2);

            consumer.Verify(t => t.StopAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);

            await service.StopAsync(TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task ExecuteAsync_ShouldNotRestart_WhenWatchdogSeesConnectedConsumer()
        {
            // Arrange
            var timeProvider = new FakeTimeProvider();
            var (mocker, service) = Create(timeProvider);

            var starts = 0;
            var consumer = mocker.GetMock<IAsyncMessageConsumer<T>>();
            consumer.Setup(t => t.StartAsync(It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    Interlocked.Increment(ref starts);
                    return Task.CompletedTask;
                });

            consumer.SetupGet(t => t.IsConnected).Returns(true);

            await service.StartAsync(TestContext.Current.CancellationToken);
            await WaitForAsync(() => service.Started);
            await Task.Delay(50, TestContext.Current.CancellationToken);

            // Act
            timeProvider.Advance(ConsumerHostedService<T>.WatchdogInterval);
            timeProvider.Advance(ConsumerHostedService<T>.WatchdogInterval);
            await Task.Delay(50, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(1, Volatile.Read(ref starts));
            Assert.True(service.Started);
            consumer.Verify(t => t.StopAsync(It.IsAny<CancellationToken>()), Times.Never);

            await service.StopAsync(TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task StopAsync_ShouldComplete_WhileStartIsHung()
        {
            // Arrange
            var hungStart = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            mocker.GetMock<IAsyncMessageConsumer<T>>()
                .Setup(t => t.StartAsync(It.IsAny<CancellationToken>()))
                .Returns((CancellationToken token) =>
                {
                    hungStart.TrySetResult();
                    return Task.Delay(Timeout.Infinite, token);
                });

            mocker.GetMock<IOptions<ConsumerConfiguration<T>>>()
                .SetupGet(t => t.Value)
                .Returns(new ConsumerConfiguration<T>());

            await service.StartAsync(TestContext.Current.CancellationToken);
            await hungStart.Task;

            // Act
            await service.StopAsync(TestContext.Current.CancellationToken);

            // Assert
            Assert.NotNull(service.ExecutingTask);
            Assert.True(service.ExecutingTask.IsCanceled);
            Assert.False(service.Started);

            mocker.GetMock<IAsyncMessageConsumer<T>>().Verify(t => t.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ExecuteAsync_ShouldReconnect_WhenStoppingTheConsumerFails()
        {
            // Arrange
            var starts = 0;
            var consumer = mocker.GetMock<IAsyncMessageConsumer<T>>();
            consumer.Setup(t => t.StartAsync(It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    Interlocked.Increment(ref starts);
                    return Task.CompletedTask;
                });

            consumer.Setup(t => t.StopAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("channel would not close"));

            mocker.GetMock<IOptions<ConsumerConfiguration<T>>>()
                .SetupGet(t => t.Value)
                .Returns(new ConsumerConfiguration<T>());

            await service.StartAsync(TestContext.Current.CancellationToken);
            await WaitForAsync(() => service.Started);

            // Act
            consumer.Raise(t => t.ConnectionLost += null, EventArgs.Empty);

            // Assert: a stop that fails is logged and does not prevent the restart.
            await WaitForAsync(() => Volatile.Read(ref starts) == 2 && service.Started);

            consumer.Setup(t => t.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            await service.StopAsync(TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task StopAsync_ShouldComplete_WhileReconnectStopIsHung()
        {
            // Arrange
            var stops = 0;
            var hungStop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var consumer = mocker.GetMock<IAsyncMessageConsumer<T>>();
            consumer.Setup(t => t.StartAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            consumer.Setup(t => t.StopAsync(It.IsAny<CancellationToken>()))
                .Returns((CancellationToken token) =>
                {
                    if (Interlocked.Increment(ref stops) == 1)
                    {
                        hungStop.TrySetResult();
                        return Task.Delay(Timeout.Infinite, token);
                    }

                    return Task.CompletedTask;
                });

            mocker.GetMock<IOptions<ConsumerConfiguration<T>>>()
                .SetupGet(t => t.Value)
                .Returns(new ConsumerConfiguration<T>());

            await service.StartAsync(TestContext.Current.CancellationToken);
            await WaitForAsync(() => service.Started);

            consumer.Raise(t => t.ConnectionLost += null, EventArgs.Empty);
            await hungStop.Task;

            // Act
            await service.StopAsync(TestContext.Current.CancellationToken);

            // Assert
            Assert.NotNull(service.ExecutingTask);
            Assert.True(service.ExecutingTask.IsCanceled);
            Assert.False(service.Started);
        }
    }
}
