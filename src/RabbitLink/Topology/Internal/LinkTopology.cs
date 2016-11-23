#region Usings

using System;
using System.Threading;
using System.Threading.Tasks;
using RabbitLink.Configuration;
using RabbitLink.Connection;
using RabbitLink.Helpers;
using RabbitLink.Internals;
using RabbitLink.Logging;

#endregion

namespace RabbitLink.Topology.Internal
{
    internal class LinkTopology : ILinkTopology
    {
        #region .ctor

        public LinkTopology(LinkConfiguration configuration, ILinkChannel channel, ILinkTopologyHandler handler,
            bool once)
        {
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));

            if (channel == null)
                throw new ArgumentNullException(nameof(channel));

            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            _configuration = configuration;
            _logger = _configuration.LoggerFactory.CreateLogger($"{GetType().Name}({Id:D})");

            if (_logger == null)
                throw new ArgumentException("Cannot create logger", nameof(configuration.LoggerFactory));

            _handler = handler;
            _isOnce = once;

            _disposedCancellationSource = new CancellationTokenSource();
            _disposedCancellation = _disposedCancellationSource.Token;

            Channel = channel;
            Channel.Disposed += ChannelOnDisposed;
            Channel.Ready += ChannelOnReady;

            _logger.Debug($"Created(channelId: {Channel.Id}, once: {once})");

            ScheduleConfiguration(false);
        }

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

        #endregion

        #region Schedule configuration

        public void ScheduleConfiguration(bool delay)
        {
            if (!Channel.IsOpen || _disposedCancellation.IsCancellationRequested)
            {
                return;
            }

            if (_isOnce && Configured)
                return;

            Configured = false;

            try
            {
                _eventLoop.ScheduleAsync(async () =>
                {
                    if (!Channel.IsOpen || _disposedCancellation.IsCancellationRequested)
                    {
                        return;
                    }

                    if (_isOnce && Configured)
                        return;

                    if (delay)
                    {
                        _logger.Info($"Retrying in {_configuration.TopologyRecoveryInterval.TotalSeconds:0.###}s");

                        await Task.Delay(_configuration.TopologyRecoveryInterval, _disposedCancellation)
                            .ConfigureAwait(false);
                    }

                    await ConfigureAsync()
                           .ConfigureAwait(false);
                }, _disposedCancellation)
                    .ConfigureAwait(false);
            }
            catch
            {
                // no op
            }
        }

        #endregion

        #region IDisposable implementation

        public void Dispose()
        {
            Dispose(false);
        }

        private void Dispose(bool onChannelDisposed)
        {
            if (_disposedCancellationSource.IsCancellationRequested) return;

            lock (_disposedCancellationSource)
            {
                if (_disposedCancellationSource.IsCancellationRequested) return;

                _disposedCancellationSource.Cancel();
                _disposedCancellationSource.Dispose();
            }

            _logger.Debug("Disposing");
            _eventLoop.Dispose();

            Channel.Ready -= ChannelOnReady;
            Channel.Disposed -= ChannelOnDisposed;

            if (!onChannelDisposed)
            {
                Channel.Dispose();
            }

            _logger.Debug("Disposed");
            _logger.Dispose();

            DisposedPriv?.Invoke(this, EventArgs.Empty);
        }

        #endregion

        #region Configure

        private async Task ConfigureAsync()
        {
            if (_disposedCancellation.IsCancellationRequested)
                return;

            if (!Channel.IsOpen)
                return;

            if (Configured && _isOnce)
                return;

            _logger.Info("Configuring topology");
            try
            {
                await _handler.Configure(new LinkTopologyConfig(_logger, Channel))
                    .ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.Warning("Exception on configuration: {0}", ex);
                try
                {
                    await _handler.ConfigurationError(ex)
                        .ConfigureAwait(false);
                }
                catch (Exception handlerException)
                {
                    _logger.Error("Error in error handler: {0}", handlerException);
                }

                ScheduleConfiguration(true);
                return;
            }

            Configured = true;

            try
            {
                await _handler.Ready()
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Error("Error in ready handler: {0}", ex);
            }

            _logger.Info("Topology configured");

            if (_isOnce)
            {
                _logger.Info("Once topology configured, disposing");
#pragma warning disable 4014
                AsyncHelper.RunAsync(Dispose);
#pragma warning restore 4014
            }
        }

        #endregion

        #region Fields

        private readonly CancellationTokenSource _disposedCancellationSource;
        private readonly CancellationToken _disposedCancellation;
        private readonly EventLoop _eventLoop = new EventLoop(EventLoop.DisposingStrategy.Wait);
        private readonly ILinkTopologyHandler _handler;
        private readonly bool _isOnce;
        private readonly ILinkLogger _logger;
        private readonly LinkConfiguration _configuration;

        #endregion

        #region Properties

        public Guid Id { get; } = Guid.NewGuid();
        public bool Configured { get; private set; }
        public ILinkChannel Channel { get; }

        #endregion

        #region Channel Event Handlers

        private void ChannelOnReady(object sender, EventArgs eventArgs)
        {
            ScheduleConfiguration(false);
        }

        private void ChannelOnDisposed(object sender, EventArgs eventArgs)
        {
            _logger.Debug("Channel disposed, disposing...");
            Dispose(true);
        }

        #endregion
    }
}