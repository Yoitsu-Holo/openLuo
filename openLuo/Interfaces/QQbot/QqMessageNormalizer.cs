using System.Text;
using Milky.Net.Model;

namespace openLuo.Interfaces.QQbot;

internal static class QqMessageNormalizer
{
    public static string Normalize(GroupIncomingMessage message, long botUserId)
        => NormalizeSegments(message.Segments, botUserId);

    public static string Normalize(FriendIncomingMessage message)
        => NormalizeSegments(message.Segments, null);

    private static string NormalizeSegments(IEnumerable<IncomingSegment> segments, long? botUserId)
    {
        var sb = new StringBuilder();
        foreach (var segment in segments)
        {
            switch (segment)
            {
                case IncomingSegment<TextIncomingSegmentData> text:
                    sb.Append(text.Data.Text);
                    break;
                case IncomingSegment<MentionIncomingSegmentData> mention:
                    if (!botUserId.HasValue || mention.Data.UserId != botUserId.Value)
                        sb.Append($"@{mention.Data.Name} ");
                    break;
                case IncomingSegment<MentionAllIncomingSegmentData>:
                    sb.Append("@全体成员 ");
                    break;
                case IncomingSegment<FaceIncomingSegmentData> face:
                    sb.Append($"[表情:{face.Data.FaceId}]");
                    break;
                case IncomingSegment<ReplyIncomingSegmentData> reply:
                    sb.AppendLine($"[回复:{reply.Data.SenderId}]");
                    break;
                case IncomingSegment<ImageIncomingSegmentData>:
                    sb.AppendLine("[图片]");
                    break;
                case IncomingSegment<RecordIncomingSegmentData>:
                    sb.AppendLine("[语音]");
                    break;
                case IncomingSegment<FileIncomingSegmentData>:
                    sb.AppendLine("[文件]");
                    break;
                default:
                    sb.AppendLine($"[未知消息:{segment}]");
                    break;
            }
        }

        return sb.ToString().Trim();
    }

    public static bool MentionsBot(GroupIncomingMessage message, long botUserId) =>
        message.Segments
            .OfType<IncomingSegment<MentionIncomingSegmentData>>()
            .Any(segment => segment.Data.UserId == botUserId);
}
