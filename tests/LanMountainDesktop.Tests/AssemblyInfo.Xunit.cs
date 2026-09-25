using Xunit;

// Avalonia 的 headless 测试集成会为每个测试建隔离会话，建会话时要 new Compositor，
// 后者要往一条线程所有的 DefaultRenderLoop 上注册（栈：AvaloniaHeadlessPlatform.Initialize →
// new Compositor → DefaultRenderLoop.Add → Dispatcher.VerifyAccess），于是偶发
// InvalidOperationException: The calling thread cannot access this object because a different thread owns it.
//
// 2026-09-26 量过之后，此前写在这里的诊断要改正：**不是 xUnit 把测试派到不同线程**。
// 现成的测量是往本程序集临时加一个 20 例的 [AvaloniaTheory] 探针，每例记录 Environment.CurrentManagedThreadId：
// - 全量 1229 条那一趟：20 次会话体全落在同一条线程（distinct=1）；
// - 单独跑这个探针类（20 例）：5 趟里 4 趟有受害者，每趟 1~2 条；
// - 探针 + 一个已知受害类（31 例）：10 趟里 8 趟红。
// 也就是说会话线程是稳定的，而"多开同形会话"本身就是复现条件——所以
// DisableTestParallelization、MaxParallelThreads=1、ParallelAlgorithm 这三条按并行度下的药都不会命中病根
// （第三条在 xunit.v3 3.2.2 的公开表面里还取不到，CS0234）。
// 另一条被测量改正是标签：报的是受害者那条测试的 "Test Case Cleanup Failure"，但栈停在它**自己**建会话的
// EnsureIsolatedApplication——红的那趟里探针 20 例只落下 18~19 条记录，测试体根本没跑到，不是"过了之后收尾坏了"。
// 形状来源是 AvaloniaHeadlessPlatform 的 static Compositor 配上按线程存的 Dispatcher：一次收尾之后
// 下一条会话可能落在别的线程归属上。要修得动 Avalonia 的 headless 集成，不在本仓表面内。
//
// [assembly: AvaloniaTestIsolation(PerAssembly)] 也试过并反而稳定挂 24 条
// （SplashWindowLifecycle / LauncherBackgroundService / VisualTestAppHarness 等要新建自己的
// Application，共用一个实例会互相污染）。
// 所以现状：本地与 CI 都按"红一次就重跑一次"兜（见 .github/workflows/code-quality.yml 的 Test 步骤）。

[assembly: CollectionBehavior(MaxParallelThreads = 1)]
