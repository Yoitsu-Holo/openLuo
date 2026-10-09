using System.Text.Json.Nodes;
using openLuo.Protocol;
using Xunit;

namespace openLuo.Protocol.Tests;

public sealed class GroupProtocolTests
{
    [Fact]
    public void TurnRequest_CarriesUserId_Mentions_ThreadId()
    {
        var request = new TurnRequestDto
        {
            Text = "天依在吗",
            UserId = "u_10001",
            Mentions = ["rin"],
            ThreadId = "t_abc",
        };

        var back = ProtocolJson.Deserialize<Envelope>(
            ProtocolJson.Serialize(EnvelopeFactory.Create(MessageTypes.TurnSubmit, request)))!.DataAs<TurnRequestDto>();

        Assert.NotNull(back);
        Assert.Equal("u_10001", back!.UserId);
        Assert.Equal("rin", Assert.Single(back.Mentions!));
        Assert.Equal("t_abc", back.ThreadId);
    }

    [Fact]
    public void Output_CarriesRecipient_And_Mentions()
    {
        var json = ProtocolJson.Serialize(new OutputDto
        {
            Id = "o1",
            Kind = OutputKind.Text,
            Payload = JsonValue.Create("hi"),
            Recipient = "u_10001",
            Mentions = ["u_10001"],
            ThreadId = "t_abc",
        });

        Assert.Contains("\"recipient\":\"u_10001\"", json);
        Assert.Contains("\"mentions\":[\"u_10001\"]", json);
        Assert.Contains("\"threadId\":\"t_abc\"", json);
    }

    [Fact]
    public void MemberJoined_Event_RoundTrips()
    {
        var json = ProtocolJson.Serialize(EnvelopeFactory.Create(EventTypes.MemberJoined,
            new MemberJoinedEvent { SessionId = "s1", Member = new MemberDto { UserId = "u1", DisplayName = "A" } }));

        Assert.Contains("\"type\":\"member.joined\"", json);

        var data = ProtocolJson.Deserialize<Envelope>(json)!.DataAs<MemberJoinedEvent>();
        Assert.Equal("u1", data!.Member.UserId);
        Assert.Equal("member.left", EventTypes.MemberLeft);
    }
}

public sealed class PresenceProtocolTests
{
    [Fact]
    public void Envelope_TargetClientId_RoundTrips_And_OmitsWhenNull()
    {
        var targeted = ProtocolJson.Serialize(new Envelope { Type = EventTypes.Output, TargetClientId = "c1" });
        Assert.Contains("\"targetClientId\":\"c1\"", targeted);

        var broadcast = ProtocolJson.Serialize(new Envelope { Type = EventTypes.Output });
        Assert.DoesNotContain("targetClientId", broadcast);
    }

    [Fact]
    public void SessionResume_CarriesSinceSequence()
    {
        var back = ProtocolJson.Deserialize<Envelope>(
            ProtocolJson.Serialize(EnvelopeFactory.Create(
                MessageTypes.SessionResume, new SessionResumeCommand { SessionId = "s1", SinceSequence = 42 })))!
            .DataAs<SessionResumeCommand>();

        Assert.NotNull(back);
        Assert.Equal(42, back!.SinceSequence);
        Assert.Equal("session.resume", MessageTypes.SessionResume);
        Assert.Equal("presence.updated", EventTypes.PresenceUpdated);
    }

    [Fact]
    public void PresenceDefaults_Online()
    {
        Assert.Equal(PresenceStatuses.Online, new PresenceDto().Status);
    }
}

public sealed class ProactiveProtocolTests
{
    [Fact]
    public void TurnStarted_CarriesOrigin()
    {
        var json = ProtocolJson.Serialize(EnvelopeFactory.Create(EventTypes.TurnStarted,
            new TurnStartedEvent { TurnId = "t1", SessionId = "s1", Origin = TurnOrigins.Scheduled, Trigger = "sch_1" }));

        Assert.Contains("\"origin\":\"scheduled\"", json);
        Assert.Contains("\"trigger\":\"sch_1\"", json);
    }

