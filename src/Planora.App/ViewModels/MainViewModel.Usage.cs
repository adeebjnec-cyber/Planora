namespace Planora.App.ViewModels;

/// <summary>Extra detail rows in the Task Usage and Resource Usage views (Format > Details in Microsoft Project).</summary>
[Flags]
public enum UsageDetail
{
    None = 0,
    ActualWork = 1,
    RemainingWork = 2,
    BaselineWork = 4,
    Cost = 8,
    ActualCost = 16,
    BaselineCost = 32,
}

public sealed partial class MainViewModel
{
    private UsageDetail _usageDetails;

    /// <summary>Detail rows shown under each row of the usage views, besides Work.</summary>
    public UsageDetail UsageDetails
    {
        get => _usageDetails;
        set
        {
            if (!SetProperty(ref _usageDetails, value))
                return;
            OnPropertyChanged(nameof(ShowActualWorkDetail));
            OnPropertyChanged(nameof(ShowRemainingWorkDetail));
            OnPropertyChanged(nameof(ShowBaselineWorkDetail));
            OnPropertyChanged(nameof(ShowCostDetail));
            OnPropertyChanged(nameof(ShowActualCostDetail));
            OnPropertyChanged(nameof(ShowBaselineCostDetail));
            Refreshed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SetDetail(UsageDetail detail, bool on) => UsageDetails = on ? UsageDetails | detail : UsageDetails & ~detail;

    public bool ShowActualWorkDetail { get => UsageDetails.HasFlag(UsageDetail.ActualWork); set => SetDetail(UsageDetail.ActualWork, value); }
    public bool ShowRemainingWorkDetail { get => UsageDetails.HasFlag(UsageDetail.RemainingWork); set => SetDetail(UsageDetail.RemainingWork, value); }
    public bool ShowBaselineWorkDetail { get => UsageDetails.HasFlag(UsageDetail.BaselineWork); set => SetDetail(UsageDetail.BaselineWork, value); }
    public bool ShowCostDetail { get => UsageDetails.HasFlag(UsageDetail.Cost); set => SetDetail(UsageDetail.Cost, value); }
    public bool ShowActualCostDetail { get => UsageDetails.HasFlag(UsageDetail.ActualCost); set => SetDetail(UsageDetail.ActualCost, value); }
    public bool ShowBaselineCostDetail { get => UsageDetails.HasFlag(UsageDetail.BaselineCost); set => SetDetail(UsageDetail.BaselineCost, value); }
}
