namespace MicroKit.Messaging.Transport.RabbitMQ
{
    /// <summary>Configuration options for the RabbitMQ message transport.</summary>
    public class RabbitMQOptions
    {
        /// <summary>Gets or sets the RabbitMQ broker hostname. Defaults to <c>localhost</c>.</summary>
        public string Host { get; set; } = "localhost";

        /// <summary>Gets or sets the AMQP port. Defaults to <c>5672</c>.</summary>
        public int Port { get; set; } = 5672;

        /// <summary>Gets or sets the AMQP username. Defaults to <c>guest</c>.</summary>
        public string UserName { get; set; } = "guest";

        /// <summary>Gets or sets the AMQP password. Defaults to <c>guest</c>.</summary>
        public string Password { get; set; } = "guest";

        /// <summary>Gets or sets the RabbitMQ virtual host. Defaults to <c>/</c>.</summary>
        public string VirtualHost { get; set; } = "/";

        /// <summary>Gets or sets the exchange name used for publishing messages. Defaults to <c>nexus.events</c>.</summary>
        public string ExchangeName { get; set; } = "nexus.events";

        /// <summary>Gets or sets the AMQP exchange type (e.g. <c>topic</c>, <c>direct</c>, <c>fanout</c>). Defaults to <c>topic</c>.</summary>
        public string ExchangeType { get; set; } = "topic";

        /// <summary>Gets or sets a value indicating whether the exchange is declared as durable. Defaults to <see langword="true"/>.</summary>
        public bool Durable { get; set; } = true;

        /// <summary>Gets or sets the number of messages the broker pre-fetches per consumer. Defaults to <c>50</c>.</summary>
        public int PrefetchCount { get; set; } = 50;

        /// <summary>Gets or sets the maximum time to wait when establishing a connection. Defaults to 30 seconds.</summary>
        public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(30);
    }
}
