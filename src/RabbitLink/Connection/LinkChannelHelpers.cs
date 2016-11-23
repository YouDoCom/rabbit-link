#region Usings

using System;
using System.Threading;
using System.Threading.Tasks;
using RabbitMQ.Client;

#endregion

namespace RabbitLink.Connection
{
    internal static class LinkChannelHelpers
    {
        public static Task InvokeActionAsync(this ILinkChannel @this, Action<IModel> action)
        {
            return @this.InvokeActionAsync(action, CancellationToken.None);
        }
    }
}