using openLuo.AgentContext.Core;

namespace openLuo.Infrastructure.Skills;

/// <summary>
/// 技能目录加载器：把 <c>data/skills/*.md</c> 解析为 <see cref="SkillDocument"/>。
/// <para>
/// 为什么手写解析而不引 YAML：技能文件只用到"扁平的 <c>key: value</c> + 方括号数组"这一极小 YAML 子集，
/// 为此引入整包 YAML 依赖（体积、反序列化行为不确定性、多一个解析面）不划算；
/// 手写约 80 行即可覆盖，且能把容错策略写死成"坏行跳过、文件不炸"。
/// </para>
/// <para>
/// 为什么容错而非抛异常：技能目录是运维可直接手改的数据面，一个手滑的冒号不该让整个进程启动失败；
/// 单文件级失败同样只跳过该文件（正文优先，宁可少一条技能也别丢用户写下的大段正文）。
/// </para>
/// <para>格式定义与容错行为见 <c>openLuo/data/skills/README.md</c>。</para>
/// </summary>
public static class SkillFileLoader
{
    /// <summary>frontmatter 分隔符：必须是独占一行的三个连字符。</summary>
    private const string Delimiter = "---";

    /// <summary>
    /// 加载目录下所有 <c>*.md</c> 技能。目录不存在（或未配置）时返回空表——技能是可选的，缺目录不是错误。
    /// </summary>
    public static IReadOnlyList<SkillDocument> Load(string skillsDir)
    {
        var documents = new List<SkillDocument>();
        if (string.IsNullOrWhiteSpace(skillsDir) || !Directory.Exists(skillsDir))
            return documents;

        // 固定按路径字典序枚举：这样"重复 id 后者覆盖前者"的胜负在每次启动都可复现（不同文件系统枚举顺序不保证稳定）。
        foreach (var file in Directory.EnumerateFiles(skillsDir, "*.md").OrderBy(static path => path, StringComparer.Ordinal))
        {
            // 目录自带的说明文档不是技能，否则会被当成一个叫 "README" 的技能挂进目录。
            if (string.Equals(Path.GetFileName(file), "README.md", StringComparison.OrdinalIgnoreCase))
                continue;

            var document = TryLoadFile(file);
            if (document is null)
                continue;

            Upsert(documents, document);
        }

        return documents;
    }

    /// <summary>读取单个文件；IO 失败（权限/占用/目录被并发删掉等）只跳过该文件，不影响其它技能。</summary>
    private static SkillDocument? TryLoadFile(string file)
    {
        string text;
        try
        {
            text = File.ReadAllText(file); // 默认 UTF-8 并自动剥离 BOM
        }
        catch (Exception)
        {
            return null;
        }

        return Parse(text, Path.GetFileNameWithoutExtension(file));
    }

    /// <summary>
    /// 解析单份技能文本。<paramref name="fallbackId"/> 为文件名的 id 兜底。
    /// </summary>
    private static SkillDocument Parse(string content, string fallbackId)
    {
        var lines = SplitLines(content);
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var blockLists = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var bodyStart = 0;

        // 定位起始分隔符：允许 frontmatter 之前有若干注释/空行（"文件头写点说明"是常见手写习惯），
        // 但第一个有效内容行不是 --- 就判定整份文件没有 frontmatter。
        var opening = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            var probe = lines[i].Trim();
            if (probe.Length == 0 || probe[0] == '#')
                continue;

            if (probe == Delimiter)
                opening = i;

            break;
        }

        if (opening >= 0)
        {
            var closing = -1;
            for (var i = opening + 1; i < lines.Length; i++)
            {
                if (IsDelimiter(lines[i]))
                {
                    closing = i;
                    break;
                }
            }

            if (closing > opening)
            {
                // pendingListKey：上一行是 "key:"（空值）时，随后的 "- 项" 归它所有（块状数组写法）。
                string? pendingListKey = null;
                for (var i = opening + 1; i < closing; i++)
                    pendingListKey = ParseFieldLine(lines[i], fields, blockLists, pendingListKey);

                // 只认第一段 frontmatter：闭合行之后的一切（含形如 --- 的行）都是正文。
                // 这是"正文里写分隔线"的唯一解释方式——否则任何正文里的分隔线都会截断技能内容。
                bodyStart = closing + 1;
            }

            // 有起始分隔符但始终没有闭合行：整个文件当正文（保留原样），宁可把元数据当正文，也不吞掉用户内容。
        }

        var id = Value(fields, "id");
        var title = Value(fields, "title");

