using System.IO.MemoryMappedFiles;
using System.Text.Json;

namespace AIReviewDesk.Infrastructure;

public enum CopilotConfigBlock
{
    Hooks, Mcp, Plugins, Extensions, Agents, Skills, LanguageServer, Provider,
    Authority, UnknownStructure, UnexpectedSchema, MalformedJson, Unreadable
}

public sealed class CopilotConfigBlockedException : InvalidOperationException
{
    public CopilotConfigBlock Category { get; }
    public CopilotConfigBlockedException(CopilotConfigBlock category)
        : base($"Copilot config.json was blocked: {category}. Credential values were not decoded or returned.") => Category = category;
}

// No identity, token, arbitrary property name, parser error, raw bytes or raw JSON can escape this API.
public readonly record struct CopilotConfigInspection(bool CredentialFieldPresent, bool AccountMetadataPresent, bool SavedAccountPresent = false);

/// <summary>
/// Narrow 1.0.91 structural contract. String VALUES are never decoded, compared, copied or hashed.
/// Only PROPERTY NAMES are matched against fixed literals. Sensitive fields are syntax-validated
/// and skipped in-place by Utf8JsonReader; unknown structures fail closed.
/// </summary>
public static class CopilotConfigScanner
{
    public const int MaximumFileBytes = 1024 * 1024;

    // A read-only mapped view avoids a managed file buffer, secret-bearing strings/objects,
    // token-sized allocations and buffer copies. The view is released before returning.
    // Unsafe code is confined to borrowing/releasing this OS-owned view, never writing it.
    public static unsafe CopilotConfigInspection InspectFile(string path)
        => InspectMappedFile(path, settingsOnly: false);

    public static void InspectSettingsFile(string path) => InspectMappedFile(path, settingsOnly: true);

