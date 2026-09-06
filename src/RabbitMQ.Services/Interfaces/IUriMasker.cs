namespace RabbitMQ.Services.Interfaces
{
    public interface IUriMasker
    {
        /// <summary>
        /// Replaces the user name and password in the URI user-info part with "***"
        /// so the value can be safely written to logs.
        /// </summary>
        string Mask(string? uri);
    }
}
