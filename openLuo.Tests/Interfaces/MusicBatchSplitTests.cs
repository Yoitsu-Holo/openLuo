using openLuo.Interfaces.QQbot;
using openLuo.OneBot;

namespace openLuo.Interfaces.Tests;

/// <summary>
/// 验证 QqBotApplication 的多音乐卡拆分发送逻辑：音乐卡必须独占一条消息
/// (LLBot 对 text+music 混发不稳),文本/图片合并为一条,超出上限折叠为文本链接。
/// </summary>
public sealed class MusicBatchSplitTests
{
    private static OneBotSegment Music(long id) => OneBotSegment.Music163(id);
    private static OneBotSegment Text(string t) => OneBotSegment.Text(t);
    private static OneBotSegment Record(string d) => OneBotSegment.Record(d);

    [Fact]
    public void TextAndMusic_SplitIntoTwoBatches()
    {
        // 文本+单卡:两条消息——text 单独一条,卡独占一条
        var batches = QqBotApplication.SplitMusicBatches([Text("给你推一首"), Music(1)]);
        Assert.Equal(2, batches.Count);
        Assert.Equal(["给你推一首"], batches[0].Select(Log).ToArray());
        Assert.Equal(["1"], batches[1].Select(Log).ToArray());
    }

    [Fact]
    public void MultiMusic_EachMusicInOwnBatch()
    {
        // text 在前、两卡随后:1 条文本 + 2 条独占卡
        var batches = QqBotApplication.SplitMusicBatches([Text("这几首都推给你"), Music(10), Music(20)]);
        Assert.Equal(3, batches.Count);
        Assert.Equal(["这几首都推给你"], batches[0].Select(Log).ToArray());
        Assert.Equal(["10"], batches[1].Select(Log).ToArray());
        Assert.Equal(["20"], batches[2].Select(Log).ToArray());
    }

    [Fact]
    public void OverflowBeyondCap_CollapsesExtraCardsToTextLink()
    {
        // 超过 MaxMusicCardsPerTurn:合法卡各独占一条,多余的折叠为文本链接追加为一条
        var music = Enumerable.Range(1, QqBotApplication.MaxMusicCardsPerTurn + 2)
            .Select(i => Music(100 + i)).ToList();
        var batches = QqBotApplication.SplitMusicBatches(music);

        // 3 张独占卡 + 1 条折叠文本
        Assert.Equal(QqBotApplication.MaxMusicCardsPerTurn + 1, batches.Count);
        for (int i = 0; i < QqBotApplication.MaxMusicCardsPerTurn; i++)
            Assert.Single(batches[i], s => s.Type == "music");   // 前 N 批各独占一张卡
        Assert.Single(batches[^1], s => s.Type == "text");       // 最后一批是纯折叠文本

        var note = batches[^1].Single(s => s.Type == "text").Data["text"]?.GetValue<string>() ?? string.Empty;
        Assert.Contains("2 首候选", note);
        Assert.Contains("music.163.com/#/song?id=104", note);
        Assert.Contains("music.163.com/#/song?id=105", note);
    }

    [Fact]
    public void InterleavedText_CardOwnsBatchOrderPreserved()
    {
        // text 卡 text 卡:文本各自成队、每卡独占,保持原始出场顺序
        var batches = QqBotApplication.SplitMusicBatches([Text("a"), Music(1), Text("b"), Music(2), Text("c")]);
        Assert.Equal(5, batches.Count);
        Assert.Equal(["a"], batches[0].Select(Log).ToArray());
        Assert.Equal(["1"], batches[1].Select(Log).ToArray());
        Assert.Equal(["b"], batches[2].Select(Log).ToArray());
        Assert.Equal(["2"], batches[3].Select(Log).ToArray());
        Assert.Equal(["c"], batches[4].Select(Log).ToArray());
    }

    [Fact]
    public void ConsecutiveText_MergedIntoOneBatch()
    {
        // 多段连续文本归并为一条(不因中间无卡而碎片化)
        var batches = QqBotApplication.SplitMusicBatches([Text("a"), Text("b"), Music(7), Text("c")]);
        Assert.Equal(3, batches.Count);
        Assert.Equal(["a", "b"], batches[0].Select(Log).ToArray());
        Assert.Equal(["7"], batches[1].Select(Log).ToArray());
        Assert.Equal(["c"], batches[2].Select(Log).ToArray());
    }

    [Fact]
    public void MultiRecord_EachRecordInOwnBatch()
    {
        // 多条 record(语音)必须各自独占一条消息(OneBot→QQ 同消息多个 record 只取第一个)
        var batches = QqBotApplication.SplitMusicBatches([Record("bin1"), Record("bin2"), Record("bin3")]);
        Assert.Equal(3, batches.Count);
        foreach (var b in batches)
            Assert.Single(b, s => s.Type == "record");
        Assert.Equal(["bin1"], batches[0].Select(Log).ToArray());
        Assert.Equal(["bin2"], batches[1].Select(Log).ToArray());
        Assert.Equal(["bin3"], batches[2].Select(Log).ToArray());
    }

    [Fact]
    public void MusicAndRecord_EachExclusiveInOwnBatch()
    {
        // text + 卡 + 语音:文本一条,卡独占一条,语音独占一条(各自成为独立消息)
        var batches = QqBotApplication.SplitMusicBatches([Text("给你听"), Music(1), Record("binX")]);
        Assert.Equal(3, batches.Count);
        Assert.Equal(["给你听"], batches[0].Select(Log).ToArray());
        Assert.Equal(["1"], batches[1].Select(Log).ToArray());
        Assert.Equal(["binX"], batches[2].Select(Log).ToArray());
    }

    private static string Log(OneBotSegment s) => s.Type switch
    {
        "text" => s.Data["text"]?.GetValue<string>() ?? string.Empty,
        "music" => (s.Data["id"]?.GetValue<long>() ?? 0L).ToString(),
        "record" => StringValue(s, "file"),
        _ => s.Type
    };

    private static string StringValue(OneBotSegment s, string key)
        => s.Data[key]?.GetValue<string>() ?? string.Empty;
}
