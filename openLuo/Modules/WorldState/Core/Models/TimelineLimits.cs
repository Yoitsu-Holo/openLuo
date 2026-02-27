using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace openLuo.Modules.WorldState.Core.Models;

public static class TimelineLimits
{
    public const int MaxEventTypeLength = 64;
    public const int MaxTitleLength = 256;
    public const int MaxRecurrenceRuleLength = 64;
    public const int MaxActionJsonBytes = 4096;
    public const int MaxContextJsonBytes = 4096;

    public static bool TryValidateCreateRequest(TimelineCreateRequest request, out string error)
    {
        if (request is null)
        {
            error = "request_required";
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.EventType))
        {
            error = "eventType_required";
            return false;
        }

        if (request.EventType.Trim().Length > MaxEventTypeLength)
        {
            error = "eventType_too_long";
            return false;
        }

        if (request.DueAtEpochMs <= 0)
        {
            error = "dueAtEpochMs_must_be_positive";
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            error = "title_required";
            return false;
        }

        if (request.Title.Trim().Length > MaxTitleLength)
        {
            error = "title_too_long";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(request.RecurrenceRule)
            && request.RecurrenceRule.Trim().Length > MaxRecurrenceRuleLength)
        {
            error = "recurrenceRule_too_long";
            return false;
        }
        if (!IsRecurrenceRuleValid(request.RecurrenceRule))
        {
            error = "recurrenceRule_invalid";
            return false;
        }

        if (!IsWithinUtf8Bytes(request.ActionJson, MaxActionJsonBytes))
        {
            error = "action_payload_too_large";
            return false;
        }
        if (!IsJsonObjectOrEmpty(request.ActionJson))
        {
            error = "action_payload_must_be_object";
            return false;
        }

        if (!IsWithinUtf8Bytes(request.ContextJson, MaxContextJsonBytes))
        {
            error = "context_payload_too_large";
            return false;
        }
        if (!IsJsonObjectOrEmpty(request.ContextJson))
        {
            error = "context_payload_must_be_object";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool IsWithinUtf8Bytes(string? text, int maxBytes) =>
        string.IsNullOrEmpty(text) || Encoding.UTF8.GetByteCount(text) <= maxBytes;

    private static bool IsJsonObjectOrEmpty(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return true;
        try
        {
            return JsonNode.Parse(text) is JsonObject;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsRecurrenceRuleValid(string? recurrenceRule)
    {
        if (string.IsNullOrWhiteSpace(recurrenceRule))
            return true;

        var rule = recurrenceRule.Trim().ToLowerInvariant();
        if (rule is "hourly" or "daily" or "weekly")
            return true;

        return Regex.IsMatch(rule, @"^every:[1-9]\d{0,3}[mhd]$", RegexOptions.IgnoreCase);
    }
}
