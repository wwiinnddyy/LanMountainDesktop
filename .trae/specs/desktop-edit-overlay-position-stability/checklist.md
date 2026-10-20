# 验收

- [x] 真实渲染对象能复现旧实现的位置归零（新增三例均在期望 120,180、实际 0,0 处失败；原有四例通过）。
- [x] 重复更新预览及候选格子保持正确位置。
- [x] 移动更新在下一帧前保留已有布局位置，下一帧到达目标位置。
- [x] 桌面编辑、网格吸附相关测试通过（55 项，含新增三项真实 CompositionVisual 回归用例）。
- [x] 桌面宿主构建通过（Debug，由下列测试命令重新构建）。

验证命令：

```powershell
dotnet test tests/LanMountainDesktop.Tests/LanMountainDesktop.Tests.csproj -c Debug --no-restore --filter 'FullyQualifiedName~DesktopEdit|FullyQualifiedName~DesktopPlacementMathTests|FullyQualifiedName~DesktopGridLayoutServiceTests' --logger 'console;verbosity=minimal'
```
