namespace ServiceDefault.Messaging
{
    /// <summary>
    /// RabbitMQ topology shared by all services:
    ///   exchange 'cityfy.events' (topic)  — every message, routing key = message type name
    ///   queue    '{consumer}'             — bound to the routing key of its message type
    ///   queue    '{consumer}.retry.{n}'   — waits RetryDelays[n-1], then dead-letters back to '{consumer}'
    ///   exchange 'cityfy.dlx' (direct)    — dead letters, routing key = consumer queue name
    ///   queue    '{consumer}.dlq'         — messages that failed every attempt
    /// </summary>
    public static class MessagingTopology
    {
        public const string EventsExchange = "cityfy.events";
        public const string DeadLetterExchange = "cityfy.dlx";

        /// <summary>Header carrying the number of retries already done.</summary>
        public const string AttemptHeader = "x-cityfy-retry";

        /// <summary>Delay before each retry: 3 retries, then the message goes to the DLQ.</summary>
        public static readonly IReadOnlyList<TimeSpan> RetryDelays =
        [
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMinutes(2)
        ];

        public static string RoutingKey<T>() => typeof(T).Name;

        public static string RetryQueue(string queue, int retry) => $"{queue}.retry.{retry}";

        public static string DeadLetterQueue(string queue) => $"{queue}.dlq";
    }
}
