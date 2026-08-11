using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LanMountainDesktop.Services.Update;
using LanMountainDesktop.Shared.Contracts.Update;

namespace LanMountainDesktop.ViewModels;

public sealed partial class UpdateProgressViewModel : ViewModelBase, IDisposable
{
    private readonly IDisposable _subscription;
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    public UpdateProgressViewModel(IObservable<InstallProgressReport> progressStream)
    {
        _subscription = progressStream.Subscribe(new ActionObserver<InstallProgressReport>(OnNext));
    }

    [ObservableProperty] private string _stageText = string.Empty;
    [ObservableProperty] private double _progressFraction;
    [ObservableProperty] private string _currentFile = string.Empty;
    [ObservableProperty] private int _filesCompleted;
    [ObservableProperty] private int _filesTotal;
    [ObservableProperty] private bool _isCompleted;
    [ObservableProperty] private bool _isSuccess;
    [ObservableProperty] private string _errorMessage = string.Empty;

    public int ProgressPercent => (int)Math.Clamp(ProgressFraction * 100, 0, 100);

    partial void OnProgressFractionChanged(double value)
    {
        OnPropertyChanged(nameof(ProgressPercent));
    }

    [RelayCommand]
    private void Cancel()
    {
        _cts.Cancel();
        IsCompleted = true;
        IsSuccess = false;
        ErrorMessage = "Cancelled by user.";
    }

    public CancellationToken CancellationToken => _cts.Token;

    private void OnNext(InstallProgressReport report)
    {
        StageText = report.Message;
        ProgressFraction = report.FilesTotal > 0
            ? (double)report.FilesCompleted / report.FilesTotal
            : report.ProgressPercent / 100.0;
        CurrentFile = report.CurrentFile ?? string.Empty;
        FilesCompleted = report.FilesCompleted;
        FilesTotal = report.FilesTotal;

        if (report.Stage is InstallStage.Completed)
        {
            IsCompleted = true;
            IsSuccess = true;
        }
        else if (report.Stage is InstallStage.Failed)
        {
            IsCompleted = true;
            IsSuccess = false;
            ErrorMessage = report.Message;
        }
    }

    private void OnError(Exception ex)
    {
        IsCompleted = true;
        IsSuccess = false;
        ErrorMessage = ex.Message;
    }

    private void OnCompleted()
    {
        IsCompleted = true;
        IsSuccess = true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _subscription.Dispose();
        _cts.Dispose();
    }
}
