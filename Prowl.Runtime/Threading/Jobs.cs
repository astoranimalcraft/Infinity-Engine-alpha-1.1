// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Infinity.Runtime;

/// <summary>
/// Small helpers for data-parallel work. They use the .NET thread pool, so a job never creates a thread per item.
/// Keep engine object access on the main thread; use these for pure calculations over arrays, meshes or asset
/// metadata.
/// </summary>
public static class Jobs
{
    /// <summary>Worker threads available to the pool, leaving one core for the main thread.</summary>
    public static int WorkerCount => Math.Max(1, Environment.ProcessorCount - 1);

    /// <summary>Runs <paramref name="body"/> for every index in [from, to).</summary>
    public static void For(int from, int to, Action<int> body)
        => Parallel.For(from, to, new ParallelOptions { MaxDegreeOfParallelism = WorkerCount }, body);

    /// <summary>Runs <paramref name="body"/> for every item.</summary>
    public static void ForEach<T>(IReadOnlyList<T> items, Action<T> body)
        => Parallel.For(0, items.Count, new ParallelOptions { MaxDegreeOfParallelism = WorkerCount },
            index => body(items[index]));

    /// <summary>Runs work on the thread pool.</summary>
    public static Task Run(Action body) => Task.Run(body);

    /// <summary>Runs work on the thread pool and returns its result.</summary>
    public static Task<T> Run<T>(Func<T> body) => Task.Run(body);

    /// <summary>Waits for every task, rethrowing the first failure.</summary>
    public static void WaitAll(params Task[] tasks) => Task.WaitAll(tasks);
}
