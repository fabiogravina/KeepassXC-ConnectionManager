using System.Buffers.Binary;
using System.Globalization;
using System.Xml;
using Kxc.Core.Entries;
using Kxc.Core.Vault;

namespace Kxc.Vault;

/// <summary>
/// Legge in streaming l'XML di "keepassxc-cli export". I valori protetti (password, campi protetti),
/// gli allegati binari e la cronologia delle voci vengono saltati senza mai diventare stringhe.
/// Le voci nel Cestino non vengono restituite.
/// </summary>
internal static class KeePassXcExportParser
{
    private static readonly HashSet<string> StandardFields = new(StringComparer.Ordinal)
    {
        "Title", "UserName", "Password", "URL", "Notes",
    };

    public static VaultSnapshot Parse(Stream xml)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true,
            CloseInput = false,
        };

        using var reader = XmlReader.Create(xml, settings);
        var state = new ParseState();

        reader.MoveToContent();
        if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "KeePassFile")
        {
            throw new XmlException("Elemento KeePassFile mancante.");
        }

        ForEachChild(reader, () =>
        {
            switch (reader.LocalName)
            {
                case "Meta":
                    ReadMeta(reader, state);
                    break;
                case "Root":
                    ReadRoot(reader, state);
                    break;
                default:
                    reader.Skip();
                    break;
            }
        });

        return new VaultSnapshot(state.DatabaseName, state.Entries);
    }

    private static void ReadMeta(XmlReader reader, ParseState state)
    {
        ForEachChild(reader, () =>
        {
            switch (reader.LocalName)
            {
                case "DatabaseName":
                    state.DatabaseName = reader.ReadElementContentAsString();
                    break;
                case "RecycleBinEnabled":
                    state.RecycleBinEnabled = ReadBool(reader);
                    break;
                case "RecycleBinUUID":
                    state.RecycleBinUuid = ReadUuid(reader);
                    break;
                default:
                    // Comprende Binaries: gli allegati non vengono letti.
                    reader.Skip();
                    break;
            }
        });
    }

    private static void ReadRoot(XmlReader reader, ParseState state)
    {
        ForEachChild(reader, () =>
        {
            if (reader.LocalName == "Group")
            {
                ReadGroup(reader, state, parentPath: [], isRoot: true);
            }
            else
            {
                reader.Skip();
            }
        });
    }

    /// <summary>KeePassXC scrive UUID e Name del gruppo prima dei gruppi figli e delle voci.</summary>
    private static void ReadGroup(XmlReader reader, ParseState state, IReadOnlyList<string> parentPath, bool isRoot)
    {
        var uuid = Guid.Empty;
        var name = string.Empty;

        ForEachChild(reader, () =>
        {
            switch (reader.LocalName)
            {
                case "UUID":
                    uuid = ReadUuid(reader);
                    break;
                case "Name":
                    name = reader.ReadElementContentAsString();
                    break;
                case "Group" or "Entry" when state.IsRecycleBin(uuid):
                    reader.Skip();
                    break;
                case "Group":
                    ReadGroup(reader, state, ChildPath(parentPath, name, isRoot), isRoot: false);
                    break;
                case "Entry":
                    state.Entries.Add(ReadEntry(reader, ChildPath(parentPath, name, isRoot)));
                    break;
                default:
                    reader.Skip();
                    break;
            }
        });
    }

    private static IReadOnlyList<string> ChildPath(IReadOnlyList<string> parentPath, string name, bool isRoot) =>
        isRoot ? parentPath : [.. parentPath, name];

    private static VaultEntry ReadEntry(XmlReader reader, IReadOnlyList<string> groupPath)
    {
        var uuid = Guid.Empty;
        var tags = string.Empty;
        DateTimeOffset? expiresAt = null;
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        var customFields = new Dictionary<string, string>(StringComparer.Ordinal);
        var attachments = new List<string>();

        ForEachChild(reader, () =>
        {
            switch (reader.LocalName)
            {
                case "UUID":
                    uuid = ReadUuid(reader);
                    break;
                case "Tags":
                    tags = reader.ReadElementContentAsString();
                    break;
                case "Times":
                    expiresAt = ReadExpiry(reader);
                    break;
                case "String":
                    ReadString(reader, fields, customFields);
                    break;
                case "Binary":
                    if (ReadBinaryName(reader) is { } attachment)
                    {
                        attachments.Add(attachment);
                    }

                    break;
                default:
                    // Comprende History: le versioni precedenti della voce non contano.
                    reader.Skip();
                    break;
            }
        });

        return new VaultEntry
        {
            Uuid = uuid,
            Title = fields.GetValueOrDefault("Title", string.Empty),
            UserName = fields.GetValueOrDefault("UserName", string.Empty),
            Url = fields.GetValueOrDefault("URL", string.Empty),
            Notes = fields.GetValueOrDefault("Notes", string.Empty),
            Tags = tags,
            GroupPath = groupPath,
            CustomFields = customFields,
            AttachmentNames = attachments,
            ExpiresAt = expiresAt,
        };
    }

    /// <summary>
    /// &lt;String&gt;&lt;Key/&gt;&lt;Value/&gt;&lt;/String&gt;. La password e ogni valore con ProtectInMemory="True"
    /// vengono saltati con Skip, senza leggerne il contenuto.
    /// </summary>
    private static void ReadString(XmlReader reader, Dictionary<string, string> fields, Dictionary<string, string> customFields)
    {
        string? key = null;
        string? value = null;

        ForEachChild(reader, () =>
        {
            switch (reader.LocalName)
            {
                case "Key":
                    key = reader.ReadElementContentAsString();
                    break;
                case "Value" when key == "Password" || reader.GetAttribute("ProtectInMemory") == "True":
                    reader.Skip();
                    break;
                case "Value":
                    value = reader.ReadElementContentAsString();
                    break;
                default:
                    reader.Skip();
                    break;
            }
        });

        if (key is null || value is null)
        {
            return;
        }

        if (StandardFields.Contains(key))
        {
            fields[key] = value;
        }
        else
        {
            customFields[key] = value;
        }
    }

    private static string? ReadBinaryName(XmlReader reader)
    {
        string? key = null;
        ForEachChild(reader, () =>
        {
            if (reader.LocalName == "Key")
            {
                key = reader.ReadElementContentAsString();
            }
            else
            {
                reader.Skip();
            }
        });
        return key;
    }

    private static DateTimeOffset? ReadExpiry(XmlReader reader)
    {
        var expires = false;
        DateTimeOffset? expiryTime = null;

        ForEachChild(reader, () =>
        {
            switch (reader.LocalName)
            {
                case "Expires":
                    expires = ReadBool(reader);
                    break;
                case "ExpiryTime":
                    expiryTime = ParseTime(reader.ReadElementContentAsString());
                    break;
                default:
                    reader.Skip();
                    break;
            }
        });

        return expires ? expiryTime : null;
    }

    /// <summary>Data ISO 8601 (KDBX 3.1) o base64 dei secondi dal 1/1/0001 (KDBX 4).</summary>
    internal static DateTimeOffset? ParseTime(string text)
    {
        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var iso))
        {
            return iso;
        }

        Span<byte> bytes = stackalloc byte[8];
        if (Convert.TryFromBase64String(text, bytes, out var written) && written == 8)
        {
            var seconds = BinaryPrimitives.ReadInt64LittleEndian(bytes);
            if (seconds >= 0 && seconds <= (long)(DateTime.MaxValue - DateTime.MinValue).TotalSeconds)
            {
                return new DateTimeOffset(DateTime.MinValue.AddSeconds(seconds), TimeSpan.Zero);
            }
        }

        return null;
    }

    /// <summary>UUID in base64, nello stesso ordine dei byte mostrato da keepassxc-cli.</summary>
    private static Guid ReadUuid(XmlReader reader)
    {
        var text = reader.ReadElementContentAsString();
        Span<byte> bytes = stackalloc byte[16];
        return Convert.TryFromBase64String(text, bytes, out var written) && written == 16
            ? new Guid(bytes, bigEndian: true)
            : Guid.Empty;
    }

    private static bool ReadBool(XmlReader reader) =>
        string.Equals(reader.ReadElementContentAsString(), "True", StringComparison.OrdinalIgnoreCase);

    /// <summary>Visita i figli dell'elemento corrente; ogni azione deve consumare l'elemento figlio.</summary>
    private static void ForEachChild(XmlReader reader, Action onChild)
    {
        if (reader.IsEmptyElement)
        {
            reader.Read();
            return;
        }

        reader.Read();
        while (reader.MoveToContent() == XmlNodeType.Element)
        {
            onChild();
        }

        if (reader.NodeType == XmlNodeType.EndElement)
        {
            reader.Read();
        }
        else if (reader.NodeType == XmlNodeType.Text)
        {
            throw new XmlException($"Testo inatteso nell'elemento: riga {(reader as IXmlLineInfo)?.LineNumber}.");
        }
    }

    private sealed class ParseState
    {
        public string DatabaseName { get; set; } = string.Empty;

        public bool RecycleBinEnabled { get; set; }

        public Guid RecycleBinUuid { get; set; }

        public List<VaultEntry> Entries { get; } = [];

        public bool IsRecycleBin(Guid groupUuid) =>
            RecycleBinEnabled && RecycleBinUuid != Guid.Empty && groupUuid == RecycleBinUuid;
    }
}
