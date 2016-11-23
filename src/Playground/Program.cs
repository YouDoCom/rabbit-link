#region Usings

using System;
using RabbitMQ.Client;

#endregion

namespace Playground
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            LinkPlayground.Run();

            //Run();

            Console.WriteLine();
            Console.WriteLine("All done");

            Console.ReadLine();
        }

        private static void Run()
        {
            var factory = new ConnectionFactory
            {
                Uri = "amqp://localhost",
                TopologyRecoveryEnabled = false,
                AutomaticRecoveryEnabled = false
            };

            using (var conn = factory.CreateConnection())
            {

                conn.ConnectionShutdown += (sender, args) =>
                {
                    Console.WriteLine("[Conn] Closed: {0}", args.ReplyText);
                };

                using (var m = conn.CreateModel())
                {

                    m.ModelShutdown += (sender, args) =>
                    {
                        Console.WriteLine("[Model] Closed: {0}", args.ReplyText);
                    };

                    Console.WriteLine("Ready for woot");
                    Console.ReadLine();
                    Console.WriteLine("Woot");
                    try
                    {
                        m.ExchangeDeclarePassive("asdjdgasfdasdfasdfasdfasdfa");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Woot error: {0}", ex);
                    }
                    Console.WriteLine("Woot done");

                    Console.WriteLine();
                    Console.WriteLine("---done");
                    Console.ReadLine();
                }
            }
        }
    }
}