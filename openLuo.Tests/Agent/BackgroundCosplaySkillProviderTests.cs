using openLuo.Modules.Agent.Application;
using openLuo.Modules.Content.Application.Registry;
using openLuo.Modules.Content.Core.Definitions;

namespace openLuo.Agent.Tests;

public sealed class CharacterArchetypeCosplaySkillProviderTests
{
    [Fact]
    public void GetProfile_CanResolveRuntimeCharacterIdToArchetype()
    {
        var catalog = new CharacterArchetypeAgentProfileCatalog(BuildRegistry(
        [
            new CharacterArchetypeDefinition
            {
                Id = "builtin-ojousama",
                DisplayName = "大小姐",
                CharacterName = "艾莉娅"
            }
        ]));

        var profile = catalog.GetProfile("char_builtin-ojousama");

        Assert.Equal("char_builtin-ojousama", profile.CharacterId);
        Assert.Equal("艾莉娅", profile.DisplayName);
        Assert.Equal("builtin-ojousama", profile.ArchetypeId);
    }

    [Fact]
    public void GetPreloadedSkills_BuildsCosplaySkillFromArchetype()
    {
        var provider = new CharacterArchetypeCosplaySkillProvider(BuildRegistry(
        [
            new CharacterArchetypeDefinition
            {
                Id = "char_builtin-nekomimi",
                DisplayName = "铃",
                CharacterName = "铃",
                Prompt = "你是铃，原本是玩家养了三年的橘猫。",
                Backstory = "在满月之夜变成了猫娘。",
                InitialLocation = "玩家的家",
                Goals = ["陪伴玩家", "守着这个家"],
                Likes = ["晒太阳"],
                Dislikes = ["突然的巨响"],
                Habits = ["蹭手背"],
                NarrativeHints = new Dictionary<string, string> { ["late_return"] = "先闻一闻再确认是否平安" },
                EmotionalTriggers = new Dictionary<string, List<string>>
                {
                    ["jealous"] = ["玩家夸别人", "玩家忽略自己"]
                },
                Traits = ["依恋而敏感", "感官主导"]
            }
        ]));

        var skills = provider.GetPreloadedSkills(new AgentProfile
        {
            CharacterId = "c1",
            DisplayName = "铃",
            ArchetypeId = "char_builtin-nekomimi"
        });

        var skill = Assert.Single(skills);
        Assert.Equal("cosplay:c1", skill.Name);
        Assert.Equal("cosplay", skill.Category);
        Assert.Contains("cosplay", skill.Capabilities);
        Assert.Contains("persona", skill.Capabilities);
        Assert.Contains("角色：铃", skill.Body);
        Assert.Contains("你是铃，原本是玩家养了三年的橘猫。", skill.Body);
        Assert.Contains("人格特征：依恋而敏感、感官主导", skill.Body);
        Assert.Contains("角色目标：陪伴玩家；守着这个家", skill.Body);
        Assert.Contains("默认场景：玩家的家", skill.Body);
        Assert.Contains("不要因为角色设定里“不懂技术”就拒绝使用能力", skill.Body);
    }

    private static ContentRegistry BuildRegistry(IReadOnlyList<CharacterArchetypeDefinition> definitions)
    {
        var entries = definitions.ToDictionary(
            x => x.Id,
            x => new RegistryEntry
            {
                Definition = x,
                SourcePack = "test"
            },
            StringComparer.OrdinalIgnoreCase);

        return new ContentRegistry(new Dictionary<ContentKind, IReadOnlyDictionary<string, RegistryEntry>>
        {
            [ContentKind.CharacterArchetype] = entries
        });
    }
}
