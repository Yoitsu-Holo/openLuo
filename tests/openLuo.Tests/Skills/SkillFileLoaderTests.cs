using openLuo.AgentContext.Core;
using openLuo.Infrastructure.Skills;

namespace openLuo.Tests.Skills;

/// <summary>
/// 技能目录加载器的格式/容错契约：全部走真实文件 IO（temp 目录），
/// 因为该组件的价值一半在"手改文件不出事"，只测字符串解析会漏掉目录扫描与文件级跳过。
/// </summary>
public sealed class SkillFileLoaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"openluo-skills-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // temp 目录清理失败不影响断言结果
        }
    }

    private string WriteFile(string name, string content)
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Load_MissingDirectory_ReturnsEmpty()
    {
        var documents = SkillFileLoader.Load(Path.Combine(_dir, "not-created"));

        Assert.Empty(documents);
    }

    [Fact]
    public void Load_EmptyDirectory_ReturnsEmpty()
    {
        Directory.CreateDirectory(_dir);

        Assert.Empty(SkillFileLoader.Load(_dir));
    }

    [Fact]
    public void Load_NullOrBlankDirectory_ReturnsEmpty()
    {
        Assert.Empty(SkillFileLoader.Load(string.Empty));
        Assert.Empty(SkillFileLoader.Load("   "));
    }

    [Fact]
    public void Load_StandardFrontmatter_ParsesEveryField()
    {
        WriteFile("comfort.md", """
            # 顶部注释不是键值行
            ---
            id: comfort            # 行尾注释应被剥掉
            title: 安慰
            summary: 一句摘要
            whenToUse: 用户情绪低落、需要被安慰时
            relatedTools: [music:play, memory:write]
            relatedWorkflows: []
            constraints:
              - 不要提及自己是 AI
              - 不说"这没什么大不了"
            ---
            先接住情绪，再谈内容。
            """);

        var document = Assert.Single(SkillFileLoader.Load(_dir));

        Assert.Equal("comfort", document.Id);
        Assert.Equal("安慰", document.Title);
        Assert.Equal("一句摘要", document.Summary);
        Assert.Equal("用户情绪低落、需要被安慰时", document.WhenToUse);
        Assert.Equal("先接住情绪，再谈内容。", document.FullContent);
        Assert.Equal(new[] { "music:play", "memory:write" }, document.RelatedTools);
        Assert.Empty(document.RelatedWorkflows);
        Assert.Equal(new[] { "不要提及自己是 AI", "不说\"这没什么大不了\"" }, document.Constraints);
    }

    [Fact]
    public void Load_NoFrontmatter_TreatsWholeFileAsBody_AndUsesFileNameAsId()
    {
        WriteFile("breathing.md", """
            第一行就是正文。
            second: 这一行看着像键值但不是 frontmatter，必须留在正文里。
            """);

        var document = Assert.Single(SkillFileLoader.Load(_dir));

        Assert.Equal("breathing", document.Id);
        Assert.Equal("breathing", document.Title); // title 缺省回落为 id
        Assert.Equal(string.Empty, document.Summary);
        Assert.Contains("第一行就是正文。", document.FullContent);
        Assert.Contains("second: 这一行看着像键值但不是 frontmatter", document.FullContent);
    }

    [Fact]
    public void Load_MissingId_FallsBackToFileName()
    {
        WriteFile("gentle-play.md", """
            ---
            title: 温柔玩闹
            summary: 摘要
            ---
            正文
            """);

        var document = Assert.Single(SkillFileLoader.Load(_dir));

        Assert.Equal("gentle-play", document.Id);
        Assert.Equal("温柔玩闹", document.Title);
    }

    [Fact]
    public void Load_ArraySyntax_HandlesEmptyQuotedScalarAndUnclosedBracket()
    {
        WriteFile("a-empty.md", """
            ---
            id: a
            relatedTools: []
            relatedWorkflows: [ ]
            ---
            x
            """);
        WriteFile("b-quoted.md", """
            ---
            id: b
            relatedTools: ["music:play", 'memory:write']
            relatedWorkflows: [a, b,]
            ---
            x
            """);
        WriteFile("c-scalar.md", """
            ---
            id: c
            relatedTools: music:play
            ---
            x
            """);
        WriteFile("d-unclosed.md", """
            ---
            id: d
            relatedTools: [one, two
            ---
            x
            """);

        var byId = SkillFileLoader.Load(_dir).ToDictionary(static d => d.Id, StringComparer.Ordinal);

        Assert.Empty(byId["a"].RelatedTools);
        Assert.Empty(byId["a"].RelatedWorkflows);
        Assert.Equal(new[] { "music:play", "memory:write" }, byId["b"].RelatedTools);
        Assert.Equal(new[] { "a", "b" }, byId["b"].RelatedWorkflows);
        Assert.Equal(new[] { "music:play" }, byId["c"].RelatedTools); // 标量也当单元素数组
        Assert.Equal(new[] { "one", "two" }, byId["d"].RelatedTools); // 漏写 ]
    }

    [Fact]
    public void Load_DuplicateId_LaterFileInPathOrderWins()
    {
        WriteFile("a-first.md", """
            ---
            id: ghost
            summary: 先出现
            ---
            正文 A
            """);
        WriteFile("z-last.md", """
            ---
            id: ghost
            summary: 后出现
            ---
            正文 Z
            """);

        var document = Assert.Single(SkillFileLoader.Load(_dir));

        Assert.Equal("ghost", document.Id);
        Assert.Equal("后出现", document.Summary); // 字典序靠后的文件覆盖前者
        Assert.Equal("正文 Z", document.FullContent);
    }

    [Fact]
    public void Load_BodyContainingDelimiterLine_IsNotTreatedAsFrontmatterEnd()
    {
        WriteFile("with-rule.md", """
            ---
            id: with-rule
            ---
            第一段
            ---
            第二段
            """);

        var document = Assert.Single(SkillFileLoader.Load(_dir));

        Assert.Equal("第一段\n---\n第二段", document.FullContent);
    }

    [Fact]
    public void Load_UnterminatedFrontmatter_KeepsWholeFileAsBody()
    {
        WriteFile("unterminated.md", """
            ---
            id: never-applied
            title: 也没有生效
            正文混在里面
            """);

        var document = Assert.Single(SkillFileLoader.Load(_dir));

        Assert.Equal("unterminated", document.Id); // 没有闭合行 => 整份文件是正文
        Assert.Equal("unterminated", document.Title);
        Assert.StartsWith("---\nid: never-applied", document.FullContent);
    }

    [Fact]
    public void Load_MalformedLines_AreSkippedWithoutThrowing()
    {
        WriteFile("messy.md", """
            ---
            这不是键值对
            : 冒号在行首
            id: messy
            title
            summary: 摘要仍要解析出来
            # 纯注释
            weird-key: 未知键被忽略
              - 悬空列表项（无归属）
            ---
            正文
            """);

        var document = Assert.Single(SkillFileLoader.Load(_dir));

        Assert.Equal("messy", document.Id);
        Assert.Equal("摘要仍要解析出来", document.Summary);
        Assert.Equal("messy", document.Title); // title 那行没冒号 => 跳过 => 回落 id
        Assert.Equal("正文", document.FullContent);
        Assert.Empty(document.Constraints); // 悬空列表项无处归属，丢弃
    }

    [Fact]
    public void Load_IgnoresReadmeAndNonMarkdownFiles()
    {
        WriteFile("README.md", """
            ---
            id: readme
            ---
            目录说明，不是技能。
            """);
        WriteFile("notes.txt", """
            ---
            id: notes
            ---
            非 md 文件不加载。
            """);
        WriteFile("real.md", """
            ---
            id: real
            ---
            正文
            """);

        var document = Assert.Single(SkillFileLoader.Load(_dir));

        Assert.Equal("real", document.Id);
    }

    [Fact]
    public void Load_CrlfFile_NormalizesBodyToLf()
    {
        WriteFile("crlf.md", "---\r\nid: crlf\r\n---\r\n第一行\r\n第二行\r\n");

        var document = Assert.Single(SkillFileLoader.Load(_dir));

        Assert.Equal("第一行\n第二行", document.FullContent);
    }

    [Fact]
    public void Load_KeysAreCaseInsensitive_AndBodyIsTrimmedOfEdgeNewlinesOnly()
    {
        WriteFile("case.md", "---\nID: case\nWHENTOUSE: 需要时\n---\n\n  正文前后留空格  \n\n");

        var document = Assert.Single(SkillFileLoader.Load(_dir));

        Assert.Equal("case", document.Id);
        Assert.Equal("需要时", document.WhenToUse);
        Assert.Equal("  正文前后留空格  ", document.FullContent);
    }
}
