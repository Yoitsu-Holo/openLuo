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
        Assert.Contains("\"errorCode\":1000", json);          // 成功 = 1000
        Assert.DoesNotContain("Payload", json);              // 无 PascalCase 泄漏

        var back = ProtocolJson.Deserialize<Envelope>(json);

        Assert.NotNull(back);
        Assert.Equal(ProtocolInfo.MajorVersion, back!.V);
        Assert.Equal(EventTypes.Output, back.Type);
        Assert.Equal("sess_1", back.SessionId);
        Assert.False(back.IsError);
        Assert.Equal(ErrorCodes.Success, back.ErrorCode);

        var output = back.DataAs<OutputDto>();
        Assert.NotNull(output);
        Assert.Equal(OutputKind.Card, output!.Kind);
        Assert.Equal(2, output.Sequence);
        Assert.Equal("163", output.Payload!["platform"]!.GetValue<string>());
    }

    [Fact]
    public void Success_Envelope_CarriesSuccessCode_And_OmitsNullFields()
    {
        var envelope = EnvelopeFactory.Create(EventTypes.Pong, new PongEvent { Ts = DateTimeOffset.UnixEpoch });
        var json = ProtocolJson.Serialize(envelope);

        Assert.Contains("\"errorCode\":1000", json);
        Assert.DoesNotContain("sessionId", json);
        Assert.DoesNotContain("traceId", json);
        Assert.DoesNotContain("replyTo", json);
    }

    [Fact]
    public void Error_Envelope_RoundTrips_WithIntCode_And_Msg()
    {
        var envelope = EnvelopeFactory.CreateError(
            EventTypes.Error, ErrorCodes.SessionNotFound, "no session", replyTo: "req_1");

        var json = ProtocolJson.Serialize(envelope);

        Assert.Contains("\"errorCode\":4001", json);
        Assert.Contains("\"errorMsg\":\"no session\"", json);
        Assert.Contains("\"replyTo\":\"req_1\"", json);

        var back = ProtocolJson.Deserialize<Envelope>(json);
        Assert.True(back!.IsError);
        Assert.Equal(ErrorCodes.SessionNotFound, back.ErrorCode);
        Assert.Equal("no session", back.ErrorMsg);
    }

    [Fact]
    public void TurnRequest_RoundTrips_And_BinaryOutput_UsesAssetRef()
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

public sealed class ErrorCodesTests
{
    [Fact]
    public void Success_Is1000_And_NamesAreStable()
    {
        Assert.Equal(1000, ErrorCodes.Success);
        Assert.Equal(2001, ErrorCodes.ProtocolVersionMismatch);
        Assert.Equal("success", ErrorCodes.NameOf(ErrorCodes.Success));
        Assert.Equal("protocol.version_mismatch", ErrorCodes.NameOf(ErrorCodes.ProtocolVersionMismatch));
        Assert.True(ErrorCodes.IsSuccess(1000));
        Assert.False(ErrorCodes.IsSuccess(ErrorCodes.SessionNotFound));
    }

    [Fact]
    public void Codes_AreUnique_And_Segmented()
    {
        int[] all =
        [
            ErrorCodes.Success, ErrorCodes.Unknown,
            ErrorCodes.ConfigNamespaceNotFound, ErrorCodes.ConfigInvalidValue,
            ErrorCodes.ConfigReadOnly, ErrorCodes.ConfigPersistFailed,
            ErrorCodes.ProtocolVersionMismatch, ErrorCodes.ProtocolBadEnvelope, ErrorCodes.ProtocolUnknownType,
            ErrorCodes.AuthUnauthorized, ErrorCodes.AuthForbidden,
            ErrorCodes.SessionNotFound, ErrorCodes.SessionLimitExceeded,
            ErrorCodes.TurnBusy, ErrorCodes.TurnCancelled, ErrorCodes.TurnBudgetExceeded,
            ErrorCodes.CapabilityConfirmationRequired, ErrorCodes.CapabilityFailed,
            ErrorCodes.AssetNotFound,
            ErrorCodes.RateLimited,
            ErrorCodes.ServerInternal,
        ];

        Assert.Equal(all.Length, all.Distinct().Count());
        Assert.All(all, code => Assert.InRange(code, 1000, 9999));
    }
}

public sealed class ConfigProtocolTests
{
    [Fact]
    public void ConfigSet_RoundTrips_WithNamespacedValues()
    {
        var json = ProtocolJson.Serialize(EnvelopeFactory.Create(MessageTypes.ConfigSet, new ConfigSetCommand
        {
            Namespace = "llm",
            Persist = true,
            Values = JsonNode.Parse("""{"routes":[{"model":"deepseek"}]}"""),
        }));

        Assert.Contains("\"type\":\"config.set\"", json);

        var back = ProtocolJson.Deserialize<Envelope>(json)!.DataAs<ConfigSetCommand>();
        Assert.NotNull(back);
        Assert.Equal("llm", back!.Namespace);
        Assert.True(back.Persist);
        Assert.Equal("deepseek", back.Values!["routes"]![0]!["model"]!.GetValue<string>());
    }

    [Fact]
    public void ConfigGet_Response_ReportsSource_And_OmitsNullOverrides()
    {
        var response = new ConfigGetResponse
        {
            Namespace = "timeouts",
            Source = ConfigSources.File,
            Values = JsonNode.Parse("""{"mcp":15}"""),
        };

        var json = ProtocolJson.Serialize(response);

        Assert.Contains("\"source\":\"file\"", json);
        Assert.DoesNotContain("overrides", json);   // null 被忽略
    }

    [Fact]
    public void ConfigDelete_DefaultsToDefaultSource()
    {
        var response = new ConfigDeleteResponse { Namespace = "world" };

        Assert.Equal(ConfigSources.Default, response.Source);
        Assert.Equal("config.del", MessageTypes.ConfigDel);
        Assert.Equal("config.updated", EventTypes.ConfigUpdated);
    }

    [Fact]
    public void ConfigErrorCodes_Are1101Series()
    {
        Assert.Equal(1101, ErrorCodes.ConfigNamespaceNotFound);
        Assert.Equal("config.namespace_not_found", ErrorCodes.NameOf(ErrorCodes.ConfigNamespaceNotFound));
        Assert.Equal("config.invalid_value", ErrorCodes.NameOf(ErrorCodes.ConfigInvalidValue));
        Assert.Equal("config", Features.Config);
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
