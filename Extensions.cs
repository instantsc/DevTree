namespace DevTree;

public static class Extensions
{
    public static string ToHexString(this int? value)
    {
        return value == null ? null : $"0x{value:X}";
    }
}