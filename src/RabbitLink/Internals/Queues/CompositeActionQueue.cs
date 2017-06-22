﻿#region Usings

using System;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace RabbitLink.Internals.Queues
{
    internal class CompositeActionQueue<TActor> : CompositeWorkQueue<ActionQueueItem<TActor>>, IActionQueue<TActor>
    {
        #region IActionQueue<TActor> Members

        public async Task<T> PutAsync<T>(Func<TActor, T> action, CancellationToken cancellation)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            var item = new ActionQueueItem<TActor>(actor => action(actor), cancellation);

            Put(item);

            return (T) await item
                .Completion
                .ConfigureAwait(false);
        }

        public Task PutAsync(Action<TActor> action, CancellationToken cancellation)
        {
            return PutAsync<object>(actor =>
            {
                action(actor);
                return null;
            }, cancellation);
        }

        #endregion
    }
}