    private static unsafe CopilotConfigInspection InspectMappedFile(string path, bool settingsOnly)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length is <= 0 or > MaximumFileBytes) throw Block(CopilotConfigBlock.UnexpectedSchema);
            using var map = MemoryMappedFile.CreateFromFile(file, null, 0, MemoryMappedFileAccess.Read,
                HandleInheritability.None, leaveOpen: true);
            using var view = map.CreateViewAccessor(0, file.Length, MemoryMappedFileAccess.Read);
            byte* pointer = null;
            view.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
            try { return Inspect(new ReadOnlySpan<byte>(pointer + view.PointerOffset, checked((int)file.Length)), settingsOnly); }
            finally { view.SafeMemoryMappedViewHandle.ReleasePointer(); }
        }
        catch (CopilotConfigBlockedException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { throw Block(CopilotConfigBlock.Unreadable); } // Deliberately no inner exception/input-bearing diagnostic.
    }

    public static CopilotConfigInspection Inspect(ReadOnlySpan<byte> utf8) => Inspect(utf8, settingsOnly: false);

    private static CopilotConfigInspection Inspect(ReadOnlySpan<byte> utf8, bool settingsOnly)
    {
        try
        {
            if (utf8.Length is <= 0 or > MaximumFileBytes) throw Block(CopilotConfigBlock.UnexpectedSchema);
            // CLI-generated config has a managed-file comment. Comments are inert and never decoded.
            var reader = new Utf8JsonReader(utf8, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, MaxDepth = 32 });
            RequireRead(ref reader, JsonTokenType.StartObject);
            var seen = new HashSet<Field>();
            var credentials = false;
            var metadata = false;
            var savedAccount = false;
            while (ReadMember(ref reader, seen) is { } field)
            {
                if (settingsOnly)
                {
                    if (!((field == Field.DisableAllHooks && reader.TokenType == JsonTokenType.True) ||
                          (field == Field.Experimental && reader.TokenType == JsonTokenType.False)))
                        throw Block(Classify(field));
                    continue;
                }
                switch (field)
                {
                    case Field.LoggedInUsers:
                        metadata = true;
                        Require(ref reader, JsonTokenType.StartArray);
                        while (Read(ref reader) != JsonTokenType.EndArray) { Account(ref reader); savedAccount = true; }
                        break;
                    case Field.LastLoggedInUser:
                        metadata = true;
                        if (reader.TokenType != JsonTokenType.Null) { Account(ref reader); savedAccount = true; }
                        break;
                    case Field.AuthTokens:
                        credentials = true;
                        TokenMap(ref reader);
                        break;
                    case Field.FirstLaunchAt:
                        Require(ref reader, JsonTokenType.String);
                        reader.Skip(); // Shipped startup timestamp/state; not a command or permission source.
                        break;
                    case Field.AppTipShown:
                        if (reader.TokenType is not (JsonTokenType.True or JsonTokenType.False)) throw Block(CopilotConfigBlock.UnexpectedSchema);
                        break;
                    case Field.ReasoningSummariesCleanupDone:
                        // Accept completed migration only; do not initiate unverified legacy cleanup during review.
                        if (reader.TokenType != JsonTokenType.True) throw Block(CopilotConfigBlock.Authority);
                        break;
                    // Empty installed-plugin and trust lists carry no execution authority.
                    // Nonempty values are blocked rather than treated as opaque metadata.
                    case Field.InstalledPlugins:
                        Require(ref reader, JsonTokenType.StartObject);
                        if (Read(ref reader) != JsonTokenType.EndObject) throw Block(CopilotConfigBlock.Plugins);
                        break;
                    case Field.TrustedFolders:
                        Require(ref reader, JsonTokenType.StartArray);
                        if (Read(ref reader) != JsonTokenType.EndArray) throw Block(CopilotConfigBlock.Authority);
                        break;
                    default: throw Block(Classify(field));
                }
            }
            if (settingsOnly && !seen.Contains(Field.DisableAllHooks)) throw Block(CopilotConfigBlock.UnexpectedSchema);
            if (reader.Read()) throw Block(CopilotConfigBlock.UnexpectedSchema);
            return new(credentials, metadata, savedAccount);
        }
        catch (CopilotConfigBlockedException) { throw; }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        { throw Block(CopilotConfigBlock.MalformedJson); }
    }

    private static void Account(ref Utf8JsonReader reader)
    {
        Require(ref reader, JsonTokenType.StartObject);
        var seen = new HashSet<Field>();
        while (ReadMember(ref reader, seen) is { } field)
        {
            if (field is not (Field.Host or Field.Login or Field.Kind)) throw Block(Classify(field));
            Require(ref reader, JsonTokenType.String);
            reader.Skip(); // Account string values also stay undecoded; not a supported identity API.
        }
        if (!seen.Contains(Field.Host) || !seen.Contains(Field.Login)) throw Block(CopilotConfigBlock.UnexpectedSchema);
    }

    private static void TokenMap(ref Utf8JsonReader reader)
    {
        Require(ref reader, JsonTokenType.StartObject);
        while (Read(ref reader) != JsonTokenType.EndObject)
        {
            Require(ref reader, JsonTokenType.PropertyName);
            // Dynamic host:login keys are not decoded, copied, compared or returned.
            RequireRead(ref reader, JsonTokenType.StartObject);
            var seen = new HashSet<Field>();
            while (ReadMember(ref reader, seen) is { } field)
            {
                if (field != Field.Token) throw Block(Classify(field));
                Require(ref reader, JsonTokenType.String);
                reader.Skip(); // Never GetString/GetRawText/ValueSpan/hash/compare on the credential value.
            }
            if (!seen.SetEquals([Field.Token])) throw Block(CopilotConfigBlock.UnexpectedSchema);
        }
    }

    private static Field? ReadMember(ref Utf8JsonReader reader, HashSet<Field> seen)
    {
        if (Read(ref reader) == JsonTokenType.EndObject) return null;
        Require(ref reader, JsonTokenType.PropertyName);
        var field = IdentifyProperty(ref reader);
        if (field == Field.Unknown) throw Block(CopilotConfigBlock.UnknownStructure);
        if (!seen.Add(field)) throw Block(CopilotConfigBlock.UnexpectedSchema);
        Read(ref reader);
        return field;
    }

    private enum Field
    {
        Unknown, LoggedInUsers, LastLoggedInUser, AuthTokens, Host, Login, Kind, Token,
        FirstLaunchAt, AppTipShown, ReasoningSummariesCleanupDone, DisableAllHooks, Experimental,
        InstalledPlugins, TrustedFolders, Hooks, Mcp, Plugins, Extensions, Agents, Skills,
        LanguageServer, Provider, Authority
    }

    private static Field IdentifyProperty(ref Utf8JsonReader reader)
    {
        // ValueTextEquals unescapes PROPERTY NAMES, preventing escaped-key bypasses.
        if (reader.ValueTextEquals("loggedInUsers")) return Field.LoggedInUsers;
        if (reader.ValueTextEquals("lastLoggedInUser")) return Field.LastLoggedInUser;
        if (reader.ValueTextEquals("authTokens")) return Field.AuthTokens;
        if (reader.ValueTextEquals("host")) return Field.Host;
        if (reader.ValueTextEquals("login")) return Field.Login;
        if (reader.ValueTextEquals("token")) return Field.Token;
        if (reader.ValueTextEquals("kind")) return Field.Kind;
        if (reader.ValueTextEquals("firstLaunchAt")) return Field.FirstLaunchAt;
        if (reader.ValueTextEquals("appTipShown")) return Field.AppTipShown;
        if (reader.ValueTextEquals("reasoningSummariesCleanupDone")) return Field.ReasoningSummariesCleanupDone;
        if (reader.ValueTextEquals("disableAllHooks")) return Field.DisableAllHooks;
        if (reader.ValueTextEquals("experimental")) return Field.Experimental;
        if (reader.ValueTextEquals("installedPlugins")) return Field.InstalledPlugins;
        if (reader.ValueTextEquals("trustedFolders") || reader.ValueTextEquals("trusted_folders")) return Field.TrustedFolders;
        if (reader.ValueTextEquals("hooks") || reader.ValueTextEquals("statusLine")) return Field.Hooks;
        if (reader.ValueTextEquals("mcp") || reader.ValueTextEquals("mcpServers") || reader.ValueTextEquals("mcpConfig")) return Field.Mcp;
        if (reader.ValueTextEquals("plugins") || reader.ValueTextEquals("enabledPlugins") || reader.ValueTextEquals("pluginDirectories")) return Field.Plugins;
        if (reader.ValueTextEquals("extensions")) return Field.Extensions;
        if (reader.ValueTextEquals("agents")) return Field.Agents;
        if (reader.ValueTextEquals("skills")) return Field.Skills;
        if (reader.ValueTextEquals("lsp") || reader.ValueTextEquals("lspServers")) return Field.LanguageServer;
        if (reader.ValueTextEquals("providers") || reader.ValueTextEquals("provider") || reader.ValueTextEquals("apiKeyCommand")) return Field.Provider;
        if (reader.ValueTextEquals("permissions") || reader.ValueTextEquals("command") || reader.ValueTextEquals("commands") ||
            reader.ValueTextEquals("allowAll") || reader.ValueTextEquals("customInstructions")) return Field.Authority;
        return Field.Unknown;
    }

    private static CopilotConfigBlock Classify(Field field) => field switch
    {
        Field.Hooks => CopilotConfigBlock.Hooks, Field.Mcp => CopilotConfigBlock.Mcp,
        Field.Plugins or Field.InstalledPlugins => CopilotConfigBlock.Plugins,
        Field.Extensions => CopilotConfigBlock.Extensions, Field.Agents => CopilotConfigBlock.Agents,
        Field.Skills => CopilotConfigBlock.Skills, Field.LanguageServer => CopilotConfigBlock.LanguageServer,
        Field.Provider => CopilotConfigBlock.Provider, Field.Authority or Field.TrustedFolders or Field.DisableAllHooks or Field.Experimental => CopilotConfigBlock.Authority,
        _ => CopilotConfigBlock.UnexpectedSchema
    };

    private static JsonTokenType Read(ref Utf8JsonReader reader) => reader.Read() ? reader.TokenType : throw Block(CopilotConfigBlock.MalformedJson);
    private static void RequireRead(ref Utf8JsonReader reader, JsonTokenType type) { Read(ref reader); Require(ref reader, type); }
    private static void Require(ref Utf8JsonReader reader, JsonTokenType type)
    { if (reader.TokenType != type) throw Block(CopilotConfigBlock.UnexpectedSchema); }
    private static CopilotConfigBlockedException Block(CopilotConfigBlock category) => new(category);
}
