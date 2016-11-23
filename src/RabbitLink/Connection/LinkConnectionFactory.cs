#region Usings

using System;
using System.Threading;
using System.Threading.Tasks;
using RabbitLink.Internals;
using RabbitLink.Logging;
using RabbitMQ.Client;

#endregion

namespace RabbitLink.Connection
{
    internal class LinkConnectionFactory : IDisposable
    {
        private readonly ConnectionFactory _connectionFactory;
        private readonly EventLoop _eventLoop = new EventLoop();
        private readonly ILinkLogger _logger;

        public LinkConnectionFactory(string url, ILinkLoggerFactory loggerFactory, TimeSpan connectionTimeout)
        {
            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentNullException(nameof(url));

            if (loggerFactory == null)
                throw new ArgumentNullException(nameof(loggerFactory));

            _logger = loggerFactory.CreateLogger($"{GetType().Name}({Id:D})");

            if (_logger == null)
                throw new ArgumentException("Cannot create logger", nameof(loggerFactory));

            Url = url;
            _connectionFactory = new ConnectionFactory
            {
                Uri = Url,
                TopologyRecoveryEnabled = false,
                AutomaticRecoveryEnabled = false,
                RequestedConnectionTimeout = (int) connectionTimeout.TotalMilliseconds
            };

            _logger.Debug("Created");
        }

        public Guid Id { get; } = Guid.NewGuid();

        public string UserName => _connectionFactory.UserName;
        public string Url { get; }

        public void Dispose()
        {
            _eventLoop.Dispose();
        }

        public Task<IConnection> GetConnectionAsync(CancellationToken cancellation)
        {
            return _eventLoop.ScheduleAsync(ConnectAsync, cancellation);
        }

        private Task<IConnection> ConnectAsync()
        {
            return Task.Factory.StartNew(
                () => _connectionFactory.CreateConnection(),
                TaskCreationOptions.LongRunning | TaskCreationOptions.RunContinuationsAsynchronously
            );
        }
    }
}