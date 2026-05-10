using System.Reflection;

namespace MicroKit.Domain.Abstractions;

/// <summary>
/// 
/// </summary>
/// <seealso cref="IComparable" />
public abstract class Enumeration : IComparable
{
    /// <summary>
    /// Gets the name.
    /// </summary>
    /// <value>
    /// The name.
    /// </value>
    public string Name { get; private set; } = null!;
    /// <summary>
    /// Gets the display name.
    /// </summary>
    /// <value>
    /// The display name.
    /// </value>
    public string? DisplayName { get; private set; }
    /// <summary>
    /// Gets the description.
    /// </summary>
    /// <value>
    /// The description.
    /// </value>
    public string? Description { get; set; }

    /// <summary>
    /// Gets the identifier.
    /// </summary>
    /// <value>
    /// The identifier.
    /// </value>
    public int Id { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Enumeration" /> class.
    /// </summary>
    protected Enumeration() { }
    /// <summary>
    /// Initializes a new instance of the <see cref="Enumeration" /> class.
    /// </summary>
    /// <param name="id">The identifier.</param>
    /// <param name="name">The name.</param>
    private Enumeration(int id, string name)
    {
        Id = id;
        Name = name;
    }
    /// <summary>
    /// Initializes a new instance of the <see cref="Enumeration" /> class.
    /// </summary>
    /// <param name="id">The identifier.</param>
    /// <param name="name">The name.</param>
    /// <param name="displayName">The display Name.</param>
    protected Enumeration(int id, string name, string? displayName = null)
        : this(id, name)
    {
        DisplayName = displayName;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Enumeration"/> class.
    /// </summary>
    /// <param name="id">The identifier.</param>
    /// <param name="name">The name.</param>
    /// <param name="displayName">The display name.</param>
    /// <param name="description">The description.</param>
    protected Enumeration(int id, string name, string? displayName = null, string? description = null)
        : this(id, name, displayName)
    {
        Description = description;
    }

    /// <summary>
    /// Converts to string.
    /// </summary>
    /// <returns>
    /// A <see cref="string" /> that represents this instance.
    /// </returns>
    public override string ToString() => Name;


    /// <summary>
    /// Gets all.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public static IEnumerable<T> GetAll<T>() where T : Enumeration
    {
        var fields = typeof(T).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);

        return fields.Select(f => f.GetValue(null)).Cast<T>();
    }

    /// <summary>
    /// Determines whether the specified <see cref="object" />, is equal to this instance.
    /// </summary>
    /// <param name="obj">The <see cref="object" /> to compare with this instance.</param>
    /// <returns>
    ///   <c>true</c> if the specified <see cref="object" /> is equal to this instance; otherwise, <c>false</c>.
    /// </returns>
    public override bool Equals(object? obj)
    {
        if (obj is not Enumeration otherValue)
        {
            return false;
        }

        var typeMatches = GetType() == obj.GetType();
        var valueMatches = Id.Equals(otherValue.Id);

        return typeMatches && valueMatches;
    }

    /// <summary>
    /// Returns a hash code for this instance.
    /// </summary>
    /// <returns>
    /// A hash code for this instance, suitable for use in hashing algorithms and data structures like a hash table.
    /// </returns>
    public override int GetHashCode() => Id.GetHashCode();

    /// <summary>
    /// Absolutes the difference.
    /// </summary>
    /// <param name="firstValue">The first value.</param>
    /// <param name="secondValue">The second value.</param>
    /// <returns></returns>
    public static int AbsoluteDifference(Enumeration firstValue, Enumeration secondValue)
    {
        var absoluteDifference = Math.Abs(firstValue.Id - secondValue.Id);
        return absoluteDifference;
    }

    /// <summary>
    /// From the value.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="id">The identifier.</param>
    /// <returns></returns>
    public static T FromId<T>(int id) where T : Enumeration
    {
        var matchingItem = Parse<T, int>(id, "id", item => item.Id == id);
        return matchingItem;
    }

    /// <summary>
    /// From the display name.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="name">The display name.</param>
    /// <returns></returns>
    public static T FromName<T>(string name) where T : Enumeration
    {
        var matchingItem = Parse<T, string>(name, "name", item => string.Equals(item.Name, name, StringComparison.CurrentCultureIgnoreCase));
        return matchingItem;
    }

    /// <summary>
    /// From the display name.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="displayName">The display name.</param>
    /// <returns></returns>
    public static T FromDisplayName<T>(string displayName) where T : Enumeration
    {
        var matchingItem = Parse<T, string>(displayName, "display name", item => string.Equals(item.DisplayName, displayName, StringComparison.CurrentCultureIgnoreCase));
        return matchingItem;
    }


    /// <summary>
    /// Parses the specified value.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <typeparam name="TK">The type of the k.</typeparam>
    /// <param name="value">The value.</param>
    /// <param name="description">The description.</param>
    /// <param name="predicate">The predicate.</param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException">'{value}' is not a valid {description} in {typeof(T)}</exception>
    /// <exception cref="InvalidOperationException">'{value}' is not a valid {description} in {typeof(T)}</exception>
    private static T Parse<T, TK>(TK value, string description, Func<T, bool> predicate) where T : Enumeration
    {
        var matchingItem = GetAll<T>().FirstOrDefault(predicate);

        return matchingItem ?? throw new InvalidOperationException($"'{value}' is not a valid {description} in {typeof(T)}");
    }

    /// <summary>
    /// Compares to.
    /// </summary>
    /// <param name="obj">The other.</param>
    /// <returns>
    /// A value that indicates the relative order of the objects being compared. The return value has these meanings:
    /// <list type="table"><listheader><term> Value</term><description> Meaning</description></listheader><item><term> Less than zero</term><description> This instance precedes <paramref name="obj" /> in the sort order.</description></item><item><term> Zero</term><description> This instance occurs in the same position in the sort order as <paramref name="obj" />.</description></item><item><term> Greater than zero</term><description> This instance follows <paramref name="obj" /> in the sort order.</description></item></list>
    /// </returns>
    public int CompareTo(object? obj) => Id.CompareTo(((Enumeration)obj!).Id);
}
