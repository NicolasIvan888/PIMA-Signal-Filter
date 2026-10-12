using System.Text;

namespace PimaFilter.Protocol;

public sealed record PimaMessage(
    bool IsValid,
    bool IsCommunicationProbe,
    string? RawObjectCode,
    string? ObjectCode,
    string? EventField,
    string? Qualifier,
    string? Checksum,
    string Ascii,
    string Hex,
    string? ValidationError)
{
    public static PimaMessage Parse(
        ReadOnlySpan<byte> frame,
        string objectPrefix,
        string frameFormat = "Andromeda38",
        string objectCodeBase = "",
        int numericObjectDigits = 5)
    {
        var ascii = Encoding.ASCII.GetString(frame);
        var hex = Convert.ToHexString(frame);

        // PIMA Guard uses '?' as a connection-supervision probe. It is not
        // an alarm frame and EvMan does not acknowledge it.
        if (frame.Length == 1 && frame[0] == (byte)'?')
        {
            return new PimaMessage(
                true, true, null, null, "Verificare comunicatie PIMA Guard", null, null, ascii, hex, null);
        }

        if (frameFormat == "Atia13")
        {
            if (frame.Length != 13 || frame[11] != 0x0A || frame[12] != 0x0D
                || frame[..5].ToArray().Any(value => !IsAlphaNumeric(value))
                || frame[5] != (byte)'-' || frame[6] != (byte)'-'
                || frame[7] != (byte)'-' || frame[8] != (byte)'-')
            {
                return Invalid(ascii, hex, "Cadru Atia13 invalid (5 caractere alfanumerice, ----, 2 caractere, LF/CR).");
            }

            var rawAccount = ascii[..5].ToUpperInvariant();
            var atiaObjectCode = BuildObjectCode(rawAccount, objectPrefix, objectCodeBase, isAndromeda38: false, numericObjectDigits);
            return new PimaMessage(
                true, false, rawAccount, atiaObjectCode, ascii.Substring(9, 2), null, null, ascii, hex, null);
        }

        if (frame.Length != 38)
        {
            return Invalid(ascii, hex, $"Lungime invalida: {frame.Length}, asteptat 38.");
        }

        if (frame[0] != 0x0A || frame[^1] != 0x0D)
        {
            return Invalid(ascii, hex, "Mesajul nu este delimitat de LF/CR.");
        }

        var body = ascii[1..^1];
        var rawObject = body.Substring(19, 6).ToUpperInvariant();
        if (rawObject.Any(character => !IsAlphaNumeric((byte)character)))
        {
            return Invalid(ascii, hex, "Codul brut nu contine exact sase caractere alfanumerice.");
        }

        var objectCode = BuildObjectCode(rawObject, objectPrefix, objectCodeBase, isAndromeda38: true, numericObjectDigits);
        return new PimaMessage(
            true,
            false,
            rawObject,
            objectCode,
            body[..19].TrimEnd('_'),
            body.Substring(25, 9).TrimEnd('_'),
            body.Substring(34, 2),
            ascii,
            hex,
            null);
    }

    private static PimaMessage Invalid(string ascii, string hex, string error) =>
        new(false, false, null, null, null, null, null, ascii, hex, error);

    private static string? BuildObjectCode(
        string rawObject,
        string objectPrefix,
        string objectCodeBase,
        bool isAndromeda38,
        int numericObjectDigits)
    {
        if (int.TryParse(objectCodeBase, out var numericBase)
            && objectCodeBase.Length == 6
            && rawObject.Length >= 5)
        {
            var numericField = isAndromeda38 ? rawObject[1..] : rawObject;
            if (numericObjectDigits is < 1 or > 5 || numericField.Length < numericObjectDigits)
            {
                return null;
            }

            var realObject = numericField[^numericObjectDigits..];
            if (int.TryParse(realObject, out var objectNumber))
            {
                return (numericBase + objectNumber).ToString("D6");
            }

            // Preserve relaying while refusing to compare an alphanumeric
            // frame with a numeric blacklist formula.
            return null;
        }

        return objectPrefix.Length == 2
            ? objectPrefix + (isAndromeda38 ? rawObject[2..] : rawObject[1..])
            : null;
    }

    private static bool IsAlphaNumeric(byte value) =>
        value is >= (byte)'0' and <= (byte)'9'
        || value is >= (byte)'A' and <= (byte)'Z'
        || value is >= (byte)'a' and <= (byte)'z';
}
