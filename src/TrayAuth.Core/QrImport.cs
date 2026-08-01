namespace TrayAuth.Core;

public sealed record QrImportOutcome(IReadOnlyList<Account> Accounts, IReadOnlyList<string> Notes);

/// <summary>
/// Turns raw QR payloads into importable accounts, whichever flavour they are: Google
/// Authenticator transfer QRs (possibly several, for multi-batch transfers), plain otpauth://
/// enrollment QRs, or things that are not authenticator QRs at all. The notes explain anything
/// that was skipped, in words meant for the import dialog.
/// </summary>
public static class QrImport
{
    public static QrImportOutcome CollectAccounts(IReadOnlyList<string> qrTexts)
    {
        var accounts = new List<Account>();
        var notes = new List<string>();
        var batches = new List<MigrationBatch>();

        int counterBased = 0;
        int unsupported = 0;
        int unrelated = 0;
        int undecodable = 0;

        foreach (string text in qrTexts)
        {
            if (GoogleAuthMigration.IsMigrationUri(text))
            {
                if (GoogleAuthMigration.TryParse(text, out MigrationResult migration, out _))
                {
                    foreach (Account account in migration.Accounts)
                    {
                        AddUnique(accounts, account);
                    }

                    counterBased += migration.SkippedCounterBased;
                    unsupported += migration.SkippedUnsupported;

                    if (migration.Batch is not null)
                    {
                        batches.Add(migration.Batch);
                    }
                }
                else
                {
                    undecodable++;
                }
            }
            else if (text.TrimStart().StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase))
            {
                if (OtpAuthUri.TryParse(text, out Account single, out _))
                {
                    AddUnique(accounts, single);
                }
                else
                {
                    unsupported++;
                }
            }
            else
            {
                unrelated++;
            }
        }

        if (counterBased > 0)
        {
            notes.Add(counterBased == 1
                ? "1 counter-based (HOTP) entry was skipped - TrayAuth only supports time-based codes."
                : $"{counterBased} counter-based (HOTP) entries were skipped - TrayAuth only supports time-based codes.");
        }

        if (unsupported > 0)
        {
            notes.Add(unsupported == 1
                ? "1 entry could not be read (unsupported algorithm or invalid data)."
                : $"{unsupported} entries could not be read (unsupported algorithm or invalid data).");
        }

        if (undecodable > 0)
        {
            notes.Add("A Google Authenticator QR was found but its payload could not be decoded.");
        }

        if (unrelated > 0 && accounts.Count == 0)
        {
            notes.Add("The QR code(s) found do not contain authenticator accounts.");
        }

        AddBatchNote(notes, batches);

        return new QrImportOutcome(accounts, notes);
    }

    /// <summary>
    /// A transfer of many accounts spans several QRs ("QR 1 of 2"). If only part of the set was
    /// scanned, say so - otherwise the user reasonably assumes everything came across.
    /// </summary>
    private static void AddBatchNote(List<string> notes, List<MigrationBatch> batches)
    {
        if (batches.Count == 0)
        {
            return;
        }

        int size = batches.Max(b => b.Size);
        List<int> seen = batches.Select(b => b.Index + 1).Distinct().Order().ToList();

        if (seen.Count >= size)
        {
            return;
        }

        string scanned = string.Join(" and ", seen);
        notes.Add(seen.Count == 1
            ? $"This transfer spans {size} QR codes and only QR {scanned} of {size} was read - the remaining accounts are on the other QR code(s)."
            : $"This transfer spans {size} QR codes and only QRs {scanned} of {size} were read - the remaining accounts are on the other QR code(s).");
    }

    private static void AddUnique(List<Account> accounts, Account candidate)
    {
        bool duplicate = accounts.Any(existing =>
            existing.Matches(candidate)
            && string.Equals(
                Base32.Normalize(existing.Secret),
                Base32.Normalize(candidate.Secret),
                StringComparison.OrdinalIgnoreCase));

        if (!duplicate)
        {
            accounts.Add(candidate);
        }
    }
}
