using Xunit;

// Avalonia 的 headless 测试集成会为每个测试建隔离会话，而建会话时要 new Compositor，
// 后者要求"当前线程拥有 Dispatcher"（DefaultRenderLoop.Add → Dispatcher.VerifyAccess）。
// xUnit 并行会把测试派到不同 worker 线程，于是偶发抛
// InvalidOperationException: The calling thread cannot access this object because a different thread owns it.
// 表现为 "Test Case Cleanup Failure"，且会在用 [AvaloniaFact] 的类之间随机游走
// （实测把那几个类并到同一个 [Collection] 无效，因为问题在线程归属而不是类之间的顺序）。
// 整个测试程序集跑在单线程上才能成立，因此这里关掉并行。
[assembly: CollectionBehavior(MaxParallelThreads = 1)]
