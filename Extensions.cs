namespace DevTree;

public static class Extensions
{
    public static string ToHexString(this int value)
    {
        return $"0x{value:X}";
    }
}