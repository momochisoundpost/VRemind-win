using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using VRemindWin.Core.Models;

namespace VRemindWin.Core.Services;

public readonly record struct DragLayoutStep(
    int TargetIndex,
    double ClampedDragTranslateY,
    IReadOnlyList<double> ItemTranslateY);

public static class ChannelOrderPolicy
{
    public const double DragActivationThresholdDip = 5.0;

    private static readonly JsonSerializerOptions IndentedJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static bool ExceedsDragThreshold(double deltaY)
        => !double.IsNaN(deltaY) && Math.Abs(deltaY) >= DragActivationThresholdDip;

    public static DragLayoutStep CalculateDragLayout(
        int draggedIndex,
        double deltaY,
        IReadOnlyList<double> itemHeights,
        double itemSpacing = 0.0)
    {
        if (itemHeights is null || itemHeights.Count == 0)
        {
            return new DragLayoutStep(0, 0.0, []);
        }

        int count = itemHeights.Count;
        if (draggedIndex < 0 || draggedIndex >= count)
        {
            return new DragLayoutStep(0, 0.0, new double[count]);
        }

        if (count == 1)
        {
            return new DragLayoutStep(0, 0.0, [0.0]);
        }

        double spacing = Math.Max(0.0, itemSpacing);
        var tops = new double[count];
        var centers = new double[count];
        double cursorY = 0.0;
        for (int i = 0; i < count; i++)
        {
            double h = Math.Max(1.0, itemHeights[i]);
            tops[i] = cursorY;
            centers[i] = cursorY + (h / 2.0);
            cursorY += h + spacing;
        }

        double totalHeight = cursorY - spacing;
        double draggedHeight = Math.Max(1.0, itemHeights[draggedIndex]);
        double minDelta = -tops[draggedIndex];
        double maxDelta = totalHeight - (tops[draggedIndex] + draggedHeight);
        double safeDelta = double.IsNaN(deltaY) || double.IsInfinity(deltaY) ? 0.0 : deltaY;
        double clampedDelta = Math.Clamp(safeDelta, minDelta, maxDelta);

        double draggedTop = tops[draggedIndex] + clampedDelta;
        double draggedBottom = draggedTop + draggedHeight;

        int targetIndex = draggedIndex;
        if (clampedDelta > 0.0)
        {
            for (int i = draggedIndex + 1; i < count; i++)
            {
                if (draggedBottom >= centers[i])
                {
                    targetIndex = i;
                }
                else
                {
                    break;
                }
            }
        }
        else if (clampedDelta < 0.0)
        {
            for (int i = draggedIndex - 1; i >= 0; i--)
            {
                if (draggedTop <= centers[i])
                {
                    targetIndex = i;
                }
                else
                {
                    break;
                }
            }
        }

        var translates = new double[count];
        translates[draggedIndex] = clampedDelta;
        double shiftAmount = draggedHeight + spacing;
        if (targetIndex > draggedIndex)
        {
            for (int i = draggedIndex + 1; i <= targetIndex; i++)
            {
                translates[i] = -shiftAmount;
            }
        }
        else if (targetIndex < draggedIndex)
        {
            for (int i = targetIndex; i < draggedIndex; i++)
            {
                translates[i] = shiftAmount;
            }
        }

        return new DragLayoutStep(targetIndex, clampedDelta, translates);
    }

    public static IReadOnlyList<CliChannelItem> ReorderSectionItems(
        IReadOnlyList<CliChannelItem> items,
        int fromIndex,
        int toIndex)
    {
        if (items is null || items.Count <= 1 ||
            fromIndex < 0 || fromIndex >= items.Count ||
            toIndex < 0 || toIndex >= items.Count ||
            fromIndex == toIndex)
        {
            return items ?? [];
        }

        var reordered = items.ToList();
        var moved = reordered[fromIndex];
        reordered.RemoveAt(fromIndex);
        reordered.Insert(toIndex, moved);
        return reordered;
    }

    public static CliStateEnvelope ApplySectionOrder(
        CliStateEnvelope state,
        string sectionKind,
        IReadOnlyList<CliChannelItem> reorderedSection)
    {
        var nextSections = sectionKind.ToLowerInvariant() switch
        {
            "live" => state.Sections with { Live = reorderedSection },
            "upcoming" => state.Sections with { Upcoming = reorderedSection },
            "offline" => state.Sections with { Offline = reorderedSection },
            _ => state.Sections
        };

        return state with
        {
            LiveCount = nextSections.Live.Count,
            Sections = nextSections
        };
    }

