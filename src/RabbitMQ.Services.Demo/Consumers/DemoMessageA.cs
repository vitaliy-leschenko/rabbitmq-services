
namespace RabbitMQ.Services.Demo.Consumers
{
    public class DemoMessageA: BaseMessage
    {
        public int Value { get; set; }
    }

    public sealed class DemoMessageAHandler(ILogger<DemoMessageAHandler> logger) : BaseMessageHandler<DemoMessageA>
    {
        private readonly ILogger<DemoMessageAHandler> logger = logger;

        public override async Task HandleAsync(DemoMessageA message, CancellationToken token)
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
