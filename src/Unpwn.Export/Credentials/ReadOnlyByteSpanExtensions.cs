namespace Unpwn.Export.Credentials;

internal static class ReadOnlyByteSpanExtensions
{
    public static int IndexOfAny(
        this ReadOnlySpan<byte> value,
        byte first,
        byte second,
        byte third,
        byte fourth)
    {
        for (var index = 0; index < value.Length; index++)
        {
            var candidate = value[index];
            if (candidate == first || candidate == second || candidate == third || candidate == fourth)
            {
                return index;
            }
        }

        return -1;
    }
}
