#region Usings

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RabbitLink.Configuration;
using RabbitLink.Exceptions;
using RabbitLink.Helpers;
using RabbitLink.Internals;
using RabbitLink.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

#endregion

namespace RabbitLink.Connection
{
    internal class LinkConnection : ILinkConnection
    {
        #region .ctor

        public LinkConnection(LinkConfiguration configuration)
        {
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));

            _configuration = configuration;
            _logger = _configuration.LoggerFactory.CreateLogger($"{GetType().Name}({Id:D})");

            if (_logger == null)
                throw new ArgumentException("Cannot create logger", nameof(configuration.LoggerFactory));

            _connectionFactory = new LinkConnectionFactory(
                _configuration.ConnectionString,
                _configuration.LoggerFactory,
                _configuration.ConnectionTimeout
                );

            _disposedCancellationSource = new CancellationTokenSource();
            _disposedCancellation = _disposedCancellationSource.Token;

            _logger.Debug($"Created(ConnectionFactoryId: {_connectionFactory.Id})");
            if (_configuration.AutoStart)
            {
                InitializeAsync();
            }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposedCancellation.IsCancellationRequested)
                return;

            lock (_disposedCancellationSource)
            {
                if (_disposedCancellation.IsCancellationRequested)
                    return;

                _disposedCancellationSource.Cancel();
                _disposedCancellationSource.Dispose();
            }

            _logger.Debug("Disposing");
            _eventLoop.Dispose();

            Cleanup();

            _logger.Debug("Disposed");
            _logger.Dispose();

            DisposedPriv?.Invoke(this, EventArgs.Empty);
        }

        #endregion

        #region Fields

        private readonly EventLoop _eventLoop = new EventLoop(EventLoop.DisposingStrategy.Wait);
        private readonly CancellationTokenSource _disposedCancellationSource;
        private readonly CancellationToken _disposedCancellation;
        private readonly LinkConnectionFactory _connectionFactory;
        private readonly LinkConfiguration _configuration;

        private readonly ILinkLogger _logger;
        private IConnection _connection;

        #endregion

        #region Events        

        private event EventHandler DisposedPriv;
        public event EventHandler Disposed
        {
            add
            {
                if (value == null)
                    return;

                if (_disposedCancellation.IsCancellationRequested)
                {
                    value(this, EventArgs.Empty);
                    return;
                }

                lock (_disposedCancellationSource)
                {

                    if (_disposedCancellation.IsCancellationRequested)
                    {
                        value(this, EventArgs.Empty);
                        return;
                    }

                    DisposedPriv += value;
                }
            }

            remove { DisposedPriv -= value; }
        }


        public event EventHandler Connected;
        public event EventHandler<LinkDisconnectedEventArgs> Disconnected;

        #endregion

        #region Properties        

        public Guid Id { get; } = Guid.NewGuid();

        public bool IsConnected => !_disposedCancellation.IsCancellationRequested &&
                                   Initialized &&
                                   _connection?.IsOpen == true;

        public bool Initialized { get; private set; }

        public string ConnectionString => _connectionFactory.Url;
        public string UserId => _connectionFactory.UserName;

        #endregion

        #region Connection management

        private async Task ConnectAsync()
        {
            // if already connected or cancelled
            if (IsConnected || _disposedCancellation.IsCancellationRequested)
                return;


            // Second check
            if (IsConnected || _disposedCancellation.IsCancellationRequested)
                return;

            _logger.Info("Connecting");

            // Cleaning old connection
            await AsyncHelper.AsyncAwaitable(Cleanup);

            // Last chance to cancel
            if (_disposedCancellation.IsCancellationRequested)
                return;

            try
            {
                _logger.Debug("Opening");

                _connection = await _connectionFactory.GetConnectionAsync(_disposedCancellation)
                    .ConfigureAwait(false);

                _connection.ConnectionShutdown += ConnectionOnConnectionShutdown;
                _connection.CallbackException += ConnectionOnCallbackException;
                _connection.ConnectionBlocked += ConnectionOnConnectionBlocked;
                _connection.ConnectionUnblocked += ConnectionOnConnectionUnblocked;

                _logger.Debug("Sucessfully opened");
            }
            catch (Exception ex)
            {
                _logger.Error($"Cannot connect: {ex.Message}");
                ScheduleReconnect(true);
                return;
            }


            await AsyncHelper.AsyncAwaitable(() => Connected?.Invoke(this, EventArgs.Empty));

            _logger.Info(
                $"Connected (Host: {_connection.Endpoint.HostName}, Port: {_connection.Endpoint.Port}, LocalPort: {_connection.LocalPort})");
        }

        private void ScheduleReconnect(bool wait)
        {
            if (IsConnected || _disposedCancellation.IsCancellationRequested)
                return;

            try
            {
                _eventLoop.ScheduleAsync(async () =>
                {
                    if (IsConnected || _disposedCancellation.IsCancellationRequested)
                        return;

                    if (wait)
                    {
                        _logger.Info($"Reconnecting in {_configuration.ConnectionRecoveryInterval.TotalSeconds:0.###}s");
                        await Task.Delay(_configuration.ConnectionRecoveryInterval, _disposedCancellation)
                            .ConfigureAwait(false);
                    }

                    await ConnectAsync()
                        .ConfigureAwait(false);
                }, _disposedCancellation);
            }
            catch
            {
                // no op
            }
        }

        private void Cleanup()
        {
            try
            {
                _connection?.Dispose();
            }
            catch (IOException)
            {
            }
            catch (Exception ex)
            {
                _logger.Warning("Cleaning exception: {0}", ex);
            }
        }

        private Task OnDisconnectedAsync(ShutdownEventArgs e)
        {
            _logger.Debug("Invoking Disconnected event");

            LinkDisconnectedInitiator initiator;

            switch (e.Initiator)
            {
                case ShutdownInitiator.Application:
                    initiator = LinkDisconnectedInitiator.Application;
                    break;
                case ShutdownInitiator.Library:
                    initiator = LinkDisconnectedInitiator.Library;
                    break;
                case ShutdownInitiator.Peer:
                    initiator = LinkDisconnectedInitiator.Peer;
                    break;
                default:
                    initiator = LinkDisconnectedInitiator.Library;
                    break;
            }

            var eventArgs = new LinkDisconnectedEventArgs(initiator, e.ReplyCode, e.ReplyText);

            return AsyncHelper.RunAsync(() =>
            {
                Disconnected?.Invoke(this, eventArgs);
                _logger.Debug("Disconnected event sucessfully invoked");
            });
        }

        #endregion

        #region Public methods

        public Task InitializeAsync()
        {
            if (_disposedCancellation.IsCancellationRequested)
                throw new ObjectDisposedException(GetType().Name);

            if (Initialized)
                return Task.CompletedTask;

            return _eventLoop.ScheduleAsync(DoInit, _disposedCancellation);
        }

        private Task DoInit()
        {
            if (Initialized)
                return Task.CompletedTask;

            _logger.Debug("Initializing");
            Initialized = true;

            ScheduleReconnect(false);
            _logger.Debug("Initialized");

            return Task.CompletedTask;
        }

        public Task<IModel> CreateModelAsync(CancellationToken cancellation)
        {
            if (_disposedCancellation.IsCancellationRequested)
                throw new ObjectDisposedException(GetType().Name);

            if (!IsConnected)
                throw new LinkNotConnectedException();

            return _eventLoop.ScheduleAsync(GetModel, cancellation);
        }

        private Task<IModel> GetModel()
        {
            if (!IsConnected)
                throw new LinkNotConnectedException();

            return AsyncHelper.RunAsync(_connection.CreateModel);
        }

        #endregion

        #region Connection event handlers

        private void ConnectionOnConnectionUnblocked(object sender, EventArgs e)
        {
            _eventLoop.ScheduleAsync(() =>
            {
                _logger.Debug("Unblocked");
                return Task.CompletedTask;
            }, CancellationToken.None);

        }

        private void ConnectionOnConnectionBlocked(object sender, ConnectionBlockedEventArgs e)
        {
            _eventLoop.ScheduleAsync(() =>
            {
                _logger.Debug($"Blocked, reason: {e.Reason}");
                return Task.CompletedTask;
            }, CancellationToken.None);
        }

        private void ConnectionOnCallbackException(object sender, CallbackExceptionEventArgs e)
        {
            _eventLoop.ScheduleAsync(() =>
            {
                _logger.Error($"Callback exception: {e.Exception}");
                return Task.CompletedTask;
            }, CancellationToken.None);
        }

        private void ConnectionOnConnectionShutdown(object sender, ShutdownEventArgs e)
        {
            _eventLoop.ScheduleAsync(async () =>
            {
                _logger.Info($"Diconnected, Initiator: {e.Initiator}, Code: {e.ReplyCode}, Message: {e.ReplyText}");
                await OnDisconnectedAsync(e)
                    .ConfigureAwait(false);

                // if initialized by application, exit
                if (e.Initiator != ShutdownInitiator.Application)
                {
                    ScheduleReconnect(true);
                }
            }, CancellationToken.None);
        }

        #endregion
    }
}