        return new SkillDocument
        {
            Id = id.Length > 0 ? id : fallbackId,
            // title 缺省时回落为 id：目录渲染依赖于"有东西可显示"，空标题会让模型看到一条无名技能。
            Title = title.Length > 0 ? title : (id.Length > 0 ? id : fallbackId),
            Summary = Value(fields, "summary"),
            WhenToUse = Value(fields, "whenToUse"),
            FullContent = string.Join("\n", lines.Skip(bodyStart)).Trim('\n'),
            RelatedTools = List(fields, blockLists, "relatedTools"),
            RelatedWorkflows = List(fields, blockLists, "relatedWorkflows"),
            Constraints = List(fields, blockLists, "constraints"),
        };
    }

    /// <summary>统一换行（CRLF/CR 都归一为 LF），避免正文尾部多出 \r。</summary>
    private static string[] SplitLines(string content) =>
        content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    private static bool IsDelimiter(string line) => line.Trim() == Delimiter;

    /// <summary>
    /// 解析 frontmatter 的一行；返回"当前可接收块状数组项"的键（无则 null）。
    /// 解析不出 key 的行（空行、注释、误置的正文行）一律跳过而不抛——手改的元数据不该炸掉进程。
    /// </summary>
    private static string? ParseFieldLine(
        string line,
        Dictionary<string, string> fields,
        Dictionary<string, List<string>> blockLists,
        string? pendingListKey)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0)
            return pendingListKey; // 块状数组里的空行只是排版

        if (trimmed[0] == '#')
            return pendingListKey; // 注释同理，不打断块状数组

        if (trimmed.StartsWith('-') && (trimmed.Length == 1 || char.IsWhiteSpace(trimmed[1])))
        {
            // 块状数组写法（constraints:\n  - x）。没有 pending 键时无处归属，只能丢弃。
            if (pendingListKey is null)
                return null;

            var item = Unquote(trimmed[1..].Trim());
            if (item.Length > 0)
            {
                if (!blockLists.TryGetValue(pendingListKey, out var items))
                    blockLists[pendingListKey] = items = [];

                items.Add(item);
            }

            return pendingListKey;
        }

        var colon = trimmed.IndexOf(':');
        if (colon <= 0)
            return null;

        var key = trimmed[..colon].Trim();
        if (key.Length == 0)
            return null;

        var value = StripComment(trimmed[(colon + 1)..]).Trim();

        // 未知键保留在字典里但从不被读取——省掉一份键名白名单，且新键不会让旧版本报错。
        fields[key] = value;

        // 空值可能后接块状数组项；非空值后面跟着的 "- 项" 就真是误置的正文行了（不认）。
        return value.Length == 0 ? key : null;
    }

    /// <summary>
    /// 去掉行尾注释（如 <c>id: comfort   # 可省略</c>）。
    /// 判定：不在方括号/引号内，且 <c>#</c> 处于行首或前一位是空白——这样 <c>music:#1</c> 这类值不会被拦腰截断。
    /// </summary>
    private static string StripComment(string value)
    {
        var inBrackets = false;
        var quote = '\0';
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (quote != '\0')
            {
                if (c == quote)
                    quote = '\0';
                continue;
            }

            switch (c)
            {
                case '"':
                case '\'':
                    quote = c;
                    break;
                case '[':
                    inBrackets = true;
                    break;
                case ']':
                    inBrackets = false;
                    break;
                case '#' when !inBrackets && (i == 0 || char.IsWhiteSpace(value[i - 1])):
                    return value[..i];
            }
        }

        return value;
    }

    private static string Value(Dictionary<string, string> fields, string key) =>
        fields.TryGetValue(key, out var value) ? value : string.Empty;

    private static IReadOnlyList<string> List(Dictionary<string, string> fields, Dictionary<string, List<string>> blockLists, string key)
    {
        var items = new List<string>();
        var raw = Value(fields, key).Trim();

        if (raw.StartsWith('['))
        {
            // 容错：漏写收尾的 ] 也照样按数组解析。
            var inner = raw.EndsWith(']') ? raw[1..^1] : raw[1..];
            foreach (var part in inner.Split(','))
            {
                var item = Unquote(part.Trim());
                if (item.Length > 0)
                    items.Add(item);
            }
        }
        else if (raw.Length > 0)
        {
            // 容错：数组键写了标量（relatedTools: music:play）也当单元素数组，而不是丢掉用户写的值。
            items.Add(Unquote(raw));
        }

        // 块状写法（key:\n  - a\n  - b）追加在行内写法之后，两种混用时不丢任一侧。
        if (blockLists.TryGetValue(key, out var block))
            items.AddRange(block);

        return items;
    }

    /// <summary>剥掉成对的单/双引号（数组元素常见写法）。</summary>
    private static string Unquote(string value)
    {
        if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0])
            return value[1..^1].Trim();

        return value;
    }

    /// <summary>重复 id：后者覆盖前者（按文件路径字典序枚举，故"后者"= 字典序靠后的文件），且保持首次出现的位置。</summary>
    private static void Upsert(List<SkillDocument> documents, SkillDocument document)
    {
        for (var i = 0; i < documents.Count; i++)
        {
            if (!string.Equals(documents[i].Id, document.Id, StringComparison.OrdinalIgnoreCase))
                continue;

            documents[i] = document;
            return;
        }

        documents.Add(document);
    }
}
