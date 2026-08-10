namespace GrevHome.Apps;

public static class AppIdentity
{
    public const int MaxAppIdLength = 80;

    public static string ValidateAppId(string appId)
    {
        if (string.IsNullOrWhiteSpace(appId) ||
            appId.Length > MaxAppIdLength ||
            appId.Any(character =>
                !(character is >= 'a' and <= 'z') &&
                !(character is >= '0' and <= '9') &&
                character is not '.' and not '-'))
        {
            throw new ArgumentException(
                "AppId must contain only lowercase ASCII letters, digits, '.' or '-' and be 80 characters or fewer.",
                nameof(appId));
        }

        return appId;
    }
}
