using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using LanMountainDesktop.Models;

namespace LanMountainDesktop.Services;

/// <summary>
/// 融合桌面布局存储服务接口
/// </summary>
public interface IFusedDesktopLayoutService
{
    /// <summary>
    /// 加载融合桌面布局
    /// </summary>
    FusedDesktopLayoutSnapshot Load();
    
    /// <summary>
    /// 保存融合桌面布局
    /// </summary>
    void Save(FusedDesktopLayoutSnapshot snapshot);
    
    /// <summary>
    /// 添加组件放置
    /// </summary>
    void AddComponentPlacement(FusedDesktopComponentPlacementSnapshot placement);
    
    /// <summary>
    /// 更新组件放置
    /// </summary>
    void UpdateComponentPlacement(FusedDesktopComponentPlacementSnapshot placement);
    
    /// <summary>
    /// 移除组件放置
    /// </summary>
    void RemoveComponentPlacement(string placementId);
    
    /// <summary>
    /// 清除所有组件放置
    /// </summary>
    void ClearAllPlacements();
}

/// <summary>
/// 融合桌面布局存储服务实现
/// </summary>
internal sealed class FusedDesktopLayoutService : IFusedDesktopLayoutService
{
    private static readonly string ConfigFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "LanMountainDesktop",
        "fused_desktop_layout.json");
    
    private readonly object _lock = new();
    private FusedDesktopLayoutSnapshot? _cachedSnapshot;
    
    public FusedDesktopLayoutSnapshot Load()
    {
        lock (_lock)
        {
            if (_cachedSnapshot is not null)
            {
                return _cachedSnapshot.Clone();
            }
            
            try
            {
                if (!File.Exists(ConfigFilePath))
                {
                    _cachedSnapshot = new FusedDesktopLayoutSnapshot();
                    return _cachedSnapshot.Clone();
                }
                
                var json = File.ReadAllText(ConfigFilePath);
                var snapshot = JsonSerializer.Deserialize<FusedDesktopLayoutSnapshot>(json, JsonOptions);
                _cachedSnapshot = snapshot ?? new FusedDesktopLayoutSnapshot();
                return _cachedSnapshot.Clone();
            }
            catch (Exception ex)
            {
                AppLogger.Warn("FusedDesktopLayout", "Failed to load fused desktop layout.", ex);
                _cachedSnapshot = new FusedDesktopLayoutSnapshot();
                return _cachedSnapshot.Clone();
            }
        }
    }
    
    public void Save(FusedDesktopLayoutSnapshot snapshot)
    {
        lock (_lock)
        {
            try
            {
                _cachedSnapshot = snapshot.Clone();
                
                var directory = Path.GetDirectoryName(ConfigFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                
                var json = JsonSerializer.Serialize(snapshot, JsonOptions);
                File.WriteAllText(ConfigFilePath, json);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("FusedDesktopLayout", "Failed to save fused desktop layout.", ex);
            }
        }
    }
    
    public void AddComponentPlacement(FusedDesktopComponentPlacementSnapshot placement)
    {
        var snapshot = Load();
        snapshot.ComponentPlacements.Add(placement);
        Save(snapshot);
    }
    
    public void UpdateComponentPlacement(FusedDesktopComponentPlacementSnapshot placement)
    {
        var snapshot = Load();
        var index = snapshot.ComponentPlacements.FindIndex(p => p.PlacementId == placement.PlacementId);
        if (index >= 0)
        {
            snapshot.ComponentPlacements[index] = placement;
            Save(snapshot);
        }
    }
    
    public void RemoveComponentPlacement(string placementId)
    {
        var snapshot = Load();
        snapshot.ComponentPlacements.RemoveAll(p => p.PlacementId == placementId);
        Save(snapshot);
    }
    
    public void ClearAllPlacements()
    {
        var snapshot = Load();
        snapshot.ComponentPlacements.Clear();
        Save(snapshot);
    }
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}

/// <summary>
/// 融合桌面布局服务提供者
/// </summary>
public static class FusedDesktopLayoutServiceProvider
{
    private static IFusedDesktopLayoutService? _instance;
    private static readonly object _lock = new();
    
    public static IFusedDesktopLayoutService GetOrCreate()
    {
        if (_instance is not null)
        {
            return _instance;
        }
        
        lock (_lock)
        {
            _instance ??= new FusedDesktopLayoutService();
            return _instance;
        }
    }
}
