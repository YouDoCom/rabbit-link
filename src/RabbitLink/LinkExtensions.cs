﻿#region Usings

using System;
using System.Threading;
using System.Threading.Tasks;
using Nito.AsyncEx;
using RabbitLink.Topology;
using RabbitLink.Topology.Internal;

#endregion

namespace RabbitLink
{
    public static class LinkExtensions
    {
        #region Base

        public static IDisposable CreateTopologyConfigurator(this Link @this,
            Func<ILinkTopologyConfig, Task> configure, Func<Task> ready = null,
            Func<Exception, Task> configurationError = null)
        {
            if (ready == null)
            {
                ready = () => Task.FromResult((object)null);
            }

            if (configurationError == null)
            {
                configurationError = ex => Task.FromResult((object)null);
            }

            return @this.CreateTopologyConfigurator(new LinkActionsTopologyHandler(configure, ready, configurationError));
        }

        public static IDisposable CreatePersistentTopologyConfigurator(this Link @this,
            Func<ILinkTopologyConfig, Task> configure, Func<Task> ready = null,
            Func<Exception, Task> configurationError = null)
        {
            if (ready == null)
            {
                ready = () => Task.FromResult((object)null);
            }

            if (configurationError == null)
            {
                configurationError = ex => Task.FromResult((object)null);
            }

            return
                @this.CreatePersistentTopologyConfigurator(new LinkActionsTopologyHandler(configure, ready,
                    configurationError));
        }

        #endregion

        #region Async

        public static async Task ConfigureTopologyAsync(this Link @this,
            Func<ILinkTopologyConfig, Task> configure, CancellationToken cancellationToken)
        {            
            var completion = TaskCompletionSourceExtensions.CreateAsyncTaskSource<object>();
            if (cancellationToken.IsCancellationRequested)
            {
                completion.TrySetCanceled();
                await completion.Task;
                return;                
            }

            using (@this.CreateTopologyConfigurator(configure, () =>
            {
                completion.TrySetResult(null);
                return Task.FromResult((object) null);
            }, ex =>
            {
                completion.TrySetException(ex);
                return Task.FromResult((object) null);
            }))
            {
                IDisposable registration = null;
                try
                {
                    registration = cancellationToken.Register(() =>
                    {
                        completion.TrySetCanceled();                    
                    });
                }
                catch (ObjectDisposedException)
                {
                    // Cancellation source already disposed                  
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    completion.TrySetCanceled();
                }

                try
                {
                    await completion.Task
                        .ConfigureAwait(false);
                }
                finally
                {
                    registration?.Dispose();
                }
            }
        }

        public static async Task ConfigureTopologyAsync(this Link @this,
            Func<ILinkTopologyConfig, Task> configure, TimeSpan timeout)
        {
            using (var cts = new CancellationTokenSource(timeout))
            {
                await @this.ConfigureTopologyAsync(configure, cts.Token)
                    .ConfigureAwait(false);
            }
        }

        public static Task ConfigureTopologyAsync(this Link @this, Func<ILinkTopologyConfig, Task> configure)
        {
            return @this.ConfigureTopologyAsync(configure, CancellationToken.None);
        }

        #endregion     
    }
}