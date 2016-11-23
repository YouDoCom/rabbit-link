#region Usings

using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

#endregion

namespace RabbitLink.Helpers
{
    internal static class AsyncHelper
    {
        public static ConfiguredTaskAwaitable AsyncAwaitable(Action action)
        {
            return RunAsync(action)
                .ConfigureAwait(false);
        }

        public static ConfiguredTaskAwaitable<T> AsyncAwaitable<T>(Func<T> action)
        {
            return RunAsync(action)
                .ConfigureAwait(false);
        }

        public static Task RunAsync(Action action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            return Task.Factory.StartNew(
                action,
                TaskCreationOptions.LongRunning | TaskCreationOptions.RunContinuationsAsynchronously
            );


        }
        public static Task<T> RunAsync<T>(Func<T> action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            return Task.Factory.StartNew(
                action,
                TaskCreationOptions.LongRunning | TaskCreationOptions.RunContinuationsAsynchronously
            );
        }
    }
}