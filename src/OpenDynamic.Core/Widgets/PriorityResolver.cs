using OpenDynamic.Core.State;

namespace OpenDynamic.Core.Widgets;

/// <summary>
/// Pure domain service that deterministically resolves primary and secondary activities
/// from a collection of candidate sources based on priority, recency, and transient lifespans.
/// Adheres strictly to Golden Rule 5 (isolated in Core, zero UI dependencies).
/// </summary>
public sealed class PriorityResolver
{
    /// <summary>
    /// Resolves the winning activity and secondary split activity from the provided candidates.
    /// </summary>
    /// <param name="sources">The available activity sources to evaluate.</param>
    /// <param name="currentTime">Optional timestamp for deterministic evaluation; defaults to <see cref="DateTimeOffset.UtcNow"/>.</param>
    /// <param name="isHovering">Indicates whether the user's cursor is currently hovering over the island notch.</param>
    /// <returns>A <see cref="PriorityResult"/> describing the active primary/secondary sources and recommended state.</returns>
    public PriorityResult Resolve(
        IEnumerable<IActivitySource>? sources,
        DateTimeOffset? currentTime = null,
        bool isHovering = false)
    {
        if (sources == null)
        {
            return new PriorityResult(null, null, IslandState.Hidden);
        }

        var now = currentTime ?? DateTimeOffset.UtcNow;
        var eventCandidates = new List<IActivitySource>();
        var onHoverCandidates = new List<IActivitySource>();
        DateTimeOffset? nextExpirationUtc = null;

        foreach (var source in sources)
        {
            if (!source.IsActive)
            {
                continue;
            }

            if (source.IsTransient)
            {
                // Check if transient source has a defined lifespan
                if (source.LastActivatedUtc.HasValue && source.TransientDuration.HasValue)
                {
                    var expiresAt = source.LastActivatedUtc.Value + source.TransientDuration.Value;
                    if (now >= expiresAt)
                    {
                        // Expired transient source is ignored
                        continue;
                    }

                    // Track earliest upcoming expiration
                    if (!nextExpirationUtc.HasValue || expiresAt < nextExpirationUtc.Value)
                    {
                        nextExpirationUtc = expiresAt;
                    }
                }
            }

            if (source.ActivationMode == ActivityActivationMode.OnHover)
            {
                onHoverCandidates.Add(source);
            }
            else
            {
                eventCandidates.Add(source);
            }
        }

        // Rule: Event-driven activities always preempt OnHover ambient widgets.
        // If any event activity is active, it wins unconditionally and ambient widgets are suppressed.
        if (eventCandidates.Count > 0)
        {
            SortCandidates(eventCandidates);

            var primary = eventCandidates[0];

            // Transient activities preempt as exclusive spotlight (no split allowed while transient alert is active)
            if (primary.IsTransient)
            {
                return new PriorityResult(primary, null, IslandState.Compact, nextExpirationUtc);
            }

            // When multiple persistent activities are active, show top 2 in Split mode
            if (eventCandidates.Count > 1)
            {
                var secondary = eventCandidates[1];
                return new PriorityResult(primary, secondary, IslandState.Split, nextExpirationUtc);
            }

            // Single persistent activity
            return new PriorityResult(primary, null, IslandState.Compact, nextExpirationUtc);
        }

        // No event activities are active (island is at rest / idle).
        // OnHover widgets only appear when hovering is actively reported, and NEVER keep the island visible persistently.
        if (onHoverCandidates.Count > 0 && isHovering)
        {
            SortCandidates(onHoverCandidates);
            var primary = onHoverCandidates[0];
            // OnHover ambient widgets never enter Split mode with other widgets
            return new PriorityResult(primary, null, IslandState.Compact, nextExpirationUtc);
        }

        return new PriorityResult(null, null, IslandState.Hidden);
    }

    private static void SortCandidates(List<IActivitySource> candidates)
    {
        candidates.Sort((a, b) =>
        {
            int priorityComparison = b.Priority.CompareTo(a.Priority);
            if (priorityComparison != 0)
            {
                return priorityComparison;
            }

            // Recency tie-breaker
            var timeA = a.LastActivatedUtc;
            var timeB = b.LastActivatedUtc;

            if (timeA.HasValue && timeB.HasValue)
            {
                int timeComparison = timeB.Value.CompareTo(timeA.Value);
                if (timeComparison != 0)
                {
                    return timeComparison;
                }
            }
            else if (timeA.HasValue && !timeB.HasValue)
            {
                return -1; // a wins (has activation timestamp)
            }
            else if (!timeA.HasValue && timeB.HasValue)
            {
                return 1; // b wins
            }

            // Deterministic string tie-breaker
            return string.Compare(a.Id, b.Id, StringComparison.Ordinal);
        });
    }
}
