using MicroKit.Domain.Abstractions;

namespace MicroKit.Domain.Enums;

/// <summary>
/// 
/// </summary>
/// <seealso cref="Enumeration" />
public class BillingCycle: Enumeration
{
    /// <summary>
    /// Gets the months.
    /// </summary>
    /// <value>
    /// The months.
    /// </value>
    public int Months { get; private set; }
    /// <summary>
    /// Gets the days.
    /// </summary>
    /// <value>
    /// The days.
    /// </value>
    public int Days => Months == 0 ? 7 : Months * 30; // Approximation pour les mois

    /// <summary>
    /// Initializes a new instance of the <see cref="BillingCycle" /> class.
    /// </summary>
    protected BillingCycle()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BillingCycle" /> class.
    /// </summary>
    /// <param name="id">The identifier.</param>
    /// <param name="months">The months.</param>
    /// <param name="name">The name.</param>
    /// <param name="displayName">The display name.</param>
    /// <param name="description">The description.</param>
    public BillingCycle(
        int id,
        int months,
        string name,
        string? displayName = null,
        string? description = null)
        : base(id, name, displayName, description)
    {
        Months = months;
    }

    /// <summary>
    /// Gets the next date.
    /// </summary>
    /// <param name="currentDate">The current date.</param>
    /// <returns></returns>
    public DateTimeOffset GetNextDate(DateTimeOffset currentDate)
    {
        if (Months == 0)
            return currentDate.AddDays(7); // Weekly

        if (Months % 12 == 0)
            return currentDate.AddYears(Months / 12);

        return currentDate.AddMonths(Months);
    }

    /// <summary>
    /// The weekly
    /// </summary>
    public static readonly BillingCycle Weekly = new(1,0, nameof(Weekly), "Hebdomadaire", "Cycle hebdomadaire");
    /// <summary>
    /// The monthly
    /// </summary>
    public static readonly BillingCycle Monthly = new(2, 1, nameof(Monthly), "Mensuel", "Cycle mensuel");
    /// <summary>
    /// The quarterly
    /// </summary>
    public static readonly BillingCycle Quarterly = new(3, 3, nameof(Quarterly), "Trimestriel", "Cycle trimestriel");
    /// <summary>
    /// The semi annually
    /// </summary>
    public static readonly BillingCycle SemiAnnually = new(4, 6, nameof(SemiAnnually), "Semestriel", "Cycle semestriel");
    /// <summary>
    /// The annually
    /// </summary>
    public static readonly BillingCycle Annually = new(5, 12, nameof(Annually), "Annuel", "Cycle annuel");
    /// <summary>
    /// The biannually
    /// </summary>
    public static readonly BillingCycle Biannually = new(6, 24, nameof(Biannually), "Bi-annuel", "Cycle biannuel");
    /// <summary>
    /// The triannually
    /// </summary>
    public static readonly BillingCycle Triannually = new(7, 36, nameof(Triannually), "Tri-annuel", "Cycle triannuel");

    /// <summary>
    /// Gets the months.
    /// </summary>
    /// <returns></returns>
    public int GetMonths() => Months;

    /// <summary>
    /// Gets the days.
    /// </summary>
    /// <returns></returns>
    public int GetDays() => Days;

    /// <summary>
    /// Calculates the next billing date.
    /// </summary>
    /// <param name="currentDate">The current date.</param>
    /// <returns></returns>
    public DateTimeOffset CalculateNextBillingDate( DateTimeOffset currentDate)
        => GetNextDate(currentDate);

    /// <summary>
    /// Converts to displaystring.
    /// </summary>
    /// <returns></returns>
    public string ToDisplayString()
    {
        if (this == Weekly) return "Weekly";
        if (this == Monthly) return "Monthly";
        if (this == Quarterly) return "Quarterly";
        if (this == SemiAnnually) return "Semi-Annually";
        if (this == Annually) return "Annually";
        if (this == Biannually) return "Biannually";
        if (this == Triannually) return "Triannually";

        return Name;
    }

}
