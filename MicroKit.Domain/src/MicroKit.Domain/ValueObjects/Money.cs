using MicroKit.Domain.Abstractions;
using MicroKit.Domain.ValueObjects.Exceptions;
using System.Globalization;

namespace MicroKit.Domain.ValueObjects;

/// <summary>Immutable value object representing a monetary amount with an ISO 4217 currency code.</summary>
public sealed class Money : ValueObject
{
    // Built once at class initialisation — O(n) culture scan happens exactly once for the process lifetime.
    private static readonly IReadOnlyDictionary<string, NumberFormatInfo> CurrencyFormatCache = BuildCurrencyFormatCache();

    private static IReadOnlyDictionary<string, NumberFormatInfo> BuildCurrencyFormatCache()
    {
        var result = new Dictionary<string, NumberFormatInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            try
            {
                var region = new RegionInfo(culture.Name);
                result.TryAdd(region.ISOCurrencySymbol, culture.NumberFormat);
            }
            catch (ArgumentException) { /* skip cultures without a valid region */ }
        }
        return result;
    }

    /// <summary>Gets the monetary amount.</summary>
    public decimal Amount { get; }
    /// <summary>Gets the ISO 4217 currency code (e.g. <c>EUR</c>).</summary>
    public string Currency { get; } = null!;

    private Money() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="Money"/> class.
    /// </summary>
    /// <param name="amount">The amount.</param>
    /// <param name="currency">The currency.</param>
    /// <exception cref="ArgumentException">
    /// Amount cannot be negative - amount
    /// or
    /// Currency cannot be empty - currency
    /// or
    /// Currency must be a 3-letter ISO code - currency
    /// </exception>
    public Money(decimal amount, string currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException("Currency cannot be empty.", nameof(currency));

        var upper = currency.Trim().ToUpperInvariant();

        if (upper.Length != 3 || !upper.All(char.IsLetter))
            throw new ArgumentException("Currency must be a 3-letter ISO 4217 code.", nameof(currency));

        Amount = amount;
        Currency = upper;
    }

    /// <summary>Returns a zero-amount money instance for <paramref name="currency"/>.</summary>
    public static Money Zero(string currency) => new(0, currency);
    /// <summary>Creates a new <see cref="Money"/> instance.</summary>
    public static Money Create(decimal amount, string currency) => new(amount, currency);

    // --- LOGIQUE DE PRÉCISION (LE CŒUR FINANCIER) ---

    /// <summary>
    /// Récupère dynamiquement le nombre de décimales via ISO Culture.
    /// Évite le switch manuel et supporte les devises exotiques.
    /// </summary>
    public int DecimalDigits => GetFormatInfo(Currency).CurrencyDecimalDigits;

    /// <summary>
    /// Retourne le montant arrondi pour la facturation/affichage.
    /// Utilise MidpointRounding.AwayFromZero (arrondi comptable standard).
    /// </summary>
    public decimal RoundedAmount => Math.Round(Amount, DecimalDigits, MidpointRounding.AwayFromZero);

    // --- INTÉGRATION STRIPE / PROVIDERS ---

    /// <summary>
    /// Convertit en la plus petite unité (ex: Cents pour EUR, Yen tel quel).
    /// </summary>
    /// <returns></returns>
    public long ToSmallestUnit()
    {
        var factor = (decimal)Math.Pow(10, DecimalDigits);
        // Utilisation de AwayFromZero pour garantir l'arrondi financier correct
        return (long)Math.Round(Amount * factor, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Creates a Money instance from the smallest currency unit (e.g., cents).
    /// </summary>
    public static Money FromSmallestUnit(long stripeAmount, string currency)
    {
        var money = Zero(currency);
        var factor = (decimal)Math.Pow(10, money.DecimalDigits);
        return new Money(stripeAmount / factor, currency);
    }


    // Opérateurs arithmétiques

    /// <summary>Adds two monetary amounts. Both operands must share the same currency.</summary>
    public static Money operator +(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount + right.Amount, left.Currency);
    }
    /// <summary>Subtracts the right amount from the left. Both operands must share the same currency.</summary>
    public static Money operator -(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount - right.Amount, left.Currency);
    }
    /// <summary>Multiplies the monetary amount by a scalar <paramref name="multiplier"/>.</summary>
    public static Money operator *(Money money, decimal multiplier)
    {
        return new Money(money.Amount * multiplier, money.Currency);
    }

    /// <summary>Divides the monetary amount by a scalar <paramref name="divisor"/>.</summary>
    public static Money operator /(Money money, decimal divisor)
    {
        if (divisor == 0)
            throw new DivideByZeroException("Cannot divide money by zero");

        return new Money(money.Amount / divisor, money.Currency);
    }

    // Opérateurs de comparaison
    /// <summary>Returns <see langword="true"/> when <paramref name="left"/> is greater than <paramref name="right"/>.</summary>
    public static bool operator >(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return left.Amount > right.Amount;
    }
    /// <summary>Returns <see langword="true"/> when <paramref name="left"/> is less than <paramref name="right"/>.</summary>
    public static bool operator <(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return left.Amount < right.Amount;
    }
    /// <summary>Returns <see langword="true"/> when <paramref name="left"/> is greater than or equal to <paramref name="right"/>.</summary>
    public static bool operator >=(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return left.Amount >= right.Amount;
    }

    /// <summary>Returns <see langword="true"/> when <paramref name="left"/> is less than or equal to <paramref name="right"/>.</summary>
    public static bool operator <=(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return left.Amount <= right.Amount;
    }

    // Méthodes utilitaires
    /// <summary>Returns <see langword="true"/> when the amount is zero.</summary>
    public bool IsZero() => Amount == 0;
    /// <summary>Returns <see langword="true"/> when the amount is greater than zero.</summary>
    public bool IsPositive() => Amount > 0;
    /// <summary>Returns <see langword="true"/> when the amount is less than zero.</summary>
    public bool IsNegative() => Amount < 0;
    /// <summary>Returns the absolute value of this money.</summary>
    public Money Abs() => new(Math.Abs(Amount), Currency);
    /// <summary>Returns the negated value of this money.</summary>
    public Money Negate() => new(-Amount, Currency);
    /// <summary>Returns a rounded copy of this money using accounting rounding (AwayFromZero).</summary>
    public Money Round(int decimals = 2) =>
        new(Math.Round(Amount, decimals, MidpointRounding.AwayFromZero), Currency);

    // Conversion vers d'autres devises (nécessiterait un service de taux de change)

    /// <summary>Converts this money to another currency using the specified exchange rate.</summary>
    /// <param name="targetCurrency">ISO 4217 target currency code.</param>
    /// <param name="exchangeRate">Positive exchange rate (1 unit of this currency = <paramref name="exchangeRate"/> units of the target).</param>
    public Money ConvertTo(string targetCurrency, decimal exchangeRate)
    {
        if (string.IsNullOrWhiteSpace(targetCurrency))
            throw new ArgumentException("Target currency cannot be empty", nameof(targetCurrency));

        if (exchangeRate <= 0)
            throw new ArgumentException("Exchange rate must be positive", nameof(exchangeRate));

        return new Money(Amount * exchangeRate, targetCurrency);
    }

    // Méthodes de formatage
    /// <inheritdoc/>
    public override string ToString() => RoundedAmount.ToString($"C{DecimalDigits}", GetFormatInfo(Currency));

    /// <summary>Returns a human-readable string with the currency symbol prefix.</summary>
    public string ToDisplayString()
    {
        string format = $"F{DecimalDigits}"; // Donne F0, F2 ou F3 selon la devise
        return Currency switch
        {
            "USD" => $"${Amount.ToString(format)}",
            "EUR" => $"€{Amount.ToString(format)}",
            "JPY" => $"¥{Amount:F0}", // Toujours 0 pour le Yen
            _ => $"{Amount.ToString(format)} {Currency}"
        };
    }


    // Méthodes de calcul pour les abonnements
    /// <summary>
    /// Calculates the proration.
    /// </summary>
    /// <param name="fromDate">From date.</param>
    /// <param name="toDate">To date.</param>
    /// <param name="billingPeriodStart">The billing period start.</param>
    /// <param name="billingPeriodEnd">The billing period end.</param>
    /// <returns></returns>
    /// <exception cref="ArgumentException">
    /// From date must be before to date
    /// or
    /// Billing period start must be before end
    /// </exception>
    public Money CalculateProration(DateTimeOffset fromDate, DateTimeOffset toDate, DateTimeOffset billingPeriodStart, DateTimeOffset billingPeriodEnd)
    {
        if (fromDate >= toDate)
            throw new ArgumentException("From date must be before to date");

        if (billingPeriodStart >= billingPeriodEnd)
            throw new ArgumentException("Billing period start must be before end");

        var totalTicks = (decimal)(billingPeriodEnd - billingPeriodStart).Ticks;
        var usedTicks = (decimal)(toDate - fromDate).Ticks;

        if (totalTicks <= 0) return Zero(Currency);

        // Utilise la précision décimale pour le ratio
        decimal ratio = usedTicks / totalTicks;
        return new Money(this.Amount * ratio, this.Currency);
    }

    /// <summary>
    /// Calculates the discount.
    /// </summary>
    /// <param name="discountPercentage">The discount percentage.</param>
    /// <returns></returns>
    /// <exception cref="ArgumentException">Discount percentage must be between 0 and 100 - discountPercentage</exception>
    public Money CalculateDiscount(decimal discountPercentage)
    {
        if (discountPercentage < 0 || discountPercentage > 100)
            throw new ArgumentException("Discount percentage must be between 0 and 100", nameof(discountPercentage));

        var discountAmount = Amount * (discountPercentage / 100);
        return new Money(discountAmount, Currency);
    }

    /// <summary>
    /// Applies the discount.
    /// </summary>
    /// <param name="discountPercentage">The discount percentage.</param>
    /// <returns></returns>
    public Money ApplyDiscount(decimal discountPercentage)
    {
        var discount = CalculateDiscount(discountPercentage);
        return this - discount;
    }

    /// <summary>
    /// Calculates the tax.
    /// </summary>
    /// <param name="taxRate">The tax rate.</param>
    /// <returns></returns>
    /// <exception cref="ArgumentException">Tax rate cannot be negative - taxRate</exception>
    public Money CalculateTax(decimal taxRate)
    {
        if (taxRate < 0)
            throw new ArgumentException("Tax rate cannot be negative", nameof(taxRate));

        return new Money(Amount * taxRate, Currency);
    }

    /// <summary>
    /// Adds the tax.
    /// </summary>
    /// <param name="taxRate">The tax rate.</param>
    /// <returns></returns>
    public Money AddTax(decimal taxRate)
    {
        var tax = CalculateTax(taxRate);
        return this + tax;
    }

    // Méthodes privées
    /// <summary>
    /// Ensures the same currency.
    /// </summary>
    /// <param name="left">The left.</param>
    /// <param name="right">The right.</param>
    /// <exception cref="CurrencyMismatchException"></exception>
    private static void EnsureSameCurrency(Money left, Money right)
    {
        if (left.Currency != right.Currency)
            throw new CurrencyMismatchException(left.Currency, right.Currency);
    }

    // Méthodes pour les collections
    /// <summary>
    /// Sums the specified amounts.
    /// </summary>
    /// <param name="amounts">The amounts.</param>
    /// <returns></returns>
    /// <exception cref="ArgumentException">Cannot sum empty collection of money amounts</exception>
    /// <exception cref="CurrencyMismatchException">All amounts must have the same currency for summation</exception>
    public static Money Sum(IEnumerable<Money> amounts)
    {
        string? currency = null;
        decimal total = 0;
        bool any = false;

        foreach (var m in amounts)
        {
            any = true;
            if (currency is null)
                currency = m.Currency;
            else if (m.Currency != currency)
                throw new CurrencyMismatchException("All amounts must have the same currency for summation");
            total += m.Amount;
        }

        if (!any)
            throw new ArgumentException("Cannot sum empty collection of money amounts");

        return new Money(total, currency!);
    }

    /// <summary>
    /// Averages the specified amounts.
    /// </summary>
    /// <param name="amounts">The amounts.</param>
    /// <returns></returns>
    /// <exception cref="ArgumentException">Cannot average empty collection of money amounts</exception>
    public static Money Average(IEnumerable<Money> amounts)
    {
        var amountsList = amounts.ToList();
        if (amountsList.Count == 0)
            throw new ArgumentException("Cannot average empty collection of money amounts");

        var sum = Sum(amountsList);
        return sum / amountsList.Count;
    }

    // --- HELPERS DE FORMATAGE ---

    /// <summary>
    /// Gets the atomic values.
    /// </summary>
    /// <returns></returns>
    protected override IEnumerable<object> GetAtomicValues()
    {
        yield return Amount;
        yield return Currency;
    }

    private static NumberFormatInfo GetFormatInfo(string currencyCode)
    {
        return CurrencyFormatCache.TryGetValue(currencyCode, out var info)
            ? info
            : CultureInfo.InvariantCulture.NumberFormat;
    }

    /// <summary>Commonly used ISO 4217 currency codes.</summary>
    public static class Currencies
    {
        /// <summary>United States Dollar.</summary>
        public const string USD = "USD";
        /// <summary>Euro.</summary>
        public const string EUR = "EUR";
        /// <summary>British Pound Sterling.</summary>
        public const string GBP = "GBP";
        /// <summary>Japanese Yen.</summary>
        public const string JPY = "JPY";
        /// <summary>Canadian Dollar.</summary>
        public const string CAD = "CAD";
        /// <summary>Australian Dollar.</summary>
        public const string AUD = "AUD";
        /// <summary>Swiss Franc.</summary>
        public const string CHF = "CHF";
        /// <summary>Chinese Yuan Renminbi.</summary>
        public const string CNY = "CNY";
    }
}

