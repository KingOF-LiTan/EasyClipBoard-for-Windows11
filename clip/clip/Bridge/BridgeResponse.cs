namespace clip.Bridge;

internal static class BridgeResponse
{
    public static object Ok()
    {
        return new { success = true };
    }

    public static object Ok(object data)
    {
        return new { success = true, data };
    }

    public static object Fail(string error)
    {
        return new { success = false, error };
    }
}