    [Fact]
    public void DeviceState_RoundTrips()
    {
        var json = ProtocolJson.Serialize(new DeviceStateEvent
        {
            Device = new DeviceStateDto
            {
                DeviceId = "light.kitchen",
                Kind = "light",
                State = JsonNode.Parse("""{"on":true}"""),
            },
        });

        Assert.Contains("\"deviceId\":\"light.kitchen\"", json);

        var back = ProtocolJson.Deserialize<DeviceStateEvent>(json);
        Assert.True(back!.Device.State!["on"]!.GetValue<bool>());
        Assert.Equal("device.report", MessageTypes.DeviceReport);
        Assert.Equal("device.state", EventTypes.DeviceState);
    }

    [Fact]
    public void Notification_RoundTrips()
    {
        var json = ProtocolJson.Serialize(new NotificationEvent { Kind = "alert", Title = "门开了" });
        Assert.Contains("\"kind\":\"alert\"", json);
        Assert.Equal("notification", EventTypes.Notification);
    }
}

public sealed class JobsProtocolTests
{
    [Fact]
    public void JobProgress_RoundTrips()
    {
        var json = ProtocolJson.Serialize(EnvelopeFactory.Create(EventTypes.JobProgress,
            new JobProgressEvent { JobId = "j1", Progress = 0.5, Message = "渲染中" }));

        Assert.Contains("\"progress\":0.5", json);
        Assert.Contains("\"jobId\":\"j1\"", json);
    }

    [Fact]
    public void JobDto_StatusDefaultsToQueued()
    {
        Assert.Equal(JobStatuses.Queued, new JobDto().Status);
        Assert.Equal("job.failed", ErrorCodes.NameOf(ErrorCodes.JobFailed));
        Assert.Equal("job.completed", EventTypes.JobCompleted);
    }
}

public sealed class AvatarProtocolTests
{
    [Fact]
    public void Lipsync_CarriesAssetRef_And_Visemes()
    {
        var json = ProtocolJson.Serialize(new AvatarLipsyncEvent
        {
            AgentId = "companion",
            Audio = new AssetRefDto { Id = "ast_1", Mime = "audio/wav", Size = 1024 },
            Visemes =
            [
                new VisemeDto { TimeMs = 0, Viseme = "A" },
                new VisemeDto { TimeMs = 120, Viseme = "I", Weight = 0.8 },
            ],
            DurationMs = 3000,
        });

        Assert.Contains("\"audio\":{", json);
        Assert.Contains("\"visemes\":[", json);
        Assert.Contains("\"durationMs\":3000", json);
        Assert.Equal("avatar.lipsync", EventTypes.AvatarLipsync);
        Assert.Equal("avatar.unsupported", ErrorCodes.NameOf(ErrorCodes.AvatarUnsupported));
    }
}

public sealed class ObservabilityProtocolTests
{
    [Fact]
    public void Metrics_And_Trace_Shape()
    {
        var metricsJson = ProtocolJson.Serialize(new MetricsDto { TurnsTotal = 10, ClientsConnected = 2 });
        Assert.Contains("\"turnsTotal\":10", metricsJson);

        var trace = new TurnTraceDto
        {
            TurnId = "t1",
            SessionId = "s1",
            Events = [new TraceEventDto { Type = "decision" }],
        };
        Assert.Single(trace.Events);
        Assert.Equal("audit.event", EventTypes.AuditEvent);
    }
}

public sealed class ErrorCodeCatalogTests
{
    [Fact]
    public void AllNewDomainCodes_HaveNames()
    {
        (int Code, string Name)[] cases =
        [
            (ErrorCodes.ScheduleNotFound, "schedule.not_found"),
            (ErrorCodes.ScheduleInvalid, "schedule.invalid"),
            (ErrorCodes.JobNotFound, "job.not_found"),
            (ErrorCodes.JobInvalid, "job.invalid"),
            (ErrorCodes.JobFailed, "job.failed"),
            (ErrorCodes.DeviceNotFound, "device.not_found"),
            (ErrorCodes.DeviceReportRejected, "device.report_rejected"),
            (ErrorCodes.PresenceUnavailable, "presence.unavailable"),
            (ErrorCodes.AvatarUnsupported, "avatar.unsupported"),
            (ErrorCodes.AssetTooLarge, "asset.too_large"),
            (ErrorCodes.AssetInvalid, "asset.invalid"),
        ];

        Assert.All(cases, c => Assert.Equal(c.Name, ErrorCodes.NameOf(c.Code)));
        Assert.Equal(cases.Length, cases.Select(c => c.Code).Distinct().Count());
    }
}
