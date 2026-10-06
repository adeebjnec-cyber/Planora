namespace Planora.Engine.Model;

/// <summary>
/// Earned-value analysis fields, using Microsoft Project's names:
/// BCWS (planned value), BCWP (earned value), ACWP (actual cost), BAC (budget at completion).
/// </summary>
public sealed record EarnedValue(decimal Bcws, decimal Bcwp, decimal Acwp, decimal Bac)
{
    public static readonly EarnedValue Empty = new(0, 0, 0, 0);

    /// <summary>Schedule variance (BCWP - BCWS).</summary>
    public decimal Sv => Bcwp - Bcws;

    /// <summary>Cost variance (BCWP - ACWP).</summary>
    public decimal Cv => Bcwp - Acwp;

    /// <summary>Schedule performance index; null when nothing was planned yet.</summary>
    public decimal? Spi => Bcws == 0 ? null : Bcwp / Bcws;

    /// <summary>Cost performance index; null when nothing has been spent yet.</summary>
    public decimal? Cpi => Acwp == 0 ? null : Bcwp / Acwp;

    /// <summary>Estimate at completion: actual cost plus remaining budgeted work at the current cost performance.</summary>
    public decimal Eac => Cpi is { } cpi and not 0 ? Acwp + (Bac - Bcwp) / cpi : Bac;

    /// <summary>Variance at completion (BAC - EAC).</summary>
    public decimal Vac => Bac - Eac;

    public static EarnedValue operator +(EarnedValue a, EarnedValue b) =>
        new(a.Bcws + b.Bcws, a.Bcwp + b.Bcwp, a.Acwp + b.Acwp, a.Bac + b.Bac);
}