    public static CliStateEnvelope ApplyCustomOrder(
        CliStateEnvelope state,
        IReadOnlyList<string> orderedIds)
    {
        if (orderedIds is null || orderedIds.Count == 0)
        {
            return state;
        }

        var rankById = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < orderedIds.Count; i++)
        {
            var normalized = orderedIds[i]?.Trim();
            if (!string.IsNullOrEmpty(normalized))
            {
                rankById.TryAdd(normalized, i);
            }
        }

        if (rankById.Count == 0)
        {
            return state;
        }

        var nextSections = new CliSections(
            Live: SortSectionByRank(state.Sections.Live, rankById),
            Upcoming: SortSectionByRank(state.Sections.Upcoming, rankById),
            Offline: SortSectionByRank(state.Sections.Offline, rankById));

        return state with
        {
            LiveCount = nextSections.Live.Count,
            Sections = nextSections
        };
    }

    public static string ReorderChannelsJson(
        string jsonContent,
        IReadOnlyList<string> orderedSectionIds)
    {
        if (string.IsNullOrWhiteSpace(jsonContent) || orderedSectionIds is null || orderedSectionIds.Count < 2)
        {
            return jsonContent;
        }

        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(jsonContent);
        }
        catch (JsonException)
        {
            return jsonContent;
        }

        if (parsed is not JsonArray array || array.Count < 2)
        {
            return jsonContent;
        }

        var rankById = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < orderedSectionIds.Count; i++)
        {
            var id = orderedSectionIds[i]?.Trim();
            if (!string.IsNullOrEmpty(id))
            {
                rankById.TryAdd(id, i);
            }
        }

        if (rankById.Count < 2)
        {
            return jsonContent;
        }

        var matchedSlots = new List<(int SlotIndex, JsonNode Node, int TargetRank)>();
        for (int i = 0; i < array.Count; i++)
        {
            if (array[i] is not JsonObject obj)
            {
                continue;
            }

            if (TryResolveTargetRank(obj, rankById, out int rank))
            {
                matchedSlots.Add((i, obj, rank));
            }
        }

        if (matchedSlots.Count < 2)
        {
            return jsonContent;
        }

        var slotIndices = matchedSlots.Select(m => m.SlotIndex).ToArray();
        var sortedNodes = matchedSlots
            .OrderBy(m => m.TargetRank)
            .ThenBy(m => m.SlotIndex)
            .Select(m => m.Node)
            .ToArray();

        // Detach matched nodes before reinserting to avoid "node already has a parent".
        foreach (int slot in slotIndices)
        {
            array[slot] = null;
        }

        for (int i = 0; i < slotIndices.Length; i++)
        {
            array[slotIndices[i]] = sortedNodes[i];
        }

        return UnescapeJsonSurrogatePairs(array.ToJsonString(IndentedJsonOptions));
    }

    public static string ResolveDefaultDataDirectory(string? configuredDataDir)
    {
        if (!string.IsNullOrWhiteSpace(configuredDataDir))
        {
            return configuredDataDir;
        }

        string? appData = Environment.GetEnvironmentVariable("APPDATA");
        if (string.IsNullOrWhiteSpace(appData))
        {
            appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        }

        if (!string.IsNullOrWhiteSpace(appData))
        {
            return Path.Combine(appData, "VRemind");
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config",
            "vremind");
    }

    public static void PersistSectionOrder(string dataDir, IReadOnlyList<string> orderedSectionIds)
    {
        if (string.IsNullOrWhiteSpace(dataDir) || orderedSectionIds is null || orderedSectionIds.Count < 2)
        {
            return;
        }

        string channelsPath = Path.Combine(dataDir, "channels.json");
        if (!File.Exists(channelsPath))
        {
            return;
        }

        Directory.CreateDirectory(dataDir);
        string lockPath = Path.Combine(dataDir, ".vremind.lock");

        using var lockStream = new FileStream(
            lockPath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.ReadWrite);

        bool locked = false;
        bool lockUnsupported = false;
        try
        {
            const int maxLockAttempts = 20;
            for (int attempt = 0; attempt < maxLockAttempts; attempt++)
            {
                try
                {
                    lockStream.Lock(0, 1);
                    locked = true;
                    break;
                }
                catch (IOException)
                {
                    if (attempt < maxLockAttempts - 1)
                    {
                        Thread.Sleep(25);
                    }
                }
                catch (PlatformNotSupportedException)
                {
                    lockUnsupported = true;
                    break;
                }
            }

            if (!locked && !lockUnsupported)
            {
                return;
            }

            if (!File.Exists(channelsPath))
            {
                return;
            }

            string originalJson = File.ReadAllText(channelsPath);
            string updatedJson = ReorderChannelsJson(originalJson, orderedSectionIds);
            if (string.Equals(updatedJson, originalJson, StringComparison.Ordinal))
            {
                return;
            }

            string tempPath = $"{channelsPath}.tmp.{Guid.NewGuid():N}";
            try
            {
                File.WriteAllText(tempPath, updatedJson);
                File.Move(tempPath, channelsPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }
        finally
        {
            if (locked)
            {
                try
                {
                    lockStream.Unlock(0, 1);
                }
                catch
                {
                    // Ignore unlock failure on cleanup.
                }
            }
        }
    }

    private static IReadOnlyList<CliChannelItem> SortSectionByRank(
        IReadOnlyList<CliChannelItem> items,
        IReadOnlyDictionary<string, int> rankById)
    {
        if (items.Count <= 1)
        {
            return items;
        }

        return items
            .Select((item, originalIndex) => (
                Item: item,
                Rank: rankById.TryGetValue(item.Id, out int r) ? r : int.MaxValue,
                OriginalIndex: originalIndex))
            .OrderBy(entry => entry.Rank)
            .ThenBy(entry => entry.OriginalIndex)
            .Select(entry => entry.Item)
            .ToList();
    }

    private static bool TryResolveTargetRank(
        JsonObject channelObj,
        IReadOnlyDictionary<string, int> rankById,
        out int rank)
    {
        string? rawId = channelObj["id"]?.GetValue<string?>()?.Trim();
        if (!string.IsNullOrEmpty(rawId))
        {
            if (rankById.TryGetValue(rawId, out rank))
            {
                return true;
            }

            if (!rawId.Contains(':'))
            {
                string? platform = channelObj["platform"]?.GetValue<string?>()?.Trim();
                string prefixed = string.IsNullOrEmpty(platform)
                    ? $"youtube:{rawId}"
                    : $"{platform.ToLowerInvariant()}:{rawId}";
                if (rankById.TryGetValue(prefixed, out rank))
                {
                    return true;
                }
            }
        }

        string? handle = channelObj["handle"]?.GetValue<string?>()?.Trim();
        if (!string.IsNullOrEmpty(handle))
        {
            string? platform = channelObj["platform"]?.GetValue<string?>()?.Trim();
            string normalizedPlatform = string.IsNullOrEmpty(platform) ? "youtube" : platform.ToLowerInvariant();
            if (rankById.TryGetValue($"{normalizedPlatform}:{handle}", out rank))
            {
                return true;
            }
        }

        rank = int.MaxValue;
        return false;
    }

    private static string UnescapeJsonSurrogatePairs(string json)
    {
        if (!json.Contains("\\u", StringComparison.OrdinalIgnoreCase))
        {
            return json;
        }

        var sb = new System.Text.StringBuilder(json.Length);
        bool inString = false;
        int i = 0;
        while (i < json.Length)
        {
            char c = json[i];
            if (!inString)
            {
                sb.Append(c);
                if (c == '"')
                {
                    inString = true;
                }
                i++;
                continue;
            }

            if (c == '"')
            {
                inString = false;
                sb.Append(c);
                i++;
                continue;
            }

            if (c == '\\' && i + 1 < json.Length)
            {
                if (json[i + 1] == 'u' &&
                    i + 11 < json.Length &&
                    json[i + 6] == '\\' &&
                    json[i + 7] == 'u' &&
                    TryParseHex4(json.AsSpan(i + 2, 4), out ushort high) &&
                    TryParseHex4(json.AsSpan(i + 8, 4), out ushort low) &&
                    char.IsHighSurrogate((char)high) &&
                    char.IsLowSurrogate((char)low))
                {
                    sb.Append((char)high);
                    sb.Append((char)low);
                    i += 12;
                    continue;
                }

                // Consume escape sequence atomically so "\\" does not masquerade as "\u...".
                sb.Append(c);
                sb.Append(json[i + 1]);
                i += 2;
                continue;
            }

            sb.Append(c);
            i++;
        }

        return sb.ToString();
    }

    private static bool TryParseHex4(ReadOnlySpan<char> hex, out ushort value)
    {
        value = 0;
        if (hex.Length != 4)
        {
            return false;
        }

        return ushort.TryParse(
            hex,
            System.Globalization.NumberStyles.AllowHexSpecifier,
            System.Globalization.CultureInfo.InvariantCulture,
            out value);
    }
}
