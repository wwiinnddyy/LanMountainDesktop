using Xunit;

// Avalonia 的 headless 测试集成会为每个测试建隔离会话，而建会话时要 new Compositor，
// 后者要求"当前线程拥有 Dispatcher"（DefaultRenderLoop.Add → Dispatcher.VerifyAccess）。
// xUnit 会把测试派到不同 worker 线程，于是偶发抛
// InvalidOperationException: The calling thread cannot access this object because a different thread owns it.
// 表现为 "Test Case Cleanup Failure"（测试体本身是过的），且会在用 [AvaloniaFact] 的类之间随机游走。
//
// 已实测无效的两条路，别再重复试：
// - CollectionBehavior(DisableTestParallelization = true)：仍偶发（同一线程归属问题不是并行度问题）。
// - [assembly: AvaloniaTestIsolation(PerAssembly)]：反而稳定挂 24 条
//   （SplashWindowLifecycle / LauncherBackgroundService / VisualTestAppHarness 等要新建自己的
//   Application，共用一个实例会互相污染）。
// 剩下的办法是给 CI 的重跑兜底（见 .github/workflows/code-quality.yml 的 Test 步骤）。
// 第三条也试过并无效：CollectionBehavior(ParallelAlgorithm = ...) 这个枚举在 xunit.v3 3.2.2
// 的公开表面里取不到（CS0234），没法用"保守复用线程"的算法换掉线程漂移。
[assembly: CollectionBehavior(MaxParallelThreads = 1)]
