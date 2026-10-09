using System.Text.Json.Nodes;
using openLuo.Protocol;
using Xunit;

namespace openLuo.Protocol.Tests;

public sealed class EnvelopeTests
{
    [Fact]
    public void Envelope_RoundTrips_WithCamelCaseKeys_And_EnumAsString()
    {
        var envelope = EnvelopeFactory.Create(EventTypes.Output, new OutputDto
        {
            Id = "out_1",
            Sequence = 2,
            Kind = OutputKind.Card,
            Payload = JsonNode.Parse("""{"platform":"163","id":42}"""),
            SourceCapability = "music:share_song",
        }, sessionId: "sess_1", traceId: "tr_1");

        var json = ProtocolJson.Serialize(envelope);

        Assert.Contains("\"type\":\"output\"", json);
        Assert.Contains("\"sessionId\":\"sess_1\"", json);
        Assert.Contains("\"kind\":\"card\"", json);          // enum → camelCase 字符串
        Assert.Contains("\"sourceCapability\":\"music:share_song\"", json);
        Assert.DoesNotContain("Payload", json);              // 无 PascalCase 泄漏

        var back = ProtocolJson.Deserialize<Envelope>(json);

        Assert.NotNull(back);
        Assert.Equal(ProtocolInfo.MajorVersion, back!.V);
        Assert.Equal(EventTypes.Output, back.Type);
        Assert.Equal("sess_1", back.SessionId);
        Assert.False(back.IsError);

        var output = back.DataAs<OutputDto>();
        Assert.NotNull(output);
        Assert.Equal(OutputKind.Card, output!.Kind);
        Assert.Equal(2, output.Sequence);
        Assert.Equal("163", output.Payload!["platform"]!.GetValue<string>());
    }

    [Fact]
    public void NullFields_AreOmitted()
    {
        var envelope = EnvelopeFactory.Create(EventTypes.Pong, new PongEvent { Ts = DateTimeOffset.UnixEpoch });
        var json = ProtocolJson.Serialize(envelope);

        Assert.DoesNotContain("sessionId", json);
        Assert.DoesNotContain("traceId", json);
        Assert.DoesNotContain("replyTo", json);
        Assert.DoesNotContain("error", json);
    }

    [Fact]
    public void Error_Envelope_RoundTrips()
    {
        var envelope = EnvelopeFactory.CreateError(
            EventTypes.Error,
            new ErrorInfo { Code = ErrorCodes.SessionNotFound, Message = "no session", Retryable = false },
            replyTo: "req_1");

        var json = ProtocolJson.Serialize(envelope);

        Assert.Contains("\"code\":\"session.not_found\"", json);
        Assert.Contains("\"replyTo\":\"req_1\"", json);

        var back = ProtocolJson.Deserialize<Envelope>(json);
        Assert.True(back!.IsError);
        Assert.Equal(ErrorCodes.SessionNotFound, back.Error!.Code);
        Assert.False(back.Error.Retryable);
    }

    [Fact]
    public void Option_TurnRequest_RoundTrips_WithAssetRefOutput()
    {
        var request = new TurnRequestDto
        {
            Text = "唱首歌",
            ActorId = "player",
            SourceId = "qq",
            Meta = new Dictionary<string, JsonNode?> { ["scene"] = JsonValue.Create("group") },
            IdempotencyKey = "idem_1",
        };

        var json = ProtocolJson.Serialize(EnvelopeFactory.Create(MessageTypes.TurnSubmit, request));
        var back = ProtocolJson.Deserialize<Envelope>(json)!.DataAs<TurnRequestDto>();

        Assert.NotNull(back);
        Assert.Equal("唱首歌", back!.Text);
        Assert.Equal("group", back.Meta!["scene"]!.GetValue<string>());
        Assert.Equal("idem_1", back.IdempotencyKey);

        var output = new OutputDto
        {
            Id = "out_audio",
            Kind = OutputKind.Audio,
            Sequence = 3,
            AssetRef = new AssetRefDto { Id = "ast_1", Mime = "audio/wav", Size = 183402, Checksum = "abc" },
        };
        var outputJson = ProtocolJson.Serialize(output);
        Assert.Contains("\"assetRef\"", outputJson);
        Assert.DoesNotContain("\"payload\"", outputJson);
    }

    [Fact]
    public void MessageTypes_And_EventTypes_DoNotCollide()
    {
        Assert.Equal("turn.submit", MessageTypes.TurnSubmit);
        Assert.Equal("turn.final", EventTypes.TurnFinal);
        Assert.NotEqual(MessageTypes.TurnCancel, EventTypes.TurnFinal);
        Assert.NotEqual(MessageTypes.OutputAck, EventTypes.Output);
    }
}

public sealed class ProtocolIdsTests
{
    [Fact]
    public void NewUlid_Is26CrockfordChars()
    {
        var id = ProtocolIds.NewUlid();

        Assert.Equal(26, id.Length);
        Assert.All(id, ch => Assert.Contains(ch, "0123456789ABCDEFGHJKMNPQRSTVWXYZ"));
    }

    [Fact]
    public void NewUlid_IsUnique()
    {
        var ids = Enumerable.Range(0, 2000).Select(_ => ProtocolIds.NewUlid()).ToArray();

        Assert.Equal(2000, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void NewUlid_IsTimeOrdered()
    {
        var first = ProtocolIds.NewUlid();
        Thread.Sleep(5);
        var second = ProtocolIds.NewUlid();

        Assert.True(string.CompareOrdinal(first, second) < 0);
    }
}
