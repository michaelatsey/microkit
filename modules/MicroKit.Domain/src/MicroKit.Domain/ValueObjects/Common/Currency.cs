using System.Collections.Frozen;
using MicroKit.Domain.Exceptions;

namespace MicroKit.Domain.ValueObjects.Common;

/// <summary>
/// An active ISO 4217 currency: its three-letter code and its minor unit.
/// </summary>
/// <remarks>
/// <para>
/// Obtain an instance through <see cref="FromCode(string)"/>, which accepts only the active codes
/// of ISO 4217 List One. The minor unit is the number of decimals the currency settles in:
/// <c>0</c> for XOF and JPY, <c>2</c> for EUR, <c>3</c> for TND and KWD.
/// </para>
/// <para>
/// Codes whose minor unit List One gives as <c>N.A.</c> (precious metals, SDR, test and
/// no-currency codes) are not accepted: they carry no minor-unit scale a <see cref="Money"/> can
/// be held to.
/// </para>
/// <para>
/// <c>default(Currency)</c> is not a currency: its <see cref="Code"/> is <see langword="null"/>,
/// and <see cref="Money"/> rejects it. A serializer or a persistence converter must therefore
/// rebuild an instance through <see cref="FromCode(string)"/>: one that fills the struct through
/// its default constructor and skips the get-only properties produces <c>default(Currency)</c>.
/// </para>
/// </remarks>
public readonly record struct Currency : IValueObject
{
    // ISO 4217 List One, active codes with a numeric minor unit.
    // Source: SIX Financial Information, list-one.xml, edition published 2026-09-17.
    private static readonly FrozenDictionary<string, int> MinorUnits =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
        ["AED"] = 2, ["AFN"] = 2, ["ALL"] = 2, ["AMD"] = 2, ["AOA"] = 2, ["ARS"] = 2, ["AUD"] = 2, ["AWG"] = 2,
        ["AZN"] = 2, ["BAM"] = 2, ["BBD"] = 2, ["BDT"] = 2, ["BHD"] = 3, ["BIF"] = 0, ["BMD"] = 2, ["BND"] = 2,
        ["BOB"] = 2, ["BOV"] = 2, ["BRL"] = 2, ["BSD"] = 2, ["BTN"] = 2, ["BWP"] = 2, ["BYN"] = 2, ["BZD"] = 2,
        ["CAD"] = 2, ["CDF"] = 2, ["CHE"] = 2, ["CHF"] = 2, ["CHW"] = 2, ["CLF"] = 4, ["CLP"] = 0, ["CNY"] = 2,
        ["COP"] = 2, ["COU"] = 2, ["CRC"] = 2, ["CUP"] = 2, ["CVE"] = 2, ["CZK"] = 2, ["DJF"] = 0, ["DKK"] = 2,
        ["DOP"] = 2, ["DZD"] = 2, ["EGP"] = 2, ["ERN"] = 2, ["ETB"] = 2, ["EUR"] = 2, ["FJD"] = 2, ["FKP"] = 2,
        ["GBP"] = 2, ["GEL"] = 2, ["GHS"] = 2, ["GIP"] = 2, ["GMD"] = 2, ["GNF"] = 0, ["GTQ"] = 2, ["GYD"] = 2,
        ["HKD"] = 2, ["HNL"] = 2, ["HTG"] = 2, ["HUF"] = 2, ["IDR"] = 2, ["ILS"] = 2, ["INR"] = 2, ["IQD"] = 3,
        ["IRR"] = 2, ["ISK"] = 0, ["JMD"] = 2, ["JOD"] = 3, ["JPY"] = 0, ["KES"] = 2, ["KGS"] = 2, ["KHR"] = 2,
        ["KMF"] = 0, ["KPW"] = 2, ["KRW"] = 0, ["KWD"] = 3, ["KYD"] = 2, ["KZT"] = 2, ["LAK"] = 2, ["LBP"] = 2,
        ["LKR"] = 2, ["LRD"] = 2, ["LSL"] = 2, ["LYD"] = 3, ["MAD"] = 2, ["MDL"] = 2, ["MGA"] = 2, ["MKD"] = 2,
        ["MMK"] = 2, ["MNT"] = 2, ["MOP"] = 2, ["MRU"] = 2, ["MUR"] = 2, ["MVR"] = 2, ["MWK"] = 2, ["MXN"] = 2,
        ["MXV"] = 2, ["MYR"] = 2, ["MZN"] = 2, ["NAD"] = 2, ["NGN"] = 2, ["NIO"] = 2, ["NOK"] = 2, ["NPR"] = 2,
        ["NZD"] = 2, ["OMR"] = 3, ["PAB"] = 2, ["PEN"] = 2, ["PGK"] = 2, ["PHP"] = 2, ["PKR"] = 2, ["PLN"] = 2,
        ["PYG"] = 0, ["QAR"] = 2, ["RON"] = 2, ["RSD"] = 2, ["RUB"] = 2, ["RWF"] = 0, ["SAR"] = 2, ["SBD"] = 2,
        ["SCR"] = 2, ["SDG"] = 2, ["SEK"] = 2, ["SGD"] = 2, ["SHP"] = 2, ["SLE"] = 2, ["SOS"] = 2, ["SRD"] = 2,
        ["SSP"] = 2, ["STN"] = 2, ["SVC"] = 2, ["SYP"] = 2, ["SZL"] = 2, ["THB"] = 2, ["TJS"] = 2, ["TMT"] = 2,
        ["TND"] = 3, ["TOP"] = 2, ["TRY"] = 2, ["TTD"] = 2, ["TWD"] = 2, ["TZS"] = 2, ["UAH"] = 2, ["UGX"] = 0,
        ["USD"] = 2, ["USN"] = 2, ["UYI"] = 0, ["UYU"] = 2, ["UYW"] = 4, ["UZS"] = 2, ["VED"] = 2, ["VES"] = 2,
        ["VND"] = 0, ["VUV"] = 0, ["WST"] = 2, ["XAD"] = 2, ["XAF"] = 0, ["XCD"] = 2, ["XCG"] = 2, ["XOF"] = 0,
        ["XPF"] = 0, ["YER"] = 2, ["ZAR"] = 2, ["ZMW"] = 2, ["ZWG"] = 2,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private Currency(string code, int minorUnit)
    {
        Code = code;
        MinorUnit = minorUnit;
    }

    /// <summary>
    /// Gets the three-letter ISO 4217 code, in upper case.
    /// </summary>
    public string Code { get; }

    /// <summary>
    /// Gets the number of decimals the currency settles in.
    /// </summary>
    public int MinorUnit { get; }

    /// <summary>
    /// Returns the currency identified by an ISO 4217 code.
    /// </summary>
    /// <param name="code">The code. Case and surrounding white space are ignored.</param>
    /// <returns>The currency, with its minor unit.</returns>
    /// <exception cref="DomainException">
    /// Thrown when the code is empty or is not an active ISO 4217 code with a minor unit.
    /// </exception>
    public static Currency FromCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new DomainException("Currency code cannot be null or empty.");

        var normalized = code.Trim().ToUpperInvariant();

        if (!MinorUnits.TryGetValue(normalized, out var minorUnit))
            throw new DomainException($"'{normalized}' is not an active ISO 4217 currency code.");

        return new Currency(normalized, minorUnit);
    }

    /// <summary>
    /// Returns the ISO 4217 code.
    /// </summary>
    /// <returns>The currency code, or an empty string for <c>default(Currency)</c>.</returns>
    public override string ToString() => Code ?? string.Empty;
}
