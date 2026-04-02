using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LanMountainDesktop.Models;

namespace LanMountainDesktop.Views;

public partial class StudySessionReportWindow : Window
{
    private StudySessionReport? _report;

    public StudySessionReportWindow()
    {
        InitializeComponent();
        CloseButton.Click += OnCloseButtonClick;
    }

    public StudySessionReportWindow(StudySessionReport report) : this()
    {
        LoadReport(report);
    }

    public void LoadReport(StudySessionReport report)
    {
        _report = report;
        
        // 设置标题
        TitleTextBlock.Text = string.IsNullOrWhiteSpace(report.Label) 
            ? "自习报告" 
            : report.Label;
        SubtitleTextBlock.Text = string.Format(
            CultureInfo.CurrentCulture,
            "{0:yyyy-MM-dd HH:mm} - {1:HH:mm} ({2})",
            report.StartedAt.ToLocalTime(),
            report.EndedAt.ToLocalTime(),
            FormatDuration(report.Duration));

        // 设置汇总数据
        AvgScoreTextBlock.Text = report.Metrics.AvgScore.ToString("F1", CultureInfo.CurrentCulture);
        MaxScoreTextBlock.Text = report.Metrics.MaxScore.ToString("F1", CultureInfo.CurrentCulture);
        MinScoreTextBlock.Text = report.Metrics.MinScore.ToString("F1", CultureInfo.CurrentCulture);
        InterruptCountTextBlock.Text = report.Metrics.TotalSegmentCount.ToString(CultureInfo.CurrentCulture);

        // 构建详细数据表
        BuildDetailDataTable(report);
    }

    private void BuildDetailDataTable(StudySessionReport report)
    {
        var items = new ObservableCollection<DetailDataRow>();

        foreach (var slice in report.Slices)
        {
            items.Add(new DetailDataRow(
                TimeRange: $"{slice.StartAt.ToLocalTime():HH:mm} - {slice.EndAt.ToLocalTime():HH:mm}",
                AvgDb: slice.Display.AvgDb,
                Score: slice.Score,
                SegmentCount: slice.Raw.SegmentCount));
        }

        DetailDataGrid.ItemsSource = items;
    }

    private void OnCloseButtonClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                "{0}小时{1}分钟",
                (int)duration.TotalHours,
                duration.Minutes);
        }

        return string.Format(
            CultureInfo.CurrentCulture,
            "{0}分钟",
            duration.Minutes);
    }
}

public record DetailDataRow(
    string TimeRange,
    double AvgDb,
    double Score,
    int SegmentCount);
