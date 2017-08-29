﻿#region Usings

using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RabbitLink;
using RabbitLink.Messaging;
using RabbitLink.Topology;

#endregion

namespace Playground
{
    internal class LinkPlayground
    {
        public static void Run()
        {
            Console.WriteLine("--- Ready to run press enter ---");
            Console.ReadLine();

            var link = LinkBuilder.Configure
                .Uri("amqp://localhost/")
                .AutoStart(false)
                .LoggerFactory(new ConsoleLinkLoggerFactory())
                .ConnectionName($"LinkPlayground: {Process.GetCurrentProcess().Id}")
                .Build();

            using(var cts = new CancellationTokenSource())
            using (link)
            {
                var cancellation = cts.Token;
                var ct = Task.Factory.StartNew(() => TestConsumer(link, cancellation), TaskCreationOptions.LongRunning);                
                TestPublish(link);

                Console.WriteLine("--- Running ---");
                Console.ReadLine();
                cts.Cancel();
                ct.Wait();
            }
        }

        private static void TestConsumer(ILink link, CancellationToken cancellation)
        {
            var tcs = new TaskCompletionSource<object>();

            Console.WriteLine("--- Creating consumer ---");
            using (var consumer = link.Consumer
                .Queue(async cfg =>
                {
                    var exchange = await cfg.ExchangeDeclarePassive("link.consume");
                    var queue = await cfg.QueueDeclare("link.consume");

                    await cfg.Bind(queue, exchange);

                    return queue;
                })
                .AutoAck(false)
                .PrefetchCount(1000)
                .Handler(msg =>
                {
                    var data = Encoding.UTF8.GetString(msg.Body);

                    Console.WriteLine("---[ Message ]---\n{0}\n\n{1}\n---------", JsonConvert.SerializeObject(msg), data);

                    return tcs.Task;
                })
                .Build()
            )
            {
                cancellation.WaitHandle.WaitOne();
            }
        }

        private static void TestPublish(ILink link)
        {
            Console.WriteLine("--- Starting ---");
            link.Initialize();
            Console.WriteLine("--- Started ---");

            using (var producer = link.Producer
                .Exchange(cfg => cfg.ExchangeDeclare("link.consume", LinkExchangeType.Fanout))
                .MessageProperties(new LinkMessageProperties
                {
                    DeliveryMode = LinkDeliveryMode.Persistent
                })
                .PublishProperties(new LinkPublishProperties
                {
                    Mandatory = false
                })
                .Build()
            )
            {
                Console.WriteLine("--- Producer started, press [ENTER] ---");
                Console.ReadLine();

                Console.WriteLine("--- Publish ---");
                var sw = Stopwatch.StartNew();

                var tasks = Enumerable
                    .Range(0, 10)
                    .Select(i => $"Item {i + 1}")
                    .Select(x => Encoding.UTF8.GetBytes(x))
                    .Select(x => new LinkPublishMessage(x));

                foreach (var msg in tasks)
                {
                    producer.PublishAsync(msg)
                        .GetAwaiter().GetResult();
                }

                Console.WriteLine("--- Waiting for publish end ---");
                //Task.WaitAll(tasks);
                Console.WriteLine("--- Publish done ---");

                sw.Stop();
                Console.WriteLine("--> Done in {0:0.###}s", sw.Elapsed.TotalSeconds);
            }
        }

        private static void TestTopology(ILink link)
        {
            Console.WriteLine("--- Creating topology configurators ---");

            link.Topology
                .Handler(PersConfigure, () => Task.CompletedTask, PersOnException)
                .Build();

            Console.WriteLine("--- Starting ---");
            link.Initialize();

            Console.WriteLine("--- Configuring topology ---");
            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                {
                    link.Topology
                        .Handler(OnceConfigure)
                        .WaitAsync(cts.Token)
                        .GetAwaiter()
                        .GetResult();
                }

                Console.WriteLine("--- Topology configured ---");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"--> Topology config exception: {ex}");
            }
        }

        private static async Task OnceConfigure(ILinkTopologyConfig config)
        {
            var ex = await config.ExchangeDeclare("link.playground.once", LinkExchangeType.Fanout, autoDelete: true);
            var q = await config.QueueDeclareExclusiveByServer();

            await config.Bind(q, ex);
        }

        private static Task PersOnException(Exception exception)
        {
            Console.WriteLine("--> PersTopology exception: {0}", exception.ToString());
            return Task.FromResult((object) null);
        }

        private static async Task PersConfigure(ILinkTopologyConfig config)
        {
            await config.QueueDeclareExclusive();
        }
    }
}