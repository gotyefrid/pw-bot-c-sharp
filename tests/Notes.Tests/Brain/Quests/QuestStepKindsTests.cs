using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Notes.Core.Brain.Quests;
using Notes.Core.Profiles;
using Xunit;

namespace Notes.Tests.Brain.Quests;

/// <summary>Шаги скриптов заданий из профиля: вид по "kind", поля — в свойства шага, ошибки — с причиной.</summary>
public class QuestStepKindsTests
{
    [Fact]
    public void KnownKind_FieldsFilled()
    {
        var step = QuestStepKinds.Parse(JObject.Parse("""{ "kind": "LearnSkill", "skill": 330 }"""), out var problem);

        Assert.Null(problem);
        Assert.Equal(330, Assert.IsType<LearnSkillStep>(step).Skill);
    }

    [Fact]
    public void UnknownKind_NullWithReason()
    {
        var step = QuestStepKinds.Parse(JObject.Parse("""{ "kind": "Dance" }"""), out var problem);

        Assert.Null(step);
        Assert.Contains("Dance", problem);
    }

    [Fact]
    public void BadFields_NullWithReason()
    {
        var step = QuestStepKinds.Parse(JObject.Parse("""{ "kind": "LearnSkill" }"""), out var problem);

        Assert.Null(step);
        Assert.Contains("skill", problem);
    }

    [Fact]
    public void ParseAll_SkipsBadKeepsOrder()
    {
        var problems = new List<string>();
        var steps = QuestStepKinds.ParseAll(
        [
            JObject.Parse("""{ "kind": "LearnSkill", "skill": 330 }"""),
            JObject.Parse("""{ "kind": "Dance" }"""),
            JObject.Parse("""{ "kind": "HatchPets" }"""),
        ], problems);

        Assert.Equal([typeof(LearnSkillStep), typeof(HatchPetsStep)], steps.Select(s => s.GetType()));
        Assert.Single(problems);
    }

    [Theory]
    [InlineData("pwclassic136")]
    [InlineData("comeback136")]
    [InlineData("comeback146")]
    public void ProfileScripts_AllStepsRead(string id)
    {
        var problems = new List<string>();
        foreach (var script in new ProfileCatalog().Load(id).Data.QuestScripts.Values)
            QuestStepKinds.ParseAll(script.After, problems);

        Assert.Empty(problems);
    }

    [Fact]
    public void Comeback146_WolfQuestLearnsPetSkillsAndHatches()
    {
        var script = new ProfileCatalog().Load("comeback146").Data.QuestScripts[5913];
        var steps = QuestStepKinds.ParseAll(script.After, []);

        Assert.Equal([330, 329], steps.OfType<LearnSkillStep>().Select(s => s.Skill));
        Assert.IsType<HatchPetsStep>(steps.Last());
    }
}
