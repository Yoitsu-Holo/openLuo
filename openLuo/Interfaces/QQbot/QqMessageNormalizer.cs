using System.Text;
using Milky.Net.Model;
using openLuo.Modules.SessionRuntime.Core.Models;

namespace openLuo.Interfaces.QQbot;

internal static class QqMessageNormalizer
{
    public static QqNormalizedMessage Normalize(GroupIncomingMessage message, long botUserId)
        => NormalizeSegments(message.Segments, botUserId);

    public static QqNormalizedMessage Normalize(FriendIncomingMessage message)
        => NormalizeSegments(message.Segments, null);

    private static QqNormalizedMessage NormalizeSegments(IEnumerable<IncomingSegment> segments, long? botUserId)
    {
        var sb = new StringBuilder();
        var parts = new List<SessionInputPart>();
        int mediaIndex = 0;

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
                case IncomingSegment<ImageIncomingSegmentData> image:
                {
                    sb.AppendLine("[图片]");
                    var index = ++mediaIndex;
                    var imagePart = new SessionInputPart
                    {
                        Kind = SessionContentKind.Binary,
                        Name = $"image_{index}",
                        MediaType = "image/jpeg",
                        Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["platform"] = "qq",
                            ["qq.segment"] = "image"
                        }
                    };
                    if (!string.IsNullOrWhiteSpace(image.Data.TempUrl))
                        imagePart.Metadata["qq.imageUrl"] = image.Data.TempUrl;
                    if (image.Data.Width > 0)
                        imagePart.Metadata["qq.imageWidth"] = image.Data.Width.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    if (image.Data.Height > 0)
                        imagePart.Metadata["qq.imageHeight"] = image.Data.Height.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    if (!string.IsNullOrWhiteSpace(image.Data.Summary))
                        imagePart.Metadata["qq.imageSummary"] = image.Data.Summary;
                    parts.Add(imagePart);
                    break;
                }
                case IncomingSegment<RecordIncomingSegmentData> record:
                {
                    sb.AppendLine("[语音]");
                    var index = ++mediaIndex;
                    var recordPart = new SessionInputPart
                    {
                        Kind = SessionContentKind.Binary,
                        Name = $"record_{index}",
                        MediaType = "audio/unknown",
                        Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["platform"] = "qq",
                            ["qq.segment"] = "record"
                        }
                    };
                    if (!string.IsNullOrWhiteSpace(record.Data.TempUrl))
                        recordPart.Metadata["qq.recordUrl"] = record.Data.TempUrl;
                    if (record.Data.Duration > 0)
                        recordPart.Metadata["qq.recordDuration"] = record.Data.Duration.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    parts.Add(recordPart);
                    break;
                }
                case IncomingSegment<FileIncomingSegmentData> file:
                {
                    sb.AppendLine("[文件]");
                    var index = ++mediaIndex;
                    var filePart = new SessionInputPart
                    {
                        Kind = SessionContentKind.Binary,
                        Name = $"file_{index}",
                        MediaType = "application/octet-stream",
                        Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["platform"] = "qq",
                            ["qq.segment"] = "file"
                        }
                    };
                    if (!string.IsNullOrWhiteSpace(file.Data.FileName))
                        filePart.Metadata["qq.fileName"] = file.Data.FileName;
                    if (file.Data.FileSize > 0)
                        filePart.Metadata["qq.fileSize"] = file.Data.FileSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    if (!string.IsNullOrWhiteSpace(file.Data.FileId))
                        filePart.Metadata["qq.fileId"] = file.Data.FileId;
                    parts.Add(filePart);
                    break;
                }
                default:
                    sb.AppendLine($"[未知消息:{segment}]");
                    break;
            }
        }

        return new QqNormalizedMessage
        {
            Text = sb.ToString().Trim(),
            Parts = parts
        };
    }

    public static bool MentionsBot(GroupIncomingMessage message, long botUserId) =>
        message.Segments
            .OfType<IncomingSegment<MentionIncomingSegmentData>>()
            .Any(segment => segment.Data.UserId == botUserId);
}
