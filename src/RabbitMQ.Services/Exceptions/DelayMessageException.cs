using System.Diagnostics.CodeAnalysis;

namespace RabbitMQ.Services.Exceptions
{
    [ExcludeFromCodeCoverage]
    public class DelayMessageException : Exception
    {
        public TimeSpan? Timeout { get; }

        public DelayMessageException()
        {
        }

        public DelayMessageException(TimeSpan? timeout)
        {
            Timeout = timeout;
        }

        public DelayMessageException(TimeSpan? timeout, string message)
            : base(message)
        {
            Timeout = timeout;
        }

        public DelayMessageException(TimeSpan? timeout, string message, Exception innerException)
            : base(message, innerException)
        {
            Timeout = timeout;
        }
    }
}
