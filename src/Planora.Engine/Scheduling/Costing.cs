using Planora.Engine.Model;

namespace Planora.Engine.Scheduling;

/// <summary>Assignment cost from the resource's rate tables, and actual work/cost from progress.</summary>
internal static class Costing
{
    public static void Calculate(Assignment a)
    {
        var resource = a.Resource;
        int table = Math.Clamp(a.CostRateTable, 0, Resource.RateTableCount - 1);
        double percent = a.Task.PercentComplete;
        decimal perUse = resource.Type == ResourceType.Cost ? 0 : resource.RateAt(table, a.Start).CostPerUse;

        switch (resource.Type)
        {
            case ResourceType.Work:
                a.Cost = WorkCost(a, table) + perUse;
                a.ActualWorkMinutes = (long)Math.Round(a.WorkMinutes * percent / 100.0);
                break;
            case ResourceType.Material:
                a.Cost = (decimal)a.Units * resource.RateAt(table, a.Start).StandardRate + perUse;
                a.ActualWorkMinutes = 0;
                break;
            default:
                a.Cost = a.CostAmount;
                a.ActualWorkMinutes = 0;
                break;
        }

        // Per-use cost is incurred as soon as the assignment starts; the rest follows the accrual method.
        a.ActualCost = resource.AccrueAt == CostAccrual.Prorated
            ? (percent > 0 ? perUse : 0) + Accrue(a.Cost - perUse, CostAccrual.Prorated, percent)
            : Accrue(a.Cost, resource.AccrueAt, percent);
    }

    public static decimal Accrue(decimal amount, CostAccrual accrual, double percentComplete) => accrual switch
    {
        CostAccrual.Start => percentComplete > 0 ? amount : 0,
        CostAccrual.End => percentComplete >= 100 ? amount : 0,
        _ => amount * (decimal)percentComplete / 100m,
    };

    /// <summary>Work cost, applying each dated rate to the work done while it is in effect.</summary>
    private static decimal WorkCost(Assignment a, int table)
    {
        if (a.Finish <= a.Start)
            return Math.Round((decimal)(a.WorkMinutes / 60.0) * a.Resource.RateAt(table, a.Start).StandardRate, 2);

        var rates = a.Resource.CostRateTables[table];
        decimal cost = 0;
        for (int i = 0; i < rates.Count; i++)
        {
            var from = rates[i].EffectiveFrom > a.Start ? rates[i].EffectiveFrom : a.Start;
            var to = i + 1 < rates.Count && rates[i + 1].EffectiveFrom < a.Finish ? rates[i + 1].EffectiveFrom : a.Finish;
            if (to > from)
                cost += (decimal)(Timephased.WorkBetween(a, from, to) / 60.0) * rates[i].StandardRate;
        }
        return Math.Round(cost, 2);
    }
}
