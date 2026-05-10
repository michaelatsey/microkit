//using MicroKit.Messaging.Abstractions.Transport;
//using RabbitMQ.Client.Events;
//using System;
//using System.Collections.Concurrent;
//using System.Collections.Generic;
//using System.Text;

//namespace MicroKit.Messaging.Transport.RabbitMQ;


//public class RabbitMQTransport : IMessageTransport, IDisposable
//{
    

//    public Task SendAsync(string destination, string messageType, string payload, string? correlationId = null, Dictionary<string, string>? properties = null, CancellationToken cancellationToken = default)
//    {
//        throw new NotImplementedException();
//    }

//    public Task SendBatchAsync(IEnumerable<TransportMessage> messages, CancellationToken cancellationToken = default)
//    {
//        throw new NotImplementedException();
//    }

//    public Task StartListeningAsync(string queueName, string consumerId, Func<TransportMessage, CancellationToken, Task> handler, CancellationToken cancellationToken = default)
//    {
//        throw new NotImplementedException();
//    }

//    public Task StopListeningAsync(CancellationToken cancellationToken = default)
//    {
//        throw new NotImplementedException();
//    }

//    public void Dispose()
//    {
//        throw new NotImplementedException();
//    }
//}
