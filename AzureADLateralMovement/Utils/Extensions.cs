using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace AzureAdLateralMovement.Utils
{
    public static class EnumerableExtensions
    {
        public static Task ForEachAsync<TSource>(
            this IEnumerable<TSource> items,
            Func<TSource, Task> action,
            int maxDegreesOfParallelism = 10)
        {
            var actionBlock = new ActionBlock<TSource>(action, new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = maxDegreesOfParallelism
            });

            foreach (var item in items) actionBlock.Post(item);

            actionBlock.Complete();

            return actionBlock.Completion;
        }

        public static void CreateOrUpdate<TKey, TValue>(
            this Dictionary<TKey, TValue> dictionary,
            TKey id,
            Func<TValue> addValueFactory,
            Func<TValue, TValue> updateValueFactory)
        {
            dictionary[id] = dictionary.ContainsKey(id) ? updateValueFactory(dictionary[id]) : addValueFactory();
        }
    }

    public static class DateTimeExtensions
    {
        public static bool IsNotOlderThan(this DateTime datetime, TimeSpan time)
        {
            return (datetime - DateTime.Now).TotalDays < time.TotalDays;
        }
    }
}