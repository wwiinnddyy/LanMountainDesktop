using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using LanMountainDesktop.Models;
using LanMountainDesktop.Platform.Windows;
using LanMountainDesktop.AirAppSdk;
using LanMountainDesktop.Services;
using LanMountainDesktop.Theme;

namespace LanMountainDesktop.Views;

public partial class MainWindow : Window
{
    private const int MinDesktopPageCount = 1;
    private const int MaxDesktopPageCount = 12;
    private enum LauncherEntryKind
    {
        Folder,
        Shortcut
    }

    private sealed record LauncherHiddenItemToken(LauncherEntryKind Kind, string Key);

    private sealed record LauncherHiddenItemView(
        LauncherEntryKind Kind,
        string Key,
        string DisplayName,
        string Monogram,
        Bitmap? IconBitmap);

    private readonly WindowsStartMenuService _windowsStartMenuService = new();
    private readonly LinuxDesktopEntryService _linuxDesktopEntryService = new();
    private readonly Dictionary<string, Bitmap> _launcherIconCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stack<StartMenuFolderNode> _launcherFolderStack = [];
    private readonly HashSet<string> _hiddenLauncherFolderPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _hiddenLauncherAppPaths = new(StringComparer.OrdinalIgnoreCase);
    private bool _showLauncherTileBackground = true;
    private Button? _selectedLauncherTileButton;
    private LauncherEntryKind? _selectedLauncherEntryKind;
    private string? _selectedLauncherEntryKey;
    private StartMenuFolderNode _startMenuRoot = new("All Apps", string.Empty);
    private byte[]? _launcherFolderIconPngBytes;
    private Bitmap? _launcherFolderIconBitmap;
    private int _desktopPageCount = MinDesktopPageCount;
    private int _currentDesktopSurfaceIndex;
    private double _desktopSurfacePageWidth;
    private TranslateTransform? _desktopPagesHostTransform;
    private Transitions? _desktopPagesHostSnapTransitions;
    private bool _desktopPagesHostTransitionsSuspended;
    private bool _isDesktopSwipeActive;
    private bool _isDesktopSwipeDirectionLocked;
    private Point _desktopSwipeStartPoint;
    private Point _desktopSwipeCurrentPoint;
    private Point _desktopSwipeLastPoint;
    private long _desktopSwipeLastTimestamp;
    private double _desktopSwipeVelocityX;
    private double _desktopSwipeBaseOffset;
    private int? _desktopSwipePointerId;
    private bool _desktopPageContextInitialized;
    private bool _desktopPageContextEditMode;
    private int _desktopPageContextActiveMask;
    private int? _desktopPageContextSettlingSourceIndex;
    private int? _desktopPageContextSettlingTargetIndex;
    private int _desktopPageContextSettleRevision;

    // 婵犵數鍋為崹鍫曞箰閹间絸鍥箥椤旂懓浜鹃柛顭戝亯婢规ɑ銇勯婊冨妤犵偛顑呴埞鎴﹀窗?闂傚倷绀侀幉锟犳偡閿旂晫绠惧┑鐘叉搐閺嬩焦銇勯幘鍗炵仼缂佺媭鍨堕弻鈥崇暤椤旂厧鏁俊銈呮噺閻撶喖鏌嶉崫鍕灓闁绘帡绠栭弻?
    private bool _isThreeFingerOrRightDragSwipeActive;
    private readonly HashSet<int> _activePointerIds = [];

    private int LauncherSurfaceIndex => Math.Max(MinDesktopPageCount, _desktopPageCount);

    private int TotalSurfaceCount => LauncherSurfaceIndex + 1;

    private void InitializeDesktopSurfaceState(DesktopLayoutSettingsSnapshot snapshot)
    {
        var loadedPageCount = snapshot.DesktopPageCount <= 0 ? MinDesktopPageCount : snapshot.DesktopPageCount;
        _desktopPageCount = Math.Clamp(loadedPageCount, MinDesktopPageCount, MaxDesktopPageCount);
        _currentDesktopSurfaceIndex = Math.Clamp(snapshot.CurrentDesktopSurfaceIndex, 0, LauncherSurfaceIndex);
    }

    private void InitializeLauncherVisibilitySettings(LauncherSettingsSnapshot snapshot)
    {
        _hiddenLauncherFolderPaths.Clear();
        if (snapshot.HiddenLauncherFolderPaths is not null)
        {
            foreach (var folderPath in snapshot.HiddenLauncherFolderPaths)
            {
                var key = NormalizeLauncherHiddenKey(folderPath);
                if (!string.IsNullOrWhiteSpace(key))
                {
                    _hiddenLauncherFolderPaths.Add(key);
                }
            }
        }

        _hiddenLauncherAppPaths.Clear();
        if (snapshot.HiddenLauncherAppPaths is not null)
        {
            foreach (var appPath in snapshot.HiddenLauncherAppPaths)
            {
                var key = NormalizeLauncherHiddenKey(appPath);
                if (!string.IsNullOrWhiteSpace(key))
                {
                    _hiddenLauncherAppPaths.Add(key);
                }
            }
        }

        _showLauncherTileBackground = snapshot.ShowTileBackground;
    }

    private void InitializeDesktopSurfaceSwipeHandlers()
    {
        // Capture swipe intent before child controls consume pointer events.
        AddHandler(PointerPressedEvent, OnDesktopPagesPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerMovedEvent, OnDesktopPagesPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnDesktopPagesPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerCaptureLostEvent, OnDesktopPagesPointerCaptureLost, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private async void LoadLauncherEntriesAsync()
    {
        try
        {
            var loadResult = await Task.Run(() =>
            {
                var loadedRoot = OperatingSystem.IsLinux()
                    ? _linuxDesktopEntryService.Load()
                    : _windowsStartMenuService.Load();
                var folderIconBytes = OperatingSystem.IsWindows()
                    ? WindowsIconService.TryGetSystemFolderIconPngBytes()
                    : null;
                return (Root: loadedRoot, FolderIcon: folderIconBytes);
            });
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _startMenuRoot = loadResult.Root;
                _launcherFolderIconPngBytes = loadResult.FolderIcon;
                _launcherFolderIconBitmap?.Dispose();
                _launcherFolderIconBitmap = null;
                RenderLauncherRootTiles();
                RenderLauncherHiddenItemsList();
            }, DispatcherPriority.Background);
        }
        catch
        {
            _startMenuRoot = new StartMenuFolderNode("All Apps", string.Empty);
            _launcherFolderIconPngBytes = null;
            _launcherFolderIconBitmap?.Dispose();
            _launcherFolderIconBitmap = null;
            RenderLauncherRootTiles();
            RenderLauncherHiddenItemsList();
        }
    }

