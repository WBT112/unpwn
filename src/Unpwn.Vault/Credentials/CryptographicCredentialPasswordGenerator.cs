using System.Security.Cryptography;
using System.Text;
using Unpwn.Application.Credentials;
using Unpwn.Core;

namespace Unpwn.Vault.Credentials;

public sealed class CryptographicCredentialPasswordGenerator : ICredentialPasswordGenerator
{
    private const string Lowercase = "abcdefghijkmnopqrstuvwxyz";
    private const string Uppercase = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Digits = "23456789";
    private const string Symbols = "!#$%&()*+,-./:;<=>?@[]^_{|}~";

    public byte[] GenerateUtf8(CredentialGenerationPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        policy.Validate();

        var selectedSets = BuildSelectedSets(policy);
        var allCharacters = string.Concat(selectedSets);
        var characters = new char[policy.Length];
        try
        {
            var index = 0;
            foreach (var set in selectedSets)
            {
                characters[index++] = set[RandomNumberGenerator.GetInt32(set.Length)];
            }

            while (index < characters.Length)
            {
                characters[index++] = allCharacters[RandomNumberGenerator.GetInt32(allCharacters.Length)];
            }

            for (var current = characters.Length - 1; current > 0; current--)
            {
                var swapIndex = RandomNumberGenerator.GetInt32(current + 1);
                (characters[current], characters[swapIndex]) = (characters[swapIndex], characters[current]);
            }

            return Encoding.UTF8.GetBytes(characters);
        }
        finally
        {
            Array.Clear(characters);
        }
    }

    private static string[] BuildSelectedSets(CredentialGenerationPolicy policy)
    {
        var sets = new List<string>(4);
        if (policy.IncludeLowercase)
        {
            sets.Add(Lowercase);
        }

        if (policy.IncludeUppercase)
        {
            sets.Add(Uppercase);
        }

        if (policy.IncludeDigits)
        {
            sets.Add(Digits);
        }

        if (policy.IncludeSymbols)
        {
            sets.Add(Symbols);
        }

        return [.. sets];
    }
}
