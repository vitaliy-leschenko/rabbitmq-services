
namespace RabbitMQ.Services.Demo.Consumers
{
    public class DemoMessageB: BaseMessage
    {
        public int Value { get; set; }
    }

    public sealed class DemoMessageBHandler(ILogger<DemoMessageAHandler> logger) : BaseMessageHandler<DemoMessageB>
    {
        private readonly ILogger<DemoMessageAHandler> logger = logger;

        public override async Task HandleAsync(DemoMessageB message, CancellationToken token)
        {
            logger.LogInformation("delay: {value}", message.Value);
            if (message.Value > 0)
            {
                await Task.Delay(message.Value, token);
            }

            logger.LogInformation("done");
        }
    }
}
