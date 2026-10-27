using System.Security.Cryptography;
using System.Text;
using Google.Cloud.Firestore;

namespace WebBase.Database.Firestore;

/// <summary>Turning the models' values into Firestore's and back, in one place.</summary>
internal static class FirestoreValues
{
    /// <summary>Firestore keeps times as Timestamps (to the microsecond), always UTC.</summary>
    public static Timestamp Time(DateTime t) =>
        Timestamp.FromDateTime(t.Kind == DateTimeKind.Utc ? t : DateTime.SpecifyKind(t.ToUniversalTime(), DateTimeKind.Utc));

    public static object? Time(DateTime? t) => t is { } value ? Time(value) : null;

    public static DateTime TimeOf(DocumentSnapshot doc, string field) => doc.GetValue<Timestamp>(field).ToDateTime();

    public static DateTime? OptionalTimeOf(DocumentSnapshot doc, string field) =>
        doc.TryGetValue<Timestamp?>(field, out var t) && t is { } value ? value.ToDateTime() : null;

    public static string? OptionalString(DocumentSnapshot doc, string field) =>
        doc.TryGetValue<string?>(field, out var s) ? s : null;

    /// <summary>
    /// The id of a "keys" document: the kind of value and a hash of it. Hashed because a
    /// document id can't contain "/", which page addresses (and in principle emails) can.
    /// </summary>
    public static string KeyId(string kind, string value) =>
        $"{kind}-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()}";

    /// <summary>A "keys" document: whose the value is.</summary>
    public static Dictionary<string, object> KeyDoc(string owner) => new() { ["owner"] = owner };
}