    private void UpdateDesktopSurfaceLayout(DesktopGridMetrics gridMetrics)
    {
        if (DesktopPagesViewport is null ||
            DesktopPagesHost is null ||
            DesktopPagesContainer is null ||
            LauncherPagePanel is null)
        {
            return;
        }

        _desktopPagesHostTransform = DesktopPagesHost.RenderTransform as TranslateTransform;
        if (_desktopPagesHostTransform is null)
        {
            _desktopPagesHostTransform = new TranslateTransform();
            DesktopPagesHost.RenderTransform = _desktopPagesHostTransform;
        }

        if (_desktopPagesHostTransitionsSuspended)
        {
            _desktopPagesHostTransform.Transitions = null;
        }
        else
        {
            _desktopPagesHostSnapTransitions ??= _desktopPagesHostTransform.Transitions;
        }

        var viewportRow = gridMetrics.RowCount > 2 ? 1 : 0;
        var viewportRowSpan = gridMetrics.RowCount > 2 ? gridMetrics.RowCount - 2 : 1;
        var pageWidth = Math.Max(1, gridMetrics.GridWidthPx);
        var pageHeight = Math.Max(
            1,
            viewportRowSpan * gridMetrics.CellSize + Math.Max(0, viewportRowSpan - 1) * gridMetrics.GapPx);

        Grid.SetRow(DesktopPagesViewport, viewportRow);
        Grid.SetColumn(DesktopPagesViewport, 0);
        Grid.SetRowSpan(DesktopPagesViewport, viewportRowSpan);
        Grid.SetColumnSpan(DesktopPagesViewport, gridMetrics.ColumnCount);
        DesktopPagesViewport.Width = pageWidth;
        DesktopPagesViewport.Height = pageHeight;
        if (DesktopEditDragLayer is not null)
        {
            DesktopEditDragLayer.Width = pageWidth;
            DesktopEditDragLayer.Height = pageHeight;
            UpdateDesktopEditOverlayViewportSize();
        }

        DesktopPagesHost.RowDefinitions.Clear();
        DesktopPagesHost.RowDefinitions.Add(new RowDefinition(new GridLength(pageHeight, GridUnitType.Pixel)));
        DesktopPagesHost.ColumnDefinitions.Clear();
        DesktopPagesHost.ColumnDefinitions.Add(
            new ColumnDefinition(new GridLength(pageWidth * _desktopPageCount, GridUnitType.Pixel)));
        DesktopPagesHost.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(pageWidth, GridUnitType.Pixel)));
        DesktopPagesHost.Width = pageWidth * TotalSurfaceCount;
        DesktopPagesHost.Height = pageHeight;

        DesktopPagesContainer.RowDefinitions.Clear();
        DesktopPagesContainer.RowDefinitions.Add(new RowDefinition(new GridLength(pageHeight, GridUnitType.Pixel)));
        DesktopPagesContainer.ColumnDefinitions.Clear();
        ClearTimeZoneServiceBindings(DesktopPagesContainer.Children.OfType<Control>().ToList());
        DesktopPagesContainer.Children.Clear();
        DesktopPagesContainer.Width = pageWidth * _desktopPageCount;
        DesktopPagesContainer.Height = pageHeight;
        _desktopPageComponentGrids.Clear();
        InvalidateDesktopPageAwareComponentContextCache();
        for (var index = 0; index < _desktopPageCount; index++)
        {
            DesktopPagesContainer.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(pageWidth, GridUnitType.Pixel)));

            var pageGrid = new Grid
            {
                Width = pageWidth,
                Height = pageHeight,
                RowSpacing = gridMetrics.GapPx,
                ColumnSpacing = gridMetrics.GapPx,
                Background = Brushes.Transparent,
                ShowGridLines = false
            };

            for (var row = 0; row < viewportRowSpan; row++)
            {
                pageGrid.RowDefinitions.Add(new RowDefinition(new GridLength(gridMetrics.CellSize, GridUnitType.Pixel)));
            }

            for (var col = 0; col < gridMetrics.ColumnCount; col++)
            {
                pageGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(gridMetrics.CellSize, GridUnitType.Pixel)));
            }

            _desktopPageComponentGrids[index] = pageGrid;
            RestoreDesktopPageComponents(index);

            Grid.SetColumn(pageGrid, index);
            Grid.SetRow(pageGrid, 0);
            DesktopPagesContainer.Children.Add(pageGrid);
        }

        Grid.SetColumn(LauncherPagePanel, 1);
        Grid.SetRow(LauncherPagePanel, 0);

        var launcherMargin = Math.Clamp(gridMetrics.CellSize * 0.15, 6, 16);
        LauncherPagePanel.Margin = new Thickness(launcherMargin);
        LauncherPagePanel.Width = Math.Max(1, pageWidth - launcherMargin * 2);
        LauncherPagePanel.Height = Math.Max(1, pageHeight - launcherMargin * 2);
        LauncherPagePanel.MaxWidth = pageWidth - launcherMargin * 2;
        LauncherPagePanel.MaxHeight = pageHeight - launcherMargin * 2;

        // 闂傚倷绀侀幖顐⒚洪妶澶嬪仱闁靛ň鏅涢拑鐔封攽閻樺弶鎼愰悷娆欓檮閵囧嫰寮介妸銊ヮ棟閻炴氨鍠栧娲川婵犲嫭鍣┑鐘灪閿氶棁澶嬫叏濡炶浜鹃悗娈垮枙缁瑥鐣烽幆閭︽Ь濡炪倕绻戦幐鎶藉箖濮椻偓閹瑩鍩℃担宄邦棜
        UpdateLauncherTileLayout();

        _desktopSurfacePageWidth = pageWidth;
        ClampSurfaceIndex();
        ApplyDesktopSurfaceOffset();
    }

    private void UpdateLauncherTileLayout()
    {
        if (LauncherRootTilePanel is null || LauncherPagePanel is null)
        {
            return;
        }

        var availableWidth = Math.Max(1, LauncherPagePanel.Bounds.Width - 36); // 18px padding on each side
        var availableHeight = Math.Max(1, LauncherPagePanel.Bounds.Height - 100); // 婵犵妲呴崑鍛熆濡皷鍋撳鐓庢珝鐎殿喗濞婇崺鈧い鎺戝閻撴稓鈧箍鍎遍幊蹇涘窗濡眹浜滈柨婵嗘处濞呮洜绱掗鍊熷閻撱倖銇勮箛鎾村珔缂?

        if (availableWidth <= 1 || availableHeight <= 1)
        {
            // 婵犵數濮烽。浠嬪焵椤掆偓閸熷潡鍩€椤掆偓缂嶅﹪骞冨Ο璇茬窞閻忕偠鍋愰崜銊╂⒑閸涘﹦绠撻悗姘卞厴瀹曠敻鎮㈤悡搴ｉ獓闂佸啿鎼导鎺楀箣濠垫捁鈧寧銇勯幘璺盒ｅ┑顖氥偢閺屻劌鈽夊Ο渚紑闂佸搫妫崜鐔煎蓟閵娿儮妲堟俊顖欒濞堫厽绻濋悽闈涗粶婵炲樊鍙冮獮鍐╃鐎ｎ€晠鏌嶉崫鍕殭缂佹绻濋弻锝夋偐闁秵顎栭梺绋匡攻濞茬喖宕洪埀?            availableWidth = 600;
            availableHeight = 400;
        }

        // 闂備浇宕垫慨宕囨閵堝洦顫曢柡鍥ュ灪閸嬧晛鈹戦悩瀹犲閻庢艾顦甸弻宥堫檨闁告挻宀搁獮蹇涘川閺夋垹顦ㄩ梺鍛婄懃椤﹂亶銆呴銏♀拺闁告繂瀚瓭濠电偛鐪伴崐婵嗩嚕娴兼潙纾兼繝褎鍎虫禍?        // 闂傚倷鑳堕崕鐢稿疾閳哄懎绐楁俊銈呮噺閸嬪鏌ㄥ┑鍡╂Ч闁哄拋鍓氶幈銊ヮ潨閸℃绠诲┑鈥崇湴閸旀垿骞冪捄琛℃婵☆垳绮幏鍗炩攽閳藉棗鐏犳い锕佷含閸?-8婵犵數鍋為崹鍫曞箹閳哄倻顩叉繝濠傚幘閻熼偊娼ㄩ柍褜鍓欓锝嗙鐎ｎ亞鍊炴俊鐐差儏濞寸兘藝椤曗偓濮婃椽宕崟顓夈儲銇勯銏╂Ц闁伙絽鐏氶幏鍛姜閻楀牆濯伴梻濠庡亜濞诧箓骞愭ィ鍐炬晩閹兼番鍔嶉崐鐢电棯椤撶偞鍣烘い銉ヮ樀閹鎮烽幍顕嗙礊闂佺懓顨庨崑濠傜暦濮椻偓閸╋繝宕掑☉鍗炴櫔
        const int minColumns = 4;
        const int maxColumns = 8;
        const double targetAspectRatio = 1.2; // 闂傚倷鐒﹂幃鍫曞磿閹惰棄纾婚柟鍓х帛閸嬪鏌ㄥ┑鍡樼闁稿鎹囬弻鍛槈濮樿京鍘梻浣虹帛缁诲秹宕伴弽顒夋毎?
        var optimalColumnCount = Math.Clamp((int)Math.Floor(availableWidth / 120), minColumns, maxColumns);

        var tileWidth = Math.Floor(availableWidth / optimalColumnCount) - 12; // 12px spacing
        var tileHeight = Math.Min(tileWidth / targetAspectRatio, availableHeight / 4); // 闂傚倷鑳堕崢褔宕查弻銉ョ柈闁秆勵殕閸庡秵銇勯弽顐粶闁告瑥锕弻娑㈠箻濡炵偓顦风紒?闂?
        // 缂傚倷鑳堕搹搴ㄥ矗鎼淬劌绐楅柡鍥╁У瀹曞弶鎱ㄥΟ鎸庣【閻庢艾顦甸弻宥堫檨闁告挻绋掔粋宥咁潰瀹€鈧悿鈧梺瑙勫劤閻°劑锝為崨瀛樼厽?        tileWidth = Math.Max(tileWidth, 100);
        tileHeight = Math.Max(tileHeight, 80);

        // 闂傚倷绀侀幖顐⒚洪妶澶嬪仱闁靛ň鏅涢拑鐔封攽閸屻倖杈渁pPanel闂傚倷鐒﹂惇褰掑礉瀹€鍕惞婵帞妫渕闂備浇顕х换鎰崲閹版澘绠规い鎰跺瘜閺?        LauncherRootTilePanel.Width = availableWidth;

        // 闂傚倷绀侀幖顐⒚洪妶澶嬪仱闁靛ň鏅涢拑鐔封攽閻樺弶鎼愮紒鐘劦閺屽秷顧侀柛鎾跺枎椤曪綁宕归銏㈢獮婵犵數濮寸€氼參骞夐妶澶嬧拺缂佸娉曠粻浼存煕閻旂顥嬬紒顔肩墕閻ｆ繈宕熼鈧崜顓㈡⒑閸涘﹥澶勯柛瀣噹鍗遍柍褜鍓熼弻?
        foreach (var child in LauncherRootTilePanel.Children)
        {
            if (child is Button button)
            {
                button.Width = tileWidth;
                button.Height = tileHeight;
            }
        }

    }

    private void ClampSurfaceIndex()
    {
        _currentDesktopSurfaceIndex = Math.Clamp(_currentDesktopSurfaceIndex, 0, LauncherSurfaceIndex);
    }

    private IBrush GetThemeBrush(string key)
    {
        if (Resources.TryGetResource(key, ActualThemeVariant, out var resource) && resource is IBrush brush)
        {
            return brush;
        }

        return Brushes.Transparent;
    }

    private void ApplyDesktopSurfaceOffset()
    {
        if (_desktopPagesHostTransform is null || _desktopSurfacePageWidth <= 0)
        {
            return;
        }

        var targetOffset = -_currentDesktopSurfaceIndex * _desktopSurfacePageWidth;
        _desktopPagesHostTransform.X = targetOffset;

        if (_currentDesktopSurfaceIndex != LauncherSurfaceIndex)
        {
            CloseLauncherFolderOverlay();
            ClearSelectedLauncherTile(refreshTaskbar: false);
        }

        UpdateDesktopPageAwareComponentContext();
    }

    private void SetDesktopPagesHostSnapAnimationEnabled(bool enabled)
    {
        if (_desktopPagesHostTransform is null)
        {
            return;
        }

        if (enabled)
        {
            if (!_desktopPagesHostTransitionsSuspended)
            {
                return;
            }

            _desktopPagesHostTransform.Transitions = _desktopPagesHostSnapTransitions;
            _desktopPagesHostTransitionsSuspended = false;
            return;
        }

        if (_desktopPagesHostTransitionsSuspended)
        {
            return;
        }

        _desktopPagesHostSnapTransitions ??= _desktopPagesHostTransform.Transitions;
        _desktopPagesHostTransform.Transitions = null;
        _desktopPagesHostTransitionsSuspended = true;
    }

    private void ClearDesktopPageContextSettle(bool refreshContext)
    {
        _desktopPageContextSettleRevision++;
        _desktopPageContextSettlingSourceIndex = null;
        _desktopPageContextSettlingTargetIndex = null;

        if (refreshContext)
        {
            UpdateDesktopPageAwareComponentContext();
        }
    }

    private void BeginDesktopPageContextSettle(int previousIndex, int targetIndex)
    {
        var sourceIndex = previousIndex >= 0 && previousIndex < _desktopPageCount
            ? previousIndex
            : (int?)null;
        var destinationIndex = targetIndex >= 0 && targetIndex < _desktopPageCount
            ? targetIndex
            : (int?)null;

        if (sourceIndex == destinationIndex && destinationIndex is not null)
        {
            ClearDesktopPageContextSettle(refreshContext: false);
            return;
        }

        if (sourceIndex is null && destinationIndex is null)
        {
            ClearDesktopPageContextSettle(refreshContext: false);
            return;
        }

        _desktopPageContextSettleRevision++;
        var settleRevision = _desktopPageContextSettleRevision;
        _desktopPageContextSettlingSourceIndex = sourceIndex;
        _desktopPageContextSettlingTargetIndex = destinationIndex;

        DispatcherTimer.RunOnce(
            () =>
            {
                if (settleRevision != _desktopPageContextSettleRevision)
                {
                    return;
                }

                _desktopPageContextSettlingSourceIndex = null;
                _desktopPageContextSettlingTargetIndex = null;
                UpdateDesktopPageAwareComponentContext();
            },
            FluttermotionToken.Page + TimeSpan.FromMilliseconds(36));
    }

    private void MoveSurfaceBy(int delta)
    {
        if (delta == 0)
        {
            return;
        }

        MoveSurfaceTo(_currentDesktopSurfaceIndex + delta);
    }

    private void MoveSurfaceTo(int targetIndex)
    {
        var target = Math.Clamp(targetIndex, 0, LauncherSurfaceIndex);
        if (target == _currentDesktopSurfaceIndex)
        {
            ApplyDesktopSurfaceOffset();
            return;
        }

        var previousIndex = _currentDesktopSurfaceIndex;
        _currentDesktopSurfaceIndex = target;
        BeginDesktopPageContextSettle(previousIndex, target);
        ApplyDesktopSurfaceOffset();
        SchedulePersistSettings(delayMs: Math.Max(280, (int)FluttermotionToken.Page.TotalMilliseconds + 80));
    }

    private bool CanSwipeDesktopSurface()
    {
        return !_isSettingsOpen &&
               !_isComponentLibraryOpen &&
               !HasActiveDesktopEditSession &&
               _desktopSurfacePageWidth > 1;
    }

    private void OnDesktopPagesPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!TryGetPointerPositionInDesktopViewport(e, out var pointerInViewport))
        {
            return;
        }

        // 婵犵數濮烽。浠嬪焵椤掆偓閸熷潡鍩€椤掆偓缂嶅﹪骞冨Ο璇茬窞闁归偊鍓氬畵宥夋⒑闂堟丹娑㈠川椤栨粌甯掓繝鐢靛仜椤曨厽鎱ㄧ€涙ɑ娅犻幖杈剧稻椤洘銇勮箛鎾村櫤缂傚秴娲弻鐔衡偓鐢告櫜鏉╃懓霉閿濆懎顥忛柛銈嗘礋閻擃偊宕惰閹癸綁鏌ｉ悢鍛婂磳闁哄矉缍侀獮鍥敊閽樺鐣梻浣规偠閸娿倝宕板鍗炲灊婵鍩栭幆鐐烘偡濞嗗繐顏村ù鐘讳憾濮婃椽宕ㄦ繝鍕吂闂佸湱鈷堥崑濠囧箖閳ユ枼鏋庨柟鎯х摠濞呮牠鏌ｈ箛鏇炰哗婵☆偄瀚濠囧箰鎼达絿顔曢梺鐟扮摠缁诲嫭鏅堕敃鍌涚厓鐟滄粓宕滃▎鎾嶅洭顢氶埀顒勫箠濞嗘挸绠ｉ柨鏃囧Г濞呮牠姊洪崜鎻掍簴闁搞劌顭烽幆宀€鈧綆鈧垹缍婇幃鈺呭传閸曨厼甯块梻浣规偠閸斿﹪宕濋幋婵堟殾闁靛鏅╅弫宥嗘叏濮楀棗鍔俊銈呮噺閻撴洘绻涢崱妯哄缂佽泛寮剁换娑氣偓娑欙公閼拌法鈧鍠曠划娆忕暦閼告妲归幖杈剧秵濡?
        if (_isComponentLibraryOpen &&
            (_selectedDesktopComponentHost is not null || _selectedLauncherTileButton is not null))
        {
            if (!IsInteractivePointerSource(e.Source))
            {
                ClearDesktopComponentSelection();
                ClearSelectedLauncherTile(refreshTaskbar: false);
                ApplyTaskbarActionVisibility(GetCurrentTaskbarContext());
            }
        }

        if (!CanSwipeDesktopSurface())
        {
            return;
        }

        var appSnapshot = _settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(AirAppSettingsScope.App);
        var isThreeFingerSwipeEnabled = appSnapshot.EnableThreeFingerSwipe;

        var currentPoint = e.GetCurrentPoint(DesktopPagesViewport);
        var pointerId = e.Pointer?.Id ?? 0;
        var isRightButtonPressed = currentPoint.Properties.IsRightButtonPressed;
        var isLeftButtonPressed = currentPoint.Properties.IsLeftButtonPressed;

        // 婵犵數濮伴崹鐓庘枖濞戞埃鍋撳鐓庢珝妤犵偛鍟换婵嬪礃椤忎焦鐏冨┑鐘灱濞夋盯顢栭崨瀛樺剨閻熸瑥瀚弧鈧繝鐢靛Т閸燁偊鎮橀妷銉㈡斀?闂傚倷绀侀幉锟犳偡閿旂晫绠惧┑鐘叉搐閺嬩焦銇勯幘鍗炵仼缂佺媭鍨堕弻鈥崇暤椤旂厧鏁俊銈勬缁诲棙銇勯弽銊ｄ粶闁稿鎸搁悾鐑藉炊閳哄﹥鏁?
        if (isThreeFingerSwipeEnabled)
        {
            if (isLeftButtonPressed || isRightButtonPressed)
            {
                _activePointerIds.Add(pointerId);
            }

            var isThreeFinger = _activePointerIds.Count >= 3;
            var isRightDrag = isRightButtonPressed;

            if (isThreeFinger || isRightDrag)
            {
                ClearDesktopPageContextSettle(refreshContext: false);
                // 婵犵數鍋為崹鍫曞箰閹间絸鍥箥椤旂懓浜?闂傚倷绀侀幉锟犳偡閿旂晫绠惧┑鐘叉搐閺嬩焦銇勯幘鍗炵仼缂佺媭鍨堕弻鈥崇暤椤旂厧鏁俊銈勬缁诲棙銇勯弽銊ｄ粶闁稿鎸搁悾鐑藉炊閳哄﹥鏁ら梻鍌欑劍鐎笛呯矙閹烘挾鈹嶆繛宸簼閸婂鏌ㄩ弮鍥撳ù婧垮€濋弻娑㈠Ψ閿濆懎顬堝銈忕稻閻擄繝寮婚敓鐘查唶婵犲灚鍔栨缂傚倷绶￠崰鏍矓閻㈢數鐭夐柟鐑橆殔鐎氬鏌涢…鎴濅簻闁衡偓椤撶喓绠鹃悗娑欘焽閻鎮介娑辨疁閽樼喖鏌涘☉娆愮稇闁藉啰鍠栭弻鏇熷緞濡櫣浠紓浣插亾濠㈣埖鍔栭悡鐔兼煃鏉炴媽鍏岄柟鐣屽█閹粙顢涘☉娆戠▏濡炪倖娲╃紞渚€宕洪埀顒併亜閹哄秶鍔嶉柛娆忕箻閹鏁愭惔鈥茬敖闂佽鐏氶崝鎴﹀蓟?                ClearDesktopPageContextSettle(refreshContext: false);
                _isThreeFingerOrRightDragSwipeActive = true;
                _isDesktopSwipeActive = true;
                _isDesktopSwipeDirectionLocked = false;
                _desktopSwipeStartPoint = pointerInViewport;
                _desktopSwipeCurrentPoint = _desktopSwipeStartPoint;
                _desktopSwipeLastPoint = _desktopSwipeStartPoint;
                _desktopSwipeVelocityX = 0;
                _desktopSwipeLastTimestamp = Stopwatch.GetTimestamp();
                _desktopSwipeBaseOffset = -_currentDesktopSurfaceIndex * _desktopSurfacePageWidth;
                _desktopSwipePointerId = pointerId;
                e.Handled = true;
                
                // 闂傚倷绀侀幖顐ょ矓閺夋嚚娲煛閸滀焦鏅╅梺鎼炲劘閸斿酣銆呴弻銉﹀€甸柨婵嗗€瑰▍鍡樸亜閹邦喗娅曢柍褜鍓涢幊鎾诲箟闄囬妵鎰板礃椤斻垹娲崺锟犲川椤旈棿鍝楅梻浣虹《濡插懘宕㈤崜褏鐭嗗鑸靛姈閳锋帡鏌涢幇鈺佸缂佺嫏鍕╀簻闁圭儤鎸鹃妴鎺旂磼鏉堛劌娴€规洜鍠栭、鏃堝椽娴ｉ晲缂撻梻鍌欑閹诧紕鎹㈤崒婊呯煋閻庡灚鐡曟慨?                e.Handled = true;
                return;
            }
        }

        // 闂傚倷绀侀幉锟犫€﹂崶顒€绐楅柟閭﹀墾閼板灝銆掑锝呬壕閻庤娲╃换婵嗩嚕閹绢喗鍋勫瀣閳诲本绻濋悽闈浶㈤柨鏇樺劦瀹曞綊宕归锝呭伎闂佸啿鎼幊蹇涙倿婵犳碍鐓涢柛鏇ㄥ亞缁犳娊鎮?        if (IsInteractivePointerSource(e.Source))
        if (IsInteractivePointerSource(e.Source))
        {
            return;
        }

        if (IsDesktopSwipeBlockedPointerSource(e.Source))
        {
            return;
        }

        if (!isLeftButtonPressed)
        {
            return;
        }

        ClearDesktopPageContextSettle(refreshContext: false);
        _isDesktopSwipeActive = true;
        _isDesktopSwipeDirectionLocked = false;
        _desktopSwipeStartPoint = pointerInViewport;
        _desktopSwipeCurrentPoint = _desktopSwipeStartPoint;
        _desktopSwipeLastPoint = _desktopSwipeStartPoint;
        _desktopSwipeVelocityX = 0;
        _desktopSwipeLastTimestamp = Stopwatch.GetTimestamp();
        _desktopSwipeBaseOffset = -_currentDesktopSurfaceIndex * _desktopSurfacePageWidth;
        _desktopSwipePointerId = pointerId;
    }

    private bool IsDesktopSwipePointer(IPointer? pointer)
    {
        return !_desktopSwipePointerId.HasValue ||
               pointer is not null && pointer.Id == _desktopSwipePointerId.Value;
    }

    private static bool IsInteractivePointerSource(object? source)
    {
        if (source is not Visual visual)
        {
            return false;
        }

        foreach (var node in visual.GetSelfAndVisualAncestors())
        {
            if (node is Control control)
            {
                if (control.Classes.Contains("desktop-component") ||
                    control.Classes.Contains("desktop-component-host"))
                {
                    return true;
                }
            }

            if (node is Button button && IsLauncherTileButton(button))
            {
                continue;
            }

            if (node is TextBox or ComboBox or ListBoxItem or Slider or ToggleSwitch)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsLauncherTileButton(Button? button)
    {
        if (button is null)
        {
            return false;
        }

        foreach (var node in button.GetSelfAndVisualAncestors())
        {
            if (node is WrapPanel panel && panel.Name == "LauncherRootTilePanel")
            {
                return true;
            }

            if (node is Grid grid && grid.Name == "LauncherFolderGridPanel")
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDesktopSwipeBlockedPointerSource(object? source)
    {
        if (source is not Visual visual)
        {
            return false;
        }

        var pendingNodes = new Stack<object>();
        var visitedNodes = new HashSet<object>(ReferenceEqualityComparer.Instance);
        pendingNodes.Push(visual);

        while (pendingNodes.Count > 0)
        {
            var node = pendingNodes.Pop();
            if (!visitedNodes.Add(node))
            {
                continue;
            }

            if (IsDesktopSwipeBlockingNode(node))
            {
                return true;
            }

            if (node is StyledElement styledElement &&
                styledElement.TemplatedParent is { } templatedParent)
            {
                pendingNodes.Push(templatedParent);
            }

            if (node is Visual currentVisual &&
                currentVisual.GetVisualParent() is { } parentVisual)
            {
                pendingNodes.Push(parentVisual);
            }
        }

        return false;
    }

    private static bool IsDesktopSwipeBlockingNode(object node)
    {
        if (node is ScrollViewer scrollViewer && IsLauncherScrollViewer(scrollViewer))
        {
            return false;
        }

        if (node is Button button && IsLauncherTileButton(button))
        {
            return false;
        }

        if (node is TextBox or ComboBox or Slider or ToggleSwitch or ListBoxItem)
        {
            return true;
        }

        if (node is Control control &&
            (control.Classes.Contains("study-history-action-button") ||
             control.Classes.Contains("desktop-component") ||
             control.Classes.Contains("desktop-component-host")))
        {
            return true;
        }

        var typeName = node.GetType().Name;
        return typeName.Contains("WebView", StringComparison.OrdinalIgnoreCase) ||
               typeName.Contains("ScrollBar", StringComparison.OrdinalIgnoreCase) ||
               typeName.Contains("NumericUpDown", StringComparison.OrdinalIgnoreCase) ||
               typeName.Contains("TextPresenter", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLauncherScrollViewer(ScrollViewer? scrollViewer)
    {
        if (scrollViewer is null)
        {
            return false;
        }

        return scrollViewer.Name == "LauncherRootScrollViewer";
    }

    private bool TryGetPointerPositionInDesktopViewport(PointerEventArgs e, out Point point)
    {
        point = default;
        if (DesktopPagesViewport is null)
        {
            return false;
        }

        point = e.GetPosition(DesktopPagesViewport);
        if (_isDesktopSwipeActive && _isDesktopSwipeDirectionLocked)
        {
            return true;
        }

        var bounds = DesktopPagesViewport.Bounds;
        return bounds.Width > 1 &&
               bounds.Height > 1 &&
               point.X >= 0 &&
               point.Y >= 0 &&
               point.X <= bounds.Width &&
               point.Y <= bounds.Height;
    }

    private void UpdateDesktopSwipeVelocity(Point pointer)
    {
        var now = Stopwatch.GetTimestamp();
        if (_desktopSwipeLastTimestamp > 0)
        {
            var elapsedSeconds = (now - _desktopSwipeLastTimestamp) / (double)Stopwatch.Frequency;
            if (elapsedSeconds > 0.0001)
            {
                var instantVelocity = (pointer.X - _desktopSwipeLastPoint.X) / elapsedSeconds;
                _desktopSwipeVelocityX = _desktopSwipeVelocityX * 0.7 + instantVelocity * 0.3;
            }
        }

        _desktopSwipeLastPoint = pointer;
        _desktopSwipeLastTimestamp = now;
    }

    private void OnDesktopPagesPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isDesktopSwipeActive && !IsDesktopSwipePointer(e.Pointer))
        {
            return;
        }

        if (!_isDesktopSwipeActive || !TryGetPointerPositionInDesktopViewport(e, out var pointerInViewport))
        {
            return;
        }

        if (_desktopPagesHostTransform is null || DesktopPagesViewport is null)
        {
            return;
        }

        _desktopSwipeCurrentPoint = pointerInViewport;
        UpdateDesktopSwipeVelocity(pointerInViewport);
        var deltaX = _desktopSwipeCurrentPoint.X - _desktopSwipeStartPoint.X;
        var deltaY = _desktopSwipeCurrentPoint.Y - _desktopSwipeStartPoint.Y;

        if (!_isDesktopSwipeDirectionLocked)
        {
            const double activationThreshold = 14;
            const double horizontalBias = 1.15;
            var absDeltaX = Math.Abs(deltaX);
            var absDeltaY = Math.Abs(deltaY);

            if (absDeltaY >= activationThreshold && absDeltaY > absDeltaX * horizontalBias)
            {
                CancelDesktopSwipeInteraction(e.Pointer);
                return;
            }

            if (absDeltaX < activationThreshold || absDeltaX <= absDeltaY * horizontalBias)
            {
                return;
            }

            _isDesktopSwipeDirectionLocked = true;
            SetDesktopPagesHostSnapAnimationEnabled(enabled: false);
            if (e.Pointer.Captured != DesktopPagesViewport)
            {
                e.Pointer.Capture(DesktopPagesViewport);
            }
        }

        var minOffset = -LauncherSurfaceIndex * _desktopSurfacePageWidth;
        var tentative = _desktopSwipeBaseOffset + deltaX;
        if (tentative > 0)
        {
            tentative *= 0.24;
        }
        else if (tentative < minOffset)
        {
            tentative = minOffset + (tentative - minOffset) * 0.24;
        }

        _desktopPagesHostTransform.X = tentative;
        UpdateDesktopPageAwareComponentContext();
        e.Handled = true;
    }

    private void OnDesktopPagesPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var pointerId = e.Pointer?.Id ?? 0;
        _activePointerIds.Remove(pointerId);

        if (_isDesktopSwipeActive && !IsDesktopSwipePointer(e.Pointer))
        {
            return;
        }
        
        if (EndDesktopSwipeInteraction(e.Pointer))
        {
            e.Handled = true;
        }
    }

    private void OnDesktopPagesPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        var pointerId = e.Pointer?.Id ?? 0;
        _activePointerIds.Remove(pointerId);

        if (!_isDesktopSwipeActive || !IsDesktopSwipePointer(e.Pointer))
        {
            return;
        }

        if (e.Pointer?.Captured == DesktopPagesViewport)
        {
            return;
        }

        EndDesktopSwipeInteraction(e.Pointer);
    }

    private void CancelDesktopSwipeInteraction(IPointer? pointer)
    {
        if (!_isDesktopSwipeActive)
        {
            return;
        }

        var wasDirectionLocked = _isDesktopSwipeDirectionLocked;
        if (pointer?.Captured == DesktopPagesViewport)
        {
            pointer.Capture(null);
        }

        _isDesktopSwipeActive = false;
        _isDesktopSwipeDirectionLocked = false;
        _isThreeFingerOrRightDragSwipeActive = false;
        _activePointerIds.Clear();
        _desktopSwipePointerId = null;
        _desktopSwipeVelocityX = 0;
        _desktopSwipeLastTimestamp = 0;
        if (wasDirectionLocked)
        {
            SetDesktopPagesHostSnapAnimationEnabled(enabled: true);
            ApplyDesktopSurfaceOffset();
        }
    }

    private bool EndDesktopSwipeInteraction(IPointer? pointer)
    {
        if (!_isDesktopSwipeActive)
        {
            return false;
        }

        var wasDirectionLocked = _isDesktopSwipeDirectionLocked;
        var wasThreeFingerOrRightDrag = _isThreeFingerOrRightDragSwipeActive;
        _isDesktopSwipeActive = false;
        _isDesktopSwipeDirectionLocked = false;
        _isThreeFingerOrRightDragSwipeActive = false;
        _activePointerIds.Clear();
        _desktopSwipePointerId = null;
        
        if (pointer?.Captured == DesktopPagesViewport)
        {
            pointer.Capture(null);
        }

        _desktopSwipeLastTimestamp = 0;
        if (!wasDirectionLocked)
        {
            _desktopSwipeVelocityX = 0;
            return false;
        }

        SetDesktopPagesHostSnapAnimationEnabled(enabled: true);

        var deltaX = _desktopSwipeCurrentPoint.X - _desktopSwipeStartPoint.X;
        var deltaY = _desktopSwipeCurrentPoint.Y - _desktopSwipeStartPoint.Y;
        var absDeltaX = Math.Abs(deltaX);
        var absDeltaY = Math.Abs(deltaY);
        var distanceThreshold = Math.Max(48, _desktopSurfacePageWidth * 0.14);
        var velocityThreshold = Math.Max(860, _desktopSurfacePageWidth * 1.08);
        var predictedDeltaX = deltaX + _desktopSwipeVelocityX * 0.18;
        var predictedOffset = _desktopSwipeBaseOffset + predictedDeltaX;
        var projectedTargetIndex = (int)Math.Round(-predictedOffset / _desktopSurfacePageWidth);
        projectedTargetIndex = Math.Clamp(projectedTargetIndex, 0, LauncherSurfaceIndex);

        var hasDistanceIntent = absDeltaX >= distanceThreshold && absDeltaX > absDeltaY * 1.05;
        var hasVelocityIntent = Math.Abs(_desktopSwipeVelocityX) >= velocityThreshold;

        // 濠电姷顣藉Σ鍛村磻閳ь剟鏌涚€ｎ偅宕岄柡宀嬬磿娴狅妇鎷犻幓鎺懶ョ紓鍌欐祰娴滎剚鏅跺Δ鍐煓濠㈣泛顑呯欢鐐烘倵閿濆簼绨芥俊?闂傚倷绀侀幉锟犳偡閿旂晫绠惧┑鐘叉搐閺嬩焦銇勯幘鍗炵仼缂佺媭鍨堕弻鈥崇暤椤旂厧鏁?&& 闂傚倷绶氬鑽ゆ嫻閻旂厧绀夐幖鎼厛閺佸嫰鏌涢妷锝呭闁崇粯妫冮弻宥堫檨闁告挻宀告俊?&& 闂傚倷绀侀幉锛勫枈瀹ュ鍨傚ù锝呭暔娴滃湱绱掔€ｎ偒鍎ラ柣鎾卞劦閺岀喓鈧稒顭囩粻鎾舵偖?
        if (wasThreeFingerOrRightDrag && 
            _currentDesktopSurfaceIndex == 0 && 
            deltaX > 0 && // 闂傚倷绀侀幉锛勫枈瀹ュ鍨傚ù锝呭暔娴滃湱绱掔€ｎ偒鍎ラ柣鎾卞劦閺岀喓鈧稒顭囩粻鎾舵偖?
            (hasDistanceIntent || hasVelocityIntent))
        {
            if (Application.Current is App app)
            {
                app.HideMainWindowToTray(this, "ThreeFingerOrRightDragSwipe");
            }
            
            ApplyDesktopSurfaceOffset();
            _desktopSwipeVelocityX = 0;
            return true;
        }

        if (projectedTargetIndex == _currentDesktopSurfaceIndex && (hasDistanceIntent || hasVelocityIntent))
        {
            projectedTargetIndex = Math.Clamp(
                _currentDesktopSurfaceIndex + (deltaX < 0 ? 1 : -1),
                0,
                LauncherSurfaceIndex);
        }

        _desktopSwipeVelocityX = 0;

        if (projectedTargetIndex != _currentDesktopSurfaceIndex)
        {
            MoveSurfaceTo(projectedTargetIndex);
            return true;
        }

        ApplyDesktopSurfaceOffset();
        return hasDistanceIntent || hasVelocityIntent;
    }

    private void OnDesktopPagesPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (!CanSwipeDesktopSurface())
        {
            return;
        }

        var prefersHorizontal = Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y) ||
                                e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (!prefersHorizontal)
        {
            return;
        }

        var delta = e.Delta.X != 0 ? e.Delta.X : e.Delta.Y;
        if (Math.Abs(delta) < double.Epsilon)
        {
            return;
        }

        MoveSurfaceBy(delta < 0 ? 1 : -1);
        e.Handled = true;
    }

    private void RenderLauncherRootTiles()
    {
        if (LauncherRootTilePanel is null)
        {
            return;
        }

        ClearSelectedLauncherTile(refreshTaskbar: false);
        LauncherRootTilePanel.Children.Clear();
        var folders = _startMenuRoot.Folders;
        var apps = _startMenuRoot.Apps;

        foreach (var folder in folders)
        {
            if (!IsLauncherFolderVisible(folder))
            {
                continue;
            }

            LauncherRootTilePanel.Children.Add(CreateLauncherFolderTile(folder));
        }

        foreach (var app in apps)
        {
            if (!IsLauncherAppVisible(app))
            {
                continue;
            }

            LauncherRootTilePanel.Children.Add(CreateLauncherAppTile(app));
        }

        if (LauncherRootTilePanel.Children.Count == 0)
        {
            LauncherRootTilePanel.Children.Add(CreateLauncherHintTile(
                GetLauncherEmptyText(),
                string.Empty));
        }

        // 闂傚倷绶氬鑽ゆ嫻閻旂厧绀夐悘鐐电叓閻熼偊娼ㄩ柍褜鍓欓锝嗙鐎ｎ亞鍊為梺闈涱煬閻撳牆煤椤掑嫭鈷戦柛婵嗗濠€浼存煙閸涘﹥鍊愰柟顕€绠栭、妤呭礋椤愩値鍚呴梻浣哥秺閸嬪﹪宕滃璺虹９闁汇垹鎲￠悡銉︾箾閹寸儐鐒鹃悗姘缁辨帡濡搁敂鎯у绩闂佽鍠曠划娆愪繆閹间礁唯鐟滄粍瀵煎畝鍕厽闊洦娲栨禍褰掓煕鐎ｎ偅宕岄柟顔款潐缁楃喐绻濋崟顓ㄧ吹闂?        Dispatcher.UIThread.Post(() => UpdateLauncherTileLayout(), DispatcherPriority.Background);
    }

    private Button CreateLauncherFolderTile(StartMenuFolderNode folder)
    {
        var title = folder.Name;
        var subtitle = Lf("launcher.folder_items_format", "{0} apps", folder.TotalAppCount);
        var folderIconBitmap = GetLauncherFolderIconBitmap();
        var folderKey = NormalizeLauncherHiddenKey(folder.RelativePath);
        return CreateLauncherTileButton(
            title,
            subtitle,
            monogram: "DIR",
            iconBitmap: folderIconBitmap,
            () => OpenLauncherFolder(folder),
            LauncherEntryKind.Folder,
            folderKey);
    }

    private Button CreateLauncherAppTile(StartMenuAppEntry app)
    {
        var iconBitmap = GetLauncherIconBitmap(app);
        var monogram = BuildMonogram(app.DisplayName);
        var appKey = NormalizeLauncherHiddenKey(app.RelativePath);
        return CreateLauncherTileButton(
            app.DisplayName,
            subtitle: string.Empty,
            monogram,
            iconBitmap,
            () => LaunchStartMenuEntry(app),
            LauncherEntryKind.Shortcut,
            appKey);
    }

    private Control CreateLauncherHintTile(string title, string subtitle)
    {
        var panel = new StackPanel
        {
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            panel.Children.Add(new TextBlock
            {
                Text = subtitle,
                Opacity = 0.75,
                HorizontalAlignment = HorizontalAlignment.Center
            });
        }

        return new Border
        {
            Classes = { "glass-panel" },
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0, 0, 12, 12),
            CornerRadius = new CornerRadius(20),
            Child = panel,
        };
    }

    private Button CreateLauncherTileButton(
        string title,
        string subtitle,
        string monogram,
        Bitmap? iconBitmap,
        Action clickAction,
        LauncherEntryKind entryKind,
        string entryKey)
    {
        Control iconControl = iconBitmap is not null
            ? new Image
            {
                Source = iconBitmap,
                Width = 40,
                Height = 40,
                Stretch = Stretch.Uniform
            }
            : new Border
            {
                Width = 40,
                Height = 40,
                CornerRadius = new CornerRadius(999),
                Background = GetThemeBrush("AdaptiveButtonBackgroundBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                BorderThickness = new Thickness(0),
                Child = new TextBlock
                {
                    Text = monogram,
                    FontWeight = FontWeight.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

        var textPanel = new StackPanel
        {
            Spacing = 3,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        textPanel.Children.Add(new TextBlock
        {
            Text = title,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 2,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch
        });

        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            textPanel.Children.Add(new TextBlock
            {
                Text = subtitle,
                Opacity = 0.72,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch
            });
        }

        var content = new StackPanel
        {
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        content.Children.Add(iconControl);
        content.Children.Add(textPanel);

        var button = new Button
        {
            Margin = new Thickness(0, 0, 12, 12),
            BorderThickness = new Thickness(0),
            BorderBrush = Brushes.Transparent,
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(10),
            Content = content,
        };
        if (_showLauncherTileBackground)
        {
            button.Classes.Add("glass-panel");
        }
        else
        {
            button.Background = Brushes.Transparent;
        }
        button.Click += (_, _) =>
        {
            if (_isComponentLibraryOpen)
            {
                if (!string.IsNullOrWhiteSpace(entryKey))
                {
                    SetSelectedLauncherTile(button, entryKind, entryKey);
                }

                return;
            }

            clickAction();
        };
        return button;
    }

    private static string NormalizeLauncherHiddenKey(string? key)
    {
        return string.IsNullOrWhiteSpace(key) ? string.Empty : key.Trim();
    }

    private bool IsLauncherFolderVisible(StartMenuFolderNode folder)
    {
        var key = NormalizeLauncherHiddenKey(folder.RelativePath);
        return string.IsNullOrWhiteSpace(key) || !_hiddenLauncherFolderPaths.Contains(key);
    }

    private bool IsLauncherAppVisible(StartMenuAppEntry app)
    {
        var key = NormalizeLauncherHiddenKey(app.RelativePath);
        return string.IsNullOrWhiteSpace(key) || !_hiddenLauncherAppPaths.Contains(key);
    }

    private bool IsLauncherTileSelected()
    {
        return _selectedLauncherEntryKind.HasValue && !string.IsNullOrWhiteSpace(_selectedLauncherEntryKey);
    }

    private void SetSelectedLauncherTile(Button button, LauncherEntryKind entryKind, string entryKey)
    {
        if (!_isComponentLibraryOpen || string.IsNullOrWhiteSpace(entryKey))
        {
            return;
        }

        var normalizedKey = NormalizeLauncherHiddenKey(entryKey);
        if (string.IsNullOrWhiteSpace(normalizedKey))
        {
            return;
        }

        if (_selectedDesktopComponentHost is not null)
        {
            ClearDesktopComponentSelection();
        }

        if (_selectedLauncherTileButton is not null && _selectedLauncherTileButton != button)
        {
            ApplyLauncherTileSelectionVisual(_selectedLauncherTileButton, isSelected: false);
        }

        _selectedLauncherTileButton = button;
        _selectedLauncherEntryKind = entryKind;
        _selectedLauncherEntryKey = normalizedKey;
        ApplyLauncherTileSelectionVisual(button, isSelected: true);
        ApplyTaskbarActionVisibility(GetCurrentTaskbarContext());
    }

    private void ClearSelectedLauncherTile(bool refreshTaskbar)
    {
        if (_selectedLauncherTileButton is not null)
        {
            ApplyLauncherTileSelectionVisual(_selectedLauncherTileButton, isSelected: false);
        }

        _selectedLauncherTileButton = null;
        _selectedLauncherEntryKind = null;
        _selectedLauncherEntryKey = null;

        if (refreshTaskbar)
        {
            ApplyTaskbarActionVisibility(GetCurrentTaskbarContext());
        }
    }

    private void ApplyLauncherTileSelectionVisual(Button button, bool isSelected)
    {
        var showSelection = isSelected && _isComponentLibraryOpen;
        button.BorderThickness = showSelection
            ? new Thickness(Math.Clamp(_currentDesktopCellSize * 0.04, 1, 3))
            : new Thickness(0);
        button.BorderBrush = showSelection ? GetThemeBrush("AdaptiveAccentBrush") : Brushes.Transparent;
    }

    private void HideSelectedLauncherEntry()
    {
        if (!_isComponentLibraryOpen ||
            _currentDesktopSurfaceIndex != LauncherSurfaceIndex ||
            _selectedLauncherEntryKind is null ||
            string.IsNullOrWhiteSpace(_selectedLauncherEntryKey))
        {
            return;
        }

        var entryKind = _selectedLauncherEntryKind.Value;
        var entryKey = _selectedLauncherEntryKey!;
        ClearSelectedLauncherTile(refreshTaskbar: false);

        var changed = entryKind switch
        {
            LauncherEntryKind.Folder => _hiddenLauncherFolderPaths.Add(entryKey),
            LauncherEntryKind.Shortcut => _hiddenLauncherAppPaths.Add(entryKey),
            _ => false
        };

        if (changed)
        {
            ApplyLauncherVisibilitySettingsChange();
            return;
        }

        ApplyTaskbarActionVisibility(GetCurrentTaskbarContext());
    }

    private void ApplyLauncherVisibilitySettingsChange()
    {
        ClearSelectedLauncherTile(refreshTaskbar: false);
        RenderLauncherRootTiles();
        if (_launcherFolderStack.Count > 0)
        {
            RenderLauncherFolderFromStack();
        }

        RenderLauncherHiddenItemsList();
        PersistSettings();
    }

    private void RenderLauncherHiddenItemsList()
    {
        if (LauncherHiddenItemsSettingsExpander is null || LauncherHiddenItemsEmptyTextBlock is null)
        {
            return;
        }

        LauncherHiddenItemsSettingsExpander.Items.Clear();
        var hiddenItems = BuildLauncherHiddenItems();
        LauncherHiddenItemsEmptyTextBlock.IsVisible = hiddenItems.Count == 0;
        if (hiddenItems.Count == 0)
        {
            return;
        }

        foreach (var hiddenItem in hiddenItems)
        {
            LauncherHiddenItemsSettingsExpander.Items.Add(CreateLauncherHiddenItemRow(hiddenItem));
        }
    }

    private IReadOnlyList<LauncherHiddenItemView> BuildLauncherHiddenItems()
    {
        var items = new List<LauncherHiddenItemView>();
        var seenFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        CollectHiddenLauncherItems(_startMenuRoot, items, seenFolders, seenApps);

        foreach (var key in _hiddenLauncherFolderPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            if (!seenFolders.Contains(key))
            {
                items.Add(new LauncherHiddenItemView(
                    LauncherEntryKind.Folder,
                    key,
                    BuildLauncherHiddenFallbackDisplayName(key),
                    "DIR",
                    GetLauncherFolderIconBitmap()));
            }
        }

        foreach (var key in _hiddenLauncherAppPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            if (!seenApps.Contains(key))
            {
                var fallbackName = BuildLauncherHiddenFallbackDisplayName(key);
                items.Add(new LauncherHiddenItemView(
                    LauncherEntryKind.Shortcut,
                    key,
                    fallbackName,
                    BuildMonogram(fallbackName),
                    IconBitmap: null));
            }
        }

        return items
            .OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void CollectHiddenLauncherItems(
        StartMenuFolderNode folder,
        List<LauncherHiddenItemView> items,
        HashSet<string> seenFolders,
        HashSet<string> seenApps)
    {
        foreach (var subFolder in folder.Folders)
        {
            var folderKey = NormalizeLauncherHiddenKey(subFolder.RelativePath);
            if (!string.IsNullOrWhiteSpace(folderKey) &&
                _hiddenLauncherFolderPaths.Contains(folderKey) &&
                seenFolders.Add(folderKey))
            {
                items.Add(new LauncherHiddenItemView(
                    LauncherEntryKind.Folder,
                    folderKey,
                    subFolder.Name,
                    "DIR",
                    GetLauncherFolderIconBitmap()));
            }

            CollectHiddenLauncherItems(subFolder, items, seenFolders, seenApps);
        }

        foreach (var app in folder.Apps)
        {
            var appKey = NormalizeLauncherHiddenKey(app.RelativePath);
            if (string.IsNullOrWhiteSpace(appKey) ||
                !_hiddenLauncherAppPaths.Contains(appKey) ||
                !seenApps.Add(appKey))
            {
                continue;
            }

            items.Add(new LauncherHiddenItemView(
                LauncherEntryKind.Shortcut,
                appKey,
                app.DisplayName,
                BuildMonogram(app.DisplayName),
                GetLauncherIconBitmap(app)));
        }
    }

    private static string BuildLauncherHiddenFallbackDisplayName(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return "Unknown";
        }

        var normalized = key.Replace('\\', '/');
        var fileName = Path.GetFileNameWithoutExtension(normalized);
        return string.IsNullOrWhiteSpace(fileName)
            ? key
            : fileName;
    }

    private FASettingsExpanderItem CreateLauncherHiddenItemRow(LauncherHiddenItemView hiddenItem)
    {
        var typeText = hiddenItem.Kind == LauncherEntryKind.Folder
            ? L("settings.launcher.hidden_type_folder", "Folder")
            : L("settings.launcher.hidden_type_shortcut", "Shortcut");

        var restoreButton = new Button
        {
            Width = 36,
            Height = 36,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Tag = new LauncherHiddenItemToken(hiddenItem.Kind, hiddenItem.Key)
        };
        restoreButton.Content = new FluentIcons.Avalonia.SymbolIcon
        {
            Symbol = FluentIcons.Common.Symbol.Eye,
            IconVariant = FluentIcons.Common.IconVariant.Regular,
            FontSize = 18,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(restoreButton, L("settings.launcher.restore_button", "Unhide"));
        restoreButton.Click += OnRestoreLauncherHiddenItemClick;

        return new FASettingsExpanderItem
        {
            Content = hiddenItem.DisplayName,
            Description = typeText,
            IconSource = CreateLauncherHiddenItemIconSource(hiddenItem),
            IsClickEnabled = false,
            Footer = restoreButton
        };
    }

    private FAIconSource? CreateLauncherHiddenItemIconSource(LauncherHiddenItemView hiddenItem)
    {
        if (hiddenItem.IconBitmap is not null)
        {
            return new FAImageIconSource
            {
                Source = hiddenItem.IconBitmap
            };
        }

        return null;
    }

    private void OnRestoreLauncherHiddenItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: LauncherHiddenItemToken token })
        {
            return;
        }

        var removed = token.Kind switch
        {
            LauncherEntryKind.Folder => _hiddenLauncherFolderPaths.Remove(token.Key),
            LauncherEntryKind.Shortcut => _hiddenLauncherAppPaths.Remove(token.Key),
            _ => false
        };

        if (!removed)
        {
            return;
        }

        ApplyLauncherVisibilitySettingsChange();
    }

    private Bitmap? GetLauncherIconBitmap(StartMenuAppEntry app)
    {
        if (app.IconPngBytes is null || app.IconPngBytes.Length == 0)
        {
            return null;
        }

        if (_launcherIconCache.TryGetValue(app.RelativePath, out var cached))
        {
            return cached;
        }

        try
        {
            using var stream = new MemoryStream(app.IconPngBytes, writable: false);
            var bitmap = new Bitmap(stream);
            _launcherIconCache[app.RelativePath] = bitmap;
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private Bitmap? GetLauncherFolderIconBitmap()
    {
        if (_launcherFolderIconBitmap is not null)
        {
            return _launcherFolderIconBitmap;
        }

        if (_launcherFolderIconPngBytes is null || _launcherFolderIconPngBytes.Length == 0)
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(_launcherFolderIconPngBytes, writable: false);
            _launcherFolderIconBitmap = new Bitmap(stream);
            return _launcherFolderIconBitmap;
        }
        catch
        {
            _launcherFolderIconBitmap = null;
            return null;
        }
    }

    private void OpenLauncherFolder(StartMenuFolderNode folder)
    {
        _launcherFolderStack.Push(folder);
        RenderLauncherFolderFromStack();
    }

    private void CloseLauncherFolderOverlay()
    {
        ClearSelectedLauncherTile(refreshTaskbar: false);
        _launcherFolderStack.Clear();
        if (LauncherFolderOverlay is not null)
        {
            LauncherFolderOverlay.IsVisible = false;
        }

        if (LauncherFolderGridPanel is not null)
        {
            LauncherFolderGridPanel.Children.Clear();
        }
    }

    private void RenderLauncherFolderFromStack()
    {
        if (LauncherFolderOverlay is null ||
            LauncherFolderGridPanel is null ||
            LauncherFolderTitleTextBlock is null)
        {
            return;
        }

        ClearSelectedLauncherTile(refreshTaskbar: false);
        if (_launcherFolderStack.Count == 0)
        {
            CloseLauncherFolderOverlay();
            return;
        }

        var folder = _launcherFolderStack.Peek();
        LauncherFolderOverlay.IsVisible = true;
        LauncherFolderTitleTextBlock.Text = folder.Name;

        LauncherFolderGridPanel.Children.Clear();

        const int maxCols = 4;
        const int maxRows = 3;
        const int maxItems = maxCols * maxRows;

        var visibleFolders = folder.Folders.Where(IsLauncherFolderVisible).ToList();
        var visibleApps = folder.Apps.Where(IsLauncherAppVisible).ToList();

        if (visibleFolders.Count == 0 && visibleApps.Count == 0)
        {
            LauncherFolderGridPanel.Children.Add(CreateLauncherFolderGridHintCell(
                L("launcher.empty_folder", "This folder is empty.")));
            return;
        }

        var allItems = new List<(StartMenuFolderNode? Folder, StartMenuAppEntry? App)>();
        foreach (var f in visibleFolders)
        {
            allItems.Add((f, null));
        }
        foreach (var a in visibleApps)
        {
            allItems.Add((null, a));
        }

        var displayCount = Math.Min(allItems.Count, maxItems);
        for (var i = 0; i < displayCount; i++)
        {
            var col = i % maxCols;
            var row = i / maxCols;
            var (itemFolder, itemApp) = allItems[i];

            Control cell;
            if (itemFolder is not null)
            {
                var capturedFolder = itemFolder;
                cell = CreateLauncherFolderGridTile(itemFolder.Name, GetLauncherFolderIconBitmap(), () => OpenLauncherFolder(capturedFolder));
            }
            else if (itemApp is not null)
            {
                var capturedApp = itemApp;
                cell = CreateLauncherFolderGridTile(capturedApp, () => LaunchStartMenuEntry(capturedApp));
            }
            else
            {
                continue;
            }

            Grid.SetColumn(cell, col);
            Grid.SetRow(cell, row);
            LauncherFolderGridPanel.Children.Add(cell);
        }
    }

    private Button CreateLauncherFolderGridTile(StartMenuAppEntry app, Action clickAction)
    {
        var iconBitmap = GetLauncherIconBitmap(app);
        var monogram = BuildMonogram(app.DisplayName);

        Control iconControl = iconBitmap is not null
            ? new Image
            {
                Source = iconBitmap,
                Width = 32,
                Height = 32,
                Stretch = Stretch.Uniform
            }
            : new Border
            {
                Width = 32,
                Height = 32,
                CornerRadius = new CornerRadius(8),
                Background = GetThemeBrush("AdaptiveButtonBackgroundBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = monogram,
                    FontSize = 13,
                    FontWeight = FontWeight.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

        var content = new StackPanel
        {
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center
        };
        content.Children.Add(iconControl);
        content.Children.Add(new TextBlock
        {
            Text = app.DisplayName,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 2,
            TextAlignment = TextAlignment.Center,
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Stretch
        });

        var button = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(8, 8, 8, 6),
            Content = content
        };

        // 闂傚倷绀侀幖顐ょ矓閻戞枻缍栧璺猴功閺嗐倕霉閿濆洤鍔嬪┑顖氥偢閺屾盯骞樺Δ鈧幊蹇涙倵椤撱垺鈷戦柛娑橈工婵洭鏌涢悢閿嬪仴闁诡喚鍋撻妶锝夊礃閵娿儱鎸ゆ俊鐐€栭悧妤冨枈瀹ュ纾垮┑鐘叉处閻撴盯鏌涢弴銊ヤ簻闁抽攱妫冮弻鏇㈠炊閵娿儱鎽甸梺纭呮珪椤ㄥ牊绂掗敃鍌涘€锋い鎺戝€哥拋?
        if (_showLauncherTileBackground)
        {
            button.Classes.Add("glass-panel");
        }
        else
        {
            button.Background = Brushes.Transparent;
        }

        button.Click += (_, _) =>
        {
            if (_isComponentLibraryOpen)
            {
                return;
            }

            clickAction();
        };
        return button;
    }

    private Button CreateLauncherFolderGridTile(string folderName, Bitmap? iconBitmap, Action clickAction)
    {
        var monogram = "DIR";

        Control iconControl = iconBitmap is not null
            ? new Image
            {
                Source = iconBitmap,
                Width = 32,
                Height = 32,
                Stretch = Stretch.Uniform
            }
            : new Border
            {
                Width = 32,
                Height = 32,
                CornerRadius = new CornerRadius(8),
                Background = GetThemeBrush("AdaptiveButtonBackgroundBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = monogram,
                    FontSize = 11,
                    FontWeight = FontWeight.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

        var content = new StackPanel
        {
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center
        };
        content.Children.Add(iconControl);
        content.Children.Add(new TextBlock
        {
            Text = folderName,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 2,
            TextAlignment = TextAlignment.Center,
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Stretch
        });

        var button = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(8, 8, 8, 6),
            Content = content
        };

        // 闂傚倷绀侀幖顐ょ矓閻戞枻缍栧璺猴功閺嗐倕霉閿濆洤鍔嬪┑顖氥偢閺屾盯骞樺Δ鈧幊蹇涙倵椤撱垺鈷戦柛娑橈工婵洭鏌涢悢閿嬪仴闁诡喚鍋撻妶锝夊礃閵娿儱鎸ゆ俊鐐€栭悧妤冨枈瀹ュ纾垮┑鐘叉处閻撴盯鏌涢弴銊ヤ簻闁抽攱妫冮弻鏇㈠炊閵娿儱鎽甸梺纭呮珪椤ㄥ牊绂掗敃鍌涘€锋い鎺戝€哥拋?
        if (_showLauncherTileBackground)
        {
            button.Classes.Add("glass-panel");
        }
        else
        {
            button.Background = Brushes.Transparent;
        }

        button.Click += (_, _) =>
        {
            if (_isComponentLibraryOpen)
            {
                return;
            }

            clickAction();
        };
        return button;
    }

    private Control CreateLauncherFolderGridHintCell(string message)
    {
        return CreateLauncherFolderGridHintCell(message, 0, 0);
    }

    private Control CreateLauncherFolderGridHintCell(string message, int col, int row)
    {
        var textBlock = new TextBlock
        {
            Text = message,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.6
        };

        var cell = new Border
        {
            Classes = { "glass-panel" },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            CornerRadius = new CornerRadius(12),
            Child = textBlock
        };

        Grid.SetColumn(cell, col);
        Grid.SetRow(cell, row);
        return cell;
    }

    private static string BuildMonogram(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "?";
        }

        var letters = text
            .Trim()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part[0])
            .Take(2)
            .ToArray();
        if (letters.Length == 0)
        {
            return "?";
        }

        return new string(letters).ToUpperInvariant();
    }

    private string GetLauncherEmptyText()
    {
        return OperatingSystem.IsLinux()
            ? L("launcher.empty_linux", "No Linux desktop entries were found.")
            : L("launcher.empty", "No Start Menu entries found.");
    }

    private static void LaunchStartMenuEntry(StartMenuAppEntry app)
    {
        try
        {
            if (OperatingSystem.IsLinux() &&
                !string.IsNullOrWhiteSpace(app.LaunchExecutable))
            {
                var linuxStartInfo = new ProcessStartInfo
                {
                    FileName = app.LaunchExecutable,
                    UseShellExecute = false
                };

                if (!string.IsNullOrWhiteSpace(app.WorkingDirectory))
                {
                    linuxStartInfo.WorkingDirectory = app.WorkingDirectory;
                }

                foreach (var argument in app.LaunchArguments)
                {
                    linuxStartInfo.ArgumentList.Add(argument);
                }

                Process.Start(linuxStartInfo);
                return;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = app.FilePath,
                UseShellExecute = true
            };
            Process.Start(startInfo);
        }
        catch
        {
            // Ignore failures to launch malformed shortcuts.
        }
    }

    private void OnLauncherFolderOverlayPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (LauncherFolderPanel is null)
        {
            return;
        }

        var point = e.GetCurrentPoint(LauncherFolderPanel).Position;
        if (point.X >= 0 &&
            point.Y >= 0 &&
            point.X <= LauncherFolderPanel.Bounds.Width &&
            point.Y <= LauncherFolderPanel.Bounds.Height)
        {
            return;
        }

        CloseLauncherFolderOverlay();
        e.Handled = true;
    }

    private void DisposeLauncherResources()
    {
        foreach (var bitmap in _launcherIconCache.Values)
        {
            bitmap.Dispose();
        }

        _launcherIconCache.Clear();
        _launcherFolderIconBitmap?.Dispose();
        _launcherFolderIconBitmap = null;
    }
}
