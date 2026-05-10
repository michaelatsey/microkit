namespace MicroKit.Messaging.Transport.RabbitMQ
{
    public class RabbitMQOptions
    {
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 5672;
        public string UserName { get; set; } = "guest";
        public string Password { get; set; } = "guest";
        public string VirtualHost { get; set; } = "/";
        public string ExchangeName { get; set; } = "nexus.events";
        public string ExchangeType { get; set; } = "topic";
        public bool Durable { get; set; } = true;
        public int PrefetchCount { get; set; } = 50;
        public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(30);
    }
}
