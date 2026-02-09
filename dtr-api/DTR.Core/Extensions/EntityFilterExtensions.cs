namespace DTR.Core;

public static class EntityFilterExtensions
{
    public static List<T> ExcludeExistingBy<T>(
        this IEnumerable<T> incoming,
        IEnumerable<T> existing,
        Func<T, int> keySelector)
    {
        var existingKeys = new HashSet<int>(
            existing.Select(keySelector)
        );
        return incoming
            .Where(x => !existingKeys.Contains(keySelector(x)))
            .ToList();
    }
}
